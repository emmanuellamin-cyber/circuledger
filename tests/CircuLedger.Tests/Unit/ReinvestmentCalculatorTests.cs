using CircuLedger.Domain.Exceptions;
using CircuLedger.Domain.Services;

namespace CircuLedger.Tests.Unit;

public class ReinvestmentCalculatorTests
{
    [Fact]
    public void Splits_thirty_percent_into_twenty_seed_and_ten_training()
    {
        var allocation = ReinvestmentCalculator.Calculate(1_000m, 20m, 10m);

        Assert.Equal(200m, allocation.SeedFundSle);
        Assert.Equal(100m, allocation.TrainingFundSle);
        Assert.Equal(300m, allocation.TotalReinvestedSle);
    }

    [Theory]
    [InlineData(333.33, 66.67, 33.33)]   // 66.666 -> 66.67, 33.333 -> 33.33
    [InlineData(0.05, 0.01, 0.01)]       // 0.01 and 0.005 rounds away from zero
    [InlineData(0, 0, 0)]
    public void Rounds_to_the_cent_away_from_zero(decimal total, decimal expectedSeed, decimal expectedTraining)
    {
        var allocation = ReinvestmentCalculator.Calculate(total, 20m, 10m);

        Assert.Equal(expectedSeed, allocation.SeedFundSle);
        Assert.Equal(expectedTraining, allocation.TrainingFundSle);
    }

    [Theory]
    [InlineData(-1, 20, 10)]
    [InlineData(100, 80, 30)]
    [InlineData(100, -5, 10)]
    [InlineData(100, 20, 101)]
    public void Rejects_invalid_inputs(decimal total, decimal seed, decimal training)
    {
        Assert.Throws<DomainException>(() => ReinvestmentCalculator.Calculate(total, seed, training));
    }
}
