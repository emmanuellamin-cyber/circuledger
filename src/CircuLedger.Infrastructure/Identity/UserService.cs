using CircuLedger.Application.Auth;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Intake;
using CircuLedger.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using IPersonalDataProtector = CircuLedger.Application.Common.Interfaces.IPersonalDataProtector;

namespace CircuLedger.Infrastructure.Identity;

public sealed class UserService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    ICitizenService citizens,
    IPersonalDataProtector protector,
    TimeProvider time) : IUserService, IUserDirectory
{
    public async Task<UserProfileDto> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(userId);
        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileDto> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(userId);
        user.FullName = request.FullName;
        user.Gender = request.Gender;
        AuthService.EnsureSucceeded(await userManager.UpdateAsync(user));
        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileDto> ChangeEmailAsync(string userId, ChangeEmailRequest request, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(userId);
        await AuthService.VerifyCurrentPasswordAsync(signInManager, user, request.CurrentPassword);

        var email = request.NewEmail.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            // Email doubles as the user name; both change together or neither does.
            await db.ExecuteInTransactionAsync(async _ =>
            {
                AuthService.EnsureSucceeded(await userManager.SetEmailAsync(user, email));
                AuthService.EnsureSucceeded(await userManager.SetUserNameAsync(user, email));
                return true;
            }, ct);
        }
        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileDto> ChangePhoneAsync(string userId, ChangePhoneRequest request, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(userId);
        await AuthService.VerifyCurrentPasswordAsync(signInManager, user, request.CurrentPassword);
        var roles = await userManager.GetRolesAsync(user);

        return await db.ExecuteInTransactionAsync(async token =>
        {
            var phone = AuthService.SetPhone(user, request.Phone, protector);
            if (roles.Contains(Roles.Citizen))
                await citizens.LinkUserAsync(phone, user.Id, user.FullName, user.Gender, token);
            AuthService.EnsureSucceeded(await userManager.UpdateAsync(user));
            return ToDto(user, roles);
        }, ct);
    }

    public async Task<PagedResult<UserProfileDto>> ListAsync(UserListQuery query, CancellationToken ct = default)
    {
        IQueryable<ApplicationUser> users = userManager.Users;
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var inRole = (await userManager.GetUsersInRoleAsync(query.Role)).Select(u => u.Id).ToList();
            users = users.Where(u => inRole.Contains(u.Id));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            users = users.Where(u => u.FullName.ToLower().Contains(term) || u.Email!.ToLower().Contains(term));
        }

        var total = await users.CountAsync(ct);
        var page = await users.OrderBy(u => u.FullName)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var items = new List<UserProfileDto>(page.Count);
        foreach (var user in page) items.Add(ToDto(user, await userManager.GetRolesAsync(user)));
        return new PagedResult<UserProfileDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<UserProfileDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FullName = request.FullName,
            Gender = request.Gender,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        AuthService.EnsureSucceeded(await userManager.CreateAsync(user, request.Password));
        AuthService.EnsureSucceeded(await userManager.AddToRolesAsync(user, request.Roles.Distinct()));
        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileDto> AssignRolesAsync(string userId, AssignRolesRequest request, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(userId);
        var current = await userManager.GetRolesAsync(user);
        var target = request.Roles.Distinct().ToList();
        AuthService.EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, current.Except(target)));
        AuthService.EnsureSucceeded(await userManager.AddToRolesAsync(user, target.Except(current)));
        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserProfileDto> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        var user = await FindOrThrowAsync(userId);
        user.IsActive = isActive;
        AuthService.EnsureSucceeded(await userManager.UpdateAsync(user));
        return ToDto(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserSummary?> FindAsync(string userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null ? null : new UserSummary(user.Id, user.FullName, user.Email, (await userManager.GetRolesAsync(user)).ToList());
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        return await userManager.Users.Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersInRoleAsync(string role, CancellationToken ct = default)
    {
        var users = await userManager.GetUsersInRoleAsync(role);
        return users.Where(u => u.IsActive)
            .Select(u => new UserSummary(u.Id, u.FullName, u.Email, [role]))
            .ToList();
    }

    private async Task<ApplicationUser> FindOrThrowAsync(string userId) =>
        await userManager.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);

    internal static UserProfileDto ToDto(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName, user.Gender,
            user.PhoneLast4 is null ? null : $"+232 ** *** {user.PhoneLast4}",
            roles.ToList(), user.IsActive, user.CreatedAt);
}
