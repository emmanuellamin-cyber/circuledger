using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Devices;
using CircuLedger.Application.Ledger;
using CircuLedger.Application.Purchasing;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Intake;

public sealed class IntakeService(
    IApplicationDbContext db,
    ICitizenService citizens,
    IPricingService pricing,
    IGeocoder geocoder,
    IPersonalDataProtector protector,
    ILedgerService ledger,
    CitizenNotifier notifier,
    TimeProvider time) : IIntakeService
{
    public async Task<IntakeRequestDto> CreateAsync(CreateIntakeRequest request, string? ussdSessionId = null, CancellationToken ct = default)
    {
        var intake = await db.ExecuteInTransactionAsync(t => CreateEntityAsync(request, ussdSessionId, t), ct);

        var handover = intake.FulfilmentMethod == FulfilmentMethod.DropOff
            ? $"Bring your phone to {intake.DropOffPoint?.Name}."
            : "A field agent will contact you to arrange pickup.";
        var offer = intake.EstimatedOfferSle is { } price ? $" Est. offer SLE {price:N0}." : string.Empty;
        await notifier.NotifyAsync(intake.Citizen!, $"CircuHub: request {intake.ReferenceCode} received. {handover}{offer} Your data will be securely wiped and certified.", ct);

        return IntakeRequestDto.From(intake);
    }

    public async Task<IntakeRequestDto> GetAsync(Guid id, CancellationToken ct = default) =>
        IntakeRequestDto.From(await LoadAsync(id, ct));

    public async Task<IntakeRequestDto?> FindByReferenceAsync(string referenceCode, CancellationToken ct = default)
    {
        var code = referenceCode.Trim().ToUpperInvariant();
        var r = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.ReferenceCode == code, ct);
        return r is null ? null : IntakeRequestDto.From(r);
    }

    public async Task<IntakeRequestDto?> FindByUssdSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var r = await Query().AsNoTracking().FirstOrDefaultAsync(x => x.UssdSessionId == sessionId, ct);
        return r is null ? null : IntakeRequestDto.From(r);
    }

    public Task<PagedResult<IntakeRequestDto>> ListAsync(IntakeListQuery query, CancellationToken ct = default)
    {
        var q = Query().AsNoTracking();
        if (query.Status is { } status) q = q.Where(r => r.Status == status);
        if (query.FulfilmentMethod is { } method) q = q.Where(r => r.FulfilmentMethod == method);
        if (query.Channel is { } channel) q = q.Where(r => r.Channel == channel);
        if (!string.IsNullOrWhiteSpace(query.ReferenceCode))
            q = q.Where(r => r.ReferenceCode == query.ReferenceCode.Trim().ToUpper());
        return q.OrderByDescending(r => r.CreatedAt).ToPagedAsync(query, IntakeRequestDto.From, ct);
    }

    public async Task<IReadOnlyList<IntakeRequestDto>> ListForCitizenUserAsync(string userId, CancellationToken ct = default)
    {
        var requests = await Query().AsNoTracking()
            .Where(r => r.Citizen!.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return requests.Select(IntakeRequestDto.From).ToList();
    }

    public async Task<IntakeRequestDto> SetPickupLocationAsync(Guid id, SetPickupLocationRequest request, CancellationToken ct = default)
    {
        var intake = await LoadAsync(id, ct);
        if (intake.FulfilmentMethod != FulfilmentMethod.Pickup || intake.Status != IntakeStatus.Pending)
            throw new DomainException("Only pending pickup requests can be relocated.");
        intake.PickupLatitude = request.Latitude;
        intake.PickupLongitude = request.Longitude;
        if (!string.IsNullOrWhiteSpace(request.PickupArea)) intake.PickupArea = request.PickupArea.Trim();
        await db.SaveChangesAsync(ct);
        return IntakeRequestDto.From(intake);
    }

    public async Task<IntakeRequestDto> CancelAsync(Guid id, CloseIntakeRequest request, CancellationToken ct = default)
    {
        var intake = await LoadAsync(id, ct);
        intake.Cancel(request.Reason);
        await db.SaveChangesAsync(ct);
        return IntakeRequestDto.From(intake);
    }

    public async Task<IntakeRequestDto> RejectAsync(Guid id, CloseIntakeRequest request, CancellationToken ct = default)
    {
        var intake = await LoadAsync(id, ct);
        intake.Reject(request.Reason);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(intake.Citizen!, $"CircuHub: request {intake.ReferenceCode} could not be accepted. Reason: {request.Reason}", ct);
        return IntakeRequestDto.From(intake);
    }

    public async Task<DeviceDto> ReceiveAsync(Guid id, ReceiveIntakeRequest request, CancellationToken ct = default)
    {
        var device = await db.ExecuteInTransactionAsync(async t =>
        {
            var intake = await LoadAsync(id, t);
            var point = await GetActivePointAsync(request.DropOffPointId, t);
            intake.MarkReceived(point.Id, time.GetUtcNow().UtcDateTime);
            return await CreateDeviceAsync(intake, point, request.Imei, request.Brand, request.Model, request.AssessedCondition, t);
        }, ct);

        await NotifyReceivedAsync(device, ct);
        return DeviceDto.From(device);
    }

    public async Task<DeviceDto> RegisterWalkInAsync(WalkInIntakeRequest request, CancellationToken ct = default)
    {
        var device = await db.ExecuteInTransactionAsync(async t =>
        {
            var intake = await CreateEntityAsync(new CreateIntakeRequest(
                request.Phone, request.Brand, request.Model, request.Condition, FulfilmentMethod.DropOff,
                request.DropOffPointId, null, null, null, IntakeChannel.DropOff), null, t);
            var point = intake.DropOffPoint!;
            intake.MarkReceived(point.Id, time.GetUtcNow().UtcDateTime);
            return await CreateDeviceAsync(intake, point, request.Imei, null, null, null, t);
        }, ct);

        await NotifyReceivedAsync(device, ct);
        return DeviceDto.From(device);
    }

    private async Task<IntakeRequest> CreateEntityAsync(CreateIntakeRequest request, string? ussdSessionId, CancellationToken ct)
    {
        var citizen = await citizens.GetOrCreateAsync(request.Phone, ct);

        DropOffPoint? point = null;
        if (request.FulfilmentMethod == FulfilmentMethod.DropOff)
            point = await GetActivePointAsync(request.DropOffPointId ?? Guid.Empty, ct);

        var (lat, lng) = (request.PickupLatitude, request.PickupLongitude);
        if (request.FulfilmentMethod == FulfilmentMethod.Pickup && lat is null && geocoder.TryGeocode(request.PickupArea) is { } located)
            (lat, lng) = (located.Latitude, located.Longitude);

        var quote = await pricing.QuoteAsync(request.Brand, request.Condition, ct);

        var intake = new IntakeRequest
        {
            ReferenceCode = await NewReferenceCodeAsync(ct),
            Citizen = citizen,
            CitizenId = citizen.Id,
            Brand = request.Brand.Trim(),
            Model = request.Model?.Trim(),
            ReportedCondition = request.Condition,
            Channel = request.Channel,
            FulfilmentMethod = request.FulfilmentMethod,
            DropOffPoint = point,
            DropOffPointId = point?.Id,
            PickupArea = request.FulfilmentMethod == FulfilmentMethod.Pickup ? request.PickupArea?.Trim() : null,
            PickupLatitude = request.FulfilmentMethod == FulfilmentMethod.Pickup ? lat : null,
            PickupLongitude = request.FulfilmentMethod == FulfilmentMethod.Pickup ? lng : null,
            EstimatedOfferSle = quote.PriceSle,
            UssdSessionId = ussdSessionId
        };
        db.IntakeRequests.Add(intake);

        await ledger.AppendAsync(LedgerEventType.IntakeCreated, LedgerEntityTypes.IntakeRequest, intake.Id, new
        {
            intake.ReferenceCode,
            intake.Brand,
            Condition = intake.ReportedCondition,
            intake.Channel,
            intake.FulfilmentMethod,
            DropOffPoint = point?.Name,
            HasPickupCoordinates = intake.HasPickupCoordinates
        }, cancellationToken: ct);

        return intake;
    }

    private async Task<Device> CreateDeviceAsync(IntakeRequest intake, DropOffPoint point, string? rawImei, string? brand,
        string? model, DeviceCondition? assessedCondition, CancellationToken ct)
    {
        string? imeiHash = null, imeiLast4 = null;
        if (Imei.Normalize(rawImei) is { } imei)
        {
            imeiHash = protector.HashImei(imei);
            imeiLast4 = IPersonalDataProtector.Last4(imei);
            var duplicate = await db.Devices.AnyAsync(
                d => d.ImeiHash == imeiHash && d.Status != DeviceStatus.Sold && d.Status != DeviceStatus.Disposed, ct);
            if (duplicate)
                throw new ConflictException("A device with this IMEI is already being processed.", "duplicate_imei");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var finalBrand = string.IsNullOrWhiteSpace(brand) ? intake.Brand : brand.Trim();
        var device = new Device
        {
            TrackingId = CodeGenerator.TrackingId(now.Year, await db.NextSequenceValueAsync(Sequences.DeviceTracking, ct)),
            Brand = finalBrand,
            Model = string.IsNullOrWhiteSpace(model) ? intake.Model : model.Trim(),
            ImeiHash = imeiHash,
            ImeiLast4 = imeiLast4,
            Condition = assessedCondition ?? intake.ReportedCondition,
            IsSamsungFlagship = Device.IsSamsung(finalBrand),
            IntakeChannel = intake.Channel,
            CitizenId = intake.CitizenId,
            Citizen = intake.Citizen,
            IntakeRequestId = intake.Id,
            IntakeRequest = intake,
            LocationId = point.Id,
            Location = point
        };
        db.Devices.Add(device);

        await ledger.AppendAsync(LedgerEventType.DeviceReceived, LedgerEntityTypes.Device, device.Id, new
        {
            device.TrackingId,
            IntakeReference = intake.ReferenceCode,
            device.Brand,
            device.Model,
            device.Condition,
            device.IsSamsungFlagship,
            ImeiLast4 = device.ImeiLast4,
            Location = point.Name
        }, device.TrackingId, ct);

        return device;
    }

    private Task NotifyReceivedAsync(Device device, CancellationToken ct) =>
        notifier.NotifyAsync(device.Citizen!,
            $"CircuHub: your phone (ref {device.IntakeRequest!.ReferenceCode}) was received at {device.Location!.Name}. Tracking ID {device.TrackingId}.", ct);

    private async Task<DropOffPoint> GetActivePointAsync(Guid id, CancellationToken ct) =>
        await db.DropOffPoints.FirstOrDefaultAsync(p => p.Id == id && p.IsActive, ct)
        ?? throw new NotFoundException("DropOffPoint", id);

    private async Task<string> NewReferenceCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = CodeGenerator.IntakeReference();
            if (!await db.IntakeRequests.IgnoreQueryFilters().AnyAsync(r => r.ReferenceCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not allocate a unique intake reference code.");
    }

    private IQueryable<IntakeRequest> Query() => db.IntakeRequests
        .Include(r => r.Citizen)
        .Include(r => r.DropOffPoint)
        .Include(r => r.Device);

    private async Task<IntakeRequest> LoadAsync(Guid id, CancellationToken ct) =>
        await Query().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("IntakeRequest", id);
}
