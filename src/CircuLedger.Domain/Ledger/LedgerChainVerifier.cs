using CircuLedger.Domain.Entities;

namespace CircuLedger.Domain.Ledger;

public sealed record LedgerVerificationResult(
    bool IsValid,
    long EntriesChecked,
    long? FirstBrokenSequence,
    string? Reason,
    string? LastHash);

/// <summary>
/// Streams entries in sequence order and reports the first broken link. It's stateful so the
/// caller can feed it from a database cursor without loading the whole ledger into memory.
/// </summary>
public sealed class LedgerChainVerifier
{
    private string _expectedPreviousHash = LedgerHasher.GenesisHash;
    private long _lastSequence = long.MinValue;
    private long _checked;
    private long? _brokenAt;
    private string? _reason;

    public bool IsBroken => _brokenAt.HasValue;

    /// <summary>Returns false (and stops accepting entries) at the first broken link.</summary>
    public bool Check(LedgerEntry entry)
    {
        if (IsBroken) return false;
        _checked++;

        if (entry.SequenceNumber <= _lastSequence)
            return Fail(entry, $"Sequence {entry.SequenceNumber} is not strictly increasing after {_lastSequence}.");

        if (!string.Equals(entry.PreviousHash, _expectedPreviousHash, StringComparison.Ordinal))
            return Fail(entry, "PreviousHash does not match the hash of the preceding entry.");

        var recomputed = LedgerHasher.ComputeHash(entry);
        if (!string.Equals(recomputed, entry.Hash, StringComparison.Ordinal))
            return Fail(entry, "Stored hash does not match the recomputed hash (payload, timestamp or metadata was altered).");

        _expectedPreviousHash = entry.Hash;
        _lastSequence = entry.SequenceNumber;
        return true;
    }

    public LedgerVerificationResult Result() =>
        new(!IsBroken, _checked, _brokenAt, _reason, IsBroken ? null : _expectedPreviousHash);

    public static LedgerVerificationResult Verify(IEnumerable<LedgerEntry> orderedEntries)
    {
        var verifier = new LedgerChainVerifier();
        foreach (var entry in orderedEntries)
        {
            if (!verifier.Check(entry)) break;
        }
        return verifier.Result();
    }

    private bool Fail(LedgerEntry entry, string reason)
    {
        _brokenAt = entry.SequenceNumber;
        _reason = reason;
        return false;
    }
}
