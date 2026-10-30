using System.Security.Cryptography;

namespace CircuLedger.Application.Common;

public static class CodeGenerator
{
    // Crockford-style alphabet with no 0/O or 1/I/L, so codes are easy to read out over USSD/SMS.
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string IntakeReference() => "SY" + Random(6);

    public static string TrackingId(int year, long sequence) => $"CL-{year}-{sequence:D6}";

    public static string CertificateNumber(int year, long sequence) => $"CDS-{year}-{sequence:D6}";

    public static string OrderNumber(int year, long sequence) => $"ORD-{year}-{sequence:D6}";

    public static string ListingCode(long sequence) => $"LST-{sequence:D6}";

    public static string BatchCode(DateOnly date, long sequence) => $"DB-{date:yyyyMMdd}-{sequence:D4}";

    public static string ComponentCode(string trackingId, int index) => $"{trackingId}-C{index:D2}";

    private static string Random(int length) => RandomNumberGenerator.GetString(Alphabet, length);
}
