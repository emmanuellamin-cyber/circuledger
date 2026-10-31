using CircuLedger.Application.Common;
using CircuLedger.Domain.Enums;
using FluentValidation;

namespace CircuLedger.Application.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    Gender Gender = Gender.Unspecified,
    string AccountType = Roles.Buyer,
    string? Phone = null);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UserProfileDto(
    string Id,
    string Email,
    string FullName,
    Gender Gender,
    string? MaskedPhone,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTime CreatedAt);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserProfileDto User);

public sealed record UpdateProfileRequest(string FullName, Gender Gender);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ChangeEmailRequest(string NewEmail, string CurrentPassword);

public sealed record ChangePhoneRequest(string Phone, string CurrentPassword);

public sealed record CreateUserRequest(string Email, string Password, string FullName, Gender Gender, IReadOnlyList<string> Roles);

public sealed record AssignRolesRequest(IReadOnlyList<string> Roles);

public sealed record UserListQuery : PageQuery
{
    public string? Role { get; init; }
    public string? Search { get; init; }
}

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task RevokeAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>Changes the signed-in user's password, revokes every existing session and returns fresh tokens.</summary>
    Task<AuthResponse> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken ct = default);
}

public interface IUserService
{
    Task<UserProfileDto> GetProfileAsync(string userId, CancellationToken ct = default);
    Task<UserProfileDto> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>Changes the login email (also the user name). Requires the current password.</summary>
    Task<UserProfileDto> ChangeEmailAsync(string userId, ChangeEmailRequest request, CancellationToken ct = default);

    /// <summary>Changes the contact phone. For citizens this also moves the USSD/SMS link to the new number.</summary>
    Task<UserProfileDto> ChangePhoneAsync(string userId, ChangePhoneRequest request, CancellationToken ct = default);
    Task<PagedResult<UserProfileDto>> ListAsync(UserListQuery query, CancellationToken ct = default);
    Task<UserProfileDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserProfileDto> AssignRolesAsync(string userId, AssignRolesRequest request, CancellationToken ct = default);
    Task<UserProfileDto> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default);
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.AccountType).Must(t => t is Roles.Buyer or Roles.Citizen)
            .WithMessage("Self-registration is only available for Buyer or Citizen accounts.");
        RuleFor(x => x.Phone).NotEmpty().When(x => x.AccountType == Roles.Citizen)
            .WithMessage("Citizens must register with the phone number they use for USSD/SMS.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Gender).IsInEnum();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from the current one.");
    }
}

public sealed class ChangeEmailRequestValidator : AbstractValidator<ChangeEmailRequest>
{
    public ChangeEmailRequestValidator()
    {
        RuleFor(x => x.NewEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
    }
}

public sealed class ChangePhoneRequestValidator : AbstractValidator<ChangePhoneRequest>
{
    public ChangePhoneRequestValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).Must(r => Common.Roles.All.Contains(r)).WithMessage("Unknown role '{PropertyValue}'.");
    }
}

public sealed class AssignRolesRequestValidator : AbstractValidator<AssignRolesRequest>
{
    public AssignRolesRequestValidator()
    {
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).Must(r => Common.Roles.All.Contains(r)).WithMessage("Unknown role '{PropertyValue}'.");
    }
}
