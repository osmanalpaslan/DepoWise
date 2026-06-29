using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>TableModel → .xlsx (ClosedXML). Sayısal hücreler sayı olarak yazılır. Ayrıca import için xlsx okur + şablon üretir.</summary>
public sealed class ExcelExportService
{
    /// <summary>Boş şablon: yalnız başlık satırı (içe aktarım örneği).</summary>
    public byte[] Template(string title, IReadOnlyList<string> headers)
        => Export(new TableModel(title, headers, System.Array.Empty<IReadOnlyList<object?>>()));

    /// <summary>xlsx → satırlar (ilk çalışma sayfası; 1. satır başlık, kalanlar veri). İçe aktarım için.</summary>
    public IReadOnlyList<ImportRow> ReadRows(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return System.Array.Empty<ImportRow>();
        var rows = used.RowsUsed().ToList();
        if (rows.Count < 2) return System.Array.Empty<ImportRow>();

        var headers = rows[0].Cells(1, used.ColumnCount()).Select(c => c.GetString().Trim()).ToList();
        var result = new List<ImportRow>();
        for (int i = 1; i < rows.Count; i++)
        {
            var dict = new Dictionary<string, string?>();
            for (int c = 0; c < headers.Count; c++)
            {
                if (string.IsNullOrEmpty(headers[c])) continue;
                var val = rows[i].Cell(c + 1).GetString();
                dict[headers[c]] = string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }
            // Tamamen boş satırı atla
            if (dict.Values.Any(v => v is not null))
                result.Add(new ImportRow(i + 1, dict));
        }
        return result;
    }


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
