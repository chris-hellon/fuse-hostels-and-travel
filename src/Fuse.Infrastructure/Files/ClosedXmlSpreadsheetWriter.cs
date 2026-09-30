using ClosedXML.Excel;
using Fuse.Application.Common.Files;
using Fuse.Contracts.Common;

namespace Fuse.Infrastructure.Files;

/// <summary>Exports with ClosedXML as legacy's ExcelWriter laid them out: one sheet named "table" holding an Excel table
/// (header row, banding and filters), typed cells, column formats and fitted widths.</summary>
internal sealed class ClosedXmlSpreadsheetWriter : ISpreadsheetWriter
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Legacy's sheet (and table) name.</summary>
    public const string SheetName = "table";

    public FileDownload Write<T>(string fileName, IReadOnlyList<T> rows, IReadOnlyList<SpreadsheetColumn<T>> columns)
    {
        using var workbook = new XLWorkbook();
        // Legacy added a DataTable named "table": the sheet and its Excel table are both called that.
        var sheet = workbook.AddWorksheet(SheetName);

        for (var c = 0; c < columns.Count; c++)
            sheet.Cell(1, c + 1).Value = columns[c].Header;

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                cell.Value = ToCell(columns[c].Value(rows[r]));
                if (columns[c].Format is { } format)
                    cell.Style.NumberFormat.Format = format;
            }
        }

        // With no rows, a table still needs a (blank) data row, as legacy's empty DataTable got.
        if (columns.Count > 0)
            sheet.Range(1, 1, Math.Max(rows.Count + 1, 2), columns.Count).CreateTable(SheetName);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 500));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new FileDownload(fileName, ContentType, stream.ToArray());
    }

    private static XLCellValue ToCell(object? value) => value switch
    {
        null => Blank.Value,
        string s => s,
        bool b => b,
        DateTime d when d.Year < 1900 => Blank.Value,
        DateTime d => d,
        decimal m => m,
        int i => i,
        double f => f,
        Enum e => e.ToString(),
        _ => value.ToString(),
    };
}
