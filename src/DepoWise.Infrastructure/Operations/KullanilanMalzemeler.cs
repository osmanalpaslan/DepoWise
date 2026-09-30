using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using System.Data.Common;
using System.Globalization;

namespace DepoWise.Infrastructure.Operations;

/// <summary>
/// Günlük Faaliyet / bakım kayıtlarında KULLANILAN MALZEMELERİN AYRINTILI metni (kullanıcı isteği 2026-09-30):
/// "toplam kalemi değil, kullanılan malzemeleri ayrı ayrı görebilmeliyim — x malzeme 1 adet, xx malzeme 5 adet".
///
/// Tek kaynak: Günlük Faaliyet listesi (masaüstü + web + Excel) ve iki Günlük Faaliyet raporu AYNI metni
/// buradan üretir. Kaynak tablo <c>maintenance_materials</c> (bakımda düşülen malzeme; miktar metin/decimal).
/// Aynı malzeme bir kayıtta birden çok satırdaysa miktarlar TOPLANIR. Toplama C#'ta decimal ile yapılır
/// (SQL SUM SQLite'ta float'a düşerdi — Money kuralı). Biçim: <c>Ad (Kod) Miktar Birim</c>, " · " ile ayrılır.
/// </summary>
public static class KullanilanMalzemeler
{
    private static readonly CultureInfo Tr = new("tr-TR");
    public const string Ayrac = " · ";

    public sealed record Kalem(string Code, string Name, string? Unit, decimal Quantity)
    {
        public string Text => $"{Name} ({Code}) {Quantity.ToString("0.##", Tr)} {Unit}".TrimEnd();
    }

    /// <summary>Kalemleri (aynı malzemeyi toplayarak, ada göre sıralı) tek metne çevirir. Boşsa null.</summary>
    public static string? Metin(IEnumerable<Kalem> kalemler)
    {
        var liste = kalemler
            .GroupBy(k => (k.Code, k.Name, k.Unit))
            .Select(g => new Kalem(g.Key.Code, g.Key.Name, g.Key.Unit, g.Sum(x => x.Quantity)))
            .OrderBy(k => k.Name, StringComparer.Create(Tr, ignoreCase: true))
            .Select(k => k.Text)
            .ToList();
        return liste.Count == 0 ? null : string.Join(Ayrac, liste);
    }

    /// <summary>Verilen bakım kayıtlarının malzeme metinleri (bakım id → metin). Malzemesi olmayan kayıt sözlükte yer almaz.
    /// Kimlikler SQL'e PARAMETRE olarak bağlanır (metne gömülmez); büyük listeler 500'lük parçalarla sorgulanır.</summary>
    public static Dictionary<string, string> BakimlarIcin(DbConnection conn, string companyId, IEnumerable<string?> maintenanceIds)
    {
        var ids = maintenanceIds.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        var kalemler = new Dictionary<string, List<Kalem>>();
        foreach (var parca in ids.Chunk(500))
        {
            using var cmd = conn.CreateCommand();
            var adlar = new List<string>(parca.Length);
            for (int i = 0; i < parca.Length; i++) { adlar.Add("@km" + i); cmd.AddWithValue("@km" + i, parca[i]); }
            cmd.CommandText = $@"SELECT mm.maintenance_id, m.code, m.name, u.name, mm.quantity
FROM maintenance_materials mm
JOIN materials m ON m.id = mm.material_id AND m.company_id = @c
LEFT JOIN units u ON u.id = m.unit_id AND u.company_id = m.company_id
WHERE mm.maintenance_id IN ({string.Join(",", adlar)});";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                if (!kalemler.TryGetValue(id, out var l)) kalemler[id] = l = new List<Kalem>();
                l.Add(new Kalem(r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), Money.Parse(r.GetString(4))));
            }
        }
        return kalemler.ToDictionary(x => x.Key, x => Metin(x.Value)!);
    }
}
