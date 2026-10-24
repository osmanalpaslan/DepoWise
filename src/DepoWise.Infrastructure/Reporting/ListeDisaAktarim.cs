using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// ═══ M-54 (AR-GE 2026-10-24, 05 F-057) — LİSTE STANDARDI: "Excel'e Aktar" ve "Yazdır" FİLTRELENMİŞ TÜM SONUCU verir ═══
/// Sayfalı liste servisleri (fatura, cari, finans …) ekranın süzgeçleriyle sayfa sayfa okunur; ekrandaki sayfa değil tüm küme
/// dışa aktarılır (liste kuralı 2). Servisin kendi sayfa sınırı (ör. 500) <see cref="GridResult{T}.PageSize"/>'tan okunur — kendi
/// tahminimizle erken durulmaz. Aşırı büyük kümede <see cref="UstSinir"/> satırda kesilir (sunucu belleği; ekran da bunu söyler).
/// Yetki ve firma/şube süzgeci servisin içinde kalır — bu yardımcı yalnız sayfaları birleştirir.
/// </summary>
public static class ListeDisaAktarim
{
    public const int SayfaBoyutu = 500;
    public const int UstSinir = 50_000;

    public static IReadOnlyList<T> TumSayfalar<T>(Func<int, int, GridResult<T>> sayfa)
    {
        var tum = new List<T>();
        for (var p = 1; p <= UstSinir; p++)
        {
            var r = sayfa(p, SayfaBoyutu);
            tum.AddRange(r.Items);
            if (r.Items.Count == 0 || r.Items.Count < r.PageSize || tum.Count >= r.TotalCount || tum.Count >= UstSinir) break;
        }
        return tum.Count > UstSinir ? tum.GetRange(0, UstSinir) : tum;
    }
}
