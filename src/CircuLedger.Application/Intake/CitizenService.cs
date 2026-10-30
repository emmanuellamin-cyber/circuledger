using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Intake;

public interface ICitizenService
{
    /// <summary>Finds a citizen by phone (via its keyed hash) or creates one. The phone is never stored in clear.</summary>
    Task<Citizen> GetOrCreateAsync(string rawPhone, CancellationToken ct = default);
    Task<Citizen?> FindByPhoneAsync(string rawPhone, CancellationToken ct = default);
    Task LinkUserAsync(string rawPhone, string userId, string? displayName, Gender gender, CancellationToken ct = default);
}

public sealed class CitizenService(IApplicationDbContext db, IPersonalDataProtector protector) : ICitizenService
{
    public async Task<Citizen> GetOrCreateAsync(string rawPhone, CancellationToken ct = default)
    {
        var phone = Normalize(rawPhone);
        var hash = protector.HashPhone(phone);
        var citizen = await db.Citizens.FirstOrDefaultAsync(c => c.PhoneHash == hash, ct);
        if (citizen is not null) return citizen;

        citizen = new Citizen
        {
            PhoneHash = hash,
            PhoneEncrypted = protector.EncryptPhone(phone),
            PhoneLast4 = IPersonalDataProtector.Last4(phone)
        };
        db.Citizens.Add(citizen);
        return citizen;
    }

    public async Task<Citizen?> FindByPhoneAsync(string rawPhone, CancellationToken ct = default)
    {
        var phone = protector.NormalizePhone(rawPhone);
        if (phone is null) return null;
        var hash = protector.HashPhone(phone);
        return await db.Citizens.FirstOrDefaultAsync(c => c.PhoneHash == hash, ct);
    }

    public async Task LinkUserAsync(string rawPhone, string userId, string? displayName, Gender gender, CancellationToken ct = default)
    {
        var citizen = await GetOrCreateAsync(rawPhone, ct);
        if (citizen.UserId is not null && citizen.UserId != userId)
            throw new ValidationException([new ValidationFailure("Phone", "This phone number is already linked to another account.")]);
        citizen.UserId = userId;
        citizen.DisplayName ??= displayName;
        citizen.Gender = gender;
        await db.SaveChangesAsync(ct);
    }

    private string Normalize(string rawPhone) =>
        protector.NormalizePhone(rawPhone)
        ?? throw new ValidationException([new ValidationFailure("Phone", "Enter a valid Sierra Leone phone number.")]);
}
