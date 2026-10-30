using CircuLedger.Application.Certificates;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Intake;
using CircuLedger.Application.Purchasing;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Geo;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CircuLedger.Application.Ussd;

/// <summary>Africa's Talking USSD callback fields (form-urlencoded).</summary>
public sealed record UssdCallbackRequest(string SessionId, string ServiceCode, string PhoneNumber, string? Text, string? NetworkCode = null);

public sealed class UssdCallbackRequestValidator : AbstractValidator<UssdCallbackRequest>
{
    public UssdCallbackRequestValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ServiceCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Text).MaximumLength(500);
    }
}

public interface IUssdService
{
    Task<string> HandleAsync(UssdCallbackRequest request, CancellationToken ct = default);
}

public sealed class UssdService(
    IApplicationDbContext db,
    IPersonalDataProtector protector,
    IIntakeService intake,
    IPricingService pricing,
    ICertificateService certificates,
    IGeocoder geocoder,
    IOptions<UssdOptions> options,
    ILogger<UssdService> logger) : IUssdService
{
    public async Task<string> HandleAsync(UssdCallbackRequest request, CancellationToken ct = default)
    {
        var phone = protector.NormalizePhone(request.PhoneNumber);
        if (phone is null) return "END Sorry, we could not read your phone number.";
        var phoneHash = protector.HashPhone(phone);
        var text = request.Text ?? string.Empty;

        var session = await db.UssdSessions.FirstOrDefaultAsync(s => s.SessionId == request.SessionId, ct);
        if (session is not null)
        {
            if (session.PhoneHash != phoneHash) return "END Session error. Please dial again.";
            // Gateway retry of the same hop: replay the exact previous answer, with no side effects.
            if (session.LastText == text && session.Hops > 0) return session.LastResponse;
        }
        else
        {
            session = new UssdSession { SessionId = request.SessionId, ServiceCode = request.ServiceCode, PhoneHash = phoneHash };
            db.UssdSessions.Add(session);
        }

        var response = await RespondAsync(UssdMenu.Evaluate(text, await BuildContextAsync(ct)), session, phone, phoneHash, ct);

        session.LastText = text;
        session.LastResponse = response;
        session.Hops++;
        session.IsCompleted = response.StartsWith("END", StringComparison.Ordinal);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent duplicate hop for a brand-new session lost the insert race. The answer is still correct.
            logger.LogWarning(ex, "USSD session {SessionId} state could not be saved", request.SessionId);
        }
        return response;
    }

    private async Task<string> RespondAsync(UssdStep step, UssdSession session, string phone, string phoneHash, CancellationToken ct) => step switch
    {
        UssdScreen screen => screen.Render(),
        UssdSellConfirmed sell => await ConfirmSellAsync(sell, session, phone, ct),
        UssdStatusLookup lookup => await StatusAsync(lookup.ReferenceCode, phoneHash, ct),
        UssdCertificateLookup cert => await CertificateAsync(cert.CertificateNumber, ct),
        UssdNearestPoints nearest => await NearestAsync(nearest.Area, ct),
        _ => "END Invalid choice. Please dial again."
    };

    private async Task<string> ConfirmSellAsync(UssdSellConfirmed sell, UssdSession session, string phone, CancellationToken ct)
    {
        // Idempotent: one session can create at most one intake request (also enforced by a unique index).
        var existing = await intake.FindByUssdSessionAsync(session.SessionId, ct);
        if (existing is null)
        {
            try
            {
                existing = await intake.CreateAsync(new CreateIntakeRequest(
                    phone, sell.Brand, null, sell.Condition, sell.Method, sell.DropOffPointId, sell.PickupArea,
                    null, null, IntakeChannel.Ussd), session.SessionId, ct);
            }
            catch (DbUpdateException)
            {
                existing = await intake.FindByUssdSessionAsync(session.SessionId, ct)
                           ?? throw new InvalidOperationException("USSD intake creation failed.");
            }
        }

        session.IntakeRequestId = existing.Id;
        var next = existing.FulfilmentMethod == FulfilmentMethod.DropOff
            ? $"Take it to {existing.DropOffPointName}."
            : "An agent will call you about pickup.";
        return $"END Thank you! Ref: {existing.ReferenceCode}\n{next}\nWe'll also SMS you.";
    }

    private async Task<string> StatusAsync(string referenceCode, string phoneHash, CancellationToken ct)
    {
        // Only show status to the phone that created the request, so reference codes can't be enumerated.
        var request = await db.IntakeRequests.AsNoTracking()
            .Include(r => r.Device)
            .FirstOrDefaultAsync(r => r.ReferenceCode == referenceCode && r.Citizen!.PhoneHash == phoneHash, ct);
        if (request is null) return $"END No request {referenceCode} found for this number.";

        var status = request.Status switch
        {
            IntakeStatus.Pending => "Pending - we will contact you soon",
            IntakeStatus.Scheduled => "Pickup scheduled",
            IntakeStatus.Collected => "Collected by our agent",
            IntakeStatus.Received => $"Received at hub. Tracking {request.Device?.TrackingId}",
            IntakeStatus.Rejected => "Not accepted",
            IntakeStatus.Cancelled => "Cancelled",
            _ => request.Status.ToString()
        };
        return $"END Ref {request.ReferenceCode}: {status}";
    }

    private async Task<string> CertificateAsync(string number, CancellationToken ct)
    {
        var result = await certificates.VerifyPublicAsync(number, ct);
        return result.IsValid
            ? $"END Certificate {result.CertificateNumber}: VALID\nIssued {result.IssuedAt:yyyy-MM-dd}\n{result.Device} IMEI {result.MaskedImei}\nData securely wiped."
            : $"END Certificate {result.CertificateNumber}: {result.Status.ToUpperInvariant()}";
    }

    private async Task<string> NearestAsync(string area, CancellationToken ct)
    {
        var points = await db.DropOffPoints.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        if (points.Count == 0) return "END No drop-off points are open right now.";

        var origin = geocoder.TryGeocode(area);
        var ranked = origin is { } o
            ? points.Select(p => (Point: p, Km: (double?)Haversine.DistanceKm(o, new GeoPoint(p.Latitude, p.Longitude)))).OrderBy(x => x.Km)
            : points.Select(p => (Point: p, Km: (double?)null)).OrderBy(x => x.Point.DisplayOrder);

        var lines = ranked.Take(3).Select((x, i) =>
            $"{i + 1}. {x.Point.Name}{(x.Km is { } km ? $" ({km:0.#}km)" : string.Empty)} {x.Point.OpeningHours}");
        var header = origin is null ? "Drop-off points:" : $"Nearest to {area}:";
        return $"END {header}\n{string.Join('\n', lines)}";
    }

    private async Task<UssdMenuContext> BuildContextAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var points = await db.DropOffPoints.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .Take(opts.MaxDropOffPointsListed)
            .Select(p => new UssdDropOffOption(p.Id, p.Name, p.OpeningHours))
            .ToListAsync(ct);
        var rules = await pricing.GetActiveRulesAsync(ct);
        return new UssdMenuContext(opts.Brands, points, (brand, condition) => PricingMatrix.Resolve(rules, brand, condition)?.PriceSle);
    }
}
