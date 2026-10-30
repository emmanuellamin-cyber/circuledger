namespace CircuLedger.Application.Common;

public sealed class HubOptions
{
    public const string Section = "Hub";

    /// <summary>Public base URL used in QR codes and SMS, e.g. https://circuledger.example.sl</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
    public string IssuerName { get; set; } = "Smart Youth Mobile Circularity Hub, Njala University";
    public string SmsSenderName { get; set; } = "CircuHub";
}

public sealed class DispatchOptions
{
    public const string Section = "Dispatch";
    public double DefaultRadiusKm { get; set; } = 3;
    public int MaxRequestsPerBatch { get; set; } = 10;
}

public sealed class PilotTargetOptions
{
    public const string Section = "PilotTargets";
    public int DevicesCollected { get; set; } = 500;
    public decimal HazardousKgDiverted { get; set; } = 50;
    public int TechniciansTrained { get; set; } = 20;
    public int JobsCreated { get; set; } = 5;
    public int SchoolsReached { get; set; } = 5;
    public int CampusesReached { get; set; } = 2;
    public decimal WomenInclusionPercent { get; set; } = 40;
}

public sealed class UssdOptions
{
    public const string Section = "Ussd";
    public List<string> Brands { get; set; } = ["Samsung", "Tecno", "Itel", "Infinix", "Apple", "Nokia", "Other"];
    public int MaxDropOffPointsListed { get; set; } = 6;
}

public sealed class UploadOptions
{
    public const string Section = "Uploads";
    public long MaxPhotoBytes { get; set; } = 5 * 1024 * 1024;
    public List<string> AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];
}
