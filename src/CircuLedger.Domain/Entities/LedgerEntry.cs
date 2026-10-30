using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

/// <summary>
/// An append-only, hash-chained event. It's deliberately not an <c>AuditableEntity</c>: it can never be updated or soft-deleted.
/// </summary>
public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public long SequenceNumber { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }

    /// <summary>The device tracking ID this event belongs to, when known. Used for the per-device history.</summary>
    public string? TrackingId { get; set; }
    public LedgerEventType EventType { get; set; }

    /// <summary>Canonical JSON (sorted keys, no whitespace), stored as jsonb.</summary>
    public required string Payload { get; set; }
    public DateTime Timestamp { get; set; }
    public required string Actor { get; set; }
    public required string PreviousHash { get; set; }
    public required string Hash { get; set; }
}
