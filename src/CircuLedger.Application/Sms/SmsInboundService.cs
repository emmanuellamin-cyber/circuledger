using CircuLedger.Application.Certificates;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Intake;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Sms;

/// <summary>Africa's Talking inbound SMS fields (form-urlencoded).</summary>
public sealed record InboundSmsRequest(string From, string Text, string? To = null, string? Id = null, string? Date = null, string? LinkId = null);

public sealed record InboundSmsResult(string Keyword, string Reply);

public sealed class InboundSmsRequestValidator : AbstractValidator<InboundSmsRequest>
{
    public InboundSmsRequestValidator()
    {
        RuleFor(x => x.From).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Text).NotNull().MaximumLength(480);
        RuleFor(x => x.Id).MaximumLength(100);
    }
}

public interface ISmsInboundService
{
    Task<InboundSmsResult> HandleAsync(InboundSmsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Keyword commands for feature phones:
/// SELL &lt;brand&gt; &lt;DEAD|SCREEN|FAULTY&gt; &lt;area...&gt; · STATUS &lt;ref&gt; · VERIFY &lt;certificate&gt; · HELP
/// </summary>
public sealed class SmsInboundService(
    IApplicationDbContext db,
    IPersonalDataProtector protector,
    IIntakeService intake,
    ICertificateService certificates,
    CitizenNotifier notifier,
    TimeProvider time) : ISmsInboundService
{
    private const string Help = "CircuHub: SELL <brand> <DEAD|SCREEN|FAULTY> <area> | STATUS <ref> | VERIFY <certificate no.>";

    public async Task<InboundSmsResult> HandleAsync(InboundSmsRequest request, CancellationToken ct = default)
    {
        var phone = protector.NormalizePhone(request.From);
        if (phone is null) return new InboundSmsResult("INVALID", "Unrecognised sender.");
        var phoneHash = protector.HashPhone(phone);

        if (request.Id is not null)
        {
            var duplicate = await db.InboundSms.AsNoTracking().FirstOrDefaultAsync(m => m.ProviderMessageId == request.Id, ct);
            if (duplicate is not null) return new InboundSmsResult(duplicate.Keyword, duplicate.ReplySummary ?? string.Empty);
        }

        var words = request.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keyword = words.Length == 0 ? "HELP" : words[0].ToUpperInvariant();

        var (reply, alreadyNotified) = keyword switch
        {
            "SELL" => await SellAsync(words, phone, ct),
            "STATUS" when words.Length >= 2 => (await StatusAsync(words[1], phoneHash, ct), false),
            "VERIFY" when words.Length >= 2 => (await VerifyAsync(words[1], ct), false),
            _ => (Help, false)
        };

        db.InboundSms.Add(new InboundSms
        {
            ProviderMessageId = request.Id,
            PhoneHash = phoneHash,
            Keyword = keyword.Length > 20 ? keyword[..20] : keyword,
            Body = request.Text.Length > 480 ? request.Text[..480] : request.Text,
            ReceivedAt = time.GetUtcNow().UtcDateTime,
            ReplySummary = reply.Length > 480 ? reply[..480] : reply
        });
        await db.SaveChangesAsync(ct);

        // A successful SELL has already been confirmed by SMS from IntakeService.
        if (!alreadyNotified)
            await notifier.NotifyPhoneAsync(phone, reply, ct);

        return new InboundSmsResult(keyword, reply);
    }

    private async Task<(string Reply, bool Notified)> SellAsync(string[] words, string phone, CancellationToken ct)
    {
        if (words.Length < 4) return (Help, false);
        DeviceCondition? condition = words[2].ToUpperInvariant() switch
        {
            "DEAD" => DeviceCondition.Dead,
            "SCREEN" or "BROKEN" => DeviceCondition.BrokenScreen,
            "FAULTY" or "WORKING" => DeviceCondition.WorkingFaulty,
            _ => null
        };
        if (condition is null) return (Help, false);

        var area = string.Join(' ', words[3..]);
        if (area.Length > 160) area = area[..160];
        var created = await intake.CreateAsync(new CreateIntakeRequest(
            phone, words[1], null, condition.Value, FulfilmentMethod.Pickup, null, area, null, null, IntakeChannel.Sms), null, ct);
        return ($"Ref {created.ReferenceCode} created for pickup at {area}.", true);
    }

    private async Task<string> StatusAsync(string reference, string phoneHash, CancellationToken ct)
    {
        var code = reference.ToUpperInvariant();
        var request = await db.IntakeRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReferenceCode == code && r.Citizen!.PhoneHash == phoneHash, ct);
        return request is null ? $"CircuHub: no request {code} for this number." : $"CircuHub: {code} is {request.Status}.";
    }

    private async Task<string> VerifyAsync(string number, CancellationToken ct)
    {
        var result = await certificates.VerifyPublicAsync(number.ToUpperInvariant(), ct);
        return result.IsValid
            ? $"CircuHub: certificate {result.CertificateNumber} is VALID. Issued {result.IssuedAt:yyyy-MM-dd} for {result.Device}."
            : $"CircuHub: certificate {result.CertificateNumber} is {result.Status.ToUpperInvariant()}.";
    }
}
