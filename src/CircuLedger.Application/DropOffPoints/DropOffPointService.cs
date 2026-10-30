using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.DropOffPoints;

public sealed record DropOffPointDto(
    Guid Id,
    string Name,
    DropOffPointType Type,
    double Latitude,
    double Longitude,
    string? Landmark,
    string OpeningHours,
    bool IsActive,
    int DisplayOrder);

public sealed record UpsertDropOffPointRequest(
    string Name,
    DropOffPointType Type,
    double Latitude,
    double Longitude,
    string? Landmark,
    string OpeningHours,
    bool IsActive = true,
    int DisplayOrder = 0);

public sealed class UpsertDropOffPointRequestValidator : AbstractValidator<UpsertDropOffPointRequest>
{
    public UpsertDropOffPointRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        // Rough bounding box for Sierra Leone, which catches swapped lat/lng.
        RuleFor(x => x.Latitude).InclusiveBetween(6.8, 10.1);
        RuleFor(x => x.Longitude).InclusiveBetween(-13.4, -10.2);
        RuleFor(x => x.OpeningHours).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Landmark).MaximumLength(200);
    }
}

public interface IDropOffPointService
{
    Task<IReadOnlyList<DropOffPointDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<DropOffPointDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DropOffPointDto> CreateAsync(UpsertDropOffPointRequest request, CancellationToken ct = default);
    Task<DropOffPointDto> UpdateAsync(Guid id, UpsertDropOffPointRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class DropOffPointService(IApplicationDbContext db) : IDropOffPointService
{
    public async Task<IReadOnlyList<DropOffPointDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var points = await db.DropOffPoints.AsNoTracking()
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .ToListAsync(ct);
        return points.Adapt<List<DropOffPointDto>>();
    }

    public async Task<DropOffPointDto> GetAsync(Guid id, CancellationToken ct = default) =>
        (await FindAsync(id, ct)).Adapt<DropOffPointDto>();

    public async Task<DropOffPointDto> CreateAsync(UpsertDropOffPointRequest request, CancellationToken ct = default)
    {
        var point = request.Adapt<DropOffPoint>();
        db.DropOffPoints.Add(point);
        await db.SaveChangesAsync(ct);
        return point.Adapt<DropOffPointDto>();
    }

    public async Task<DropOffPointDto> UpdateAsync(Guid id, UpsertDropOffPointRequest request, CancellationToken ct = default)
    {
        var point = await FindAsync(id, ct);
        request.Adapt(point);
        await db.SaveChangesAsync(ct);
        return point.Adapt<DropOffPointDto>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var point = await FindAsync(id, ct);
        db.DropOffPoints.Remove(point); // soft delete via DbContext
        await db.SaveChangesAsync(ct);
    }

    private async Task<DropOffPoint> FindAsync(Guid id, CancellationToken ct) =>
        await db.DropOffPoints.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("DropOffPoint", id);
}
