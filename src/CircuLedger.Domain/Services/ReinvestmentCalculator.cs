using CircuLedger.Domain.Exceptions;

namespace CircuLedger.Domain.Services;

public sealed record ReinvestmentAllocation(decimal SaleTotalSle, decimal SeedFundSle, decimal TrainingFundSle)
{
    public decimal TotalReinvestedSle => SeedFundSle + TrainingFundSle;
}

public static class ReinvestmentCalculator
{
    /// <summary>Splits a sale total into fund allocations, rounded to the cent (away from zero).</summary>
    public static ReinvestmentAllocation Calculate(decimal saleTotalSle, decimal seedFundPercent, decimal trainingFundPercent)
    {
        if (saleTotalSle < 0) throw new DomainException("Sale total cannot be negative.");
        if (seedFundPercent is < 0 or > 100 || trainingFundPercent is < 0 or > 100)
            throw new DomainException("Reinvestment percentages must be between 0 and 100.");
        if (seedFundPercent + trainingFundPercent > 100)
            throw new DomainException("Reinvestment percentages cannot exceed 100% in total.");

        var seed = Math.Round(saleTotalSle * seedFundPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var training = Math.Round(saleTotalSle * trainingFundPercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new ReinvestmentAllocation(saleTotalSle, seed, training);
    }
}
