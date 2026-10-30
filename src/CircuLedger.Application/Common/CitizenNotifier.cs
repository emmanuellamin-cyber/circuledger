using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CircuLedger.Application.Common;

/// <summary>
/// Sends SMS to a citizen without ever logging the phone number. SMS failures are logged but never
/// fail the business operation, because the data has already been committed.
/// </summary>
public sealed class CitizenNotifier(IPersonalDataProtector protector, ISmsSender sms, ILogger<CitizenNotifier> logger)
{
    public async Task NotifyAsync(Citizen citizen, string message, CancellationToken ct = default)
    {
        try
        {
            var phone = protector.DecryptPhone(citizen.PhoneEncrypted);
            await sms.SendAsync(phone, message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMS notification to citizen {CitizenId} failed", citizen.Id);
        }
    }

    public async Task NotifyPhoneAsync(string normalizedPhone, string message, CancellationToken ct = default)
    {
        try
        {
            await sms.SendAsync(normalizedPhone, message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMS reply failed");
        }
    }
}
