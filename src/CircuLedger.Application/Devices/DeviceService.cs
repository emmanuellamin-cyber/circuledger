using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Devices;

public interface IDeviceService
{
    Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken ct = default);
    Task<DeviceDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DeviceDto> GetByTrackingIdAsync(string trackingId, CancellationToken ct = default);
}

public sealed class DeviceService(IApplicationDbContext db) : IDeviceService
{
    public Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken ct = default)
    {
        var q = Query().AsNoTracking();
        if (query.Status is { } status) q = q.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Brand)) q = q.Where(d => d.Brand.ToLower() == query.Brand.Trim().ToLower());
        if (query.SamsungOnly == true) q = q.Where(d => d.IsSamsungFlagship);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(d => d.TrackingId.ToLower().Contains(term) || (d.Model != null && d.Model.ToLower().Contains(term)));
        }
        return q.OrderByDescending(d => d.CreatedAt).ToPagedAsync(query, DeviceDto.From, ct);
    }

    public async Task<DeviceDto> GetAsync(Guid id, CancellationToken ct = default) =>
        DeviceDto.From(await Query().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Device", id));

    public async Task<DeviceDto> GetByTrackingIdAsync(string trackingId, CancellationToken ct = default) =>
        DeviceDto.From(await Query().AsNoTracking().FirstOrDefaultAsync(d => d.TrackingId == trackingId.ToUpper(), ct)
                       ?? throw new NotFoundException("Device", trackingId));

    internal static IQueryable<Device> Include(IQueryable<Device> devices) => devices
        .Include(d => d.Location)
        .Include(d => d.IntakeRequest)
        .Include(d => d.Certificate)
        .Include(d => d.Purchase);

    private IQueryable<Device> Query() => Include(db.Devices);
}
