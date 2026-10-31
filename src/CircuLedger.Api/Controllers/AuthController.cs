using CircuLedger.Application.Auth;
using CircuLedger.Application.Common;
using CircuLedger.Application.Intake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CircuLedger.Api.Controllers;

[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Self-registration for Buyer or Citizen accounts. Staff accounts are created by an Admin.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        Ok(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));

    /// <summary>Exchanges a refresh token for a new access + refresh token pair. Refresh tokens are single-use.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct) =>
        Ok(await auth.RefreshAsync(request, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await auth.RevokeAsync(request, ct);
        return NoContent();
    }
}

[Route("api/v{version:apiVersion}/users")]
[Authorize]
public sealed class UsersController(IUserService users, IAuthService auth, IIntakeService intake) : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> Me(CancellationToken ct) => Ok(await users.GetProfileAsync(CurrentUserId, ct));

    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMe(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await users.UpdateProfileAsync(CurrentUserId, request, ct));

    /// <summary>Changes the login email. Requires the current password.</summary>
    [HttpPut("me/email")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<UserProfileDto>> ChangeEmail(ChangeEmailRequest request, CancellationToken ct) =>
        Ok(await users.ChangeEmailAsync(CurrentUserId, request, ct));

    /// <summary>Changes the contact phone. For citizens, USSD/SMS requests from the new number appear under this account.</summary>
    [HttpPut("me/phone")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<UserProfileDto>> ChangePhone(ChangePhoneRequest request, CancellationToken ct) =>
        Ok(await users.ChangePhoneAsync(CurrentUserId, request, ct));

    /// <summary>Changes the signed-in user's password. All other sessions are signed out; the response carries new tokens.</summary>
    [HttpPut("me/password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        Ok(await auth.ChangePasswordAsync(CurrentUserId, request, ct));

    /// <summary>A registered citizen's own sell requests (matched by the phone number they registered with).</summary>
    [HttpGet("me/intake-requests")]
    [Authorize(Roles = Roles.Citizen)]
    public async Task<ActionResult<IReadOnlyList<IntakeRequestDto>>> MyIntakeRequests(CancellationToken ct) =>
        Ok(await intake.ListForCitizenUserAsync(CurrentUserId, ct));

    [HttpGet]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<PagedResult<UserProfileDto>>> List([FromQuery] UserListQuery query, CancellationToken ct) =>
        Ok(await users.ListAsync(query, ct));

    /// <summary>Creates a staff account (OperationsLead, Technician, FieldAgent...).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<UserProfileDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        Ok(await users.CreateAsync(request, ct));

    [HttpPut("{id}/roles")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<UserProfileDto>> AssignRoles(string id, AssignRolesRequest request, CancellationToken ct) =>
        Ok(await users.AssignRolesAsync(id, request, ct));

    [HttpPut("{id}/active")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<UserProfileDto>> SetActive(string id, [FromQuery] bool value, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, value, ct));
}
