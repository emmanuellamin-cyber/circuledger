using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

public class DispatchBatch : AuditableEntity
{
    public required string BatchCode { get; set; }
    public required string FieldAgentUserId { get; set; }
    public TransportMode TransportMode { get; set; }
    public DispatchBatchStatus Status { get; set; } = DispatchBatchStatus.Planned;
    public double CentroidLatitude { get; set; }
    public double CentroidLongitude { get; set; }
    public double RadiusKm { get; set; }
    public double SpreadKm { get; set; }
    public DateOnly ScheduledFor { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<IntakeRequest> Requests { get; set; } = [];
}
