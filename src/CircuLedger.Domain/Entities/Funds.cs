using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;

namespace CircuLedger.Domain.Entities;

public class Fund : AuditableEntity
{
    public FundType Type { get; set; }
    public required string Name { get; set; }
    public decimal BalanceSle { get; set; }
    public ICollection<FundTransaction> Transactions { get; set; } = [];

    public FundTransaction Credit(decimal amount, FundTransactionReason reason, string referenceType, string referenceId, DateTime now, string? description = null)
    {
        if (amount <= 0) throw new DomainException("Credit amount must be positive.");
        BalanceSle += amount;
        return NewTransaction(FundTransactionDirection.Credit, amount, reason, referenceType, referenceId, now, description);
    }

    /// <summary>Debits the fund and refuses to go negative. This is what blocks purchases when the seed fund is low.</summary>
    public FundTransaction Debit(decimal amount, FundTransactionReason reason, string referenceType, string referenceId, DateTime now, string? description = null)
    {
        if (amount <= 0) throw new DomainException("Debit amount must be positive.");
        if (BalanceSle < amount) throw new InsufficientFundsException(Name, BalanceSle, amount);
        BalanceSle -= amount;
        return NewTransaction(FundTransactionDirection.Debit, amount, reason, referenceType, referenceId, now, description);
    }

    private FundTransaction NewTransaction(FundTransactionDirection direction, decimal amount, FundTransactionReason reason,
        string referenceType, string referenceId, DateTime now, string? description) => new()
    {
        FundId = Id,
        Direction = direction,
        AmountSle = amount,
        BalanceAfterSle = BalanceSle,
        Reason = reason,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        OccurredAt = now,
        Description = description
    };
}

public class FundTransaction : AuditableEntity
{
    public Guid FundId { get; set; }
    public Fund? Fund { get; set; }
    public FundTransactionDirection Direction { get; set; }
    public decimal AmountSle { get; set; }
    public decimal BalanceAfterSle { get; set; }
    public FundTransactionReason Reason { get; set; }
    public required string ReferenceType { get; set; }
    public required string ReferenceId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Description { get; set; }
}

/// <summary>For example, 30% of each sale: 20% to the Seed Fund and 10% to the Training Fund.</summary>
public class ReinvestmentRule : AuditableEntity
{
    public required string Name { get; set; }
    public decimal SeedFundPercent { get; set; }
    public decimal TrainingFundPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EffectiveFrom { get; set; }
    public decimal TotalPercent => SeedFundPercent + TrainingFundPercent;
}
