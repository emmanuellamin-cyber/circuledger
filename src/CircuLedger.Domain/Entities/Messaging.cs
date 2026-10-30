using CircuLedger.Domain.Common;

namespace CircuLedger.Domain.Entities;

/// <summary>Persisted USSD session state. Replaying the same (sessionId, text) returns <see cref="LastResponse"/>.</summary>
public class UssdSession : AuditableEntity
{
    public required string SessionId { get; set; }
    public required string ServiceCode { get; set; }
    public required string PhoneHash { get; set; }
    public string LastText { get; set; } = string.Empty;
    public string LastResponse { get; set; } = string.Empty;
    public int Hops { get; set; }
    public bool IsCompleted { get; set; }
    public Guid? IntakeRequestId { get; set; }
}

public class InboundSms : AuditableEntity
{
    public string? ProviderMessageId { get; set; }
    public required string PhoneHash { get; set; }
    public required string Keyword { get; set; }
    public required string Body { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReplySummary { get; set; }
}
