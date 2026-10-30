using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Purchasing;

public sealed record PricingRuleDto(Guid Id, string Brand, DeviceCondition Condition, decimal PriceSle, bool IsActive);

public sealed record UpsertPricingRuleRequest(string Brand, DeviceCondition Condition, decimal PriceSle, bool IsActive = true);

public sealed record PriceQuoteDto(string Brand, DeviceCondition Condition, decimal? PriceSle, string MatchedBrand);

public sealed class UpsertPricingRuleRequestValidator : AbstractValidator<UpsertPricingRuleRequest>
{
    public UpsertPricingRuleRequestValidator()
    {
        RuleFor(x => x.Brand).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.PriceSle).GreaterThanOrEqualTo(0).LessThan(100_000);
    }
}

/// <summary>Brand × condition lookup. An exact brand match wins, otherwise the "*" row applies.</summary>
public static class PricingMatrix
{
    public static PricingRule? Resolve(IEnumerable<PricingRule> rules, string brand, DeviceCondition condition)
    {
        var active = rules.Where(r => r.IsActive && r.Condition == condition).ToList();
        return active.FirstOrDefault(r => r.Brand.Equals(brand.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? active.FirstOrDefault(r => r.Brand == PricingRule.AnyBrand);
    }
}

public interface IPricingService
{
    Task<IReadOnlyList<PricingRuleDto>> GetMatrixAsync(CancellationToken ct = default);
    Task<PriceQuoteDto> QuoteAsync(string brand, DeviceCondition condition, CancellationToken ct = default);
    Task<PricingRuleDto> UpsertAsync(UpsertPricingRuleRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PricingRule>> GetActiveRulesAsync(CancellationToken ct = default);
}

public sealed class PricingService(IApplicationDbContext db) : IPricingService
{
    public async Task<IReadOnlyList<PricingRuleDto>> GetMatrixAsync(CancellationToken ct = default) =>
        (await db.PricingRules.AsNoTracking().OrderBy(r => r.Brand).ThenBy(r => r.Condition).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<PriceQuoteDto> QuoteAsync(string brand, DeviceCondition condition, CancellationToken ct = default)
    {
        var rule = PricingMatrix.Resolve(await GetActiveRulesAsync(ct), brand, condition);
        return new PriceQuoteDto(brand, condition, rule?.PriceSle, rule?.Brand ?? "none");
    }

    public async Task<PricingRuleDto> UpsertAsync(UpsertPricingRuleRequest request, CancellationToken ct = default)
    {
        var brand = request.Brand.Trim();
        var rule = await db.PricingRules.FirstOrDefaultAsync(r => r.Brand == brand && r.Condition == request.Condition, ct);
        if (rule is null)
        {
            rule = new PricingRule { Brand = brand, Condition = request.Condition };
            db.PricingRules.Add(rule);
        }
        rule.PriceSle = request.PriceSle;
        rule.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task<IReadOnlyList<PricingRule>> GetActiveRulesAsync(CancellationToken ct = default) =>
        await db.PricingRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);

    private static PricingRuleDto ToDto(PricingRule r) => new(r.Id, r.Brand, r.Condition, r.PriceSle, r.IsActive);
}
