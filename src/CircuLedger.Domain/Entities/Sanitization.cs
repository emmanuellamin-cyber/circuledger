using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

public class SanitizationRecord : AuditableEntity
{
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public SanitizationMethod Method { get; set; }
    public required string TechnicianUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int Passes { get; set; }
    public SanitizationOutcome Outcome { get; set; }
    public string? ToolName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Certificate of Data Sanitization. The payload is stored canonically so its hash and signature can be re-verified.</summary>
public class Certificate : AuditableEntity
{
    public required string CertificateNumber { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid SanitizationRecordId { get; set; }
    public SanitizationRecord? SanitizationRecord { get; set; }
    public DateTime IssuedAt { get; set; }
    public required string Payload { get; set; }
    public required string PayloadHash { get; set; }
    public required string Signature { get; set; }
    public required string SignatureAlgorithm { get; set; }
    public required string SigningKeyId { get; set; }
    public required string VerificationUrl { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
}
