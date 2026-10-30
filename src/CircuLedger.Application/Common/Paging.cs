using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.Common;

public record PageQuery
{
    private const int MaxPageSize = 100;
    private readonly int _page = 1;
    private readonly int _pageSize = 20;

    public int Page { get => _page; init => _page = Math.Max(1, value); }
    public int PageSize { get => _pageSize; init => _pageSize = Math.Clamp(value, 1, MaxPageSize); }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<TDto>> ToPagedAsync<TEntity, TDto>(
        this IQueryable<TEntity> query, PageQuery page, Func<TEntity, TDto> map, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<TDto>(items.Select(map).ToList(), page.Page, page.PageSize, total);
    }
}
