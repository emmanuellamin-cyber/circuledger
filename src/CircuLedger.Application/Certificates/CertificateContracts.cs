using CircuLedger.Application.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Application.Certificates;

public sealed record CertificateDto(
    Guid Id,
    string CertificateNumber,
    Guid DeviceId,
    string TrackingId,
    DateTime IssuedAt,
    string PayloadHash,
    string SignatureAlgorithm,
    string SigningKeyId,
    string VerificationUrl,
    bool IsRevoked,
    DateTime? RevokedAt,
    string? RevocationReason);

/// <summary>Public verification result. It contains no personal data, and device identifiers are masked.</summary>
public sealed record PublicCertificateVerificationDto(
    string CertificateNumber,
    bool IsValid,
    string Status,
    DateTime? IssuedAt,
    string? Issuer,
    string? Device,
    string? MaskedImei,
    SanitizationMethod? Method,
    int? Passes,
    string? PayloadHash,
    string? Reason);

public sealed record RevokeCertificateRequest(string Reason);

public sealed record CertificateFile(string FileName, string ContentType, byte[] Content);

public interface ICertificateService
{
    /// <summary>Issues a certificate for a Passed sanitization record. Called inside the sanitization transaction.</summary>
    Task<CertificateDto> IssueAsync(Guid sanitizationRecordId, CancellationToken ct = default);
    Task<CertificateDto> GetAsync(string certificateNumber, CancellationToken ct = default);
    Task<PagedResult<CertificateDto>> ListAsync(PageQuery query, CancellationToken ct = default);
    Task<CertificateFile> GetPdfAsync(string certificateNumber, CancellationToken ct = default);
    Task<CertificateFile> GetQrCodeAsync(string certificateNumber, CancellationToken ct = default);
    Task<PublicCertificateVerificationDto> VerifyPublicAsync(string certificateNumber, CancellationToken ct = default);
    Task<CertificateDto> RevokeAsync(string certificateNumber, RevokeCertificateRequest request, CancellationToken ct = default);
}
