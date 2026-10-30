using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Recovery;

public sealed record SetRecoveryOutcomeRequest(RecoveryOutcome Outcome);

public sealed record DismantleDeviceRequest(string? Notes);

public sealed record RecordComponentRequest(ComponentType Type, Grade Grade, string? Notes);

public sealed record LogHazardousMaterialRequest(
    Guid? DeviceId,
    HazardousMaterial Material,
    string? Description,
    decimal WeightKg,
    string StorageLocation,
    bool IsSwollenBattery);

public sealed record ComponentDto(
    Guid Id,
    string ComponentCode,
    Guid DeviceId,
    string TrackingId,
    string Brand,
    ComponentType Type,
    Grade Grade,
    ComponentStatus Status,
    string? Notes,
    DateTime CreatedAt);

public sealed record HazardousMaterialLogDto(
    Guid Id,
    Guid? DeviceId,
    string? TrackingId,
    HazardousMaterial Material,
    string? Description,
    decimal WeightKg,
    string StorageLocation,
    bool IsSwollenBattery,
    string HandledByUserId,
    DateTime LoggedAt);

public sealed record HazardousSummaryDto(decimal TotalKg, decimal LithiumKg, decimal LeadKg, decimal OtherKg, int SwollenBatteries);

public sealed record ComponentQuery : PageQuery
{
    public ComponentType? Type { get; init; }
    public ComponentStatus? Status { get; init; }
}

public sealed class RecordComponentRequestValidator : AbstractValidator<RecordComponentRequest>
{
    public RecordComponentRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Grade).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class LogHazardousMaterialRequestValidator : AbstractValidator<LogHazardousMaterialRequest>
{
    public LogHazardousMaterialRequestValidator()
    {
        RuleFor(x => x.Material).IsInEnum();
        RuleFor(x => x.WeightKg).GreaterThan(0).LessThanOrEqualTo(500);
        RuleFor(x => x.StorageLocation).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Description).NotEmpty().When(x => x.Material == HazardousMaterial.Other);
        RuleFor(x => x.IsSwollenBattery).Equal(false).When(x => x.Material != HazardousMaterial.LithiumBattery)
            .WithMessage("Only lithium batteries can be flagged as swollen.");
    }
}

public sealed class SetRecoveryOutcomeRequestValidator : AbstractValidator<SetRecoveryOutcomeRequest>
{
    public SetRecoveryOutcomeRequestValidator() => RuleFor(x => x.Outcome).IsInEnum();
}

/// <summary>Application-layer rule: only staff with completed safety training may dismantle devices or handle batteries.</summary>
public interface ISafetyTrainingGuard
{
    Task EnsureCertifiedAsync(string userId, CancellationToken ct = default);
}

public sealed class SafetyTrainingGuard(IApplicationDbContext db) : ISafetyTrainingGuard
{
    public async Task EnsureCertifiedAsync(string userId, CancellationToken ct = default)
    {
        var certified = await db.TraineeEnrollments.AnyAsync(e => e.UserId == userId && e.SafetyTrainingCompleted, ct);
        if (!certified)
            throw new ForbiddenAccessException(
                "Completed safety training is required before dismantling devices or handling batteries and hazardous material.");
    }
}

public interface IRecoveryService
{
    Task<Devices.DeviceDto> SetOutcomeAsync(Guid deviceId, SetRecoveryOutcomeRequest request, CancellationToken ct = default);
    Task<Devices.DeviceDto> DismantleAsync(Guid deviceId, DismantleDeviceRequest request, CancellationToken ct = default);
    Task<ComponentDto> RecordComponentAsync(Guid deviceId, RecordComponentRequest request, CancellationToken ct = default);
    Task<PagedResult<ComponentDto>> ListComponentsAsync(ComponentQuery query, CancellationToken ct = default);
    Task<HazardousMaterialLogDto> LogHazardousMaterialAsync(LogHazardousMaterialRequest request, CancellationToken ct = default);
    Task<PagedResult<HazardousMaterialLogDto>> ListHazardousLogsAsync(PageQuery query, CancellationToken ct = default);
    Task<HazardousSummaryDto> GetHazardousSummaryAsync(CancellationToken ct = default);
}

public sealed class RecoveryService(
    IApplicationDbContext db,
    ISafetyTrainingGuard safety,
    ILedgerService ledger,
    ICurrentUser currentUser,
    TimeProvider time) : IRecoveryService
{
    public async Task<Devices.DeviceDto> SetOutcomeAsync(Guid deviceId, SetRecoveryOutcomeRequest request, CancellationToken ct = default)
    {
        var device = await LoadDeviceAsync(deviceId, ct);
        if (device.Status is DeviceStatus.Dismantled or DeviceStatus.Disposed or DeviceStatus.Listed or DeviceStatus.Sold)
            throw new DomainException($"Device {device.TrackingId} is {device.Status}; its recovery outcome can no longer change.");

        var wiped = device.Status is DeviceStatus.Sanitized or DeviceStatus.Refurbishing;
        var notEligible = await db.SanitizationRecords.AnyAsync(r => r.DeviceId == deviceId && r.Outcome == SanitizationOutcome.NotEligible, ct);

        switch (request.Outcome)
        {
            case RecoveryOutcome.Refurbish when device.Certificate is not { IsRevoked: false }:
                throw new DomainException("Only devices with a valid sanitization certificate can be refurbished for resale.");
            case RecoveryOutcome.DismantleForParts or RecoveryOutcome.HazardousDisposalOnly when !wiped && !notEligible:
                throw new DomainException("A device must be sanitized (or recorded as not eligible for wiping) before dismantling or disposal.");
        }

        device.RecoveryOutcome = request.Outcome;
        device.Status = request.Outcome == RecoveryOutcome.Refurbish ? DeviceStatus.Refurbishing : device.Status;
        await db.SaveChangesAsync(ct);
        return Devices.DeviceDto.From(device);
    }

    public async Task<Devices.DeviceDto> DismantleAsync(Guid deviceId, DismantleDeviceRequest request, CancellationToken ct = default)
    {
        await safety.EnsureCertifiedAsync(currentUser.Actor, ct);

        return await db.ExecuteInTransactionAsync(async t =>
        {
            var device = await LoadDeviceAsync(deviceId, t);
            if (device.RecoveryOutcome is not (RecoveryOutcome.DismantleForParts or RecoveryOutcome.HazardousDisposalOnly))
                throw new DomainException("Set the recovery outcome to DismantleForParts or HazardousDisposalOnly first.");
            if (device.Status is DeviceStatus.Dismantled or DeviceStatus.Disposed)
                throw new ConflictException($"Device {device.TrackingId} has already been dismantled.", "already_dismantled");

            device.DismantledAt = time.GetUtcNow().UtcDateTime;
            device.Status = device.RecoveryOutcome == RecoveryOutcome.HazardousDisposalOnly ? DeviceStatus.Disposed : DeviceStatus.Dismantled;

            await ledger.AppendAsync(LedgerEventType.Dismantled, LedgerEntityTypes.Device, device.Id, new
            {
                device.TrackingId,
                device.RecoveryOutcome,
                Notes = request.Notes
            }, device.TrackingId, t);

            return Devices.DeviceDto.From(device);
        }, ct);
    }

    public async Task<ComponentDto> RecordComponentAsync(Guid deviceId, RecordComponentRequest request, CancellationToken ct = default)
    {
        await safety.EnsureCertifiedAsync(currentUser.Actor, ct);

        return await db.ExecuteInTransactionAsync(async t =>
        {
            var device = await LoadDeviceAsync(deviceId, t);
            if (device.Status != DeviceStatus.Dismantled)
                throw new DomainException($"Device {device.TrackingId} must be dismantled before components are recovered.");

            var index = await db.RecoveredComponents.IgnoreQueryFilters().CountAsync(c => c.DeviceId == deviceId, t) + 1;
            var component = new RecoveredComponent
            {
                ComponentCode = CodeGenerator.ComponentCode(device.TrackingId, index),
                DeviceId = device.Id,
                Device = device,
                Type = request.Type,
                Grade = request.Grade,
                Notes = request.Notes,
                RecoveredByUserId = currentUser.Actor
            };
            db.RecoveredComponents.Add(component);

            await ledger.AppendAsync(LedgerEventType.ComponentRecovered, LedgerEntityTypes.Component, component.Id, new
            {
                component.ComponentCode,
                device.TrackingId,
                component.Type,
                component.Grade
            }, device.TrackingId, t);

            return ToDto(component);
        }, ct);
    }

    public Task<PagedResult<ComponentDto>> ListComponentsAsync(ComponentQuery query, CancellationToken ct = default)
    {
        var q = db.RecoveredComponents.AsNoTracking().Include(c => c.Device).AsQueryable();
        if (query.Type is { } type) q = q.Where(c => c.Type == type);
        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        return q.OrderByDescending(c => c.CreatedAt).ToPagedAsync(query, ToDto, ct);
    }

    public async Task<HazardousMaterialLogDto> LogHazardousMaterialAsync(LogHazardousMaterialRequest request, CancellationToken ct = default)
    {
        await safety.EnsureCertifiedAsync(currentUser.Actor, ct);

        return await db.ExecuteInTransactionAsync(async t =>
        {
            Device? device = null;
            if (request.DeviceId is { } deviceId)
            {
                device = await LoadDeviceAsync(deviceId, t);
                if (device.Status is not (DeviceStatus.Dismantled or DeviceStatus.Disposed))
                    throw new DomainException($"Device {device.TrackingId} must be dismantled before its materials are logged.");
            }

            var log = new HazardousMaterialLog
            {
                DeviceId = device?.Id,
                Device = device,
                Material = request.Material,
                Description = request.Description,
                WeightKg = Math.Round(request.WeightKg, 3),
                StorageLocation = request.StorageLocation,
                IsSwollenBattery = request.IsSwollenBattery,
                HandledByUserId = currentUser.Actor,
                LoggedAt = time.GetUtcNow().UtcDateTime
            };
            db.HazardousMaterialLogs.Add(log);

            await ledger.AppendAsync(LedgerEventType.MaterialDiverted, LedgerEntityTypes.HazardousMaterial, log.Id, new
            {
                device?.TrackingId,
                log.Material,
                log.WeightKg,
                log.StorageLocation,
                log.IsSwollenBattery
            }, device?.TrackingId, t);

            return ToDto(log);
        }, ct);
    }

    public Task<PagedResult<HazardousMaterialLogDto>> ListHazardousLogsAsync(PageQuery query, CancellationToken ct = default) =>
        db.HazardousMaterialLogs.AsNoTracking().Include(h => h.Device)
            .OrderByDescending(h => h.LoggedAt)
            .ToPagedAsync(query, ToDto, ct);

    public async Task<HazardousSummaryDto> GetHazardousSummaryAsync(CancellationToken ct = default)
    {
        var totals = await db.HazardousMaterialLogs.AsNoTracking()
            .GroupBy(h => h.Material)
            .Select(g => new { Material = g.Key, Kg = g.Sum(h => h.WeightKg), Swollen = g.Count(h => h.IsSwollenBattery) })
            .ToListAsync(ct);
        decimal Kg(HazardousMaterial m) => totals.Where(t => t.Material == m).Sum(t => t.Kg);
        return new HazardousSummaryDto(totals.Sum(t => t.Kg), Kg(HazardousMaterial.LithiumBattery), Kg(HazardousMaterial.Lead),
            Kg(HazardousMaterial.Other), totals.Sum(t => t.Swollen));
    }

    private async Task<Device> LoadDeviceAsync(Guid id, CancellationToken ct) =>
        await Devices.DeviceService.Include(db.Devices).FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException("Device", id);

    private static ComponentDto ToDto(RecoveredComponent c) => new(
        c.Id, c.ComponentCode, c.DeviceId, c.Device?.TrackingId ?? string.Empty, c.Device?.Brand ?? string.Empty,
        c.Type, c.Grade, c.Status, c.Notes, c.CreatedAt);

    private static HazardousMaterialLogDto ToDto(HazardousMaterialLog h) => new(
        h.Id, h.DeviceId, h.Device?.TrackingId, h.Material, h.Description, h.WeightKg, h.StorageLocation,
        h.IsSwollenBattery, h.HandledByUserId, h.LoggedAt);
}
