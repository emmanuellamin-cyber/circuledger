using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Ledger;
using CircuLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Infrastructure.Ledger;

public sealed class LedgerService(ApplicationDbContext db, ICurrentUser currentUser, TimeProvider time) : ILedgerService
{
    /// <summary>Key for the transaction-scoped advisory lock ("CLLEDGER" in ASCII).</summary>
    private const long LedgerLockKey = 0x434C4C4544474552;

    public Task<LedgerEntry> AppendAsync(
        LedgerEventType eventType,
        string entityType,
        Guid entityId,
        object payload,
        string? trackingId = null,
        CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync(async ct =>
        {
            // 1. Serialise writers. In READ COMMITTED, a writer blocked on "FOR UPDATE" of the old tail would
            //    re-check that same row after the other writer commits and chain off it, forking the ledger.
            //    The advisory lock makes the next statement take a fresh snapshot. It also covers an empty table.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LedgerLockKey})", ct);

            // 2. Row-lock the current tail (SELECT ... FOR UPDATE) and read its hash.
            var tail = await db.LedgerEntries
                .FromSql($"SELECT * FROM ledger_entries ORDER BY sequence_number DESC LIMIT 1 FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(ct);
            var previousHash = tail.Count == 0 ? LedgerHasher.GenesisHash : tail[0].Hash;

            // 3. The sequence is taken while holding the lock, so sequence order equals chain order.
            var sequence = await db.NextSequenceValueAsync(Sequences.Ledger, ct);
            var timestamp = LedgerHasher.NormalizeTimestamp(time.GetUtcNow().UtcDateTime);
            var canonicalPayload = JsonCanonicalizer.Serialize(payload);
            var entityIdText = entityId.ToString();
            var actor = currentUser.Actor;

            var entry = new LedgerEntry
            {
                SequenceNumber = sequence,
                EntityType = entityType,
                EntityId = entityIdText,
                TrackingId = trackingId,
                EventType = eventType,
                Payload = canonicalPayload,
                Timestamp = timestamp,
                Actor = actor,
                PreviousHash = previousHash,
                Hash = LedgerHasher.ComputeHash(previousHash, canonicalPayload, timestamp, sequence,
                    entityType, entityIdText, eventType, trackingId, actor)
            };

            db.LedgerEntries.Add(entry);
            // Saving here (inside the caller's transaction) means a second append in the same unit of work sees this tail.
            await db.SaveChangesAsync(ct);
            return entry;
        }, cancellationToken);

    public async Task<DeviceHistoryDto> GetDeviceHistoryAsync(string trackingId, CancellationToken cancellationToken = default)
    {
        var device = await db.Devices.AsNoTracking()
                         .FirstOrDefaultAsync(d => d.TrackingId == trackingId, cancellationToken)
                     ?? throw new NotFoundException("Device", trackingId);

        var intakeId = device.IntakeRequestId.ToString();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.TrackingId == trackingId
                        || (e.EntityType == LedgerEntityTypes.IntakeRequest && e.EntityId == intakeId))
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(cancellationToken);

        return new DeviceHistoryDto(device.TrackingId, device.Brand, device.Model, device.Status,
            entries.Select(LedgerEntryDto.From).ToList());
    }

    public Task<PagedResult<LedgerEntryDto>> ListAsync(LedgerQuery query, CancellationToken cancellationToken = default)
    {
        var q = db.LedgerEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(e => e.EntityType == query.EntityType);
        if (query.EventType is { } eventType) q = q.Where(e => e.EventType == eventType);
        return q.OrderByDescending(e => e.SequenceNumber).ToPagedAsync(query, LedgerEntryDto.From, cancellationToken);
    }

    public async Task<LedgerVerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var verifier = new LedgerChainVerifier();
        await foreach (var entry in db.LedgerEntries.AsNoTracking()
                           .OrderBy(e => e.SequenceNumber)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            if (!verifier.Check(entry)) break;
        }
        return verifier.Result();
    }
}
