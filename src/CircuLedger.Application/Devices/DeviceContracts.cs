using CircuLedger.Application.Common;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Application.Devices;

public sealed record DevicePurchaseSummary(decimal PaidPriceSle, PayoutMethod PayoutMethod, PayoutStatus PayoutStatus, DateTime PurchasedAt);

public sealed record DeviceDto(
    Guid Id,
    string TrackingId,
    string Brand,
    string? Model,
    string MaskedImei,
    DeviceCondition Condition,
    bool IsSamsungFlagship,
    IntakeChannel IntakeChannel,
    DeviceStatus Status,
    RecoveryOutcome? RecoveryOutcome,
    Guid? LocationId,
    string? LocationName,
    string? IntakeReference,
    string? CertificateNumber,
    DevicePurchaseSummary? Purchase,
    DateTime CreatedAt)
{
    public static DeviceDto From(Device d) => new(
        d.Id, d.TrackingId, d.Brand, d.Model, d.MaskedImei, d.Condition, d.IsSamsungFlagship, d.IntakeChannel,
        d.Status, d.RecoveryOutcome, d.LocationId, d.Location?.Name, d.IntakeRequest?.ReferenceCode,
        d.Certificate?.CertificateNumber,
        d.Purchase is null ? null : new DevicePurchaseSummary(d.Purchase.PaidPriceSle, d.Purchase.PayoutMethod, d.Purchase.PayoutStatus, d.Purchase.CreatedAt),
        d.CreatedAt);
}

public sealed record DeviceListQuery : PageQuery
{
    public DeviceStatus? Status { get; init; }
    public string? Brand { get; init; }
    public bool? SamsungOnly { get; init; }
    public string? Search { get; init; }
}

/// <summary>IMEI format check: 15 digits with a valid Luhn check digit.</summary>
public static class Imei
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 15 && IsLuhnValid(digits) ? digits : null;
    }

    private static bool IsLuhnValid(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return sum % 10 == 0;
    }
}
