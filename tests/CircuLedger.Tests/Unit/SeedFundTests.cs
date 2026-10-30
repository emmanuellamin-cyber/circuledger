using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;

namespace CircuLedger.Tests.Unit;

public class SeedFundTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static Fund SeedFund(decimal balance) => new() { Type = FundType.SeedFund, Name = "Seed Fund", BalanceSle = balance };

    [Fact]
    public void Purchase_is_blocked_when_the_seed_fund_is_insufficient()
    {
        var fund = SeedFund(100m);

        var ex = Assert.Throws<InsufficientFundsException>(() =>
            fund.Debit(150m, FundTransactionReason.DevicePurchase, "Device", "d1", Now));

        Assert.Equal("insufficient_funds", ex.Code);
        Assert.Equal(100m, ex.Balance);
        Assert.Equal(150m, ex.Requested);
        Assert.Equal(100m, fund.BalanceSle); // unchanged
    }

    [Fact]
    public void Purchase_debits_the_seed_fund_and_records_balance_after()
    {
        var fund = SeedFund(500m);

        var tx = fund.Debit(200m, FundTransactionReason.DevicePurchase, "Device", "d1", Now);

        Assert.Equal(300m, fund.BalanceSle);
        Assert.Equal(FundTransactionDirection.Debit, tx.Direction);
        Assert.Equal(300m, tx.BalanceAfterSle);
        Assert.Equal(fund.Id, tx.FundId);
    }

    [Fact]
    public void Exact_balance_can_be_spent()
    {
        var fund = SeedFund(200m);
        fund.Debit(200m, FundTransactionReason.DevicePurchase, "Device", "d1", Now);
        Assert.Equal(0m, fund.BalanceSle);
    }

    [Fact]
    public void Reinvestment_credit_refills_the_fund()
    {
        var fund = SeedFund(0m);
        fund.Credit(60m, FundTransactionReason.Reinvestment, "Order", "o1", Now);

        Assert.Equal(60m, fund.BalanceSle);
        Assert.Throws<InsufficientFundsException>(() => fund.Debit(61m, FundTransactionReason.DevicePurchase, "Device", "d2", Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_amounts_are_rejected(decimal amount)
    {
        var fund = SeedFund(100m);
        Assert.Throws<DomainException>(() => fund.Debit(amount, FundTransactionReason.DevicePurchase, "Device", "d1", Now));
        Assert.Throws<DomainException>(() => fund.Credit(amount, FundTransactionReason.Grant, "Grant", "g1", Now));
    }
}
