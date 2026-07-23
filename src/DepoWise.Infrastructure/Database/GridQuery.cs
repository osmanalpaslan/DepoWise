using System.Globalization;
using System.Text.RegularExpressions;
using System.Data.Common;

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
///
/// SAYISAL kolonlar (kullanıcı isteği 2026-07-18: "stokta sadece 5 olanları listelemek istiyorum ama
/// bütün içinde 5 olan malzemeler listeleniyor"): "içerir" araması "5" için 15/25/50/0.5'i de yakalıyordu.
/// <see cref="ColumnKind.Numeric"/> işaretli kolonlar önce tam-sayı/karşılaştırma/aralık söz dizimi
/// dener (bkz. <see cref="TryParseNumeric"/>); tanınmazsa (kullanıcı sayısal olmayan bir şey yazdıysa)
/// eski "içerir" davranışına DÜŞER — filtre kutusu asla sessizce hiçbir şey yapmaz.
/// </summary>
public static class GridQuery
{
    public enum ColumnKind { Text, Numeric }

    /// <summary>Bir kolon için: SQL takma adı (metin/"içerir" araması ve tanınmayan sayısal girişte kullanılır),
    /// kullanıcının yazdığı filtre metni, kolon türü, ve SAYISAL kolonlarda karşılaştırma için ham (CAST edilebilir)
    /// takma ad (verilmezse Alias kullanılır).</summary>
    public readonly record struct ColumnFilter(string Alias, string? Value, ColumnKind Kind = ColumnKind.Text, string? RawAlias = null);

    // "5" (tam sayı) · ">5" / "<5" / ">=5" / "<=5" (karşılaştırma) · "5-10" (aralık, negatif sınır destekli: "-9--5").
    private static readonly Regex CompareRe = new(@"^(>=|<=|>|<)\s*(-?\d+(?:[.,]\d+)?)$", RegexOptions.Compiled);
    private static readonly Regex RangeRe = new(@"^(-?\d+(?:[.,]\d+)?)\s*-\s*(-?\d+(?:[.,]\d+)?)$", RegexOptions.Compiled);
    private static readonly Regex ExactRe = new(@"^-?\d+(?:[.,]\d+)?$", RegexOptions.Compiled);

    private static double ParseNum(string s) => double.Parse(s.Replace(',', '.').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture);

    /// <summary>WHERE + ORDER BY parçalarını üretir; parametreleri (ad, değer) listesi olarak döner —
    /// çağıran bunları hem COUNT hem SAYFA komutuna aynı şekilde ekler.
    ///
    /// <paramref name="sort"/> verilirse (kullanıcı bir kolon başlığına tıkladı, madde 5) sıralama O kolona
    /// göredir: metin kolonu A→Z / Z→A, SAYISAL kolon küçük→büyük / büyük→küçük (ham değeri CAST ederek).
    /// Bu durumda filtrelerin "başlangıca göre" öncelik sıralaması UYGULANMAZ — kullanıcının açık seçimi kazanır.
    /// Sona daima <paramref name="tieBreakerAlias"/> eklenir (kararlı/benzersiz sıralama).</summary>
    public static (string WhereSql, string OrderBySql, List<(string Name, object Value)> Params) Build(
        IReadOnlyList<ColumnFilter> filters, string tieBreakerAlias, ColumnFilter? sort = null, bool sortDesc = false)
    {
        var whereParts = new List<string>();
        var orderParts = new List<string>();
        var ps = new List<(string, object)>();
        int i = 0;
        foreach (var f in filters)
        {
            if (string.IsNullOrWhiteSpace(f.Value)) continue;
            var term = f.Value.Trim();

            if (f.Kind == ColumnKind.Numeric)
            {
                var rawAlias = f.RawAlias ?? f.Alias;
                var mCompare = CompareRe.Match(term);
                if (mCompare.Success)
                {
                    var p = $"$gf{i}n";
                    whereParts.Add($"CAST({rawAlias} AS REAL) {mCompare.Groups[1].Value} {p}");
                    ps.Add((p, ParseNum(mCompare.Groups[2].Value)));
                    i++;
                    continue;   // karşılaştırmada "başlangıca göre" öncelik anlamsız → sıralamaya katkı yok
                }
                var mRange = RangeRe.Match(term);
                if (mRange.Success)
                {
                    var lo = ParseNum(mRange.Groups[1].Value);
                    var hi = ParseNum(mRange.Groups[2].Value);
                    if (lo > hi) (lo, hi) = (hi, lo);
                    var pLo = $"$gf{i}lo"; var pHi = $"$gf{i}hi";
                    whereParts.Add($"CAST({rawAlias} AS REAL) BETWEEN {pLo} AND {pHi}");
                    ps.Add((pLo, lo)); ps.Add((pHi, hi));
                    i++;
                    continue;
                }
                if (ExactRe.IsMatch(term))
                {
                    var p = $"$gf{i}n";
                    whereParts.Add($"CAST({rawAlias} AS REAL) = {p}");
                    ps.Add((p, ParseNum(term)));
                    i++;
                    continue;
                }
                // Tanınmayan sayısal söz dizimi → eski "içerir" davranışına düş (biçimlendirilmiş metin kolonunda).
            }

            var pContains = $"$gf{i}c";
            var pStarts = $"$gf{i}s";
            whereParts.Add($"{f.Alias} LIKE {pContains}");
            orderParts.Add($"CASE WHEN {f.Alias} LIKE {pStarts} THEN 0 ELSE 1 END");
            ps.Add((pContains, "%" + term + "%"));
            ps.Add((pStarts, term + "%"));
            i++;
        }
        var whereSql = whereParts.Count == 0 ? "" : "WHERE " + string.Join(" AND ", whereParts) + " ";

        // Açık sıralama (başlığa tıklama) filtrelerin "başlangıca göre" önceliğini EZER.
        if (sort is { } srt)
        {
            orderParts.Clear();
            var dir = sortDesc ? "DESC" : "ASC";
            orderParts.Add(srt.Kind == ColumnKind.Numeric
                ? $"CAST({srt.RawAlias ?? srt.Alias} AS REAL) {dir}"
                : $"{srt.Alias} COLLATE TRNOCASE {dir}");   // Türkçe harf-duyarsız (Ç, C'den sonra)
        }
        orderParts.Add(tieBreakerAlias);
        var orderSql = "ORDER BY " + string.Join(", ", orderParts) + " ";
        return (whereSql, orderSql, ps);
    }

    public static void AddParams(DbCommand cmd, List<(string Name, object Value)> ps)
    {
        foreach (var (name, value) in ps) cmd.AddWithValue(name, value);
    }
}
