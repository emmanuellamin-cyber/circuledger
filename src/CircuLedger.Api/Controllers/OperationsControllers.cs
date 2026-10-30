using System.Text;
using CircuLedger.Application.Common;
using CircuLedger.Application.Dashboard;
using CircuLedger.Application.Funds;
using CircuLedger.Application.Ledger;
using CircuLedger.Application.People;
using CircuLedger.Domain.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CircuLedger.Api.Controllers;

[Route("api/v{version:apiVersion}/funds")]
[Authorize(Policy = Policies.Operations)]
public sealed class FundsController(IFundService funds) : ApiControllerBase
{
    /// <summary>Fund balances, totals by reason, and paginated transaction history.</summary>
    [HttpGet]
    public async Task<ActionResult<FundsOverviewDto>> Overview([FromQuery] FundTransactionQuery query, CancellationToken ct) =>
        Ok(await funds.GetOverviewAsync(query, ct));

    /// <summary>Records a grant (credit), training expense or adjustment (debit).</summary>
    [HttpPost("movements")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<FundTransactionDto>> RecordMovement(FundMovementRequest request, CancellationToken ct) =>
        Ok(await funds.RecordMovementAsync(request, ct));
}

[Route("api/v{version:apiVersion}/reinvestment-rule")]
[Authorize(Policy = Policies.Staff)]
public sealed class ReinvestmentRuleController(IReinvestmentService reinvestment) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ReinvestmentRuleDto>> Active(CancellationToken ct) => Ok(await reinvestment.GetActiveRuleAsync(ct));

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<ReinvestmentRuleDto>>> History(CancellationToken ct) => Ok(await reinvestment.GetHistoryAsync(ct));

    /// <summary>Replaces the active rule. The previous rule is kept for history.</summary>
    [HttpPut]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<ReinvestmentRuleDto>> Update(UpdateReinvestmentRuleRequest request, CancellationToken ct) =>
        Ok(await reinvestment.UpdateRuleAsync(request, ct));
}

[Route("api/v{version:apiVersion}/training")]
[Authorize]
public sealed class TrainingController(ITrainingService training) : ApiControllerBase
{
    [HttpGet("cycles")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<IReadOnlyList<TrainingCycleDto>>> Cycles(CancellationToken ct) => Ok(await training.ListCyclesAsync(ct));

    [HttpPost("cycles")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<TrainingCycleDto>> CreateCycle(CreateTrainingCycleRequest request, CancellationToken ct) =>
        Ok(await training.CreateCycleAsync(request, ct));

    [HttpGet("cycles/{id:guid}/enrollments")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<IReadOnlyList<TraineeEnrollmentDto>>> Enrollments(Guid id, CancellationToken ct) =>
        Ok(await training.ListEnrollmentsAsync(id, ct));

    [HttpPost("cycles/{id:guid}/enrollments")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<TraineeEnrollmentDto>> Enroll(Guid id, EnrollTraineeRequest request, CancellationToken ct) =>
        Ok(await training.EnrollAsync(id, request, ct));

    /// <summary>Updates progress, including SafetyTrainingCompleted (which unlocks dismantling and battery handling).</summary>
    [HttpPut("enrollments/{enrollmentId:guid}")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<TraineeEnrollmentDto>> UpdateEnrollment(Guid enrollmentId, UpdateEnrollmentRequest request, CancellationToken ct) =>
        Ok(await training.UpdateEnrollmentAsync(enrollmentId, request, ct));
}

[Route("api/v{version:apiVersion}/employment")]
[Authorize(Policy = Policies.Operations)]
public sealed class EmploymentController(IEmploymentService employment) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmploymentRecordDto>>> List(CancellationToken ct) => Ok(await employment.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<EmploymentRecordDto>> Create(CreateEmploymentRecordRequest request, CancellationToken ct) =>
        Ok(await employment.CreateAsync(request, ct));
}

[Route("api/v{version:apiVersion}/awareness-campaigns")]
[Authorize(Policy = Policies.Staff)]
public sealed class AwarenessController(IAwarenessService awareness) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AwarenessCampaignDto>>> List(CancellationToken ct) => Ok(await awareness.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<AwarenessCampaignDto>> Create(CreateAwarenessCampaignRequest request, CancellationToken ct) =>
        Ok(await awareness.CreateAsync(request, ct));
}

[Route("api/v{version:apiVersion}/dashboard")]
[Authorize(Policy = Policies.Operations)]
public sealed class DashboardController(IDashboardService dashboard) : ApiControllerBase
{
    /// <summary>Pilot KPIs against targets (500 devices, 50 kg, 20 technicians, 5 jobs, 5 schools + 2 campuses, â‰¥40% women).</summary>
    [HttpGet("kpis")]
    public async Task<ActionResult<DashboardKpisDto>> Kpis(CancellationToken ct) => Ok(await dashboard.GetKpisAsync(ct));

    [HttpGet("time-series/intake")]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPoint>>> WeeklyIntake([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await dashboard.GetWeeklyIntakeAsync(new TimeSeriesQuery(from, to), ct));

    [HttpGet("time-series/sales")]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPoint>>> WeeklySales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await dashboard.GetWeeklySalesAsync(new TimeSeriesQuery(from, to), ct));

    [HttpGet("export/kpis.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportKpis(CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await dashboard.ExportKpisCsvAsync(ct)), "text/csv", $"circuledger-kpis-{DateTime.UtcNow:yyyyMMdd}.csv");

    [HttpGet("export/weekly.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportWeekly([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await dashboard.ExportWeeklyCsvAsync(new TimeSeriesQuery(from, to), ct)), "text/csv",
            $"circuledger-weekly-{DateTime.UtcNow:yyyyMMdd}.csv");
}

[Route("api/v{version:apiVersion}/ledger")]
[Authorize(Policy = Policies.Staff)]
public sealed class LedgerController(ILedgerService ledger) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<PagedResult<LedgerEntryDto>>> List([FromQuery] LedgerQuery query, CancellationToken ct) =>
        Ok(await ledger.ListAsync(query, ct));

    /// <summary>Full, ordered event history of a device, from intake to sale.</summary>
    [HttpGet("devices/{trackingId}")]
    public async Task<ActionResult<DeviceHistoryDto>> DeviceHistory(string trackingId, CancellationToken ct) =>
        Ok(await ledger.GetDeviceHistoryAsync(trackingId.ToUpperInvariant(), ct));

    /// <summary>Recomputes the whole hash chain and reports the first broken link, if any.</summary>
    [HttpGet("verify")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<LedgerVerificationResult>> Verify(CancellationToken ct) => Ok(await ledger.VerifyAsync(ct));
}
