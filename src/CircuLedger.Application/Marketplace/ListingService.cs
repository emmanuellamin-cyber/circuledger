using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CircuLedger.Application.Marketplace;

public sealed record CreateListingRequest(
    ListingItemType ItemType,
    Guid? DeviceId,
    Guid? ComponentId,
    string Title,
    string? Description,
    decimal PriceSle,
    Grade Condition,
    int WarrantyDays);

public sealed record UpdateListingRequest(string Title, string? Description, decimal PriceSle, Grade Condition, int WarrantyDays);

public sealed record ListingPhotoDto(
    Guid Id,
    string Url,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTime UploadedAt,
    bool IsVerified,
    DateTime? VerifiedAt);

public sealed record ListingDto(
    Guid Id,
    string ListingCode,
    ListingItemType ItemType,
    string Title,
    string? Description,
    string Brand,
    string? Model,
    bool IsSamsung,
    ComponentType? ComponentType,
    decimal PriceSle,
    Grade Condition,
    int WarrantyDays,
    ListingStatus Status,
    DateTime? PublishedAt,
    string? CertificateNumber,
    string? CertificateVerificationUrl,
    IReadOnlyList<ListingPhotoDto> Photos);

public sealed record ListingSearchQuery : PageQuery
{
    public string? Search { get; init; }
    public string? Brand { get; init; }
    public ListingItemType? ItemType { get; init; }
    public ComponentType? ComponentType { get; init; }
    public Grade? Condition { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? SamsungOnly { get; init; }
    public ListingStatus? Status { get; init; }

    /// <summary>newest (default), price_asc, price_desc</summary>
    public string? Sort { get; init; }
}

public sealed record PhotoUpload(Stream Content, string FileName, string ContentType, long Length);

public sealed record PhotoContent(Stream Content, string ContentType);

public sealed class CreateListingRequestValidator : AbstractValidator<CreateListingRequest>
{
    public CreateListingRequestValidator()
    {
        RuleFor(x => x.ItemType).IsInEnum();
        RuleFor(x => x.DeviceId).NotEmpty().When(x => x.ItemType == ListingItemType.RefurbishedDevice);
        RuleFor(x => x.ComponentId).NotEmpty().When(x => x.ItemType == ListingItemType.Component);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.PriceSle).GreaterThan(0).LessThan(1_000_000);
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.WarrantyDays).InclusiveBetween(0, 365);
    }
}

public sealed class UpdateListingRequestValidator : AbstractValidator<UpdateListingRequest>
{
    public UpdateListingRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.PriceSle).GreaterThan(0).LessThan(1_000_000);
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.WarrantyDays).InclusiveBetween(0, 365);
    }
}

public interface IListingService
{
    Task<ListingDto> CreateAsync(CreateListingRequest request, CancellationToken ct = default);
    Task<ListingDto> UpdateAsync(Guid id, UpdateListingRequest request, CancellationToken ct = default);
    Task<ListingPhotoDto> UploadPhotoAsync(Guid listingId, PhotoUpload upload, CancellationToken ct = default);
    Task<ListingPhotoDto> VerifyPhotoAsync(Guid listingId, Guid photoId, CancellationToken ct = default);
    Task<ListingDto> PublishAsync(Guid id, CancellationToken ct = default);
    Task<ListingDto> WithdrawAsync(Guid id, CancellationToken ct = default);
    Task<ListingDto> GetAsync(Guid id, bool publicOnly, CancellationToken ct = default);
    Task<PagedResult<ListingDto>> SearchAsync(ListingSearchQuery query, bool publicOnly, CancellationToken ct = default);
    Task<PhotoContent> GetPhotoAsync(Guid listingId, Guid photoId, bool publicOnly, CancellationToken ct = default);
}

public sealed class ListingService(
    IApplicationDbContext db,
    IFileStorage storage,
    ILedgerService ledger,
    ICurrentUser currentUser,
    IOptions<UploadOptions> uploadOptions,
    IOptions<HubOptions> hubOptions,
    TimeProvider time) : IListingService
{
    public async Task<ListingDto> CreateAsync(CreateListingRequest request, CancellationToken ct = default)
    {
        var listing = new Listing
        {
            ListingCode = CodeGenerator.ListingCode(await db.NextSequenceValueAsync(Sequences.Listing, ct)),
            ItemType = request.ItemType,
            Title = request.Title.Trim(),
            Description = request.Description,
            Brand = string.Empty,
            PriceSle = request.PriceSle,
            Condition = request.Condition,
            WarrantyDays = request.WarrantyDays
        };

        if (request.ItemType == ListingItemType.RefurbishedDevice)
        {
            var device = await db.Devices.Include(d => d.Certificate).FirstOrDefaultAsync(d => d.Id == request.DeviceId, ct)
                         ?? throw new NotFoundException("Device", request.DeviceId!);
            if (device.RecoveryOutcome != RecoveryOutcome.Refurbish || device.Status != DeviceStatus.Refurbishing)
                throw new DomainException($"Device {device.TrackingId} is not marked for refurbishment.");
            if (device.Certificate is not { IsRevoked: false })
                throw new DomainException("Refurbished devices can only be listed with a valid sanitization certificate.");
            if (await db.Listings.AnyAsync(l => l.DeviceId == device.Id && l.Status != ListingStatus.Withdrawn, ct))
                throw new ConflictException($"Device {device.TrackingId} already has an active listing.", "already_listed");

            listing.DeviceId = device.Id;
            listing.Brand = device.Brand;
            listing.Model = device.Model;
            listing.IsSamsung = device.IsSamsungFlagship;
            listing.CertificateNumber = device.Certificate.CertificateNumber;
        }
        else
        {
            var component = await db.RecoveredComponents.Include(c => c.Device).FirstOrDefaultAsync(c => c.Id == request.ComponentId, ct)
                            ?? throw new NotFoundException("Component", request.ComponentId!);
            if (component.Status != ComponentStatus.InStock)
                throw new DomainException($"Component {component.ComponentCode} is {component.Status}, not in stock.");
            if (await db.Listings.AnyAsync(l => l.ComponentId == component.Id && l.Status != ListingStatus.Withdrawn, ct))
                throw new ConflictException($"Component {component.ComponentCode} already has an active listing.", "already_listed");

            listing.ComponentId = component.Id;
            listing.ComponentType = component.Type;
            listing.Brand = component.Device!.Brand;
            listing.Model = component.Device.Model;
            listing.IsSamsung = component.Device.IsSamsungFlagship;
        }

        db.Listings.Add(listing);
        await db.SaveChangesAsync(ct);
        return ToDto(listing);
    }

    public async Task<ListingDto> UpdateAsync(Guid id, UpdateListingRequest request, CancellationToken ct = default)
    {
        var listing = await LoadAsync(id, ct);
        if (listing.Status is ListingStatus.Reserved or ListingStatus.Sold)
            throw new DomainException("Reserved or sold listings cannot be edited.");
        listing.Title = request.Title.Trim();
        listing.Description = request.Description;
        listing.PriceSle = request.PriceSle;
        listing.Condition = request.Condition;
        listing.WarrantyDays = request.WarrantyDays;
        await db.SaveChangesAsync(ct);
        return ToDto(listing);
    }

    public async Task<ListingPhotoDto> UploadPhotoAsync(Guid listingId, PhotoUpload upload, CancellationToken ct = default)
    {
        var listing = await LoadAsync(listingId, ct);
        if (listing.Status is ListingStatus.Sold) throw new DomainException("Sold listings cannot receive new photos.");

        var opts = uploadOptions.Value;
        if (upload.Length <= 0 || upload.Length > opts.MaxPhotoBytes)
            throw new ValidationException($"Photo must be between 1 byte and {opts.MaxPhotoBytes / 1024 / 1024} MB.");
        if (!opts.AllowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException($"Photo type must be one of: {string.Join(", ", opts.AllowedContentTypes)}.");

        // Don't trust the declared type: check the file signature (magic bytes).
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > opts.MaxPhotoBytes) throw new ValidationException("Photo is too large.");
        var detected = ImageSignature.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)))
                       ?? throw new ValidationException("File content is not a JPEG, PNG or WebP image.");
        if (!detected.ContentType.Equals(upload.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"File content ({detected.ContentType}) does not match declared type ({upload.ContentType}).");

        buffer.Position = 0;
        var stored = await storage.SaveAsync(buffer, detected.Extension, detected.ContentType, ct);
        var photo = new ListingPhoto
        {
            ListingId = listing.Id,
            StorageKey = stored.Key,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            UploadedByUserId = currentUser.Actor,
            UploadedAt = time.GetUtcNow().UtcDateTime
        };
        db.ListingPhotos.Add(photo);
        await db.SaveChangesAsync(ct);
        return ToDto(listing.Id, photo);
    }

    public async Task<ListingPhotoDto> VerifyPhotoAsync(Guid listingId, Guid photoId, CancellationToken ct = default)
    {
        var photo = await db.ListingPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.ListingId == listingId, ct)
                    ?? throw new NotFoundException("ListingPhoto", photoId);
        // Four-eyes principle: the uploader can't verify their own picture evidence (Admin may override).
        if (photo.UploadedByUserId == currentUser.UserId && !currentUser.IsInRole(Roles.Admin))
            throw new ForbiddenAccessException("Photos must be verified by someone other than the uploader.");

        photo.IsVerified = true;
        photo.VerifiedByUserId = currentUser.Actor;
        photo.VerifiedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(listingId, photo);
    }

    public async Task<ListingDto> PublishAsync(Guid id, CancellationToken ct = default) =>
        await db.ExecuteInTransactionAsync(async t =>
        {
            var listing = await LoadAsync(id, t);
            listing.Publish(time.GetUtcNow().UtcDateTime);

            string? trackingId = null;
            if (listing.Device is { } device)
            {
                device.Status = DeviceStatus.Listed;
                trackingId = device.TrackingId;
            }
            if (listing.Component is { } component)
            {
                component.Status = ComponentStatus.Listed;
                trackingId = component.Device?.TrackingId;
            }

            await ledger.AppendAsync(LedgerEventType.Listed, LedgerEntityTypes.Listing, listing.Id, new
            {
                listing.ListingCode,
                listing.ItemType,
                listing.PriceSle,
                listing.Condition,
                listing.WarrantyDays,
                listing.CertificateNumber,
                ComponentCode = listing.Component?.ComponentCode,
                VerifiedPhotoHashes = listing.Photos.Where(p => p.IsVerified).Select(p => p.Sha256).ToList()
            }, trackingId, t);

            return ToDto(listing);
        }, ct);

    public async Task<ListingDto> WithdrawAsync(Guid id, CancellationToken ct = default)
    {
        var listing = await LoadAsync(id, ct);
        if (listing.Status is ListingStatus.Reserved or ListingStatus.Sold)
            throw new DomainException("Reserved or sold listings cannot be withdrawn.");
        listing.Status = ListingStatus.Withdrawn;
        if (listing.Device is { } device) device.Status = DeviceStatus.Refurbishing;
        if (listing.Component is { } component) component.Status = ComponentStatus.InStock;
        await db.SaveChangesAsync(ct);
        return ToDto(listing);
    }

    public async Task<ListingDto> GetAsync(Guid id, bool publicOnly, CancellationToken ct = default)
    {
        var listing = await Query().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (listing is null || (publicOnly && listing.Status != ListingStatus.Published))
            throw new NotFoundException("Listing", id);
        return ToDto(listing, publicOnly);
    }

    public Task<PagedResult<ListingDto>> SearchAsync(ListingSearchQuery query, bool publicOnly, CancellationToken ct = default)
    {
        var q = Query().AsNoTracking();
        if (publicOnly) q = q.Where(l => l.Status == ListingStatus.Published);
        else if (query.Status is { } status) q = q.Where(l => l.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(l => l.Title.ToLower().Contains(term) || l.Brand.ToLower().Contains(term)
                             || (l.Model != null && l.Model.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(query.Brand)) q = q.Where(l => l.Brand.ToLower() == query.Brand.Trim().ToLower());
        if (query.ItemType is { } itemType) q = q.Where(l => l.ItemType == itemType);
        if (query.ComponentType is { } componentType) q = q.Where(l => l.ComponentType == componentType);
        if (query.Condition is { } condition) q = q.Where(l => l.Condition == condition);
        if (query.MinPrice is { } min) q = q.Where(l => l.PriceSle >= min);
        if (query.MaxPrice is { } max) q = q.Where(l => l.PriceSle <= max);
        if (query.SamsungOnly == true) q = q.Where(l => l.IsSamsung);

        q = query.Sort?.ToLowerInvariant() switch
        {
            "price_asc" => q.OrderBy(l => l.PriceSle).ThenBy(l => l.ListingCode),
            "price_desc" => q.OrderByDescending(l => l.PriceSle).ThenBy(l => l.ListingCode),
            _ => q.OrderByDescending(l => l.PublishedAt ?? l.CreatedAt).ThenBy(l => l.ListingCode)
        };
        return q.ToPagedAsync(query, l => ToDto(l, publicOnly), ct);
    }

    public async Task<PhotoContent> GetPhotoAsync(Guid listingId, Guid photoId, bool publicOnly, CancellationToken ct = default)
    {
        var photo = await db.ListingPhotos.AsNoTracking().Include(p => p.Listing)
                        .FirstOrDefaultAsync(p => p.Id == photoId && p.ListingId == listingId, ct)
                    ?? throw new NotFoundException("ListingPhoto", photoId);
        if (publicOnly && (!photo.IsVerified || photo.Listing!.Status is not (ListingStatus.Published or ListingStatus.Reserved or ListingStatus.Sold)))
            throw new NotFoundException("ListingPhoto", photoId);

        var stream = await storage.OpenReadAsync(photo.StorageKey, ct) ?? throw new NotFoundException("ListingPhoto file", photoId);
        return new PhotoContent(stream, photo.ContentType);
    }

    private IQueryable<Listing> Query() => db.Listings
        .Include(l => l.Photos)
        .Include(l => l.Device)
        .Include(l => l.Component).ThenInclude(c => c!.Device);

    private async Task<Listing> LoadAsync(Guid id, CancellationToken ct) =>
        await Query().FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Listing", id);

    private ListingDto ToDto(Listing l) => ToDto(l, publicOnly: false);

    private ListingDto ToDto(Listing l, bool publicOnly) => new(
        l.Id, l.ListingCode, l.ItemType, l.Title, l.Description, l.Brand, l.Model, l.IsSamsung, l.ComponentType,
        l.PriceSle, l.Condition, l.WarrantyDays, l.Status, l.PublishedAt, l.CertificateNumber,
        l.CertificateNumber is null ? null : $"{hubOptions.Value.PublicBaseUrl.TrimEnd('/')}/api/v1/public/certificates/{l.CertificateNumber}/verify",
        l.Photos.Where(p => !p.IsDeleted && (!publicOnly || p.IsVerified)).OrderBy(p => p.UploadedAt).Select(p => ToDto(l.Id, p)).ToList());

    private static ListingPhotoDto ToDto(Guid listingId, ListingPhoto p) => new(
        p.Id, $"/api/v1/public/listings/{listingId}/photos/{p.Id}", p.ContentType, p.SizeBytes, p.Sha256, p.UploadedAt,
        p.IsVerified, p.VerifiedAt);
}

internal sealed record DetectedImage(string ContentType, string Extension);

internal static class ImageSignature
{
    public static DetectedImage? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return new DetectedImage("image/jpeg", ".jpg");
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return new DetectedImage("image/png", ".png");
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return new DetectedImage("image/webp", ".webp");
        return null;
    }
}
