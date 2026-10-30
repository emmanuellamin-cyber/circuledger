using System.Text.Json;
using CircuLedger.Application.Common;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Ledger;

namespace CircuLedger.Application.Ledger;

public sealed record LedgerEntryDto(
    long SequenceNumber,
    string EntityType,
    string EntityId,
    string? TrackingId,
    LedgerEventType EventType,
    JsonElement Payload,
    DateTime Timestamp,
    string Actor,
    string PreviousHash,
    string Hash)
{
    public static LedgerEntryDto From(LedgerEntry e) => new(
        e.SequenceNumber, e.EntityType, e.EntityId, e.TrackingId, e.EventType,
        JsonDocument.Parse(e.Payload).RootElement.Clone(), e.Timestamp, e.Actor, e.PreviousHash, e.Hash);
}

public sealed record DeviceHistoryDto(string TrackingId, string Brand, string? Model, DeviceStatus Status, IReadOnlyList<LedgerEntryDto> Events);

public sealed record LedgerQuery : PageQuery
{
    public string? EntityType { get; init; }
    public LedgerEventType? EventType { get; init; }
}

/// <summary>
/// Append-only, hash-chained event log. Every write is serialised with a transaction-scoped lock
/// so two concurrent writers can never chain off the same previous hash.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Appends an entry and saves pending changes in the current transaction (or a new one).
    /// The payload must not contain personal data.
    /// </summary>
    Task<LedgerEntry> AppendAsync(
        LedgerEventType eventType,
        string entityType,
        Guid entityId,
        object payload,
        string? trackingId = null,
        CancellationToken cancellationToken = default);

    Task<DeviceHistoryDto> GetDeviceHistoryAsync(string trackingId, CancellationToken cancellationToken = default);

    Task<PagedResult<LedgerEntryDto>> ListAsync(LedgerQuery query, CancellationToken cancellationToken = default);

    Task<LedgerVerificationResult> VerifyAsync(CancellationToken cancellationToken = default);
}
