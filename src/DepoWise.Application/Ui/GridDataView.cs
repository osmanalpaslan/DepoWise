using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// Ortak tablo hücresi (Birim 4 + 2026-08-08 revizesi): GÖRÜNTÜ metni (<see cref="Text"/>) + HAM sayısal değer
/// (<see cref="Num"/>, yalnız sayısal kolonlarda dolu). Sıralama / filtre / karşılaştırma (&gt; &lt; &gt;= &lt;=) /
/// aralık HAM değer üzerinden yapılır; hücre <see cref="Text"/> ile gösterilir. Böylece "₺ 12.345,67", "1.250 km",
/// "-" gibi BİÇİMLİ görünüm, sayısal davranışı BOZMADAN korunur (kullanıcı isteği: değer ile görüntü ayrı).
/// </summary>
public sealed record GridCell(string Text, double? Num = null);

/// <summary>
/// Ortak tablonun İSTEMCİ TARAFI filtre + sıralama çekirdeği. Saf/deterministik → test edilebilir. Masaüstü
/// GridController + web DwDataGrid AYNI mantığı kullanır (web aynası). Sayısal kolonlarda HAM <see cref="GridCell.Num"/>
/// ile çalışır (biçimli metin sayısal davranışı bozmaz); metin kolonlarında <see cref="GridCell.Text"/> "içerir".
/// </summary>
public static class GridDataView
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly GridCell Empty = new("", null);

    public static List<IReadOnlyList<GridCell>> Compute(
        IReadOnlyList<ListColumn> columns,
        IReadOnlyList<IReadOnlyList<GridCell>> rows,
        IReadOnlyDictionary<string, string> filters,
        string? sortKey, bool sortDesc)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < columns.Count; i++) index[columns[i].Key] = i;
        GridCell At(IReadOnlyList<GridCell> r, int i) => i >= 0 && i < r.Count ? r[i] : Empty;

        IEnumerable<IReadOnlyList<GridCell>> q = rows;
        foreach (var col in columns)
        {
            if (!filters.TryGetValue(col.Key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            var f = raw.Trim(); var i = index[col.Key]; var numeric = col.IsNumeric;
            q = q.Where(r => Match(At(r, i), f, numeric));
        }

        var list = q.ToList();
        if (!string.IsNullOrEmpty(sortKey) && index.TryGetValue(sortKey!, out var si))
        {
            bool num = columns.FirstOrDefault(c => c.Key == sortKey)?.IsNumeric ?? false;
            list.Sort((a, b) =>
            {
                var ca = At(a, si); var cb = At(b, si);
                int c = num
                    ? Nullable.Compare(ca.Num, cb.Num)                                   // HAM değer sıralaması
                    : string.Compare(ca.Text, cb.Text, StringComparison.CurrentCultureIgnoreCase);
                return sortDesc ? -c : c;
            });
        }
        return list;
    }

    /// <summary>Excel-benzeri eşleşme. Metin: içerir. Sayısal: tam / karşılaştırma (&gt; &lt; &gt;= &lt;=) / aralık (5-10)
    /// — HAM <see cref="GridCell.Num"/> üzerinden (görüntü metni değil).</summary>
    public static bool Match(GridCell cell, string filter, bool numeric)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        filter = filter.Trim();
        if (!numeric) return (cell.Text ?? "").Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        if (cell.Num is not double v) return false;
        var dash = filter.IndexOf('-', 1);
        if (dash > 0 && TryNum(filter[..dash], out var lo) && TryNum(filter[(dash + 1)..], out var hi))
            return v >= (double)Math.Min(lo, hi) && v <= (double)Math.Max(lo, hi);
        if (filter.StartsWith(">=") && TryNum(filter[2..], out var g1)) return v >= (double)g1;
        if (filter.StartsWith("<=") && TryNum(filter[2..], out var l1)) return v <= (double)l1;
        if (filter.StartsWith(">") && TryNum(filter[1..], out var g2)) return v > (double)g2;
        if (filter.StartsWith("<") && TryNum(filter[1..], out var l2)) return v < (double)l2;
        return TryNum(filter, out var eq) && Math.Round(v, 4) == Math.Round((double)eq, 4);
    }

    /// <summary>Kullanıcının yazdığı filtre operandını (düz sayı) ayrıştırır: TR virgül + Invariant nokta.</summary>
    public static bool TryNum(string? s, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().Replace(" ", "").Replace(" ", "");
        if (decimal.TryParse(t, NumberStyles.Number, Tr, out value)) return true;
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return true;
        return false;
    }
}
