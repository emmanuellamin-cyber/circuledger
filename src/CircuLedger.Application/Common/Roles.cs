namespace CircuLedger.Application.Common;

public static class Roles
{
    public const string Admin = "Admin";
    public const string OperationsLead = "OperationsLead";
    public const string Technician = "Technician";
    public const string FieldAgent = "FieldAgent";
    public const string Buyer = "Buyer";
    public const string Citizen = "Citizen";

    public static readonly IReadOnlyList<string> All = [Admin, OperationsLead, Technician, FieldAgent, Buyer, Citizen];
}

public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);
    /// <summary>Admin, OperationsLead.</summary>
    public const string Operations = nameof(Operations);
    /// <summary>Admin, OperationsLead, Technician.</summary>
    public const string Workshop = nameof(Workshop);
    /// <summary>Admin, OperationsLead, FieldAgent.</summary>
    public const string Field = nameof(Field);
    /// <summary>Every staff role: Admin, OperationsLead, Technician, FieldAgent.</summary>
    public const string Staff = nameof(Staff);
    /// <summary>Buyer (Admin/OperationsLead may act on behalf of buyers).</summary>
    public const string Buyer = nameof(Buyer);
}

public static class LedgerEntityTypes
{
    public const string IntakeRequest = nameof(IntakeRequest);
    public const string Device = nameof(Device);
    public const string Purchase = nameof(Purchase);
    public const string SanitizationRecord = nameof(SanitizationRecord);
    public const string Certificate = nameof(Certificate);
    public const string Component = "RecoveredComponent";
    public const string HazardousMaterial = "HazardousMaterialLog";
    public const string Listing = nameof(Listing);
    public const string Order = nameof(Order);
}
