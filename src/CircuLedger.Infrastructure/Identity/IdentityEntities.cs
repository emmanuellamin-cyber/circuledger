using CircuLedger.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace CircuLedger.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Used for the ≥40% young-women inclusion KPI.</summary>
    public Gender Gender { get; set; } = Gender.Unspecified;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>Contact phone, AES-GCM encrypted like a citizen's. Identity's clear-text PhoneNumber is never used.</summary>
    public string? PhoneEncrypted { get; set; }
    public string? PhoneLast4 { get; set; }
}

/// <summary>Only the SHA-256 of the refresh token is stored. Tokens rotate on every use.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
