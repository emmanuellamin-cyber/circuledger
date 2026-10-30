using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Geo;
using Microsoft.Extensions.Options;

namespace CircuLedger.Infrastructure.Geo;

public sealed class GazetteerPlace
{
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class GazetteerOptions
{
    public const string Section = "Geo";
    public List<GazetteerPlace> Places { get; set; } = [];
}

/// <summary>
/// Offline geocoder for USSD free-text areas: matches known towns, neighbourhoods and landmarks from configuration.
/// Unmatched requests are left without coordinates, and an Operations Lead can set them manually.
/// </summary>
public sealed class GazetteerGeocoder(IOptions<GazetteerOptions> options) : IGeocoder
{
    private readonly List<(string Term, GeoPoint Point)> _terms = options.Value.Places
        .SelectMany(p => p.Aliases.Append(p.Name).Select(term => (Term: term.Trim().ToLowerInvariant(), Point: new GeoPoint(p.Latitude, p.Longitude))))
        .Where(t => t.Term.Length > 0)
        .OrderByDescending(t => t.Term.Length) // most specific first: "bo towama" beats "bo"
        .ToList();

    public GeoPoint? TryGeocode(string? areaOrLandmark)
    {
        if (string.IsNullOrWhiteSpace(areaOrLandmark)) return null;
        var text = $" {areaOrLandmark.Trim().ToLowerInvariant().Replace('/', ' ').Replace(',', ' ')} ";
        foreach (var (term, point) in _terms)
        {
            if (text.Contains($" {term} ", StringComparison.Ordinal)) return point;
        }
        return null;
    }
}
