namespace CircuLedger.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "circuledger-api";
    public string Audience { get; set; } = "circuledger-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply it via env var Jwt__SigningKey, never in source.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
