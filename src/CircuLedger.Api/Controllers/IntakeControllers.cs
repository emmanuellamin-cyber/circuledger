using CircuLedger.Application.Common;
using CircuLedger.Application.Devices;
using CircuLedger.Application.Dispatch;
using CircuLedger.Application.DropOffPoints;
using CircuLedger.Application.Intake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CircuLedger.Api.Controllers;

[Route("api/v{version:apiVersion}/drop-off-points")]
[Authorize]
public sealed class DropOffPointsController(IDropOffPointService points) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<IReadOnlyList<DropOffPointDto>>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        Ok(await points.ListAsync(includeInactive, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<DropOffPointDto>> Get(Guid id, CancellationToken ct) => Ok(await points.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<DropOffPointDto>> Create(UpsertDropOffPointRequest request, CancellationToken ct)
    {
        var created = await points.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1" }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<DropOffPointDto>> Update(Guid id, UpsertDropOffPointRequest request, CancellationToken ct) =>
        Ok(await points.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await points.DeleteAsync(id, ct);
        return NoContent();
    }
}

[Route("api/v{version:apiVersion}/intake-requests")]
[Authorize]
public sealed class IntakeRequestsController(IIntakeService intake) : ApiControllerBase
{
    /// <summary>Creates a sell request on behalf of a citizen (web/phone channel). Citizens can also self-serve via USSD/SMS.</summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.OperationsLead},{Roles.Technician},{Roles.FieldAgent},{Roles.Citizen}")]
    public async Task<ActionResult<IntakeRequestDto>> Create(CreateIntakeRequest request, CancellationToken ct)
    {
        var created = await intake.CreateAsync(request, null, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1" }, created);
    }

    [HttpGet]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<PagedResult<IntakeRequestDto>>> List([FromQuery] IntakeListQuery query, CancellationToken ct) =>
        Ok(await intake.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<IntakeRequestDto>> Get(Guid id, CancellationToken ct) => Ok(await intake.GetAsync(id, ct));

    /// <summary>Sets GPS coordinates for a pickup whose USSD area text could not be geocoded.</summary>
    [HttpPut("{id:guid}/pickup-location")]
    [Authorize(Policy = Policies.Field)]
    public async Task<ActionResult<IntakeRequestDto>> SetPickupLocation(Guid id, SetPickupLocationRequest request, CancellationToken ct) =>
        Ok(await intake.SetPickupLocationAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<IntakeRequestDto>> Cancel(Guid id, CloseIntakeRequest request, CancellationToken ct) =>
        Ok(await intake.CancelAsync(id, request, ct));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<IntakeRequestDto>> Reject(Guid id, CloseIntakeRequest request, CancellationToken ct) =>
        Ok(await intake.RejectAsync(id, request, ct));

    /// <summary>Hub staff receive the phone. This creates the Device and its tracking ID (CL-YYYY-NNNNNN).</summary>
    [HttpPost("{id:guid}/receive")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<DeviceDto>> Receive(Guid id, ReceiveIntakeRequest request, CancellationToken ct) =>
        Ok(await intake.ReceiveAsync(id, request, ct));

    /// <summary>A walk-in seller at a drop-off point: creates the intake request and the device in one step.</summary>
    [HttpPost("walk-in")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<DeviceDto>> WalkIn(WalkInIntakeRequest request, CancellationToken ct) =>
        Ok(await intake.RegisterWalkInAsync(request, ct));
}

[Route("api/v{version:apiVersion}/dispatch")]
[Authorize(Policy = Policies.Field)]
public sealed class DispatchController(IDispatchService dispatch) : ApiControllerBase
{
    /// <summary>Groups pending pickups by proximity (Haversine, configurable radius) and assigns them to field agents.</summary>
    [HttpPost("batches/generate")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<GenerateBatchesResult>> Generate(GenerateBatchesRequest request, CancellationToken ct) =>
        Ok(await dispatch.GenerateAsync(request, ct));

    [HttpGet("batches")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<PagedResult<DispatchBatchDto>>> List([FromQuery] DispatchBatchQuery query, CancellationToken ct) =>
        Ok(await dispatch.ListAsync(query, ct));

    [HttpGet("batches/{id:guid}")]
    public async Task<ActionResult<DispatchBatchDto>> Get(Guid id, CancellationToken ct) => Ok(await dispatch.GetAsync(id, ct));

    /// <summary>The signed-in field agent's open batches.</summary>
    [HttpGet("my-batches")]
    public async Task<ActionResult<IReadOnlyList<DispatchBatchDto>>> MyBatches(CancellationToken ct) =>
        Ok(await dispatch.GetAgentBatchesAsync(CurrentUserId, ct));

    [HttpPost("batches/{id:guid}/pickups/{intakeRequestId:guid}/collect")]
    public async Task<ActionResult<DispatchBatchDto>> Collect(Guid id, Guid intakeRequestId, CancellationToken ct) =>
        Ok(await dispatch.MarkCollectedAsync(id, intakeRequestId, ct));

    /// <summary>Reveals the seller's phone number to the assigned agent. Access is logged.</summary>
    [HttpGet("batches/{id:guid}/pickups/{intakeRequestId:guid}/contact")]
    public async Task<ActionResult<PickupContactDto>> Contact(Guid id, Guid intakeRequestId, CancellationToken ct) =>
        Ok(await dispatch.GetPickupContactAsync(id, intakeRequestId, ct));
}
