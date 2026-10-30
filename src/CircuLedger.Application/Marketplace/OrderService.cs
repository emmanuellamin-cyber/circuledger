using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Funds;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Marketplace;

public sealed record PlaceOrderRequest(IReadOnlyList<Guid> ListingIds);

public sealed record MarkOrderPaidRequest(string PaymentMethod, string? PaymentReference);

public sealed record CancelOrderRequest(string Reason);

public sealed record OrderItemDto(Guid ListingId, string ListingCode, string Title, decimal PriceSle, string? CertificateNumber);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    string BuyerUserId,
    OrderStatus Status,
    decimal TotalSle,
    string? PaymentMethod,
    string? PaymentReference,
    DateTime CreatedAt,
    DateTime? PaidAt,
    DateTime? FulfilledAt,
    DateTime? CancelledAt,
    string? CancellationReason,
    IReadOnlyList<OrderItemDto> Items,
    ReinvestmentSummaryDto? Reinvestment);

public sealed record OrderQuery : PageQuery
{
    public OrderStatus? Status { get; init; }
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.ListingIds).NotEmpty().Must(ids => ids.Count <= 20).WithMessage("An order can contain at most 20 items.");
        RuleFor(x => x.ListingIds).Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each listing can only appear once.");
    }
}

public sealed class MarkOrderPaidRequestValidator : AbstractValidator<MarkOrderPaidRequest>
{
    public MarkOrderPaidRequestValidator()
    {
        RuleFor(x => x.PaymentMethod).NotEmpty().MaximumLength(40);
        RuleFor(x => x.PaymentReference).MaximumLength(100);
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public interface IOrderService
{
    Task<OrderDto> PlaceAsync(PlaceOrderRequest request, CancellationToken ct = default);
    Task<OrderDto> MarkPaidAsync(Guid orderId, MarkOrderPaidRequest request, CancellationToken ct = default);
    Task<OrderDto> FulfilAsync(Guid orderId, CancellationToken ct = default);
    Task<OrderDto> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken ct = default);
    Task<OrderDto> GetAsync(Guid orderId, CancellationToken ct = default);
    Task<PagedResult<OrderDto>> ListAsync(OrderQuery query, bool mineOnly, CancellationToken ct = default);
}

public sealed class OrderService(
    IApplicationDbContext db,
    IReinvestmentService reinvestment,
    ILedgerService ledger,
    ICurrentUser currentUser,
    TimeProvider time) : IOrderService
{
    public Task<OrderDto> PlaceAsync(PlaceOrderRequest request, CancellationToken ct = default) =>
        db.ExecuteInTransactionAsync(async t =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            var ids = request.ListingIds.Distinct().ToList();

            // Atomic reservation: only listings that are still Published flip to Reserved, so two buyers can't both win.
            var reserved = await db.Listings
                .Where(l => ids.Contains(l.Id) && l.Status == ListingStatus.Published)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.Status, ListingStatus.Reserved)
                    .SetProperty(l => l.UpdatedAt, now)
                    .SetProperty(l => l.UpdatedBy, currentUser.Actor), t);
            if (reserved != ids.Count)
                throw new ConflictException("One or more listings are no longer available.", "listing_unavailable");

            var listings = await db.Listings.AsNoTracking().Where(l => ids.Contains(l.Id)).ToListAsync(t);
            var order = new Order
            {
                OrderNumber = CodeGenerator.OrderNumber(now.Year, await db.NextSequenceValueAsync(Sequences.Order, t)),
                BuyerUserId = currentUser.Actor,
                TotalSle = listings.Sum(l => l.PriceSle),
                Items = listings.Select(l => new OrderItem { ListingId = l.Id, TitleSnapshot = l.Title, PriceSle = l.PriceSle }).ToList()
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync(t);
            return await LoadDtoAsync(order.Id, t);
        }, ct);

    public Task<OrderDto> MarkPaidAsync(Guid orderId, MarkOrderPaidRequest request, CancellationToken ct = default) =>
        db.ExecuteInTransactionAsync(async t =>
        {
            var order = await LoadAsync(orderId, t);
            order.MarkPaid(request.PaymentMethod, request.PaymentReference, time.GetUtcNow().UtcDateTime);

            foreach (var item in order.Items)
            {
                var listing = item.Listing!;
                listing.Status = ListingStatus.Sold;
                string? trackingId = null;
                if (listing.Device is { } device)
                {
                    device.Status = DeviceStatus.Sold;
                    trackingId = device.TrackingId;
                }
                if (listing.Component is { } component)
                {
                    component.Status = ComponentStatus.Sold;
                    trackingId = component.Device?.TrackingId;
                }

                await ledger.AppendAsync(LedgerEventType.Sold, LedgerEntityTypes.Listing, listing.Id, new
                {
                    order.OrderNumber,
                    listing.ListingCode,
                    item.PriceSle,
                    listing.CertificateNumber,
                    ComponentCode = listing.Component?.ComponentCode,
                    Currency = "SLE"
                }, trackingId, t);
            }

            // Same DB transaction: fund credits + Reinvested ledger entry commit (or roll back) with the payment.
            await reinvestment.ApplyForOrderAsync(order, t);
            await db.SaveChangesAsync(t);
            return await LoadDtoAsync(order.Id, t);
        }, ct);

    public async Task<OrderDto> FulfilAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);
        order.MarkFulfilled(time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(order.Id, ct);
    }

    public Task<OrderDto> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken ct = default) =>
        db.ExecuteInTransactionAsync(async t =>
        {
            var order = await LoadAsync(orderId, t);
            EnsureCanAccess(order);
            order.Cancel(request.Reason, time.GetUtcNow().UtcDateTime);
            foreach (var item in order.Items) item.Listing!.Status = ListingStatus.Published;
            await db.SaveChangesAsync(t);
            return await LoadDtoAsync(order.Id, t);
        }, ct);

    public async Task<OrderDto> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);
        EnsureCanAccess(order);
        return await LoadDtoAsync(orderId, ct);
    }

    public async Task<PagedResult<OrderDto>> ListAsync(OrderQuery query, bool mineOnly, CancellationToken ct = default)
    {
        var q = db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Listing).AsQueryable();
        if (mineOnly) q = q.Where(o => o.BuyerUserId == currentUser.UserId);
        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        var page = await q.OrderByDescending(o => o.CreatedAt).ToPagedAsync(query, o => o, ct);
        var summaries = await ReinvestmentSummariesAsync(page.Items.Select(o => o.Id).ToList(), ct);
        return new PagedResult<OrderDto>(page.Items.Select(o => ToDto(o, summaries.GetValueOrDefault(o.Id))).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    private void EnsureCanAccess(Order order)
    {
        var isOps = currentUser.IsInRole(Roles.Admin) || currentUser.IsInRole(Roles.OperationsLead);
        if (!isOps && order.BuyerUserId != currentUser.UserId)
            throw new ForbiddenAccessException("You can only access your own orders.");
    }

    private async Task<Order> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Listing).ThenInclude(l => l!.Device)
            .Include(o => o.Items).ThenInclude(i => i.Listing).ThenInclude(l => l!.Component).ThenInclude(c => c!.Device)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException("Order", id);

    private async Task<OrderDto> LoadDtoAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).ThenInclude(i => i.Listing)
            .FirstAsync(o => o.Id == id, ct);
        var summaries = await ReinvestmentSummariesAsync([id], ct);
        return ToDto(order, summaries.GetValueOrDefault(id));
    }

    private async Task<Dictionary<Guid, ReinvestmentSummaryDto>> ReinvestmentSummariesAsync(List<Guid> orderIds, CancellationToken ct)
    {
        var refs = orderIds.Select(id => id.ToString()).ToList();
        var txs = await db.FundTransactions.AsNoTracking().Include(t => t.Fund)
            .Where(t => t.ReferenceType == LedgerEntityTypes.Order && refs.Contains(t.ReferenceId) && t.Reason == FundTransactionReason.Reinvestment)
            .ToListAsync(ct);
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.TotalSle, ct);
        return txs.GroupBy(t => Guid.Parse(t.ReferenceId)).ToDictionary(g => g.Key, g =>
        {
            var seed = g.Where(t => t.Fund!.Type == FundType.SeedFund).Sum(t => t.AmountSle);
            var training = g.Where(t => t.Fund!.Type == FundType.TrainingFund).Sum(t => t.AmountSle);
            return new ReinvestmentSummaryDto(orders.GetValueOrDefault(g.Key), seed, training, seed + training);
        });
    }

    private static OrderDto ToDto(Order o, ReinvestmentSummaryDto? reinvestment) => new(
        o.Id, o.OrderNumber, o.BuyerUserId, o.Status, o.TotalSle, o.PaymentMethod, o.PaymentReference, o.CreatedAt,
        o.PaidAt, o.FulfilledAt, o.CancelledAt, o.CancellationReason,
        o.Items.Select(i => new OrderItemDto(i.ListingId, i.Listing?.ListingCode ?? string.Empty, i.TitleSnapshot, i.PriceSle, i.Listing?.CertificateNumber)).ToList(),
        reinvestment);
}
