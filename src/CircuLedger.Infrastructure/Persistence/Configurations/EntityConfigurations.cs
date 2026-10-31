using CircuLedger.Domain.Entities;
using CircuLedger.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CircuLedger.Infrastructure.Persistence.Configurations;

internal static class Precision
{
    public static PropertyBuilder<decimal> Money(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 2);
    public static PropertyBuilder<decimal?> Money(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 2);
    public const string NotDeleted = "is_deleted = false";
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.PhoneEncrypted).HasMaxLength(200);
        b.Property(x => x.PhoneLast4).HasMaxLength(4);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CitizenConfiguration : IEntityTypeConfiguration<Citizen>
{
    public void Configure(EntityTypeBuilder<Citizen> b)
    {
        b.Property(x => x.PhoneHash).HasMaxLength(64);
        b.Property(x => x.PhoneEncrypted).HasMaxLength(200);
        b.Property(x => x.PhoneLast4).HasMaxLength(4);
        b.Property(x => x.DisplayName).HasMaxLength(100);
        b.HasIndex(x => x.PhoneHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.Ignore(x => x.MaskedPhone);
    }
}

public class DropOffPointConfiguration : IEntityTypeConfiguration<DropOffPoint>
{
    public void Configure(EntityTypeBuilder<DropOffPoint> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Landmark).HasMaxLength(200);
        b.Property(x => x.OpeningHours).HasMaxLength(120);
    }
}

public class IntakeRequestConfiguration : IEntityTypeConfiguration<IntakeRequest>
{
    public void Configure(EntityTypeBuilder<IntakeRequest> b)
    {
        b.Property(x => x.ReferenceCode).HasMaxLength(12);
        b.Property(x => x.Brand).HasMaxLength(50);
        b.Property(x => x.Model).HasMaxLength(80);
        b.Property(x => x.PickupArea).HasMaxLength(160);
        b.Property(x => x.ClosedReason).HasMaxLength(300);
        b.Property(x => x.UssdSessionId).HasMaxLength(100);
        b.Property(x => x.EstimatedOfferSle).Money();
        b.HasIndex(x => x.ReferenceCode).IsUnique();
        b.HasIndex(x => x.UssdSessionId).IsUnique();
        b.HasIndex(x => new { x.Status, x.FulfilmentMethod });
        b.HasOne(x => x.Citizen).WithMany(c => c.IntakeRequests).HasForeignKey(x => x.CitizenId);
        b.HasOne(x => x.DropOffPoint).WithMany().HasForeignKey(x => x.DropOffPointId);
        b.HasOne(x => x.DispatchBatch).WithMany(d => d.Requests).HasForeignKey(x => x.DispatchBatchId);
        b.HasOne<DropOffPoint>().WithMany().HasForeignKey(x => x.ReceivedAtDropOffPointId);
        b.Ignore(x => x.HasPickupCoordinates);
    }
}

public class UssdSessionConfiguration : IEntityTypeConfiguration<UssdSession>
{
    public void Configure(EntityTypeBuilder<UssdSession> b)
    {
        b.Property(x => x.SessionId).HasMaxLength(100);
        b.Property(x => x.ServiceCode).HasMaxLength(30);
        b.Property(x => x.PhoneHash).HasMaxLength(64);
        b.Property(x => x.LastText).HasMaxLength(500);
        b.Property(x => x.LastResponse).HasMaxLength(1000);
        b.HasIndex(x => x.SessionId).IsUnique();
        b.HasIndex(x => x.PhoneHash);
    }
}

public class InboundSmsConfiguration : IEntityTypeConfiguration<InboundSms>
{
    public void Configure(EntityTypeBuilder<InboundSms> b)
    {
        b.ToTable("inbound_sms");
        b.Property(x => x.ProviderMessageId).HasMaxLength(100);
        b.Property(x => x.PhoneHash).HasMaxLength(64);
        b.Property(x => x.Keyword).HasMaxLength(20);
        b.Property(x => x.Body).HasMaxLength(480);
        b.Property(x => x.ReplySummary).HasMaxLength(480);
        b.HasIndex(x => x.ProviderMessageId).IsUnique();
        b.HasIndex(x => x.PhoneHash);
    }
}

public class DispatchBatchConfiguration : IEntityTypeConfiguration<DispatchBatch>
{
    public void Configure(EntityTypeBuilder<DispatchBatch> b)
    {
        b.Property(x => x.BatchCode).HasMaxLength(30);
        b.Property(x => x.FieldAgentUserId).HasMaxLength(450);
        b.HasIndex(x => x.BatchCode).IsUnique();
        b.HasIndex(x => new { x.FieldAgentUserId, x.Status });
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.Property(x => x.TrackingId).HasMaxLength(20);
        b.Property(x => x.Brand).HasMaxLength(50);
        b.Property(x => x.Model).HasMaxLength(80);
        b.Property(x => x.ImeiHash).HasMaxLength(64);
        b.Property(x => x.ImeiLast4).HasMaxLength(4);
        b.HasIndex(x => x.TrackingId).IsUnique();
        b.HasIndex(x => x.ImeiHash);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.IntakeRequestId).IsUnique();
        b.HasOne(x => x.Citizen).WithMany().HasForeignKey(x => x.CitizenId);
        b.HasOne(x => x.IntakeRequest).WithOne(r => r.Device).HasForeignKey<Device>(x => x.IntakeRequestId);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
        b.Ignore(x => x.MaskedImei);
    }
}

public class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> b)
    {
        b.Property(x => x.Brand).HasMaxLength(50);
        b.Property(x => x.PriceSle).Money();
        b.HasIndex(x => new { x.Brand, x.Condition }).IsUnique().HasFilter(Precision.NotDeleted);
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.Property(x => x.OfferedPriceSle).Money();
        b.Property(x => x.PaidPriceSle).Money();
        b.Property(x => x.PayoutReference).HasMaxLength(100);
        b.Property(x => x.PurchasedByUserId).HasMaxLength(450);
        b.HasIndex(x => x.DeviceId).IsUnique();
        b.HasOne(x => x.Device).WithOne(d => d.Purchase).HasForeignKey<Purchase>(x => x.DeviceId);
        b.HasOne<FundTransaction>().WithMany().HasForeignKey(x => x.SeedFundTransactionId);
    }
}

public class SanitizationRecordConfiguration : IEntityTypeConfiguration<SanitizationRecord>
{
    public void Configure(EntityTypeBuilder<SanitizationRecord> b)
    {
        b.Property(x => x.TechnicianUserId).HasMaxLength(450);
        b.Property(x => x.ToolName).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne(x => x.Device).WithMany(d => d.SanitizationRecords).HasForeignKey(x => x.DeviceId);
    }
}

public class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> b)
    {
        b.Property(x => x.CertificateNumber).HasMaxLength(20);
        b.Property(x => x.PayloadHash).HasMaxLength(64);
        b.Property(x => x.Signature).HasMaxLength(1024);
        b.Property(x => x.SignatureAlgorithm).HasMaxLength(30);
        b.Property(x => x.SigningKeyId).HasMaxLength(64);
        b.Property(x => x.VerificationUrl).HasMaxLength(300);
        b.Property(x => x.RevocationReason).HasMaxLength(300);
        b.HasIndex(x => x.CertificateNumber).IsUnique();
        b.HasIndex(x => x.DeviceId).IsUnique();
        b.HasOne(x => x.Device).WithOne(d => d.Certificate).HasForeignKey<Certificate>(x => x.DeviceId);
        b.HasOne(x => x.SanitizationRecord).WithMany().HasForeignKey(x => x.SanitizationRecordId);
    }
}

public class RecoveredComponentConfiguration : IEntityTypeConfiguration<RecoveredComponent>
{
    public void Configure(EntityTypeBuilder<RecoveredComponent> b)
    {
        b.Property(x => x.ComponentCode).HasMaxLength(30);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.RecoveredByUserId).HasMaxLength(450);
        b.HasIndex(x => x.ComponentCode).IsUnique();
        b.HasIndex(x => new { x.Type, x.Status });
        b.HasOne(x => x.Device).WithMany(d => d.Components).HasForeignKey(x => x.DeviceId);
    }
}

public class HazardousMaterialLogConfiguration : IEntityTypeConfiguration<HazardousMaterialLog>
{
    public void Configure(EntityTypeBuilder<HazardousMaterialLog> b)
    {
        b.Property(x => x.WeightKg).HasPrecision(10, 3);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.StorageLocation).HasMaxLength(150);
        b.Property(x => x.HandledByUserId).HasMaxLength(450);
        b.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId);
    }
}

public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> b)
    {
        b.Property(x => x.ListingCode).HasMaxLength(20);
        b.Property(x => x.Title).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Brand).HasMaxLength(50);
        b.Property(x => x.Model).HasMaxLength(80);
        b.Property(x => x.CertificateNumber).HasMaxLength(20);
        b.Property(x => x.PriceSle).Money();
        b.HasIndex(x => x.ListingCode).IsUnique();
        b.HasIndex(x => new { x.Status, x.Brand });
        b.HasIndex(x => x.PriceSle);
        b.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId);
        b.HasOne(x => x.Component).WithMany().HasForeignKey(x => x.ComponentId);
    }
}

public class ListingPhotoConfiguration : IEntityTypeConfiguration<ListingPhoto>
{
    public void Configure(EntityTypeBuilder<ListingPhoto> b)
    {
        b.Property(x => x.StorageKey).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(50);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.Property(x => x.UploadedByUserId).HasMaxLength(450);
        b.Property(x => x.VerifiedByUserId).HasMaxLength(450);
        b.HasOne(x => x.Listing).WithMany(l => l.Photos).HasForeignKey(x => x.ListingId);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.OrderNumber).HasMaxLength(20);
        b.Property(x => x.BuyerUserId).HasMaxLength(450);
        b.Property(x => x.TotalSle).Money();
        b.Property(x => x.PaymentMethod).HasMaxLength(40);
        b.Property(x => x.PaymentReference).HasMaxLength(100);
        b.Property(x => x.CancellationReason).HasMaxLength(300);
        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.HasIndex(x => new { x.BuyerUserId, x.Status });
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.Property(x => x.TitleSnapshot).HasMaxLength(150);
        b.Property(x => x.PriceSle).Money();
        b.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId);
        b.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId);
    }
}

public class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.BalanceSle).Money();
        b.HasIndex(x => x.Type).IsUnique().HasFilter(Precision.NotDeleted);
        b.ToTable(t => t.HasCheckConstraint("ck_funds_balance_non_negative", "balance_sle >= 0"));
    }
}

public class FundTransactionConfiguration : IEntityTypeConfiguration<FundTransaction>
{
    public void Configure(EntityTypeBuilder<FundTransaction> b)
    {
        b.Property(x => x.AmountSle).Money();
        b.Property(x => x.BalanceAfterSle).Money();
        b.Property(x => x.ReferenceType).HasMaxLength(40);
        b.Property(x => x.ReferenceId).HasMaxLength(64);
        b.Property(x => x.Description).HasMaxLength(300);
        b.HasIndex(x => new { x.FundId, x.OccurredAt });
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        b.HasOne(x => x.Fund).WithMany(f => f.Transactions).HasForeignKey(x => x.FundId);
    }
}

public class ReinvestmentRuleConfiguration : IEntityTypeConfiguration<ReinvestmentRule>
{
    public void Configure(EntityTypeBuilder<ReinvestmentRule> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.SeedFundPercent).HasPrecision(5, 2);
        b.Property(x => x.TrainingFundPercent).HasPrecision(5, 2);
        b.Ignore(x => x.TotalPercent);
        b.HasIndex(x => x.IsActive).IsUnique().HasFilter("is_active = true AND is_deleted = false");
    }
}

public class TrainingConfiguration :
    IEntityTypeConfiguration<TrainingCycle>,
    IEntityTypeConfiguration<TraineeEnrollment>,
    IEntityTypeConfiguration<EmploymentRecord>,
    IEntityTypeConfiguration<AwarenessCampaign>
{
    public void Configure(EntityTypeBuilder<TrainingCycle> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Focus).HasMaxLength(200);
        b.Property(x => x.Location).HasMaxLength(120);
    }

    public void Configure(EntityTypeBuilder<TraineeEnrollment> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.UserId).HasMaxLength(450);
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.TrainingCycle).WithMany(c => c.Enrollments).HasForeignKey(x => x.TrainingCycleId);
    }

    public void Configure(EntityTypeBuilder<EmploymentRecord> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.JobTitle).HasMaxLength(100);
        b.Property(x => x.UserId).HasMaxLength(450);
    }

    public void Configure(EntityTypeBuilder<AwarenessCampaign> b)
    {
        b.Property(x => x.InstitutionName).HasMaxLength(150);
        b.Property(x => x.Location).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(1000);
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.Property(x => x.EntityType).HasMaxLength(40);
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.TrackingId).HasMaxLength(20);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.Actor).HasMaxLength(450);
        b.Property(x => x.PreviousHash).HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Hash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(x => x.SequenceNumber).IsUnique();
        b.HasIndex(x => x.Hash).IsUnique();
        b.HasIndex(x => x.TrackingId);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
