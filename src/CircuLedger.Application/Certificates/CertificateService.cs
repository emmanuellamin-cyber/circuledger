using System.Security.Cryptography;
using System.Text;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;
using CircuLedger.Domain.Ledger;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CircuLedger.Application.Certificates;

public sealed class RevokeCertificateRequestValidator : AbstractValidator<RevokeCertificateRequest>
{
    public RevokeCertificateRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

/// <summary>
/// The "Trust Engine". The certificate payload is canonical JSON, hashed with SHA-256 and signed with RSA.
/// Anyone can verify it with the published public key and the canonical payload.
/// </summary>
public sealed class CertificateService(
    IApplicationDbContext db,
    ICertificateSigner signer,
    ICertificateDocumentRenderer renderer,
    IUserDirectory users,
    ILedgerService ledger,
    IOptions<HubOptions> hubOptions,
    TimeProvider time) : ICertificateService
{
    private readonly HubOptions _hub = hubOptions.Value;

    public async Task<CertificateDto> IssueAsync(Guid sanitizationRecordId, CancellationToken ct = default) =>
        await db.ExecuteInTransactionAsync(async t =>
        {
            var record = await db.SanitizationRecords.Include(r => r.Device)
                             .FirstOrDefaultAsync(r => r.Id == sanitizationRecordId, t)
                         ?? throw new NotFoundException("SanitizationRecord", sanitizationRecordId);
            if (record.Outcome != SanitizationOutcome.Passed)
                throw new DomainException("Certificates are only issued for sanitization that passed.");
            var device = record.Device!;
            if (await db.Certificates.AnyAsync(c => c.DeviceId == device.Id, t))
                throw new ConflictException($"Device {device.TrackingId} already has a certificate.", "certificate_exists");

            var issuedAt = LedgerHasher.NormalizeTimestamp(time.GetUtcNow().UtcDateTime);
            var number = CodeGenerator.CertificateNumber(issuedAt.Year, await db.NextSequenceValueAsync(Sequences.Certificate, t));
            var verificationUrl = VerificationUrl(number);

            var payload = JsonCanonicalizer.Serialize(new
            {
                Type = "CertificateOfDataSanitization",
                Version = 1,
                CertificateNumber = number,
                Issuer = _hub.IssuerName,
                IssuedAt = issuedAt,
                Device = new { device.TrackingId, device.Brand, device.Model, device.ImeiLast4, device.IsSamsungFlagship },
                Sanitization = new
                {
                    RecordId = record.Id,
                    record.Method,
                    NistCategory = NistCategory(record.Method),
                    record.Passes,
                    record.StartedAt,
                    record.CompletedAt,
                    record.ToolName,
                    TechnicianRef = record.TechnicianUserId,
                    Outcome = record.Outcome
                },
                VerificationUrl = verificationUrl
            });

            var signature = signer.Sign(payload);
            var certificate = new Certificate
            {
                CertificateNumber = number,
                DeviceId = device.Id,
                Device = device,
                SanitizationRecordId = record.Id,
                IssuedAt = issuedAt,
                Payload = payload,
                PayloadHash = Sha256(payload),
                Signature = signature.Signature,
                SignatureAlgorithm = signature.Algorithm,
                SigningKeyId = signature.KeyId,
                VerificationUrl = verificationUrl
            };
            db.Certificates.Add(certificate);

            await ledger.AppendAsync(LedgerEventType.CertificateIssued, LedgerEntityTypes.Certificate, certificate.Id, new
            {
                certificate.CertificateNumber,
                device.TrackingId,
                certificate.PayloadHash,
                certificate.SignatureAlgorithm,
                certificate.SigningKeyId
            }, device.TrackingId, t);

            return ToDto(certificate);
        }, ct);

    public async Task<CertificateDto> GetAsync(string certificateNumber, CancellationToken ct = default) =>
        ToDto(await LoadAsync(certificateNumber, ct));

    public Task<PagedResult<CertificateDto>> ListAsync(PageQuery query, CancellationToken ct = default) =>
        db.Certificates.AsNoTracking().Include(c => c.Device)
            .OrderByDescending(c => c.IssuedAt)
            .ToPagedAsync(query, ToDto, ct);

    public async Task<CertificateFile> GetPdfAsync(string certificateNumber, CancellationToken ct = default)
    {
        var c = await LoadAsync(certificateNumber, ct);
        var record = c.SanitizationRecord!;
        var names = await users.GetDisplayNamesAsync([record.TechnicianUserId], ct);
        var model = new CertificateDocumentModel(
            c.CertificateNumber, c.IssuedAt, c.Device!.TrackingId, c.Device.Brand, c.Device.Model, c.Device.MaskedImei,
            Humanize(record.Method), record.Passes, record.StartedAt, record.CompletedAt,
            names.GetValueOrDefault(record.TechnicianUserId, "Certified technician"),
            c.PayloadHash, c.SignatureAlgorithm, c.SigningKeyId, c.VerificationUrl);
        return new CertificateFile($"{c.CertificateNumber}.pdf", "application/pdf", renderer.RenderPdf(model));
    }

    public async Task<CertificateFile> GetQrCodeAsync(string certificateNumber, CancellationToken ct = default)
    {
        var c = await LoadAsync(certificateNumber, ct);
        return new CertificateFile($"{c.CertificateNumber}.png", "image/png", renderer.RenderQrPng(c.VerificationUrl));
    }

    public async Task<PublicCertificateVerificationDto> VerifyPublicAsync(string certificateNumber, CancellationToken ct = default)
    {
        var number = certificateNumber.Trim().ToUpperInvariant();
        var c = await db.Certificates.AsNoTracking()
            .Include(x => x.Device)
            .Include(x => x.SanitizationRecord)
            .FirstOrDefaultAsync(x => x.CertificateNumber == number, ct);

        if (c is null)
            return new PublicCertificateVerificationDto(number, false, "NotFound", null, null, null, null, null, null, null,
                "No certificate with this number exists.");

        var hashOk = string.Equals(Sha256(JsonCanonicalizer.Canonicalize(c.Payload)), c.PayloadHash, StringComparison.Ordinal);
        var signatureOk = hashOk && signer.Verify(c.Payload, c.Signature, c.SigningKeyId);
        var (valid, status, reason) = (hashOk, signatureOk, c.IsRevoked) switch
        {
            (false, _, _) => (false, "Invalid", "Certificate contents do not match the recorded hash."),
            (_, false, _) => (false, "Invalid", "Digital signature could not be verified."),
            (_, _, true) => (false, "Revoked", c.RevocationReason),
            _ => (true, "Valid", (string?)null)
        };

        var device = c.Device!;
        return new PublicCertificateVerificationDto(
            c.CertificateNumber, valid, status, c.IssuedAt, _hub.IssuerName,
            $"{device.Brand} {device.Model}".Trim(), device.MaskedImei,
            c.SanitizationRecord?.Method, c.SanitizationRecord?.Passes, c.PayloadHash, reason);
    }

    public async Task<CertificateDto> RevokeAsync(string certificateNumber, RevokeCertificateRequest request, CancellationToken ct = default)
    {
        var c = await LoadAsync(certificateNumber, ct);
        if (c.IsRevoked) throw new ConflictException("Certificate is already revoked.", "already_revoked");
        c.IsRevoked = true;
        c.RevokedAt = time.GetUtcNow().UtcDateTime;
        c.RevocationReason = request.Reason;
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    private string VerificationUrl(string number) =>
        $"{_hub.PublicBaseUrl.TrimEnd('/')}/api/v1/public/certificates/{number}/verify";

    private async Task<Certificate> LoadAsync(string number, CancellationToken ct) =>
        await db.Certificates
            .Include(c => c.Device)
            .Include(c => c.SanitizationRecord)
            .FirstOrDefaultAsync(c => c.CertificateNumber == number.Trim().ToUpper(), ct)
        ?? throw new NotFoundException("Certificate", number);

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Maps to NIST SP 800-88 Rev. 1 sanitization categories.</summary>
    private static string NistCategory(SanitizationMethod method) => method switch
    {
        SanitizationMethod.FactoryResetOverwrite => "Clear",
        SanitizationMethod.SamsungKnoxGradeErase or SanitizationMethod.CryptographicErase => "Purge",
        SanitizationMethod.StoragePhysicalDestruction => "Destroy",
        _ => "Unspecified"
    };

    private static string Humanize(SanitizationMethod method) => method switch
    {
        SanitizationMethod.FactoryResetOverwrite => "Factory reset + overwrite",
        SanitizationMethod.SamsungKnoxGradeErase => "Samsung Knox-grade erase",
        SanitizationMethod.CryptographicErase => "Cryptographic erase",
        SanitizationMethod.StoragePhysicalDestruction => "Physical destruction of storage",
        _ => method.ToString()
    };

    private static CertificateDto ToDto(Certificate c) => new(
        c.Id, c.CertificateNumber, c.DeviceId, c.Device?.TrackingId ?? string.Empty, c.IssuedAt, c.PayloadHash,
        c.SignatureAlgorithm, c.SigningKeyId, c.VerificationUrl, c.IsRevoked, c.RevokedAt, c.RevocationReason);
}
