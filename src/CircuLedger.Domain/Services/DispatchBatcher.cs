using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Geo;

namespace CircuLedger.Domain.Services;

public sealed record BatchCandidate(Guid RequestId, GeoPoint Location, DateTime RequestedAt);

public sealed record ProposedBatch(
    IReadOnlyList<Guid> RequestIds,
    GeoPoint Centroid,
    double SpreadKm,
    TransportMode SuggestedTransport);

/// <summary>
/// Greedy proximity clustering. The oldest unassigned request seeds a batch, which then takes the nearest
/// unassigned requests within <c>radiusKm</c> of the seed, up to <c>maxPerBatch</c>. Oldest-first keeps waiting times fair.
/// </summary>
public static class DispatchBatcher
{
    public static IReadOnlyList<ProposedBatch> Group(IEnumerable<BatchCandidate> candidates, double radiusKm, int maxPerBatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusKm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPerBatch);

        var remaining = candidates
            .OrderBy(c => c.RequestedAt)
            .ThenBy(c => c.RequestId)
            .ToList();
        var batches = new List<ProposedBatch>();

        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            var members = remaining
                .Select(c => (Candidate: c, Distance: Haversine.DistanceKm(seed.Location, c.Location)))
                .Where(x => x.Distance <= radiusKm)
                .OrderBy(x => x.Distance)
                .ThenBy(x => x.Candidate.RequestedAt)
                .Take(maxPerBatch)
                .Select(x => x.Candidate)
                .ToList();

            var points = members.Select(m => m.Location).ToList();
            var centroid = Haversine.Centroid(points);
            var spread = points.Max(p => Haversine.DistanceKm(centroid, p));

            batches.Add(new ProposedBatch(members.Select(m => m.RequestId).ToList(), centroid, Math.Round(spread, 3), SuggestTransport(spread)));
            foreach (var member in members) remaining.Remove(member);
        }

        return batches;
    }

    /// <summary>Walkable clusters go on foot, town-scale clusters by bicycle, and anything wider by motorcycle.</summary>
    public static TransportMode SuggestTransport(double spreadKm) => spreadKm switch
    {
        <= 1.5 => TransportMode.Foot,
        <= 6 => TransportMode.Bicycle,
        _ => TransportMode.Motorcycle
    };
}
