using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// Ortak tablonun (Birim 4) İSTEMCİ TARAFI filtre + sıralama çekirdeği — in-memory satırlar üzerinde çalışır
/// (sunucuya tekrar sorulmaz). Excel-benzeri eşleşme: metin=içerir; sayısal=tam / karşılaştırma (> &lt; &gt;= &lt;=) /
/// aralık (5-10). Saf ve deterministiktir → test edilebilir. Masaüstü GridController bunu kullanır; web bileşeni
/// (Application'a referansı olmadığından) AYNI mantığın aynasını taşır.
/// </summary>
public static class GridDataView
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Filtreleri (anahtar→metin) ve isteğe bağlı sıralamayı uygular; sonucu (satır referansları) döndürür.</summary>
    public static List<IReadOnlyList<string>> Compute(
        IReadOnlyList<ListColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> filters,
        string? sortKey, bool sortDesc)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < columns.Count; i++) index[columns[i].Key] = i;
        string Cell(IReadOnlyList<string> r, string key) => index.TryGetValue(key, out var i) && i < r.Count ? r[i] : "";

        IEnumerable<IReadOnlyList<string>> q = rows;
        foreach (var col in columns)
        {
            if (!filters.TryGetValue(col.Key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            var f = raw.Trim(); var key = col.Key; var numeric = col.IsNumeric;
            q = q.Where(r => Match(Cell(r, key), f, numeric));
        }

        var list = q.ToList();
        if (!string.IsNullOrEmpty(sortKey) && index.ContainsKey(sortKey!))
        {
            bool num = columns.FirstOrDefault(c => c.Key == sortKey)?.IsNumeric ?? false;
            var key = sortKey!; var desc = sortDesc;
            list.Sort((a, b) =>
            {
                var av = Cell(a, key); var bv = Cell(b, key);
                int c = (num && TryNum(av, out var ad) && TryNum(bv, out var bd))
                    ? ad.CompareTo(bd)
                    : string.Compare(av, bv, StringComparison.CurrentCultureIgnoreCase);
                return desc ? -c : c;
            });
        }
        return list;
    }

    /// <summary>Tek hücre bir filtre metniyle eşleşiyor mu (Excel-benzeri).</summary>
    public static bool Match(string cell, string filter, bool numeric)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        filter = filter.Trim();
        if (!numeric) return (cell ?? "").Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        if (!TryNum(cell, out var v)) return false;
        var dash = filter.IndexOf('-', 1);
        if (dash > 0 && TryNum(filter[..dash], out var lo) && TryNum(filter[(dash + 1)..], out var hi))
            return v >= Math.Min(lo, hi) && v <= Math.Max(lo, hi);
        if (filter.StartsWith(">=") && TryNum(filter[2..], out var g1)) return v >= g1;
        if (filter.StartsWith("<=") && TryNum(filter[2..], out var l1)) return v <= l1;
        if (filter.StartsWith(">") && TryNum(filter[1..], out var g2)) return v > g2;
        if (filter.StartsWith("<") && TryNum(filter[1..], out var l2)) return v < l2;
        return TryNum(filter, out var eq) && v == eq;
    }

    /// <summary>Kültür-duyarlı sayı ayrıştırma (TR virgül + Invariant nokta). Baştaki/sondaki boşlukları atar.</summary>
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
