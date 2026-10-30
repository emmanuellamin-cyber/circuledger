namespace CircuLedger.Domain.Enums;

public enum Gender { Unspecified = 0, Female = 1, Male = 2, Other = 3 }

public enum DropOffPointType { Hub = 1, SolarMakit = 2, Kiosk = 3 }

public enum IntakeChannel { Ussd = 1, Sms = 2, DropOff = 3, AgentPickup = 4, Web = 5 }

public enum FulfilmentMethod { DropOff = 1, Pickup = 2 }

/// <summary>Condition as reported by the seller / assessed at intake. Drives the pricing matrix.</summary>
public enum DeviceCondition { Dead = 1, BrokenScreen = 2, WorkingFaulty = 3 }

public enum IntakeStatus { Pending = 1, Scheduled = 2, Collected = 3, Received = 4, Rejected = 5, Cancelled = 6 }

public enum TransportMode { Foot = 1, Bicycle = 2, Motorcycle = 3 }

public enum DispatchBatchStatus { Planned = 1, InProgress = 2, Completed = 3, Cancelled = 4 }

public enum DeviceStatus
{
    Received = 1,
    Purchased = 2,
    Sanitized = 3,
    SanitizationFailed = 4,
    Refurbishing = 5,
    Dismantled = 6,
    Disposed = 7,
    Listed = 8,
    Sold = 9
}

public enum PayoutMethod { Cash = 1, OrangeMoney = 2, Afrimoney = 3 }

public enum PayoutStatus { Pending = 1, Paid = 2, Failed = 3 }

public enum SanitizationMethod
{
    FactoryResetOverwrite = 1,
    SamsungKnoxGradeErase = 2,
    CryptographicErase = 3,
    StoragePhysicalDestruction = 4
}

public enum SanitizationOutcome { Passed = 1, Failed = 2, NotEligible = 3 }

public enum RecoveryOutcome { Refurbish = 1, DismantleForParts = 2, HazardousDisposalOnly = 3 }

public enum ComponentType { Screen = 1, Board = 2, Battery = 3, Camera = 4, Housing = 5, ChargingPort = 6, Speaker = 7, Other = 99 }

public enum Grade { A = 1, B = 2, C = 3, Salvage = 4 }

public enum ComponentStatus { InStock = 1, Listed = 2, Sold = 3, Scrapped = 4 }

public enum HazardousMaterial { LithiumBattery = 1, Lead = 2, Other = 99 }

public enum ListingItemType { RefurbishedDevice = 1, Component = 2 }

public enum ListingStatus { Draft = 1, Published = 2, Reserved = 3, Sold = 4, Withdrawn = 5 }

public enum OrderStatus { Pending = 1, Paid = 2, Fulfilled = 3, Cancelled = 4 }

public enum FundType { SeedFund = 1, TrainingFund = 2 }

public enum FundTransactionDirection { Credit = 1, Debit = 2 }

public enum FundTransactionReason { Grant = 1, DevicePurchase = 2, Reinvestment = 3, TrainingExpense = 4, Adjustment = 5 }

public enum EnrollmentStatus { Enrolled = 1, Completed = 2, Dropped = 3 }

public enum EmploymentType { FullTime = 1, PartTime = 2, Stipend = 3, Apprenticeship = 4 }

public enum InstitutionType { School = 1, Campus = 2, Community = 3 }

public enum CampaignType { Talk = 1, Workshop = 2, Radio = 3, CollectionDrive = 4, SocialMedia = 5 }

public enum LedgerEventType
{
    IntakeCreated = 1,
    Purchased = 2,
    Sanitized = 3,
    CertificateIssued = 4,
    Dismantled = 5,
    ComponentRecovered = 6,
    MaterialDiverted = 7,
    Listed = 8,
    Sold = 9,
    Reinvested = 10,
    DeviceReceived = 11
}
