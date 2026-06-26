using ClosedXML.Excel;
using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>TableModel → .xlsx (ClosedXML). Sayısal hücreler sayı olarak yazılır.</summary>
public sealed class ExcelExportService
{
    public byte[] Export(TableModel model)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Sanitize(model.Title));

        for (int c = 0; c < model.Headers.Count; c++)
            ws.Cell(1, c + 1).Value = model.Headers[c];
        ws.Row(1).Style.Font.Bold = true;

        for (int rIdx = 0; rIdx < model.Rows.Count; rIdx++)
        {
            var row = model.Rows[rIdx];
            for (int c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(rIdx + 2, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case double d: cell.Value = d; break;
                    case decimal m: cell.Value = m; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Sanitize(string title)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "Rapor" : title;
        foreach (var ch in new[] { ':', '\\', '/', '?', '*', '[', ']' }) t = t.Replace(ch, ' ');
        return t.Length > 31 ? t[..31] : t;
    }
}
