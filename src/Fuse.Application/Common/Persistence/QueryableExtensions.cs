using System.Linq.Expressions;
using System.Reflection;

namespace Fuse.Application.Common.Persistence;

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PagedQuery paging,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, paging.PageNumber);
        var pageSize = Math.Clamp(paging.PageSize, 1, 500);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<T>(items, total, pageNumber, pageSize);
    }

    /// <summary>A page of rows already in memory (e.g. from a stored procedure), after sorting.</summary>
    public static PagedResult<T> ToPage<T>(this IQueryable<T> rows, PagedQuery paging)
    {
        var pageNumber = Math.Max(1, paging.PageNumber);
        var pageSize = Math.Clamp(paging.PageSize, 1, 500);
        var list = rows.ToList();
        return new PagedResult<T>([.. list.Skip((pageNumber - 1) * pageSize).Take(pageSize)], list.Count, pageNumber, pageSize);
    }

    /// <summary>
    /// Applies the client's sort (<c>"Name"</c>, <c>"CreatedOn desc"</c>) by projected property name,
    /// or <paramref name="fallback"/> (newest first with <paramref name="descending"/>) when none is given.
    /// Unknown names are ignored, never reflected into SQL.
    /// </summary>
    public static IQueryable<T> OrderBy<T>(this IQueryable<T> query, string[]? orderBy, Expression<Func<T, object?>> fallback, bool descending = false)
    {
        IOrderedQueryable<T>? ordered = null;
        foreach (var clause in orderBy ?? [])
        {
            var parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            var property = typeof(T).GetProperty(parts[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || property.IsDefined(typeof(NotSortableAttribute)))
                continue;

            var desc = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            var parameter = Expression.Parameter(typeof(T), "x");
            var selector = Expression.Lambda(Expression.Property(parameter, property), parameter);
            var method = (ordered, desc) switch
            {
                (null, false) => nameof(Queryable.OrderBy),
                (null, true) => nameof(Queryable.OrderByDescending),
                (_, false) => nameof(Queryable.ThenBy),
                (_, true) => nameof(Queryable.ThenByDescending),
            };

            ordered = (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(Expression.Call(
                typeof(Queryable), method, [typeof(T), property.PropertyType], (ordered ?? query).Expression, Expression.Quote(selector)));
        }

        return ordered ?? (descending ? query.OrderByDescending(fallback) : query.OrderBy(fallback));
    }
}
