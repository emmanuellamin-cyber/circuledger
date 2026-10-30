using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Funds;

public sealed record FundDto(Guid Id, FundType Type, string Name, decimal BalanceSle);

public sealed record FundTransactionDto(
    Guid Id,
    FundType FundType,
    FundTransactionDirection Direction,
    decimal AmountSle,
    decimal BalanceAfterSle,
    FundTransactionReason Reason,
    string ReferenceType,
    string ReferenceId,
    DateTime OccurredAt,
    string? Description);

public sealed record FundsOverviewDto(
    IReadOnlyList<FundDto> Funds,
    decimal TotalPurchasesSle,
    decimal TotalReinvestedSle,
    decimal TotalGrantsSle,
    PagedResult<FundTransactionDto> Transactions);

public sealed record FundTransactionQuery : PageQuery
{
    public FundType? Fund { get; init; }
    public FundTransactionReason? Reason { get; init; }
}

/// <summary>A manual movement. Grants are credits, training expenses and adjustments are debits.</summary>
public sealed record FundMovementRequest(FundType Fund, FundTransactionReason Reason, decimal AmountSle, string Description);

public sealed class FundMovementRequestValidator : AbstractValidator<FundMovementRequest>
{
    public FundMovementRequestValidator()
    {
        RuleFor(x => x.Fund).IsInEnum();
        RuleFor(x => x.Reason).Must(r => r is FundTransactionReason.Grant or FundTransactionReason.TrainingExpense or FundTransactionReason.Adjustment)
            .WithMessage("Manual movements must be Grant, TrainingExpense or Adjustment.");
        RuleFor(x => x.AmountSle).GreaterThan(0).LessThan(10_000_000);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
    }
}

public interface IFundService
{
    Task<FundsOverviewDto> GetOverviewAsync(FundTransactionQuery query, CancellationToken ct = default);
    Task<FundTransactionDto> RecordMovementAsync(FundMovementRequest request, CancellationToken ct = default);
}

public sealed class FundService(IApplicationDbContext db, TimeProvider time) : IFundService
{
    public async Task<FundsOverviewDto> GetOverviewAsync(FundTransactionQuery query, CancellationToken ct = default)
    {
        var funds = await db.Funds.AsNoTracking().OrderBy(f => f.Type).ToListAsync(ct);
        var totals = await db.FundTransactions.AsNoTracking()
            .GroupBy(t => t.Reason)
            .Select(g => new { Reason = g.Key, Amount = g.Sum(t => t.AmountSle) })
            .ToListAsync(ct);
        decimal Total(FundTransactionReason r) => totals.Where(t => t.Reason == r).Sum(t => t.Amount);

        var q = db.FundTransactions.AsNoTracking().Include(t => t.Fund).AsQueryable();
        if (query.Fund is { } fund) q = q.Where(t => t.Fund!.Type == fund);
        if (query.Reason is { } reason) q = q.Where(t => t.Reason == reason);
        var page = await q.OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id).ToPagedAsync(query, ToDto, ct);

        return new FundsOverviewDto(
            funds.Select(f => new FundDto(f.Id, f.Type, f.Name, f.BalanceSle)).ToList(),
            Total(FundTransactionReason.DevicePurchase),
            Total(FundTransactionReason.Reinvestment),
            Total(FundTransactionReason.Grant),
            page);
    }

    public Task<FundTransactionDto> RecordMovementAsync(FundMovementRequest request, CancellationToken ct = default) =>
        db.ExecuteInTransactionAsync(async t =>
        {
            var fund = await db.GetFundForUpdateAsync(request.Fund, t);
            var now = time.GetUtcNow().UtcDateTime;
            var tx = request.Reason == FundTransactionReason.Grant
                ? fund.Credit(request.AmountSle, request.Reason, "Manual", now.ToString("yyyyMMddHHmmss"), now, request.Description)
                : fund.Debit(request.AmountSle, request.Reason, "Manual", now.ToString("yyyyMMddHHmmss"), now, request.Description);
            tx.Fund = fund;
            db.FundTransactions.Add(tx);
            await db.SaveChangesAsync(t);
            return ToDto(tx);
        }, ct);

    private static FundTransactionDto ToDto(FundTransaction t) => new(
        t.Id, t.Fund?.Type ?? default, t.Direction, t.AmountSle, t.BalanceAfterSle, t.Reason, t.ReferenceType,
        t.ReferenceId, t.OccurredAt, t.Description);
}
