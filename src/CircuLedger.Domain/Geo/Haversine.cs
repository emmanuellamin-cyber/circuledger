namespace CircuLedger.Domain.Geo;

public readonly record struct GeoPoint(double Latitude, double Longitude);

public static class Haversine
{
    private const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var lat1 = ToRadians(a.Latitude);
        var lat2 = ToRadians(b.Latitude);

        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static GeoPoint Centroid(IReadOnlyCollection<GeoPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));
        // Arithmetic mean is accurate enough at district scale (a few tens of km).
        return new GeoPoint(points.Average(p => p.Latitude), points.Average(p => p.Longitude));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
