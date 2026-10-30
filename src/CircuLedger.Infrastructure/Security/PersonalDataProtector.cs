using System.Security.Cryptography;
using System.Text;
using CircuLedger.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace CircuLedger.Infrastructure.Security;

public sealed class DataProtectionKeysOptions
{
    public const string Section = "DataProtection";

    /// <summary>Base64, at least 32 bytes. Key for the phone-number HMAC (lookup hash).</summary>
    public string PhoneHashKey { get; set; } = string.Empty;

    /// <summary>Base64, at least 32 bytes. Key for the IMEI HMAC.</summary>
    public string ImeiHashKey { get; set; } = string.Empty;

    /// <summary>Base64, exactly 32 bytes (AES-256-GCM). Used to encrypt phone numbers so we can send SMS.</summary>
    public string PhoneEncryptionKey { get; set; } = string.Empty;
}

/// <summary>
/// Keyed HMAC-SHA256 for lookups (a plain hash of an 8-digit number is trivially brute-forced),
/// plus AES-256-GCM for the ciphertext needed to send SMS.
/// </summary>
public sealed class PersonalDataProtector : IPersonalDataProtector
{
    private const string CipherVersion = "v1";
    private readonly byte[] _phoneHashKey;
    private readonly byte[] _imeiHashKey;
    private readonly byte[] _encryptionKey;

    public PersonalDataProtector(IOptions<DataProtectionKeysOptions> options)
    {
        var o = options.Value;
        _phoneHashKey = Decode(o.PhoneHashKey, nameof(o.PhoneHashKey), minLength: 32);
        _imeiHashKey = Decode(o.ImeiHashKey, nameof(o.ImeiHashKey), minLength: 32);
        _encryptionKey = Decode(o.PhoneEncryptionKey, nameof(o.PhoneEncryptionKey), minLength: 32);
        if (_encryptionKey.Length != 32)
            throw new InvalidOperationException("DataProtection:PhoneEncryptionKey must be exactly 32 bytes (base64).");
    }

    public string? NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        // Sierra Leone: +232 followed by an 8-digit national number.
        if (digits.StartsWith("232") && digits.Length == 11) return "+" + digits;
        if (digits.StartsWith('0') && digits.Length == 9) return "+232" + digits[1..];
        if (!hasPlus && digits.Length == 8) return "+232" + digits;
        // Other E.164 numbers (for example, gateway sandbox test numbers).
        if (hasPlus && digits.Length is >= 10 and <= 15) return "+" + digits;
        return null;
    }

    public string HashPhone(string normalizedPhone) => Hmac(_phoneHashKey, normalizedPhone);

    public string HashImei(string imei) => Hmac(_imeiHashKey, imei);

    public string EncryptPhone(string normalizedPhone)
    {
        var plaintext = Encoding.UTF8.GetBytes(normalizedPhone);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(_encryptionKey, tag.Length);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return $"{CipherVersion}:{Convert.ToBase64String([.. nonce, .. tag, .. cipher])}";
    }

    public string DecryptPhone(string cipherText)
    {
        var parts = cipherText.Split(':', 2);
        if (parts.Length != 2 || parts[0] != CipherVersion) throw new CryptographicException("Unsupported cipher format.");
        var data = Convert.FromBase64String(parts[1]);
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var nonce = data.AsSpan(0, nonceSize);
        var tag = data.AsSpan(nonceSize, tagSize);
        var cipher = data.AsSpan(nonceSize + tagSize);
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(_encryptionKey, tagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static string Hmac(byte[] key, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));

    private static byte[] Decode(string base64, string name, int minLength)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException($"DataProtection:{name} is not configured. Set it via environment variable DataProtection__{name}.");
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length < minLength)
            throw new InvalidOperationException($"DataProtection:{name} must be at least {minLength} bytes.");
        return bytes;
    }
}
