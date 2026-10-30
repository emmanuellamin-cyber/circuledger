using System.Security.Cryptography;
using CircuLedger.Domain.Ledger;
using CircuLedger.Infrastructure.Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CircuLedger.Tests.Unit;

public class CertificateSigningTests
{
    private static RsaCertificateSigner CreateSigner(string privateKeyPem, params string[] retiredPublicKeys)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(Path.GetTempPath());
        var options = Options.Create(new CertificateSigningOptions { PrivateKeyPem = privateKeyPem, RetiredPublicKeysPem = [.. retiredPublicKeys] });
        return new RsaCertificateSigner(options, env, NullLogger<RsaCertificateSigner>.Instance);
    }

    private static string NewKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static readonly string Payload = JsonCanonicalizer.Serialize(new
    {
        CertificateNumber = "CDS-2026-000001",
        Device = new { TrackingId = "CL-2026-000123", Brand = "Samsung", ImeiLast4 = "3809" },
        Method = "SamsungKnoxGradeErase",
        Passes = 1
    });

    [Fact]
    public void Signed_payload_verifies_with_the_same_key()
    {
        using var signer = CreateSigner(NewKeyPem());
        var signature = signer.Sign(Payload);

        Assert.Equal("RS256", signature.Algorithm);
        Assert.Equal(signer.KeyId, signature.KeyId);
        Assert.True(signer.Verify(Payload, signature.Signature, signature.KeyId));
    }

    [Fact]
    public void Tampered_payload_fails_verification()
    {
        using var signer = CreateSigner(NewKeyPem());
        var signature = signer.Sign(Payload);

        var tampered = Payload.Replace("3809", "0000");

        Assert.False(signer.Verify(tampered, signature.Signature, signature.KeyId));
    }

    [Fact]
    public void Signature_from_another_key_fails_verification()
    {
        using var signerA = CreateSigner(NewKeyPem());
        using var signerB = CreateSigner(NewKeyPem());
        var signature = signerA.Sign(Payload);

        Assert.False(signerB.Verify(Payload, signature.Signature, signature.KeyId));
        Assert.False(signerB.Verify(Payload, signature.Signature, signerB.KeyId));
    }

    [Fact]
    public void Garbage_signature_is_rejected_without_throwing()
    {
        using var signer = CreateSigner(NewKeyPem());
        Assert.False(signer.Verify(Payload, "not-base64!!", signer.KeyId));
    }

    [Fact]
    public void Certificates_signed_by_a_retired_key_still_verify_after_rotation()
    {
        var oldPem = NewKeyPem();
        using var oldSigner = CreateSigner(oldPem);
        var signature = oldSigner.Sign(Payload);

        using var newSigner = CreateSigner(NewKeyPem(), oldSigner.PublicKeyPem);

        Assert.NotEqual(oldSigner.KeyId, newSigner.KeyId);
        Assert.True(newSigner.Verify(Payload, signature.Signature, signature.KeyId));
    }

    [Fact]
    public void Public_key_alone_is_enough_for_third_party_verification()
    {
        using var signer = CreateSigner(NewKeyPem());
        var signature = signer.Sign(Payload);

        using var publicOnly = RSA.Create();
        publicOnly.ImportFromPem(signer.PublicKeyPem);
        var ok = publicOnly.VerifyData(System.Text.Encoding.UTF8.GetBytes(Payload), Convert.FromBase64String(signature.Signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.True(ok);
    }

    [Fact]
    public void Keys_shorter_than_2048_bits_are_rejected()
    {
        using var weak = RSA.Create(1024);
        Assert.Throws<InvalidOperationException>(() => CreateSigner(weak.ExportPkcs8PrivateKeyPem()));
    }
}
