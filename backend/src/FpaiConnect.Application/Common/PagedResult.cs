using Microsoft.AspNetCore.Http;

namespace FpaiConnect.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>
/// Common list-query envelope. Bound via <see cref="BindAsync"/> so every paging parameter is
/// optional and clamped in one place — a client cannot request page 0 or the whole table.
/// </summary>
public class PageQuery
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 25;

    private int _pageSize = DefaultPageSize;
    private int _page = 1;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }

    /// <summary>Minimal-API binder: reads optional query parameters and applies the clamps above.</summary>
    public static ValueTask<PageQuery?> BindAsync(HttpContext context)
    {
        var q = context.Request.Query;

        var result = new PageQuery
        {
            Page = int.TryParse(q["page"], out var page) ? page : 1,
            PageSize = int.TryParse(q["pageSize"], out var size) ? size : DefaultPageSize,
            Search = string.IsNullOrWhiteSpace(q["search"]) ? null : q["search"].ToString().Trim(),
            SortBy = string.IsNullOrWhiteSpace(q["sortBy"]) ? null : q["sortBy"].ToString().Trim(),
            SortDescending = bool.TryParse(q["sortDescending"], out var desc) && desc
        };

        return ValueTask.FromResult<PageQuery?>(result);
    }
}
