using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

public class DropOffPoint : AuditableEntity
{
    public required string Name { get; set; }
    public DropOffPointType Type { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Landmark { get; set; }
    public required string OpeningHours { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Order in which the point appears in the USSD menu.</summary>
    public int DisplayOrder { get; set; }
}
