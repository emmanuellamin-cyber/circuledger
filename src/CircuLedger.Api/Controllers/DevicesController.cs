using CircuLedger.Application.Common;
using CircuLedger.Application.Devices;
using CircuLedger.Application.Purchasing;
using CircuLedger.Application.Recovery;
using CircuLedger.Application.Sanitization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CircuLedger.Api.Controllers;

[Route("api/v{version:apiVersion}/devices")]
[Authorize(Policy = Policies.Staff)]
public sealed class DevicesController(
    IDeviceService devices,
    IPurchaseService purchases,
    ISanitizationService sanitization,
    IRecoveryService recovery) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeviceDto>>> List([FromQuery] DeviceListQuery query, CancellationToken ct) =>
        Ok(await devices.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceDto>> Get(Guid id, CancellationToken ct) => Ok(await devices.GetAsync(id, ct));

    [HttpGet("tracking/{trackingId}")]
    public async Task<ActionResult<DeviceDto>> GetByTrackingId(string trackingId, CancellationToken ct) =>
        Ok(await devices.GetByTrackingIdAsync(trackingId, ct));

    // ---- Purchasing ----

    /// <summary>Buys the device at the pricing-matrix price (or an agreed price). Debits the Seed Fund; returns 409 if it is insufficient.</summary>
    [HttpPost("{id:guid}/purchase")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<PurchaseDto>> Purchase(Guid id, PurchaseDeviceRequest request, CancellationToken ct) =>
        Ok(await purchases.PurchaseAsync(id, request, ct));

    [HttpGet("{id:guid}/purchase")]
    public async Task<ActionResult<PurchaseDto>> GetPurchase(Guid id, CancellationToken ct) => Ok(await purchases.GetAsync(id, ct));

    [HttpPut("{id:guid}/purchase/payout")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<PurchaseDto>> UpdatePayout(Guid id, UpdatePayoutRequest request, CancellationToken ct) =>
        Ok(await purchases.UpdatePayoutAsync(id, request, ct));

    // ---- Sanitization ----

    /// <summary>Records a wipe. If the outcome is Passed, a signed Certificate of Data Sanitization is issued and sent to the owner by SMS.</summary>
    [HttpPost("{id:guid}/sanitizations")]
    [Authorize(Policy = Policies.Workshop)]
    public async Task<ActionResult<SanitizationRecordDto>> Sanitize(Guid id, RecordSanitizationRequest request, CancellationToken ct) =>
        Ok(await sanitization.RecordAsync(id, request, ct));

    [HttpGet("{id:guid}/sanitizations")]
    public async Task<ActionResult<IReadOnlyList<SanitizationRecordDto>>> Sanitizations(Guid id, CancellationToken ct) =>
        Ok(await sanitization.ListForDeviceAsync(id, ct));

    // ---- Resource recovery ----

    [HttpPut("{id:guid}/recovery-outcome")]
    [Authorize(Policy = Policies.Workshop)]
    public async Task<ActionResult<DeviceDto>> SetRecoveryOutcome(Guid id, SetRecoveryOutcomeRequest request, CancellationToken ct) =>
        Ok(await recovery.SetOutcomeAsync(id, request, ct));

    /// <summary>Requires a completed safety training record.</summary>
    [HttpPost("{id:guid}/dismantle")]
    [Authorize(Policy = Policies.Workshop)]
    public async Task<ActionResult<DeviceDto>> Dismantle(Guid id, DismantleDeviceRequest request, CancellationToken ct) =>
        Ok(await recovery.DismantleAsync(id, request, ct));

    /// <summary>Requires a completed safety training record.</summary>
    [HttpPost("{id:guid}/components")]
    [Authorize(Policy = Policies.Workshop)]
    public async Task<ActionResult<ComponentDto>> RecordComponent(Guid id, RecordComponentRequest request, CancellationToken ct) =>
        Ok(await recovery.RecordComponentAsync(id, request, ct));
}

[Route("api/v{version:apiVersion}/recovery")]
[Authorize(Policy = Policies.Staff)]
public sealed class RecoveryController(IRecoveryService recovery) : ApiControllerBase
{
    [HttpGet("components")]
    public async Task<ActionResult<PagedResult<ComponentDto>>> Components([FromQuery] ComponentQuery query, CancellationToken ct) =>
        Ok(await recovery.ListComponentsAsync(query, ct));

    /// <summary>Logs diverted hazardous material (lithium, lead...). Requires a completed safety training record.</summary>
    [HttpPost("hazardous-materials")]
    [Authorize(Policy = Policies.Workshop)]
    public async Task<ActionResult<HazardousMaterialLogDto>> LogHazardous(LogHazardousMaterialRequest request, CancellationToken ct) =>
        Ok(await recovery.LogHazardousMaterialAsync(request, ct));

    [HttpGet("hazardous-materials")]
    public async Task<ActionResult<PagedResult<HazardousMaterialLogDto>>> HazardousLogs([FromQuery] PageQuery query, CancellationToken ct) =>
        Ok(await recovery.ListHazardousLogsAsync(query, ct));

    [HttpGet("hazardous-materials/summary")]
    public async Task<ActionResult<HazardousSummaryDto>> HazardousSummary(CancellationToken ct) =>
        Ok(await recovery.GetHazardousSummaryAsync(ct));
}

[Route("api/v{version:apiVersion}/pricing")]
[Authorize(Policy = Policies.Staff)]
public sealed class PricingController(IPricingService pricing) : ApiControllerBase
{
    [HttpGet("matrix")]
    public async Task<ActionResult<IReadOnlyList<PricingRuleDto>>> Matrix(CancellationToken ct) => Ok(await pricing.GetMatrixAsync(ct));

    [HttpPut("matrix")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<PricingRuleDto>> Upsert(UpsertPricingRuleRequest request, CancellationToken ct) =>
        Ok(await pricing.UpsertAsync(request, ct));
}
