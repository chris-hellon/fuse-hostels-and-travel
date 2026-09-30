namespace Fuse.Admin.Components.Tables;

/// <summary>What <see cref="EntityTable{T}"/> asks a page to load; maps onto any <see cref="PagedQuery"/>.</summary>
public sealed record GridRequest(int PageNumber, int PageSize, string? Keyword, string[]? OrderBy)
{
    public TQuery Apply<TQuery>(TQuery query) where TQuery : PagedQuery =>
        query with { PageNumber = PageNumber, PageSize = PageSize, Keyword = Keyword, OrderBy = OrderBy };
}
