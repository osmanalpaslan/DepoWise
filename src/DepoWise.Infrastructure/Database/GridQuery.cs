using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database;

/// <summary>Genel sonuç: sayfa + toplam kayıt sayısı (numaralı sayfalama + sayfa boyutu seçimi için).</summary>
public sealed record GridResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// Kolon bazlı filtreli liste sorguları için ortak yardımcı (kullanıcı isteği 2026-07-17: "metin girilen
/// filtre alanlarında içinde arama ve metnin başlangıcına göre arama yapmalı" + "gösterilecek kayıt sayısı
/// seçim alanı ve 1,2,3... şeklinde sayfa yapısı").
///
/// Çağıran, alan adlarını (SQL kolon takma adları) ve kullanıcının o alan için yazdığı filtre metnini SIRALI
/// olarak verir. Bu sıra ÖNEMLİDİR: birden çok filtre doluysa, önce ilk filtrenin "başlangıca göre" önceliği
/// uygulanır, sonra ikincisi — böylece davranış hangi kutuyu önce doldurduğuna göre değişmez, DAİMA aynı
/// deterministik sırayla çalışır (kolon kataloğundaki sabit sıra).
///
/// Neden derived-table (SELECT * FROM (...) t WHERE ...): hesaplanan/join'lenmiş kolonlar (stok bakiyesi,
/// durum metni, uyumlu araç listesi gibi) SQL WHERE'de SELECT takma adıyla doğrudan kullanılamaz (standart
/// SQL değerlendirme sırası) — sarma sorgusu, ham VE hesaplanan her kolonu AYNI filtre/sıralama mantığıyla
/// ele almayı sağlar.
/// </summary>
public static class GridQuery
{
    /// <summary>Bir kolon için (SQL takma adı, kullanıcının yazdığı filtre metni ya da null/boş).</summary>
    public readonly record struct ColumnFilter(string Alias, string? Value);

    /// <summary>WHERE + ORDER BY parçalarını üretir; parametreleri (ad, değer) listesi olarak döner —
    /// çağıran bunları hem COUNT hem SAYFA komutuna aynı şekilde ekler.</summary>
    public static (string WhereSql, string OrderBySql, List<(string Name, object Value)> Params) Build(
        IReadOnlyList<ColumnFilter> filters, string tieBreakerAlias)
    {
        var whereParts = new List<string>();
        var orderParts = new List<string>();
        var ps = new List<(string, object)>();
        int i = 0;
        foreach (var f in filters)
        {
            if (string.IsNullOrWhiteSpace(f.Value)) continue;
            var term = f.Value.Trim();
            var pContains = $"$gf{i}c";
            var pStarts = $"$gf{i}s";
            whereParts.Add($"{f.Alias} LIKE {pContains}");
            orderParts.Add($"CASE WHEN {f.Alias} LIKE {pStarts} THEN 0 ELSE 1 END");
            ps.Add((pContains, "%" + term + "%"));
            ps.Add((pStarts, term + "%"));
            i++;
        }
        var whereSql = whereParts.Count == 0 ? "" : "WHERE " + string.Join(" AND ", whereParts) + " ";
        orderParts.Add(tieBreakerAlias);
        var orderSql = "ORDER BY " + string.Join(", ", orderParts) + " ";
        return (whereSql, orderSql, ps);
    }

    public static void AddParams(SqliteCommand cmd, List<(string Name, object Value)> ps)
    {
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
    }
}
