using System.Linq.Expressions;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Common;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CircuLedger.Infrastructure.Persistence;

public sealed class LedgerImmutabilityException(string message) : InvalidOperationException(message);

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentUser? currentUser = null,
    TimeProvider? timeProvider = null)
    : IdentityDbContext<ApplicationUser>(options), IApplicationDbContext
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public DbSet<Citizen> Citizens => Set<Citizen>();
    public DbSet<DropOffPoint> DropOffPoints => Set<DropOffPoint>();
    public DbSet<IntakeRequest> IntakeRequests => Set<IntakeRequest>();
    public DbSet<UssdSession> UssdSessions => Set<UssdSession>();
    public DbSet<InboundSms> InboundSms => Set<InboundSms>();
    public DbSet<DispatchBatch> DispatchBatches => Set<DispatchBatch>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<SanitizationRecord> SanitizationRecords => Set<SanitizationRecord>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<RecoveredComponent> RecoveredComponents => Set<RecoveredComponent>();
    public DbSet<HazardousMaterialLog> HazardousMaterialLogs => Set<HazardousMaterialLog>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingPhoto> ListingPhotos => Set<ListingPhoto>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<FundTransaction> FundTransactions => Set<FundTransaction>();
    public DbSet<ReinvestmentRule> ReinvestmentRules => Set<ReinvestmentRule>();
    public DbSet<TrainingCycle> TrainingCycles => Set<TrainingCycle>();
    public DbSet<TraineeEnrollment> TraineeEnrollments => Set<TraineeEnrollment>();
    public DbSet<EmploymentRecord> EmploymentRecords => Set<EmploymentRecord>();
    public DbSet<AwarenessCampaign> AwarenessCampaigns => Set<AwarenessCampaign>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var sequence in Sequences.All)
            builder.HasSequence<long>(sequence).StartsAt(1).IncrementsBy(1);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            // Store enums as readable strings. M&E reports are run directly against the database.
            foreach (var property in entityType.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!type.IsEnum) continue;
                var converter = (ValueConverter)Activator.CreateInstance(typeof(EnumToStringConverter<>).MakeGenericType(type))!;
                property.SetValueConverter(converter);
                property.SetMaxLength(40);
            }

            // Domain rows are soft-deleted, so a hard cascade would only ever be a mistake.
            if (entityType.ClrType.Assembly == typeof(BaseEntity).Assembly)
            {
                foreach (var fk in entityType.GetForeignKeys())
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var filter = Expression.Lambda(
                    Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))), parameter);
                builder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Enforces ledger immutability, fills audit columns, and turns deletes into soft deletes.</summary>
    private void ApplyRules()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var actor = currentUser?.Actor ?? "system";

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is LedgerEntry && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new LedgerImmutabilityException($"Ledger entries are append-only; {entry.State} is not allowed.");

            if (entry.Entity is ISoftDeletable softDeletable && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = now;
                softDeletable.DeletedBy = actor;
            }

            if (entry.Entity is not IAuditable auditable) continue;
            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedAt = now;
                    auditable.CreatedBy ??= actor;
                    break;
                case EntityState.Modified:
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = actor;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Fund> GetFundForUpdateAsync(FundType type, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Fund row locks require an open transaction.");

        var ids = await Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM funds WHERE type = {type.ToString()} AND NOT is_deleted FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (ids.Count != 1)
            throw new InvalidOperationException($"Fund {type} is not configured.");

        var fund = await Funds.SingleAsync(f => f.Id == ids[0], cancellationToken);
        // If the fund was already tracked, refresh it now that we hold the row lock.
        await Entry(fund).ReloadAsync(cancellationToken);
        return fund;
    }

    public async Task<long> NextSequenceValueAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        if (!Sequences.All.Contains(sequenceName))
            throw new ArgumentException($"Unknown sequence '{sequenceName}'.", nameof(sequenceName));

        var values = await Database
            .SqlQuery<long>($"SELECT nextval({sequenceName}::regclass) AS \"Value\"")
            .ToListAsync(cancellationToken);
        return values[0];
    }
}
