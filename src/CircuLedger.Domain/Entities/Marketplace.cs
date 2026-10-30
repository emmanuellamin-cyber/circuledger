using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;

namespace CircuLedger.Domain.Entities;

public class Listing : AuditableEntity
{
    public required string ListingCode { get; set; }
    public ListingItemType ItemType { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid? ComponentId { get; set; }
    public RecoveredComponent? Component { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Brand { get; set; }
    public string? Model { get; set; }
    public bool IsSamsung { get; set; }
    public ComponentType? ComponentType { get; set; }
    public decimal PriceSle { get; set; }
    public Grade Condition { get; set; }
    public int WarrantyDays { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public DateTime? PublishedAt { get; set; }

    /// <summary>Denormalised for buyer trust: shown on refurbished-device listings.</summary>
    public string? CertificateNumber { get; set; }

    public ICollection<ListingPhoto> Photos { get; set; } = [];

    public void Publish(DateTime now)
    {
        if (Status is not (ListingStatus.Draft or ListingStatus.Withdrawn))
            throw new InvalidStateTransitionException(nameof(Listing), Status, ListingStatus.Published);
        if (!Photos.Any(p => p.IsVerified && !p.IsDeleted))
            throw new DomainException("A listing needs at least one verified photo before it can be published.", "photo_required");
        Status = ListingStatus.Published;
        PublishedAt = now;
    }
}

/// <summary>Picture evidence: who uploaded it, when, and who verified it.</summary>
public class ListingPhoto : AuditableEntity
{
    public Guid ListingId { get; set; }
    public Listing? Listing { get; set; }
    public required string StorageKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public required string UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
    public bool IsVerified { get; set; }
    public string? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class Order : AuditableEntity
{
    public required string OrderNumber { get; set; }
    public required string BuyerUserId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalSle { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];

    public void MarkPaid(string method, string? reference, DateTime now)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidStateTransitionException(nameof(Order), Status, OrderStatus.Paid);
        Status = OrderStatus.Paid;
        PaymentMethod = method;
        PaymentReference = reference;
        PaidAt = now;
    }

    public void MarkFulfilled(DateTime now)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidStateTransitionException(nameof(Order), Status, OrderStatus.Fulfilled);
        Status = OrderStatus.Fulfilled;
        FulfilledAt = now;
    }

    public void Cancel(string reason, DateTime now)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidStateTransitionException(nameof(Order), Status, OrderStatus.Cancelled);
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = now;
    }
}

public class OrderItem : AuditableEntity
{
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ListingId { get; set; }
    public Listing? Listing { get; set; }
    public required string TitleSnapshot { get; set; }
    public decimal PriceSle { get; set; }
}
