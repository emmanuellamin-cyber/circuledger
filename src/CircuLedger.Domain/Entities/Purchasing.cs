using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

/// <summary>One cell of the pricing matrix (brand × condition). Brand "*" is the fallback for any brand.</summary>
public class PricingRule : AuditableEntity
{
    public const string AnyBrand = "*";
    public required string Brand { get; set; }
    public DeviceCondition Condition { get; set; }
    public decimal PriceSle { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Purchase : AuditableEntity
{
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public decimal OfferedPriceSle { get; set; }
    public decimal PaidPriceSle { get; set; }
    public PayoutMethod PayoutMethod { get; set; }
    public PayoutStatus PayoutStatus { get; set; } = PayoutStatus.Pending;
    public string? PayoutReference { get; set; }
    public DateTime? PayoutCompletedAt { get; set; }
    public required string PurchasedByUserId { get; set; }
    public Guid SeedFundTransactionId { get; set; }
}
