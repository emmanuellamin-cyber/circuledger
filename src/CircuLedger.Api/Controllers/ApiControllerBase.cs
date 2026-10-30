using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace CircuLedger.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected string CurrentUserId =>
        User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? throw new UnauthorizedAccessException("Not signed in.");
}

public static class RateLimitPolicies
{
    public const string Public = "public";
    public const string Ussd = "ussd";
    public const string Auth = "auth";
}
