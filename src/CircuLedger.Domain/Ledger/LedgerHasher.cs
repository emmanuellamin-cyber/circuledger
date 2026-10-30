using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Ledger;

/// <summary>
/// Hash = SHA256(PreviousHash + payload + timestamp + metadata).
/// The spec formula (previous hash, payload, timestamp) comes first. The entry metadata (sequence, entity, event,
/// tracking ID, actor) is bound in too, so tampering with those columns also breaks the chain.
/// </summary>
public static class LedgerHasher
{
    public static readonly string GenesisHash = new('0', 64);

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>PostgreSQL timestamptz keeps microseconds. Truncate so the value we hash is the value we read back.</summary>
    public static DateTime NormalizeTimestamp(DateTime timestamp)
    {
        var utc = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    public static string ComputeHash(
        string previousHash,
        string canonicalPayload,
        DateTime timestamp,
        long sequenceNumber,
        string entityType,
        string entityId,
        LedgerEventType eventType,
        string? trackingId,
        string actor)
    {
        var material = string.Join('|',
            previousHash,
            canonicalPayload,
            NormalizeTimestamp(timestamp).ToString(TimestampFormat, CultureInfo.InvariantCulture),
            sequenceNumber.ToString(CultureInfo.InvariantCulture),
            entityType,
            entityId,
            eventType.ToString(),
            trackingId ?? string.Empty,
            actor);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>Recomputes the hash of a stored entry. The payload is re-canonicalised because jsonb reformats it.</summary>
    public static string ComputeHash(LedgerEntry entry) => ComputeHash(
        entry.PreviousHash,
        JsonCanonicalizer.Canonicalize(entry.Payload),
        entry.Timestamp,
        entry.SequenceNumber,
        entry.EntityType,
        entry.EntityId,
        entry.EventType,
        entry.TrackingId,
        entry.Actor);
}
