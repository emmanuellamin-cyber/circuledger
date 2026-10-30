using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;

namespace CircuLedger.Domain.Entities;

/// <summary>A citizen's offer to sell one phone. Exactly one <see cref="Device"/> is created when it is received.</summary>
public class IntakeRequest : AuditableEntity
{
    public required string ReferenceCode { get; set; }
    public Guid CitizenId { get; set; }
    public Citizen? Citizen { get; set; }

    public required string Brand { get; set; }
    public string? Model { get; set; }
    public DeviceCondition ReportedCondition { get; set; }
    public IntakeChannel Channel { get; set; }
    public FulfilmentMethod FulfilmentMethod { get; set; }

    public Guid? DropOffPointId { get; set; }
    public DropOffPoint? DropOffPoint { get; set; }

    public string? PickupArea { get; set; }
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }

    public decimal? EstimatedOfferSle { get; set; }
    public IntakeStatus Status { get; set; } = IntakeStatus.Pending;

    public Guid? DispatchBatchId { get; set; }
    public DispatchBatch? DispatchBatch { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public DateTime? CollectedAt { get; set; }
    public string? CollectedByUserId { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public Guid? ReceivedAtDropOffPointId { get; set; }
    public string? ClosedReason { get; set; }

    /// <summary>USSD session that created this request. Unique, which makes USSD confirmation idempotent.</summary>
    public string? UssdSessionId { get; set; }

    public Device? Device { get; set; }

    public bool HasPickupCoordinates => PickupLatitude.HasValue && PickupLongitude.HasValue;

    public void Schedule(Guid batchId, DateTime now)
    {
        if (Status != IntakeStatus.Pending || FulfilmentMethod != FulfilmentMethod.Pickup)
            throw new InvalidStateTransitionException(nameof(IntakeRequest), Status, IntakeStatus.Scheduled);
        DispatchBatchId = batchId;
        ScheduledAt = now;
        Status = IntakeStatus.Scheduled;
    }

    public void MarkCollected(string agentUserId, DateTime now)
    {
        if (Status != IntakeStatus.Scheduled)
            throw new InvalidStateTransitionException(nameof(IntakeRequest), Status, IntakeStatus.Collected);
        CollectedByUserId = agentUserId;
        CollectedAt = now;
        Status = IntakeStatus.Collected;
    }

    public void MarkReceived(Guid dropOffPointId, DateTime now)
    {
        var allowed = FulfilmentMethod == FulfilmentMethod.DropOff
            ? Status is IntakeStatus.Pending
            : Status is IntakeStatus.Collected;
        if (!allowed)
            throw new InvalidStateTransitionException(nameof(IntakeRequest), Status, IntakeStatus.Received);
        ReceivedAtDropOffPointId = dropOffPointId;
        ReceivedAt = now;
        Status = IntakeStatus.Received;
    }

    public void Reject(string reason) => Close(IntakeStatus.Rejected, reason);

    public void Cancel(string reason) => Close(IntakeStatus.Cancelled, reason);

    private void Close(IntakeStatus target, string reason)
    {
        if (Status is IntakeStatus.Received or IntakeStatus.Rejected or IntakeStatus.Cancelled)
            throw new InvalidStateTransitionException(nameof(IntakeRequest), Status, target);
        Status = target;
        ClosedReason = reason;
        DispatchBatchId = null;
    }
}
