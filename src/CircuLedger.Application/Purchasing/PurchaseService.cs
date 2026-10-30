using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Devices;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Purchasing;

public sealed record PurchaseDeviceRequest(PayoutMethod PayoutMethod, decimal? AgreedPriceSle = null, string? PayoutReference = null);

public sealed record UpdatePayoutRequest(PayoutStatus Status, string? PayoutReference);

public sealed record PurchaseDto(
    Guid Id,
    Guid DeviceId,
    string TrackingId,
    decimal OfferedPriceSle,
    decimal PaidPriceSle,
    PayoutMethod PayoutMethod,
    PayoutStatus PayoutStatus,
    string? PayoutReference,
    DateTime? PayoutCompletedAt,
    DateTime CreatedAt);

public sealed class PurchaseDeviceRequestValidator : AbstractValidator<PurchaseDeviceRequest>
{
    public PurchaseDeviceRequestValidator()
    {
        RuleFor(x => x.PayoutMethod).IsInEnum();
        RuleFor(x => x.AgreedPriceSle).GreaterThan(0).LessThan(100_000).When(x => x.AgreedPriceSle.HasValue);
        RuleFor(x => x.PayoutReference).MaximumLength(100);
    }
}

public sealed class UpdatePayoutRequestValidator : AbstractValidator<UpdatePayoutRequest>
{
    public UpdatePayoutRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.PayoutReference).MaximumLength(100);
        RuleFor(x => x.PayoutReference).NotEmpty()
            .When(x => x.Status == PayoutStatus.Paid)
            .WithMessage("A mobile money / cash receipt reference is required when marking a payout as paid.");
    }
}

public interface IPurchaseService
{
    Task<PurchaseDto> PurchaseAsync(Guid deviceId, PurchaseDeviceRequest request, CancellationToken ct = default);
    Task<PurchaseDto> UpdatePayoutAsync(Guid deviceId, UpdatePayoutRequest request, CancellationToken ct = default);
    Task<PurchaseDto> GetAsync(Guid deviceId, CancellationToken ct = default);
}

public sealed class PurchaseService(
    IApplicationDbContext db,
    IPricingService pricing,
    ILedgerService ledger,
    ICurrentUser currentUser,
    CitizenNotifier notifier,
    TimeProvider time) : IPurchaseService
{
    public async Task<PurchaseDto> PurchaseAsync(Guid deviceId, PurchaseDeviceRequest request, CancellationToken ct = default)
    {
        var (purchase, device) = await db.ExecuteInTransactionAsync(async t =>
        {
            var device = await db.Devices.Include(d => d.Purchase).Include(d => d.Citizen)
                             .FirstOrDefaultAsync(d => d.Id == deviceId, t)
                         ?? throw new NotFoundException("Device", deviceId);
            if (device.Purchase is not null)
                throw new ConflictException($"Device {device.TrackingId} has already been purchased.", "already_purchased");
            if (device.Status != DeviceStatus.Received)
                throw new DomainException($"Device {device.TrackingId} is {device.Status}; only received devices can be purchased.");

            var quote = await pricing.QuoteAsync(device.Brand, device.Condition, t);
            var price = request.AgreedPriceSle ?? quote.PriceSle
                        ?? throw new DomainException($"No price is configured for {device.Brand} / {device.Condition}.", "no_price");

            // Lock the seed fund row (SELECT ... FOR UPDATE). Fund.Debit throws InsufficientFundsException if it's too low.
            var now = time.GetUtcNow().UtcDateTime;
            var seedFund = await db.GetFundForUpdateAsync(FundType.SeedFund, t);
            var debit = seedFund.Debit(price, FundTransactionReason.DevicePurchase, LedgerEntityTypes.Device, device.Id.ToString(), now,
                $"Purchase of {device.TrackingId}");
            db.FundTransactions.Add(debit);

            var purchase = new Purchase
            {
                DeviceId = device.Id,
                Device = device,
                OfferedPriceSle = quote.PriceSle ?? price,
                PaidPriceSle = price,
                PayoutMethod = request.PayoutMethod,
                PayoutReference = request.PayoutReference,
                PurchasedByUserId = currentUser.Actor,
                SeedFundTransactionId = debit.Id
            };
            db.Purchases.Add(purchase);
            device.Status = DeviceStatus.Purchased;

            await ledger.AppendAsync(LedgerEventType.Purchased, LedgerEntityTypes.Purchase, purchase.Id, new
            {
                device.TrackingId,
                purchase.OfferedPriceSle,
                purchase.PaidPriceSle,
                purchase.PayoutMethod,
                SeedFundBalanceAfterSle = seedFund.BalanceSle,
                Currency = "SLE"
            }, device.TrackingId, t);

            return (purchase, device);
        }, ct);

        await notifier.NotifyAsync(device.Citizen!,
            $"CircuHub: we bought your phone {device.TrackingId} for SLE {purchase.PaidPriceSle:N0} via {purchase.PayoutMethod}. Thank you for recycling!", ct);
        return ToDto(purchase, device.TrackingId);
    }

    public async Task<PurchaseDto> UpdatePayoutAsync(Guid deviceId, UpdatePayoutRequest request, CancellationToken ct = default)
    {
        var purchase = await db.Purchases.Include(p => p.Device).FirstOrDefaultAsync(p => p.DeviceId == deviceId, ct)
                       ?? throw new NotFoundException("Purchase for device", deviceId);
        if (purchase.PayoutStatus == PayoutStatus.Paid)
            throw new ConflictException("Payout is already completed.", "payout_completed");

        purchase.PayoutStatus = request.Status;
        purchase.PayoutReference = request.PayoutReference ?? purchase.PayoutReference;
        if (request.Status == PayoutStatus.Paid) purchase.PayoutCompletedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(purchase, purchase.Device!.TrackingId);
    }

    public async Task<PurchaseDto> GetAsync(Guid deviceId, CancellationToken ct = default)
    {
        var purchase = await db.Purchases.AsNoTracking().Include(p => p.Device).FirstOrDefaultAsync(p => p.DeviceId == deviceId, ct)
                       ?? throw new NotFoundException("Purchase for device", deviceId);
        return ToDto(purchase, purchase.Device!.TrackingId);
    }

    private static PurchaseDto ToDto(Purchase p, string trackingId) => new(
        p.Id, p.DeviceId, trackingId, p.OfferedPriceSle, p.PaidPriceSle, p.PayoutMethod, p.PayoutStatus,
        p.PayoutReference, p.PayoutCompletedAt, p.CreatedAt);
}
