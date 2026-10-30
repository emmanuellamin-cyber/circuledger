using System.IdentityModel.Tokens.Jwt;
using CircuLedger.Application.Common.Interfaces;

namespace CircuLedger.Api.Infrastructure;

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => accessor.HttpContext?.User.IsInRole(role) == true;
}
