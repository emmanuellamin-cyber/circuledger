using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Ledger;

namespace CircuLedger.Tests.Unit;

public class LedgerHashChainTests
{
    private static List<LedgerEntry> BuildChain(int count)
    {
        var entries = new List<LedgerEntry>();
        var previous = LedgerHasher.GenesisHash;
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= count; i++)
        {
            var payload = JsonCanonicalizer.Serialize(new { TrackingId = $"CL-2026-{i:D6}", PriceSle = 150.50m + i, Brand = "Samsung" });
            var timestamp = LedgerHasher.NormalizeTimestamp(start.AddMinutes(i).AddTicks(1234567));
            var entry = new LedgerEntry
            {
                SequenceNumber = i,
                EntityType = "Device",
                EntityId = Guid.NewGuid().ToString(),
                TrackingId = $"CL-2026-{i:D6}",
                EventType = LedgerEventType.Purchased,
                Payload = payload,
                Timestamp = timestamp,
                Actor = "tester",
                PreviousHash = previous,
                Hash = string.Empty
            };
            entry.Hash = LedgerHasher.ComputeHash(entry);
            previous = entry.Hash;
            entries.Add(entry);
        }
        return entries;
    }

    [Fact]
    public void Valid_chain_verifies()
    {
        var result = LedgerChainVerifier.Verify(BuildChain(5));

        Assert.True(result.IsValid);
        Assert.Equal(5, result.EntriesChecked);
        Assert.Null(result.FirstBrokenSequence);
    }

    [Fact]
    public void First_entry_links_to_genesis_hash()
    {
        var chain = BuildChain(2);
        Assert.Equal(new string('0', 64), chain[0].PreviousHash);
        Assert.Equal(chain[0].Hash, chain[1].PreviousHash);
    }

    [Fact]
    public void Tampered_payload_is_reported_at_its_sequence()
    {
        var chain = BuildChain(5);
        chain[2].Payload = chain[2].Payload.Replace("153.50", "9999.00");

        var result = LedgerChainVerifier.Verify(chain);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.FirstBrokenSequence);
        Assert.Contains("recomputed", result.Reason);
    }

    [Fact]
    public void Tampered_metadata_is_detected()
    {
        var chain = BuildChain(3);
        chain[1].EventType = LedgerEventType.Sold;

        Assert.Equal(2, LedgerChainVerifier.Verify(chain).FirstBrokenSequence);
    }

    [Fact]
    public void Deleted_entry_breaks_the_previous_hash_link()
    {
        var chain = BuildChain(4);
        chain.RemoveAt(1);

        var result = LedgerChainVerifier.Verify(chain);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.FirstBrokenSequence);
        Assert.Contains("PreviousHash", result.Reason);
    }

    [Fact]
    public void Recomputed_hash_after_rehashing_one_entry_still_breaks_the_next_link()
    {
        var chain = BuildChain(4);
        chain[1].Payload = JsonCanonicalizer.Serialize(new { Forged = true });
        chain[1].Hash = LedgerHasher.ComputeHash(chain[1]); // attacker recomputes this entry's hash

        Assert.Equal(3, LedgerChainVerifier.Verify(chain).FirstBrokenSequence);
    }

    [Fact]
    public void Jsonb_reformatting_does_not_break_verification()
    {
        var chain = BuildChain(1);
        // PostgreSQL jsonb returns keys reordered with whitespace, e.g. {"brand": "Samsung", "priceSle": 151.50, ...}
        chain[0].Payload = """{"brand": "Samsung", "priceSle": 151.50, "trackingId": "CL-2026-000001"}""";

        Assert.True(LedgerChainVerifier.Verify(chain).IsValid);
    }

    [Fact]
    public void Timestamps_are_truncated_to_postgres_microsecond_precision()
    {
        var withTicks = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var normalized = LedgerHasher.NormalizeTimestamp(withTicks);

        Assert.Equal(0, normalized.Ticks % 10);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }
}
