using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

/// <summary>
/// A member of the public who sells a phone, usually via USSD/SMS. The phone number is never stored
/// in clear: <see cref="PhoneHash"/> (keyed HMAC) is used for lookups, <see cref="PhoneEncrypted"/>
/// (AES-GCM) only to send SMS, and <see cref="PhoneLast4"/> for masked display.
/// </summary>
public class Citizen : AuditableEntity
{
    public required string PhoneHash { get; set; }
    public required string PhoneEncrypted { get; set; }
    public required string PhoneLast4 { get; set; }
    public string? DisplayName { get; set; }
    public Gender Gender { get; set; } = Gender.Unspecified;

    /// <summary>Optional link to an Identity account if the citizen later registers.</summary>
    public string? UserId { get; set; }

    public ICollection<IntakeRequest> IntakeRequests { get; set; } = [];

    public string MaskedPhone => $"+232 ** *** {PhoneLast4}";
}
