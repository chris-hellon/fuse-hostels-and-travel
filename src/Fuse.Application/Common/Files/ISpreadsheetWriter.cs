namespace Fuse.Application.Common.Files;

/// <summary>A column in a spreadsheet export: header, value and (optionally) an Excel number format.</summary>
public sealed record SpreadsheetColumn<T>(string Header, Func<T, object?> Value, string? Format = null);

/// <summary>Writes rows to an .xlsx file (one sheet, a header row, then a row per item).</summary>
public interface ISpreadsheetWriter
{
    FileDownload Write<T>(string fileName, IReadOnlyList<T> rows, IReadOnlyList<SpreadsheetColumn<T>> columns);
}
