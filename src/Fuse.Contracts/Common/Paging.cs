namespace Fuse.Contracts.Common;

/// <summary>Base for list queries. Matches the legacy PaginationFilter shape the admin tables use.</summary>
public abstract record PagedQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Keyword { get; init; }

    /// <summary>Sort expressions, e.g. <c>"Name"</c> or <c>"CreatedOn desc"</c>.</summary>
    public string[]? OrderBy { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Empty(int pageSize = 10) => new([], 0, 1, pageSize);
}
