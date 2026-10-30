using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

public class RecoveredComponent : AuditableEntity
{
    public required string ComponentCode { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public ComponentType Type { get; set; }
    public Grade Grade { get; set; }
    public ComponentStatus Status { get; set; } = ComponentStatus.InStock;
    public string? Notes { get; set; }
    public required string RecoveredByUserId { get; set; }
}

public class HazardousMaterialLog : AuditableEntity
{
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public HazardousMaterial Material { get; set; }
    public string? Description { get; set; }
    public decimal WeightKg { get; set; }
    public required string StorageLocation { get; set; }
    public bool IsSwollenBattery { get; set; }
    public required string HandledByUserId { get; set; }
    public DateTime LoggedAt { get; set; }
}
