using CircuLedger.Application.Certificates;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Sanitization;

public sealed record RecordSanitizationRequest(
    SanitizationMethod Method,
    DateTime StartedAt,
    DateTime CompletedAt,
    int Passes,
    SanitizationOutcome Outcome,
    string? ToolName,
    string? Notes);

public sealed record SanitizationRecordDto(
    Guid Id,
    Guid DeviceId,
    string TrackingId,
    SanitizationMethod Method,
    string TechnicianUserId,
    DateTime StartedAt,
    DateTime CompletedAt,
    int Passes,
    SanitizationOutcome Outcome,
    string? ToolName,
    string? Notes,
    CertificateDto? Certificate);

public sealed class RecordSanitizationRequestValidator : AbstractValidator<RecordSanitizationRequest>
{
    public RecordSanitizationRequestValidator(TimeProvider time)
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Outcome).IsInEnum();
        RuleFor(x => x.Passes).InclusiveBetween(1, 35);
        RuleFor(x => x.StartedAt).LessThanOrEqualTo(x => x.CompletedAt).WithMessage("StartedAt must be before CompletedAt.");
        RuleFor(x => x.CompletedAt).LessThanOrEqualTo(_ => time.GetUtcNow().UtcDateTime.AddMinutes(5))
            .WithMessage("CompletedAt cannot be in the future.");
        RuleFor(x => x.ToolName).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Outcome != SanitizationOutcome.Passed)
            .WithMessage("Explain why sanitization failed or the device is not eligible.");
    }
}

public interface ISanitizationService
{
    Task<SanitizationRecordDto> RecordAsync(Guid deviceId, RecordSanitizationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SanitizationRecordDto>> ListForDeviceAsync(Guid deviceId, CancellationToken ct = default);
}

public sealed class SanitizationService(
    IApplicationDbContext db,
    ICertificateService certificates,
    ILedgerService ledger,
    ICurrentUser currentUser,
    CitizenNotifier notifier) : ISanitizationService
{
    public async Task<SanitizationRecordDto> RecordAsync(Guid deviceId, RecordSanitizationRequest request, CancellationToken ct = default)
    {
        var (record, device, certificate) = await db.ExecuteInTransactionAsync(async t =>
        {
            var device = await db.Devices.Include(d => d.Citizen).FirstOrDefaultAsync(d => d.Id == deviceId, t)
                         ?? throw new NotFoundException("Device", deviceId);
            if (device.Status is not (DeviceStatus.Purchased or DeviceStatus.SanitizationFailed))
                throw new DomainException($"Device {device.TrackingId} is {device.Status}; it must be purchased (or have a failed wipe) before sanitization.");
            if (request.Method == SanitizationMethod.SamsungKnoxGradeErase && !device.IsSamsungFlagship)
                throw new DomainException("Samsung Knox-grade erase is only available for Samsung devices.");

            var record = new SanitizationRecord
            {
                DeviceId = device.Id,
                Device = device,
                Method = request.Method,
                TechnicianUserId = currentUser.Actor,
                StartedAt = request.StartedAt.ToUniversalTime(),
                CompletedAt = request.CompletedAt.ToUniversalTime(),
                Passes = request.Passes,
                Outcome = request.Outcome,
                ToolName = request.ToolName,
                Notes = request.Notes
            };
            db.SanitizationRecords.Add(record);

            device.Status = request.Outcome switch
            {
                SanitizationOutcome.Passed => DeviceStatus.Sanitized,
                SanitizationOutcome.Failed => DeviceStatus.SanitizationFailed,
                _ => device.Status // NotEligible: stays Purchased and can only be dismantled or disposed of.
            };

            await ledger.AppendAsync(LedgerEventType.Sanitized, LedgerEntityTypes.SanitizationRecord, record.Id, new
            {
                device.TrackingId,
                record.Method,
                record.Passes,
                record.Outcome,
                record.StartedAt,
                record.CompletedAt,
                record.ToolName
            }, device.TrackingId, t);

            CertificateDto? certificate = null;
            if (record.Outcome == SanitizationOutcome.Passed)
                certificate = await certificates.IssueAsync(record.Id, t);

            return (record, device, certificate);
        }, ct);

        if (certificate is not null)
        {
            await notifier.NotifyAsync(device.Citizen!,
                $"CircuHub: data on your phone {device.TrackingId} was securely wiped. Certificate {certificate.CertificateNumber}. Verify: {certificate.VerificationUrl}", ct);
        }

        return ToDto(record, device.TrackingId, certificate);
    }

    public async Task<IReadOnlyList<SanitizationRecordDto>> ListForDeviceAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await db.Devices.AsNoTracking().Include(d => d.Certificate).FirstOrDefaultAsync(d => d.Id == deviceId, ct)
                     ?? throw new NotFoundException("Device", deviceId);
        var records = await db.SanitizationRecords.AsNoTracking()
            .Where(r => r.DeviceId == deviceId).OrderBy(r => r.StartedAt).ToListAsync(ct);

        var certificate = device.Certificate is null ? null : await certificates.GetAsync(device.Certificate.CertificateNumber, ct);
        return records
            .Select(r => ToDto(r, device.TrackingId, device.Certificate?.SanitizationRecordId == r.Id ? certificate : null))
            .ToList();
    }

    private static SanitizationRecordDto ToDto(SanitizationRecord r, string trackingId, CertificateDto? certificate) => new(
        r.Id, r.DeviceId, trackingId, r.Method, r.TechnicianUserId, r.StartedAt, r.CompletedAt, r.Passes, r.Outcome,
        r.ToolName, r.Notes, certificate);
}
