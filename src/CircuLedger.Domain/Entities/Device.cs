using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

public class Device : AuditableEntity
{
    /// <summary>Human-friendly unique ID, e.g. CL-2026-000123.</summary>
    public required string TrackingId { get; set; }
    public required string Brand { get; set; }
    public string? Model { get; set; }
    public string? ImeiHash { get; set; }
    public string? ImeiLast4 { get; set; }
    public DeviceCondition Condition { get; set; }
    public bool IsSamsungFlagship { get; set; }
    public IntakeChannel IntakeChannel { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Received;
    public RecoveryOutcome? RecoveryOutcome { get; set; }
    public DateTime? DismantledAt { get; set; }

    public Guid CitizenId { get; set; }
    public Citizen? Citizen { get; set; }
    public Guid IntakeRequestId { get; set; }
    public IntakeRequest? IntakeRequest { get; set; }
    public Guid? LocationId { get; set; }
    public DropOffPoint? Location { get; set; }

    public Purchase? Purchase { get; set; }
    public ICollection<SanitizationRecord> SanitizationRecords { get; set; } = [];
    public Certificate? Certificate { get; set; }
    public ICollection<RecoveredComponent> Components { get; set; } = [];

    public static bool IsSamsung(string brand) => brand.Trim().Equals("Samsung", StringComparison.OrdinalIgnoreCase);

    public string MaskedImei => ImeiLast4 is null ? "not recorded" : $"***********{ImeiLast4}";
}
