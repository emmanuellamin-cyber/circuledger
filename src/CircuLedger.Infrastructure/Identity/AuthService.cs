using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CircuLedger.Application.Auth;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Intake;
using CircuLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CircuLedger.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    ICitizenService citizens,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        return await db.ExecuteInTransactionAsync(async token =>
        {
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName,
                Gender = request.Gender,
                CreatedAt = time.GetUtcNow().UtcDateTime
            };
            var result = await userManager.CreateAsync(user, request.Password);
            EnsureSucceeded(result);
            EnsureSucceeded(await userManager.AddToRoleAsync(user, request.AccountType));

            if (request.AccountType == Roles.Citizen && request.Phone is not null)
                await citizens.LinkUserAsync(request.Phone, user.Id, request.FullName, request.Gender, token);

            logger.LogInformation("Registered {AccountType} user {UserId}", request.AccountType, user.Id);
            return await IssueTokensAsync(user, token);
        }, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Invalid email or password.");

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            throw new UnauthorizedAccessException("Account is temporarily locked after repeated failed attempts.");
        if (!result.Succeeded)
            throw new UnauthorizedAccessException("Invalid email or password.");

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var hash = HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (stored.RevokedAt is not null)
        {
            // A revoked token being replayed suggests theft, so revoke every active token for this user.
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            logger.LogWarning("Refresh token reuse detected for user {UserId}; all sessions revoked", stored.UserId);
            throw new UnauthorizedAccessException("Refresh token has been revoked.");
        }

        if (!stored.IsActive(now) || stored.User is not { IsActive: true })
            throw new UnauthorizedAccessException("Refresh token has expired.");

        var response = await IssueTokensAsync(stored.User, ct, rotated: stored);
        return response;
    }

    public async Task RevokeAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var hash = HashToken(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, CancellationToken ct, RefreshToken? rotated = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var roles = (await userManager.GetRolesAsync(user)).ToList();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName),
        };
        // Short "role" claim type, matched by TokenValidationParameters.RoleClaimType in Program.cs.
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now, accessExpires, credentials);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshHash = HashToken(refreshToken);
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = refreshExpires
        });
        if (rotated is not null)
        {
            rotated.RevokedAt = now;
            rotated.ReplacedByTokenHash = refreshHash;
        }
        await db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, accessExpires, refreshToken, refreshExpires, UserService.ToDto(user, roles));
    }

    internal static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded) return;
        var failures = result.Errors.Select(e => new FluentValidation.Results.ValidationFailure(e.Code, e.Description));
        throw new FluentValidation.ValidationException(failures);
    }
}
