using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Funds;

public sealed record ReinvestmentRuleDto(
    Guid Id,
    string Name,
    decimal SeedFundPercent,
    decimal TrainingFundPercent,
    decimal TotalPercent,
    bool IsActive,
    DateTime EffectiveFrom);

public sealed record UpdateReinvestmentRuleRequest(string Name, decimal SeedFundPercent, decimal TrainingFundPercent);

public sealed record ReinvestmentSummaryDto(
    decimal SaleTotalSle,
    decimal SeedFundSle,
    decimal TrainingFundSle,
    decimal TotalReinvestedSle);

public sealed class UpdateReinvestmentRuleRequestValidator : AbstractValidator<UpdateReinvestmentRuleRequest>
{
    public UpdateReinvestmentRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SeedFundPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.TrainingFundPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.SeedFundPercent + x.TrainingFundPercent).LessThanOrEqualTo(100)
            .WithName("TotalPercent").WithMessage("Seed + training percentages cannot exceed 100%.");
    }
}

public interface IReinvestmentService
{
    Task<ReinvestmentRuleDto> GetActiveRuleAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReinvestmentRuleDto>> GetHistoryAsync(CancellationToken ct = default);
    Task<ReinvestmentRuleDto> UpdateRuleAsync(UpdateReinvestmentRuleRequest request, CancellationToken ct = default);

    /// <summary>Credits the funds for a paid order. Must run inside the order-payment transaction.</summary>
    Task<ReinvestmentSummaryDto> ApplyForOrderAsync(Order order, CancellationToken ct = default);
}

public sealed class ReinvestmentService(IApplicationDbContext db, ILedgerService ledger, TimeProvider time) : IReinvestmentService
{
    public async Task<ReinvestmentRuleDto> GetActiveRuleAsync(CancellationToken ct = default) =>
        ToDto(await ActiveRuleAsync(ct));

    public async Task<IReadOnlyList<ReinvestmentRuleDto>> GetHistoryAsync(CancellationToken ct = default) =>
        (await db.ReinvestmentRules.AsNoTracking().OrderByDescending(r => r.EffectiveFrom).ToListAsync(ct)).Select(ToDto).ToList();

    /// <summary>Rules are versioned: the old rule is deactivated, never edited, so past allocations stay explainable.</summary>
    public Task<ReinvestmentRuleDto> UpdateRuleAsync(UpdateReinvestmentRuleRequest request, CancellationToken ct = default) =>
        db.ExecuteInTransactionAsync(async t =>
        {
            var current = await db.ReinvestmentRules.Where(r => r.IsActive).ToListAsync(t);
            foreach (var rule in current) rule.IsActive = false;
            await db.SaveChangesAsync(t);

            var next = new ReinvestmentRule
            {
                Name = request.Name,
                SeedFundPercent = request.SeedFundPercent,
                TrainingFundPercent = request.TrainingFundPercent,
                IsActive = true,
                EffectiveFrom = time.GetUtcNow().UtcDateTime
            };
            db.ReinvestmentRules.Add(next);
            await db.SaveChangesAsync(t);
            return ToDto(next);
        }, ct);

    public async Task<ReinvestmentSummaryDto> ApplyForOrderAsync(Order order, CancellationToken ct = default)
    {
        var orderRef = order.Id.ToString();
        var alreadyApplied = await db.FundTransactions.AnyAsync(
            t => t.ReferenceType == LedgerEntityTypes.Order && t.ReferenceId == orderRef && t.Reason == FundTransactionReason.Reinvestment, ct);
        if (alreadyApplied) throw new ConflictException($"Reinvestment for order {order.OrderNumber} was already applied.", "already_reinvested");

        var rule = await ActiveRuleAsync(ct);
        var allocation = ReinvestmentCalculator.Calculate(order.TotalSle, rule.SeedFundPercent, rule.TrainingFundPercent);
        var now = time.GetUtcNow().UtcDateTime;
        var description = $"{rule.Name}: order {order.OrderNumber}";

        decimal? seedBalance = null, trainingBalance = null;
        if (allocation.SeedFundSle > 0)
        {
            var seed = await db.GetFundForUpdateAsync(FundType.SeedFund, ct);
            db.FundTransactions.Add(seed.Credit(allocation.SeedFundSle, FundTransactionReason.Reinvestment, LedgerEntityTypes.Order, orderRef, now, description));
            seedBalance = seed.BalanceSle;
        }
        if (allocation.TrainingFundSle > 0)
        {
            var training = await db.GetFundForUpdateAsync(FundType.TrainingFund, ct);
            db.FundTransactions.Add(training.Credit(allocation.TrainingFundSle, FundTransactionReason.Reinvestment, LedgerEntityTypes.Order, orderRef, now, description));
            trainingBalance = training.BalanceSle;
        }

        await ledger.AppendAsync(LedgerEventType.Reinvested, LedgerEntityTypes.Order, order.Id, new
        {
            order.OrderNumber,
            allocation.SaleTotalSle,
            allocation.SeedFundSle,
            allocation.TrainingFundSle,
            allocation.TotalReinvestedSle,
            RuleId = rule.Id,
            rule.SeedFundPercent,
            rule.TrainingFundPercent,
            SeedFundBalanceAfterSle = seedBalance,
            TrainingFundBalanceAfterSle = trainingBalance,
            Currency = "SLE"
        }, cancellationToken: ct);

        return new ReinvestmentSummaryDto(allocation.SaleTotalSle, allocation.SeedFundSle, allocation.TrainingFundSle, allocation.TotalReinvestedSle);
    }

    private async Task<ReinvestmentRule> ActiveRuleAsync(CancellationToken ct) =>
        await db.ReinvestmentRules.AsNoTracking().FirstOrDefaultAsync(r => r.IsActive, ct)
        ?? throw new InvalidOperationException("No active reinvestment rule is configured.");

    private static ReinvestmentRuleDto ToDto(ReinvestmentRule r) =>
        new(r.Id, r.Name, r.SeedFundPercent, r.TrainingFundPercent, r.TotalPercent, r.IsActive, r.EffectiveFrom);
}
