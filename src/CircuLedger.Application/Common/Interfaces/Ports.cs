using CircuLedger.Domain.Geo;

namespace CircuLedger.Application.Common.Interfaces;

public interface ICurrentUser
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);

    /// <summary>Value written to audit columns and ledger actor: the user ID, or "system".</summary>
    string Actor => UserId ?? "system";
}

/// <summary>Protects personal identifiers. Phones and IMEIs are hashed with a keyed HMAC, and phones are also AES-GCM encrypted.</summary>
public interface IPersonalDataProtector
{
    /// <summary>Normalises to E.164 (+232XXXXXXXX). Returns null if the value isn't a valid phone number.</summary>
    string? NormalizePhone(string raw);
    string HashPhone(string normalizedPhone);
    string EncryptPhone(string normalizedPhone);
    string DecryptPhone(string cipherText);
    string HashImei(string imei);
    static string Last4(string value) => value.Length <= 4 ? value : value[^4..];
}

public interface ISmsSender
{
    Task SendAsync(string normalizedPhone, string message, CancellationToken cancellationToken = default);
}

public sealed record StoredFile(string Key, long SizeBytes, string Sha256, string ContentType);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string extension, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record CertificateSignature(string Signature, string Algorithm, string KeyId);

public interface ICertificateSigner
{
    string KeyId { get; }
    string PublicKeyPem { get; }
    CertificateSignature Sign(string canonicalPayload);
    bool Verify(string canonicalPayload, string signature, string keyId);
}

public sealed record CertificateDocumentModel(
    string CertificateNumber,
    DateTime IssuedAt,
    string TrackingId,
    string Brand,
    string? Model,
    string MaskedImei,
    string Method,
    int Passes,
    DateTime SanitizationStartedAt,
    DateTime SanitizationCompletedAt,
    string TechnicianName,
    string PayloadHash,
    string SignatureAlgorithm,
    string SigningKeyId,
    string VerificationUrl);

public interface ICertificateDocumentRenderer
{
    byte[] RenderPdf(CertificateDocumentModel model);
    byte[] RenderQrPng(string url);
}

/// <summary>Resolves a free-text area or landmark (from USSD) to coordinates using a configured gazetteer.</summary>
public interface IGeocoder
{
    GeoPoint? TryGeocode(string? areaOrLandmark);
}

public sealed record UserSummary(string Id, string FullName, string? Email, IReadOnlyList<string> Roles);

/// <summary>Read-side access to Identity users, so Application services don't depend on ASP.NET Identity.</summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserSummary>> GetUsersInRoleAsync(string role, CancellationToken cancellationToken = default);
}
