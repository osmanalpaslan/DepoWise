using System.Text.Json;

namespace DepoWise.Infrastructure.Sync;

/// <summary>Yarıda kalan sayfalı çekimin kalıcı ilerlemesi: hangi imleçten (After), hangi ufukla (U), hangi sayfada.</summary>
public sealed record SayfaIlerlemesi(long After, long U, string Sayfa)
{
    public string Yaz() => $"{After}|{U}|{Sayfa}";

    public static SayfaIlerlemesi? Oku(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Split('|', 3);
        return p.Length == 3 && long.TryParse(p[0], out var a) && long.TryParse(p[1], out var u) && p[2].Length > 0
            ? new SayfaIlerlemesi(a, u, p[2]) : null;
    }
}

public enum SayfaliDurum { Tamam, Basarisiz, SunucuReddetti }

public sealed record SayfaliSonuc(SayfaliDurum Durum, long NextAfter, int Uygulanan, int Sayfa, int HttpDurum = 200);

/// <summary>
/// ═══ M-17 (AR-GE 2026-10-17) — SAYFALI, KALDIĞI YERDEN SÜREN ÇEKME DÖNGÜSÜ (ağdan bağımsız, test edilebilir) ═══
///
/// Sunucu (<c>business-pull-v2</c>) her yanıtta en fazla <c>limit</c> satır ve <c>nextPage</c> verir. Döngü: sayfayı çek →
/// uygula → ilerlemeyi KAYDET → sonraki sayfa. Bağlantı koparsa ilerleme kayıtlı kalır; bir sonraki çağrı AYNI imleç
/// (<c>after</c>) için kaldığı sayfadan, AYNI ufukla (<c>u</c>) devam eder. Her sayfa kendi işleminde uygulanır (bellek
/// tavanı sabit). İmleç (<c>NextAfter</c>) yalnız SON sayfa da uygulanınca döner — çağıran onu ancak o zaman saklar.
/// </summary>
public static class SayfaliCekim
{
    public const int SayfaBoyu = 2000;

    /// <param name="after">Çekme imleci (0 = tam).</param>
    /// <param name="devam">Kayıtlı yarım ilerleme (yoksa null). Yalnız <c>After</c> eşleşirse kullanılır.</param>
    /// <param name="getir">Sorgu dizesini (after=…&amp;limit=…[&amp;page=…&amp;u=…]) alır, (HTTP durumu, gövde) döner.</param>
    /// <param name="ilkSayfa">Yeni başlayan çekimin İLK yanıtı (after, nextAfter, epoch) — false dönerse çekim durur
    /// (ör. sunucu dönemi değişti; paket uygulanmaz).</param>
    /// <param name="uygula">Bir sayfayı yerele uygular; uygulanan satır sayısını döner. Hata fırlatırsa ilerleme o sayfada kalır.</param>
    /// <param name="ilerlemeYaz">İlerlemeyi kalıcı yazar (null = çekim bitti, ilerleme silinir).</param>
    public static async Task<SayfaliSonuc> CalistirAsync(long after, SayfaIlerlemesi? devam,
        Func<string, Task<(int Durum, string? Govde)>> getir,
        Func<long, long, string, bool> ilkSayfa,
        Func<JsonElement, int> uygula,
        Action<SayfaIlerlemesi?> ilerlemeYaz,
        int sayfaBoyu = SayfaBoyu)
    {
        string? sayfa = null; long? u = null;
        if (devam is not null && devam.After == after) { sayfa = devam.Sayfa; u = devam.U; }
        else if (devam is not null) ilerlemeYaz(null);   // başka imleç için kalmış ilerleme geçersiz

        int toplam = 0, sayfaNo = 0;
        while (true)
        {
            var q = $"after={after}&limit={sayfaBoyu}" + (sayfa is null ? "" : $"&page={Uri.EscapeDataString(sayfa)}&u={u}");
            var (durum, govde) = await getir(q);
            if (durum != 200 || govde is null) return new SayfaliSonuc(SayfaliDurum.Basarisiz, 0, toplam, sayfaNo, durum);
            sayfaNo++;

            using var doc = JsonDocument.Parse(govde);
            var kok = doc.RootElement;
            long next = kok.TryGetProperty("nextAfter", out var na) && na.TryGetInt64(out var n) ? n : 0;
            var epoch = kok.TryGetProperty("epoch", out var ep) && ep.ValueKind == JsonValueKind.String ? ep.GetString() ?? "" : "";
            var sonraki = kok.TryGetProperty("nextPage", out var np) && np.ValueKind == JsonValueKind.String ? np.GetString() : null;

            if (u is null)
            {
                if (!ilkSayfa(after, next, epoch)) return new SayfaliSonuc(SayfaliDurum.SunucuReddetti, 0, toplam, sayfaNo);
                u = next;
            }

            toplam += uygula(kok);

            if (string.IsNullOrEmpty(sonraki))
            {
                ilerlemeYaz(null);
                return new SayfaliSonuc(SayfaliDurum.Tamam, u.Value, toplam, sayfaNo);
            }
            ilerlemeYaz(new SayfaIlerlemesi(after, u.Value, sonraki));
            sayfa = sonraki;
        }
    }
}
