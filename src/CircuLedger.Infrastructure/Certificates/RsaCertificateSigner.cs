using System.Security.Cryptography;
using System.Text;
using CircuLedger.Application.Common.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CircuLedger.Infrastructure.Certificates;

public sealed class CertificateSigningOptions
{
    public const string Section = "Certificates:Signing";

    /// <summary>PKCS#8 / PKCS#1 PEM private key. It takes precedence over <see cref="PrivateKeyPath"/>. Supply it via env var.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>PEM file path (absolute or relative to the content root).</summary>
    public string PrivateKeyPath { get; set; } = "App_Data/keys/certificate-signing.pem";

    /// <summary>Generate and persist a 3072-bit key if none exists. Intended for development/pilot only.</summary>
    public bool AutoGenerateIfMissing { get; set; }

    /// <summary>Public keys of retired signing keys, so certificates they signed keep verifying after rotation.</summary>
    public List<string> RetiredPublicKeysPem { get; set; } = [];
}

/// <summary>
/// RSASSA-PKCS1-v1_5 with SHA-256 ("RS256") over the canonical certificate payload. Third parties can verify
/// with the published public key, e.g. <c>openssl dgst -sha256 -verify pub.pem -signature sig.bin payload.json</c>.
/// </summary>
public sealed class RsaCertificateSigner : ICertificateSigner, IDisposable
{
    public const string Algorithm = "RS256";
    private readonly RSA _privateKey;
    private readonly Dictionary<string, RSA> _verificationKeys = new(StringComparer.Ordinal);

    public RsaCertificateSigner(IOptions<CertificateSigningOptions> options, IHostEnvironment environment, ILogger<RsaCertificateSigner> logger)
    {
        var o = options.Value;
        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(LoadPem(o, environment, logger));
        if (_privateKey.KeySize < 2048) throw new InvalidOperationException("Certificate signing key must be at least 2048 bits.");

        PublicKeyPem = _privateKey.ExportSubjectPublicKeyInfoPem();
        KeyId = ComputeKeyId(_privateKey);
        _verificationKeys[KeyId] = _privateKey;

        foreach (var pem in o.RetiredPublicKeysPem.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            _verificationKeys.TryAdd(ComputeKeyId(rsa), rsa);
        }
    }

    public string KeyId { get; }
    public string PublicKeyPem { get; }

    public CertificateSignature Sign(string canonicalPayload)
    {
        var signature = _privateKey.SignData(Encoding.UTF8.GetBytes(canonicalPayload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new CertificateSignature(Convert.ToBase64String(signature), Algorithm, KeyId);
    }

    public bool Verify(string canonicalPayload, string signature, string keyId)
    {
        if (!_verificationKeys.TryGetValue(keyId, out var key)) return false;
        try
        {
            return key.VerifyData(Encoding.UTF8.GetBytes(canonicalPayload), Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Key ID = first 16 hex chars of SHA-256(SubjectPublicKeyInfo DER).</summary>
    public static string ComputeKeyId(RSA rsa) =>
        Convert.ToHexStringLower(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];

    private static string LoadPem(CertificateSigningOptions o, IHostEnvironment environment, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(o.PrivateKeyPem)) return o.PrivateKeyPem.Replace("\\n", "\n");

        var path = Path.IsPathRooted(o.PrivateKeyPath) ? o.PrivateKeyPath : Path.Combine(environment.ContentRootPath, o.PrivateKeyPath);
        if (File.Exists(path)) return File.ReadAllText(path);

        if (!o.AutoGenerateIfMissing)
            throw new InvalidOperationException(
                "No certificate signing key configured. Set Certificates__Signing__PrivateKeyPem or mount a key at Certificates:Signing:PrivateKeyPath.");

        using var rsa = RSA.Create(3072);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, pem);
        logger.LogWarning("Generated a new certificate signing key at {Path}. Back it up: losing it invalidates verification of issued certificates.", path);
        return pem;
    }

    public void Dispose()
    {
        foreach (var key in _verificationKeys.Values.Distinct()) key.Dispose();
    }
}
