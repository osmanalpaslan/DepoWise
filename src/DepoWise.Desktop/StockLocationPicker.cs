using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop;

/// <summary>
/// BKM-04 / KARAR-9 (2026-08-11) — "MALZEMENİN ÇEKİLDİĞİ DEPO" seçeneklerinin TEK kaynağı (masaüstü).
///
/// Kural (iki ekranda da aynı olsun diye tek yerde):
///  • Seçenekler YALNIZ gerçek, aktif depo/şantiyeler — <b>"Atanmamış" yeni yazma hedefi olarak SUNULMAZ</b>
///    (KARAR-9 md. 7). Atanmamış bir depo değil, geçmişte lokasyonu girilmemiş stoğun kovasıdır.
///  • Varsayılan = kullanıcının aktif/oturum şubesi (KARAR-9 md. 1-3). Oturum şubesi listede yoksa
///    (ör. "Tüm Şubeler" ile giriş) varsayılan seçilmez — sistem rastgele bir depo TAHMİN ETMEZ.
///  • Liste YEREL veritabanından gelir → <b>çevrimdışı çalışır</b>, hiçbir API çağrısı yoktur.
///
/// ⚠️ Bu yalnız VARSAYILANI belirler. Kullanıcı başka bir depo seçerse o seçim korunur ve olduğu gibi
/// servise gider; hiçbir yerde sessizce oturum şubesine geri çevrilmez (KARAR-9 kırmızı çizgisi).
/// </summary>
public static class StockLocationPicker
{
    /// <summary>Seçilebilir depolar + oturum şubesine karşılık gelen varsayılan (yoksa null).
    /// Yerel veri okunamazsa boş liste döner — ekran bunu "depo yok" uyarısıyla gösterir.</summary>
    public static (List<BranchRow> Options, BranchRow? Default) Load(SessionContext session)
    {
        var options = new List<BranchRow>();
        try { options.AddRange(DesktopServices.Branches.List(session)); }
        catch { return (options, null); }
        return (options, DefaultFor(session, options));
    }

    /// <summary>Zaten yüklenmiş bir depo listesi için varsayılanı seçer — kural TEK yerde kalsın diye
    /// (ekranlar kendi listelerini yeniden sorgulamaz; N+1 yok).</summary>
    public static BranchRow? DefaultFor(SessionContext session, IEnumerable<BranchRow> options)
        => string.IsNullOrEmpty(session.OperatingBranchId)
            ? null
            : options.FirstOrDefault(b => b.Id == session.OperatingBranchId);
}
