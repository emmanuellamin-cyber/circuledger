using System.Globalization;
using System.Text;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CircuLedger.Application.Dashboard;

public sealed record TargetMetric(decimal Actual, decimal Target)
{
    public decimal PercentOfTarget => Target == 0 ? 0 : Math.Round(Actual / Target * 100m, 1);
}

public sealed record DashboardKpisDto(
    DateTime GeneratedAt,
    TargetMetric DevicesCollected,
    int IntakeRequestsOpen,
    int CertificatesIssued,
    decimal PercentSanitized,
    TargetMetric HazardousKgDiverted,
    TargetMetric TechniciansTrained,
    TargetMetric JobsCreated,
    TargetMetric SchoolsReached,
    TargetMetric CampusesReached,
    int AwarenessPeopleReached,
    decimal DevicePurchasesSle,
    decimal MarketplaceRevenueSle,
    int OrdersPaid,
    decimal TotalReinvestedSle,
    decimal SeedFundBalanceSle,
    decimal TrainingFundBalanceSle,
    decimal SamsungSharePercent,
    decimal WomenTraineesPercent,
    decimal WomenHiresPercent,
    decimal WomenInclusionTargetPercent,
    int ComponentsRecovered,
    int RefurbishedDevicesSold);

public sealed record TimeSeriesPoint(DateOnly WeekStart, int Count, decimal Value);

public sealed record TimeSeriesQuery(DateOnly? From, DateOnly? To);

public interface IDashboardService
{
    Task<DashboardKpisDto> GetKpisAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TimeSeriesPoint>> GetWeeklyIntakeAsync(TimeSeriesQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<TimeSeriesPoint>> GetWeeklySalesAsync(TimeSeriesQuery query, CancellationToken ct = default);
    Task<string> ExportKpisCsvAsync(CancellationToken ct = default);
    Task<string> ExportWeeklyCsvAsync(TimeSeriesQuery query, CancellationToken ct = default);
}

public sealed class DashboardService(IApplicationDbContext db, IOptions<PilotTargetOptions> targets, TimeProvider time) : IDashboardService
{
    private readonly PilotTargetOptions _targets = targets.Value;

    public async Task<DashboardKpisDto> GetKpisAsync(CancellationToken ct = default)
    {
        var devices = await db.Devices.CountAsync(ct);
        var samsung = await db.Devices.CountAsync(d => d.IsSamsungFlagship, ct);
        var purchased = await db.Purchases.CountAsync(ct);
        var sanitizedDevices = await db.SanitizationRecords
            .Where(r => r.Outcome == SanitizationOutcome.Passed).Select(r => r.DeviceId).Distinct().CountAsync(ct);
        var openIntake = await db.IntakeRequests.CountAsync(
            r => r.Status == IntakeStatus.Pending || r.Status == IntakeStatus.Scheduled || r.Status == IntakeStatus.Collected, ct);
        var certificates = await db.Certificates.CountAsync(c => !c.IsRevoked, ct);
        var kg = await db.HazardousMaterialLogs.SumAsync(h => (decimal?)h.WeightKg, ct) ?? 0;

        var enrollments = await db.TraineeEnrollments.AsNoTracking()
            .Select(e => new { e.Gender, e.Status }).ToListAsync(ct);
        var trained = enrollments.Count(e => e.Status == EnrollmentStatus.Completed);
        var jobs = await db.EmploymentRecords.AsNoTracking().Select(e => e.Gender).ToListAsync(ct);

        var campaigns = await db.AwarenessCampaigns.AsNoTracking()
            .Select(a => new { a.InstitutionName, a.InstitutionType, a.ReachCount }).ToListAsync(ct);
        int Distinct(InstitutionType type) => campaigns.Where(c => c.InstitutionType == type)
            .Select(c => c.InstitutionName.Trim().ToLowerInvariant()).Distinct().Count();

        var paidStatuses = new[] { OrderStatus.Paid, OrderStatus.Fulfilled };
        var revenue = await db.Orders.Where(o => paidStatuses.Contains(o.Status)).SumAsync(o => (decimal?)o.TotalSle, ct) ?? 0;
        var ordersPaid = await db.Orders.CountAsync(o => paidStatuses.Contains(o.Status), ct);

        var reinvested = await db.FundTransactions.Where(t => t.Reason == FundTransactionReason.Reinvestment)
            .SumAsync(t => (decimal?)t.AmountSle, ct) ?? 0;
        var purchases = await db.FundTransactions.Where(t => t.Reason == FundTransactionReason.DevicePurchase)
            .SumAsync(t => (decimal?)t.AmountSle, ct) ?? 0;
        var funds = await db.Funds.AsNoTracking().ToDictionaryAsync(f => f.Type, f => f.BalanceSle, ct);

        var components = await db.RecoveredComponents.CountAsync(ct);
        var refurbSold = await db.Devices.CountAsync(d => d.Status == DeviceStatus.Sold, ct);

        return new DashboardKpisDto(
            time.GetUtcNow().UtcDateTime,
            new TargetMetric(devices, _targets.DevicesCollected),
            openIntake,
            certificates,
            Percent(sanitizedDevices, purchased),
            new TargetMetric(kg, _targets.HazardousKgDiverted),
            new TargetMetric(trained, _targets.TechniciansTrained),
            new TargetMetric(jobs.Count, _targets.JobsCreated),
            new TargetMetric(Distinct(InstitutionType.School), _targets.SchoolsReached),
            new TargetMetric(Distinct(InstitutionType.Campus), _targets.CampusesReached),
            campaigns.Sum(c => c.ReachCount),
            purchases,
            revenue,
            ordersPaid,
            reinvested,
            funds.GetValueOrDefault(FundType.SeedFund),
            funds.GetValueOrDefault(FundType.TrainingFund),
            Percent(samsung, devices),
            Percent(enrollments.Count(e => e.Gender == Gender.Female), enrollments.Count),
            Percent(jobs.Count(g => g == Gender.Female), jobs.Count),
            _targets.WomenInclusionPercent,
            components,
            refurbSold);
    }

    public async Task<IReadOnlyList<TimeSeriesPoint>> GetWeeklyIntakeAsync(TimeSeriesQuery query, CancellationToken ct = default)
    {
        var (from, to) = Range(query);
        var rows = await db.Devices.AsNoTracking()
            .Where(d => d.CreatedAt >= from && d.CreatedAt < to)
            .Select(d => new { d.CreatedAt, Paid = d.Purchase != null ? d.Purchase.PaidPriceSle : 0m })
            .ToListAsync(ct);
        return Bucket(from, to, rows.Select(r => (r.CreatedAt, r.Paid)));
    }

    public async Task<IReadOnlyList<TimeSeriesPoint>> GetWeeklySalesAsync(TimeSeriesQuery query, CancellationToken ct = default)
    {
        var (from, to) = Range(query);
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.PaidAt != null && o.PaidAt >= from && o.PaidAt < to)
            .Select(o => new { PaidAt = o.PaidAt!.Value, o.TotalSle })
            .ToListAsync(ct);
        return Bucket(from, to, rows.Select(r => (r.PaidAt, r.TotalSle)));
    }

    public async Task<string> ExportKpisCsvAsync(CancellationToken ct = default)
    {
        var k = await GetKpisAsync(ct);
        var sb = new StringBuilder("indicator,actual,target,percent_of_target\n");
        void Row(string name, decimal actual, decimal? target = null) =>
            sb.Append(name).Append(',').Append(Num(actual)).Append(',')
              .Append(target is { } t ? Num(t) : string.Empty).Append(',')
              .Append(target is { } tt && tt != 0 ? Num(Math.Round(actual / tt * 100, 1)) : string.Empty).Append('\n');

        Row("devices_collected", k.DevicesCollected.Actual, k.DevicesCollected.Target);
        Row("certificates_issued", k.CertificatesIssued);
        Row("percent_sanitized", k.PercentSanitized);
        Row("hazardous_kg_diverted", k.HazardousKgDiverted.Actual, k.HazardousKgDiverted.Target);
        Row("technicians_trained", k.TechniciansTrained.Actual, k.TechniciansTrained.Target);
        Row("jobs_created", k.JobsCreated.Actual, k.JobsCreated.Target);
        Row("schools_reached", k.SchoolsReached.Actual, k.SchoolsReached.Target);
        Row("campuses_reached", k.CampusesReached.Actual, k.CampusesReached.Target);
        Row("awareness_people_reached", k.AwarenessPeopleReached);
        Row("device_purchases_sle", k.DevicePurchasesSle);
        Row("marketplace_revenue_sle", k.MarketplaceRevenueSle);
        Row("total_reinvested_sle", k.TotalReinvestedSle);
        Row("seed_fund_balance_sle", k.SeedFundBalanceSle);
        Row("training_fund_balance_sle", k.TrainingFundBalanceSle);
        Row("samsung_share_percent", k.SamsungSharePercent);
        Row("women_trainees_percent", k.WomenTraineesPercent, k.WomenInclusionTargetPercent);
        Row("women_hires_percent", k.WomenHiresPercent, k.WomenInclusionTargetPercent);
        Row("components_recovered", k.ComponentsRecovered);
        return sb.ToString();
    }

    public async Task<string> ExportWeeklyCsvAsync(TimeSeriesQuery query, CancellationToken ct = default)
    {
        var intake = await GetWeeklyIntakeAsync(query, ct);
        var sales = (await GetWeeklySalesAsync(query, ct)).ToDictionary(p => p.WeekStart);
        var sb = new StringBuilder("week_start,devices_collected,purchase_spend_sle,orders_paid,sales_revenue_sle\n");
        foreach (var p in intake)
        {
            var s = sales.GetValueOrDefault(p.WeekStart);
            sb.Append(p.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
              .Append(p.Count).Append(',').Append(Num(p.Value)).Append(',')
              .Append(s?.Count ?? 0).Append(',').Append(Num(s?.Value ?? 0)).Append('\n');
        }
        return sb.ToString();
    }

    private (DateTime From, DateTime To) Range(TimeSeriesQuery query)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var to = (query.To ?? today).AddDays(1);
        var from = WeekStart(query.From ?? today.AddDays(-7 * 12));
        return (from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    /// <summary>ISO weeks start on Monday. Empty weeks are included, so charts show gaps.</summary>
    private static IReadOnlyList<TimeSeriesPoint> Bucket(DateTime from, DateTime to, IEnumerable<(DateTime At, decimal Value)> rows)
    {
        var groups = rows.GroupBy(r => WeekStart(DateOnly.FromDateTime(r.At)))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Value: g.Sum(x => x.Value)));
        var points = new List<TimeSeriesPoint>();
        for (var week = WeekStart(DateOnly.FromDateTime(from)); week.ToDateTime(TimeOnly.MinValue) < to; week = week.AddDays(7))
        {
            var g = groups.GetValueOrDefault(week);
            points.Add(new TimeSeriesPoint(week, g.Count, g.Value));
        }
        return points;
    }

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static decimal Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100m / whole, 1);

    private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
