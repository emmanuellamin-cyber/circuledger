using System.Text;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Application.Ussd;

public sealed record UssdDropOffOption(Guid Id, string Name, string OpeningHours);

public sealed record UssdMenuContext(
    IReadOnlyList<string> Brands,
    IReadOnlyList<UssdDropOffOption> DropOffPoints,
    Func<string, DeviceCondition, decimal?> Quote);

/// <summary>The result of evaluating the cumulative USSD input: either a screen to show, or an action to perform.</summary>
public abstract record UssdStep;

public sealed record UssdScreen(string Text, bool IsFinal) : UssdStep
{
    public string Render() => (IsFinal ? "END " : "CON ") + Text;
}

public sealed record UssdSellConfirmed(
    string Brand,
    DeviceCondition Condition,
    FulfilmentMethod Method,
    Guid? DropOffPointId,
    string? PickupArea) : UssdStep;

public sealed record UssdStatusLookup(string ReferenceCode) : UssdStep;

public sealed record UssdCertificateLookup(string CertificateNumber) : UssdStep;

public sealed record UssdNearestPoints(string Area) : UssdStep;

/// <summary>
/// Stateless USSD menu. Africa's Talking sends the full input history as "1*2*3", so every screen is a pure
/// function of that text. That makes retries naturally idempotent and keeps the engine unit-testable.
/// </summary>
public static class UssdMenu
{
    public const string MainMenu =
        "Smart Youth Circularity Hub\n1. Sell a phone\n2. Check request status\n3. Verify certificate\n4. Nearest drop-off points";

    private static readonly (DeviceCondition Condition, string Label)[] Conditions =
    [
        (DeviceCondition.Dead, "Dead (won't switch on)"),
        (DeviceCondition.BrokenScreen, "Broken screen"),
        (DeviceCondition.WorkingFaulty, "Working but faulty")
    ];

    public static UssdStep Evaluate(string? text, UssdMenuContext ctx)
    {
        var parts = string.IsNullOrEmpty(text) ? [] : text.Split('*').Select(p => p.Trim()).ToArray();
        if (parts.Length == 0) return Continue(MainMenu);

        return parts[0] switch
        {
            "1" => Sell(parts[1..], ctx),
            "2" => parts.Length == 1
                ? Continue("Enter your request reference code (e.g. SY7K3Q2M):")
                : Lookup(parts, p => new UssdStatusLookup(p.ToUpperInvariant())),
            "3" => parts.Length == 1
                ? Continue("Enter the certificate number (e.g. CDS-2026-000045):")
                : Lookup(parts, p => new UssdCertificateLookup(p.ToUpperInvariant())),
            "4" => parts.Length == 1
                ? Continue("Enter your town or area (e.g. Njala, Bo, Towama):")
                : Lookup(parts, p => new UssdNearestPoints(p)),
            _ => Invalid()
        };
    }

    private static UssdStep Sell(string[] p, UssdMenuContext ctx)
    {
        // p[0]=brand, p[1]=condition, p[2]=method, p[3]=drop-off point or area, p[4]=confirm
        if (p.Length == 0) return Continue(Numbered("Select phone brand:", ctx.Brands));
        if (!TryPick(p[0], ctx.Brands.Count, out var brandIndex)) return Invalid();
        var brand = ctx.Brands[brandIndex];

        if (p.Length == 1) return Continue(Numbered("Phone condition:", Conditions.Select(c => c.Label).ToList()));
        if (!TryPick(p[1], Conditions.Length, out var conditionIndex)) return Invalid();
        var condition = Conditions[conditionIndex];

        if (p.Length == 2) return Continue("How will you hand it over?\n1. Drop-off point\n2. Request pickup");

        switch (p[2])
        {
            case "1":
            {
                if (ctx.DropOffPoints.Count == 0) return End("No drop-off points are open right now. Please choose pickup.");
                if (p.Length == 3) return Continue(Numbered("Choose drop-off point:", ctx.DropOffPoints.Select(d => d.Name).ToList()));
                if (!TryPick(p[3], ctx.DropOffPoints.Count, out var pointIndex)) return Invalid();
                var point = ctx.DropOffPoints[pointIndex];
                if (p.Length == 4) return Confirm(brand, condition, $"Drop-off: {point.Name}", ctx);
                return p.Length == 5 ? Decide(p[4], new UssdSellConfirmed(brand, condition.Condition, FulfilmentMethod.DropOff, point.Id, null)) : Invalid();
            }
            case "2":
            {
                if (p.Length == 3) return Continue("Enter your area or nearest landmark:");
                var area = p[3];
                if (area.Length is < 2 or > 60) return End("Area must be 2-60 characters. Please dial again.");
                if (p.Length == 4) return Confirm(brand, condition, $"Pickup: {area}", ctx);
                return p.Length == 5 ? Decide(p[4], new UssdSellConfirmed(brand, condition.Condition, FulfilmentMethod.Pickup, null, area)) : Invalid();
            }
            default:
                return Invalid();
        }
    }

    private static UssdStep Confirm(string brand, (DeviceCondition Condition, string Label) condition, string handover, UssdMenuContext ctx)
    {
        var quote = ctx.Quote(brand, condition.Condition);
        var offer = quote is { } q ? $"\nEst. offer: SLE {q:N0}" : string.Empty;
        return Continue($"Confirm sale:\n{brand}, {condition.Label}\n{handover}{offer}\n1. Confirm\n2. Cancel");
    }

    private static UssdStep Decide(string choice, UssdSellConfirmed confirmed) => choice switch
    {
        "1" => confirmed,
        "2" => End("Request cancelled. Dial again any time."),
        _ => Invalid()
    };

    private static UssdStep Lookup(string[] parts, Func<string, UssdStep> make) =>
        parts.Length == 2 && parts[1].Length is >= 2 and <= 60 ? make(parts[1]) : Invalid();

    private static bool TryPick(string input, int count, out int index)
    {
        index = int.TryParse(input, out var n) ? n - 1 : -1;
        return index >= 0 && index < count;
    }

    private static string Numbered(string title, IReadOnlyList<string> options)
    {
        var sb = new StringBuilder(title);
        for (var i = 0; i < options.Count; i++) sb.Append('\n').Append(i + 1).Append(". ").Append(options[i]);
        return sb.ToString();
    }

    private static UssdScreen Continue(string text) => new(text, false);
    private static UssdScreen End(string text) => new(text, true);
    private static UssdScreen Invalid() => End("Invalid choice. Please dial again.");
}
