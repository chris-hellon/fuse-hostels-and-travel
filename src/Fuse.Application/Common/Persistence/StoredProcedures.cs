using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fuse.Application.Common.Persistence;

/// <summary>Stored procedures (see /database) run on the unit of work's own connection.</summary>
public static class StoredProcedures
{
    public static async Task<IReadOnlyList<T>> QueryProcedureAsync<T>(this IAppDbContext db, string procedure, object parameters,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var rows = await connection.QueryAsync<T>(new CommandDefinition(procedure, parameters, transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>A <c>dbo.GuidList</c> table-valued parameter.</summary>
    public static SqlMapper.ICustomQueryParameter GuidList(IEnumerable<DefaultIdType> ids)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(DefaultIdType));
        foreach (var id in ids.Distinct())
            table.Rows.Add(id);
        return table.AsTableValuedParameter("dbo.GuidList");
    }

    /// <summary>Maps the client's sort (<c>"FullName desc"</c>) to the procs' <c>"FullName Descending"</c>.</summary>
    public static string ProcedureOrderBy(string[]? orderBy, string fallback)
    {
        var first = orderBy?.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o));
        if (first is null)
            return $"{fallback} Ascending";
        var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var direction = parts.Length > 1 && parts[1].StartsWith("desc", StringComparison.OrdinalIgnoreCase) ? "Descending" : "Ascending";
        return $"{parts[0]} {direction}";
    }
}
