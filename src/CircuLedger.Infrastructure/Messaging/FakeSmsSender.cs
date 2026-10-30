using System.Collections.Concurrent;
using CircuLedger.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CircuLedger.Infrastructure.Messaging;

public sealed record SentSms(string To, string Message, DateTime SentAt);

/// <summary>
/// Development/test SMS sender. Messages stay in memory (inspect them in tests via <see cref="Outbox"/>).
/// Only a masked number is logged. Swap for an Africa's Talking implementation in production.
/// </summary>
public sealed class FakeSmsSender(ILogger<FakeSmsSender> logger, TimeProvider time) : ISmsSender
{
    private const int MaxRetained = 500;
    private readonly ConcurrentQueue<SentSms> _outbox = new();

    public IReadOnlyCollection<SentSms> Outbox => _outbox.ToArray();

    public Task SendAsync(string normalizedPhone, string message, CancellationToken cancellationToken = default)
    {
        _outbox.Enqueue(new SentSms(normalizedPhone, message, time.GetUtcNow().UtcDateTime));
        while (_outbox.Count > MaxRetained) _outbox.TryDequeue(out _);

        logger.LogInformation("Fake SMS to ***{Last4} ({Length} chars)", IPersonalDataProtector.Last4(normalizedPhone), message.Length);
        return Task.CompletedTask;
    }
}
