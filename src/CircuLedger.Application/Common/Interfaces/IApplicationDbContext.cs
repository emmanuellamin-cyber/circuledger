using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Citizen> Citizens { get; }
    DbSet<DropOffPoint> DropOffPoints { get; }
    DbSet<IntakeRequest> IntakeRequests { get; }
    DbSet<UssdSession> UssdSessions { get; }
    DbSet<InboundSms> InboundSms { get; }
    DbSet<DispatchBatch> DispatchBatches { get; }
    DbSet<Device> Devices { get; }
    DbSet<PricingRule> PricingRules { get; }
    DbSet<Purchase> Purchases { get; }
    DbSet<SanitizationRecord> SanitizationRecords { get; }
    DbSet<Certificate> Certificates { get; }
    DbSet<RecoveredComponent> RecoveredComponents { get; }
    DbSet<HazardousMaterialLog> HazardousMaterialLogs { get; }
    DbSet<Listing> Listings { get; }
    DbSet<ListingPhoto> ListingPhotos { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Fund> Funds { get; }
    DbSet<FundTransaction> FundTransactions { get; }
    DbSet<ReinvestmentRule> ReinvestmentRules { get; }
    DbSet<TrainingCycle> TrainingCycles { get; }
    DbSet<TraineeEnrollment> TraineeEnrollments { get; }
    DbSet<EmploymentRecord> EmploymentRecords { get; }
    DbSet<AwarenessCampaign> AwarenessCampaigns { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <paramref name="operation"/> in a database transaction, or joins the ambient one if it's already open.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>Loads a fund with <c>SELECT ... FOR UPDATE</c>. Must be called inside a transaction.</summary>
    Task<Fund> GetFundForUpdateAsync(FundType type, CancellationToken cancellationToken = default);

    /// <summary>Returns the next value of a PostgreSQL sequence (see <see cref="Sequences"/>).</summary>
    Task<long> NextSequenceValueAsync(string sequenceName, CancellationToken cancellationToken = default);
}

public static class Sequences
{
    public const string Ledger = "ledger_entry_seq";
    public const string DeviceTracking = "device_tracking_seq";
    public const string Certificate = "certificate_number_seq";
    public const string Order = "order_number_seq";
    public const string Listing = "listing_code_seq";
    public const string DispatchBatch = "dispatch_batch_seq";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Ledger, DeviceTracking, Certificate, Order, Listing, DispatchBatch };
}
