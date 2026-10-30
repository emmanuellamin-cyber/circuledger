using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Geo;
using CircuLedger.Domain.Services;

namespace CircuLedger.Tests.Unit;

public class DispatchBatcherTests
{
    private static readonly GeoPoint Njala = new(8.1131, -12.0783);
    private static readonly GeoPoint Towama = new(7.9786, -11.7519);
    private static readonly GeoPoint Kowama = new(7.9523, -11.7296);

    private static BatchCandidate At(GeoPoint p, int minutes) =>
        new(Guid.NewGuid(), p, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddMinutes(minutes));

    [Fact]
    public void Haversine_distance_between_Njala_and_Bo_is_about_38_km()
    {
        var km = Haversine.DistanceKm(Njala, new GeoPoint(7.9647, -11.7383));
        Assert.InRange(km, 36, 42);
    }

    [Fact]
    public void Nearby_requests_are_grouped_and_distant_ones_split()
    {
        var njala1 = At(Njala, 0);
        var njala2 = At(new GeoPoint(Njala.Latitude + 0.003, Njala.Longitude), 1); // ~330 m away
        var towama = At(Towama, 2);

        var batches = DispatchBatcher.Group([njala1, njala2, towama], radiusKm: 3, maxPerBatch: 10);

        Assert.Equal(2, batches.Count);
        Assert.Equal(2, batches[0].RequestIds.Count);
        Assert.Contains(njala1.RequestId, batches[0].RequestIds);
        Assert.Contains(njala2.RequestId, batches[0].RequestIds);
        Assert.Equal(TransportMode.Foot, batches[0].SuggestedTransport);
        Assert.Equal(towama.RequestId, Assert.Single(batches[1].RequestIds));
    }

    [Fact]
    public void Towama_and_Kowama_fall_into_one_batch_with_a_5km_radius_but_not_2km()
    {
        var a = At(Towama, 0);
        var b = At(Kowama, 1);

        Assert.Single(DispatchBatcher.Group([a, b], 5, 10));
        Assert.Equal(2, DispatchBatcher.Group([a, b], 2, 10).Count);
    }

    [Fact]
    public void Batches_respect_the_maximum_size()
    {
        var candidates = Enumerable.Range(0, 7).Select(i => At(new GeoPoint(Njala.Latitude + i * 0.0005, Njala.Longitude), i)).ToList();

        var batches = DispatchBatcher.Group(candidates, 3, maxPerBatch: 3);

        Assert.Equal([3, 3, 1], batches.Select(b => b.RequestIds.Count));
        Assert.Equal(7, batches.SelectMany(b => b.RequestIds).Distinct().Count());
    }

    [Fact]
    public void Oldest_request_seeds_the_first_batch()
    {
        var older = At(Towama, 0);
        var newer = At(Njala, 10);

        var batches = DispatchBatcher.Group([newer, older], 3, 10);

        Assert.Equal(older.RequestId, batches[0].RequestIds[0]);
    }

    [Theory]
    [InlineData(0.5, TransportMode.Foot)]
    [InlineData(4, TransportMode.Bicycle)]
    [InlineData(12, TransportMode.Motorcycle)]
    public void Transport_mode_is_suggested_from_spread(double spreadKm, TransportMode expected)
    {
        Assert.Equal(expected, DispatchBatcher.SuggestTransport(spreadKm));
    }

    [Fact]
    public void Empty_input_yields_no_batches()
    {
        Assert.Empty(DispatchBatcher.Group([], 3, 10));
    }
}
