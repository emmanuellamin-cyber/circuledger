using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Devices;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;

namespace CircuLedger.Application.Intake;

public sealed record CreateIntakeRequest(
    string Phone,
    string Brand,
    string? Model,
    DeviceCondition Condition,
    FulfilmentMethod FulfilmentMethod,
    Guid? DropOffPointId,
    string? PickupArea,
    double? PickupLatitude,
    double? PickupLongitude,
    IntakeChannel Channel = IntakeChannel.Web);

public sealed record IntakeRequestDto(
    Guid Id,
    string ReferenceCode,
    string CitizenPhoneMasked,
    string Brand,
    string? Model,
    DeviceCondition ReportedCondition,
    IntakeChannel Channel,
    FulfilmentMethod FulfilmentMethod,
    Guid? DropOffPointId,
    string? DropOffPointName,
    string? PickupArea,
    double? PickupLatitude,
    double? PickupLongitude,
    decimal? EstimatedOfferSle,
    IntakeStatus Status,
    Guid? DispatchBatchId,
    DateTime CreatedAt,
    DateTime? ScheduledAt,
    DateTime? CollectedAt,
    DateTime? ReceivedAt,
    string? ClosedReason,
    string? DeviceTrackingId)
{
    public static IntakeRequestDto From(IntakeRequest r) => new(
        r.Id, r.ReferenceCode, r.Citizen?.MaskedPhone ?? "hidden", r.Brand, r.Model, r.ReportedCondition, r.Channel,
        r.FulfilmentMethod, r.DropOffPointId, r.DropOffPoint?.Name, r.PickupArea, r.PickupLatitude, r.PickupLongitude,
        r.EstimatedOfferSle, r.Status, r.DispatchBatchId, r.CreatedAt, r.ScheduledAt, r.CollectedAt, r.ReceivedAt,
        r.ClosedReason, r.Device?.TrackingId);
}

public sealed record IntakeListQuery : PageQuery
{
    public IntakeStatus? Status { get; init; }
    public FulfilmentMethod? FulfilmentMethod { get; init; }
    public IntakeChannel? Channel { get; init; }
    public string? ReferenceCode { get; init; }
}

public sealed record SetPickupLocationRequest(double Latitude, double Longitude, string? PickupArea);

public sealed record CloseIntakeRequest(string Reason);

/// <summary>Staff receive the phone at a hub. This creates the Device (tracking ID) for the request.</summary>
public sealed record ReceiveIntakeRequest(
    Guid DropOffPointId,
    string? Imei,
    string? Brand,
    string? Model,
    DeviceCondition? AssessedCondition);

/// <summary>A citizen walks in with a phone and no prior USSD request.</summary>
public sealed record WalkInIntakeRequest(
    string Phone,
    string Brand,
    string? Model,
    string? Imei,
    DeviceCondition Condition,
    Guid DropOffPointId);

public interface IIntakeService
{
    Task<IntakeRequestDto> CreateAsync(CreateIntakeRequest request, string? ussdSessionId = null, CancellationToken ct = default);
    Task<IntakeRequestDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<IntakeRequestDto?> FindByReferenceAsync(string referenceCode, CancellationToken ct = default);
    Task<IntakeRequestDto?> FindByUssdSessionAsync(string sessionId, CancellationToken ct = default);
    Task<PagedResult<IntakeRequestDto>> ListAsync(IntakeListQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<IntakeRequestDto>> ListForCitizenUserAsync(string userId, CancellationToken ct = default);
    Task<IntakeRequestDto> SetPickupLocationAsync(Guid id, SetPickupLocationRequest request, CancellationToken ct = default);
    Task<IntakeRequestDto> CancelAsync(Guid id, CloseIntakeRequest request, CancellationToken ct = default);
    Task<IntakeRequestDto> RejectAsync(Guid id, CloseIntakeRequest request, CancellationToken ct = default);
    Task<DeviceDto> ReceiveAsync(Guid id, ReceiveIntakeRequest request, CancellationToken ct = default);
    Task<DeviceDto> RegisterWalkInAsync(WalkInIntakeRequest request, CancellationToken ct = default);
}

public sealed class CreateIntakeRequestValidator : AbstractValidator<CreateIntakeRequest>
{
    public CreateIntakeRequestValidator(IPersonalDataProtector protector)
    {
        RuleFor(x => x.Phone).NotEmpty().Must(p => protector.NormalizePhone(p) is not null)
            .WithMessage("Enter a valid Sierra Leone phone number.");
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Model).MaximumLength(80);
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.FulfilmentMethod).IsInEnum();
        RuleFor(x => x.DropOffPointId).NotNull().When(x => x.FulfilmentMethod == FulfilmentMethod.DropOff);
        RuleFor(x => x.PickupArea).NotEmpty().MaximumLength(160).When(x => x.FulfilmentMethod == FulfilmentMethod.Pickup);
        RuleFor(x => x.PickupLatitude).InclusiveBetween(6.8, 10.1).When(x => x.PickupLatitude.HasValue);
        RuleFor(x => x.PickupLongitude).InclusiveBetween(-13.4, -10.2).When(x => x.PickupLongitude.HasValue);
        RuleFor(x => x).Must(x => x.PickupLatitude.HasValue == x.PickupLongitude.HasValue)
            .WithMessage("Provide both latitude and longitude, or neither.");
    }
}

public sealed class SetPickupLocationRequestValidator : AbstractValidator<SetPickupLocationRequest>
{
    public SetPickupLocationRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(6.8, 10.1);
        RuleFor(x => x.Longitude).InclusiveBetween(-13.4, -10.2);
        RuleFor(x => x.PickupArea).MaximumLength(160);
    }
}

public sealed class CloseIntakeRequestValidator : AbstractValidator<CloseIntakeRequest>
{
    public CloseIntakeRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public sealed class ReceiveIntakeRequestValidator : AbstractValidator<ReceiveIntakeRequest>
{
    public ReceiveIntakeRequestValidator()
    {
        RuleFor(x => x.DropOffPointId).NotEmpty();
        RuleFor(x => x.Imei).Must(i => Imei.Normalize(i) is not null).When(x => !string.IsNullOrWhiteSpace(x.Imei))
            .WithMessage("IMEI must be 15 digits with a valid check digit.");
        RuleFor(x => x.Brand).MaximumLength(50);
        RuleFor(x => x.Model).MaximumLength(80);
        RuleFor(x => x.AssessedCondition).IsInEnum().When(x => x.AssessedCondition.HasValue);
    }
}

public sealed class WalkInIntakeRequestValidator : AbstractValidator<WalkInIntakeRequest>
{
    public WalkInIntakeRequestValidator(IPersonalDataProtector protector)
    {
        RuleFor(x => x.Phone).NotEmpty().Must(p => protector.NormalizePhone(p) is not null)
            .WithMessage("Enter a valid Sierra Leone phone number.");
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Model).MaximumLength(80);
        RuleFor(x => x.Imei).Must(i => Imei.Normalize(i) is not null).When(x => !string.IsNullOrWhiteSpace(x.Imei))
            .WithMessage("IMEI must be 15 digits with a valid check digit.");
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.DropOffPointId).NotEmpty();
    }
}
