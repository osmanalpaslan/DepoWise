using System;

namespace DepoWise.Application.Common;

/// <summary>
/// ═══ İŞ GÜNÜ / TAKVİM TARİHİ → UNIX ms — TEK DOĞRU KAYNAK ═══ (ARA İŞ 3 · ADR-184, 2026-08-29)
///
/// <b>Neden var.</b> Sistemde iki farklı zaman kavramı vardır ve KARIŞTIRILMAMALIDIR:
/// <list type="bullet">
///   <item><b>İş günü / takvim tarihi</b> — kullanıcının seçtiği GÜN (<c>doc_date</c>, <c>entry_date</c>,
///   <c>performed_date</c>, fatura/vade/işlem/talep/muayene tarihleri…). Saat bileşeni taşımaz; günün
///   kendisi bilgidir.</item>
///   <item><b>Gerçek zaman damgası</b> — <c>created_at</c>, <c>updated_at</c>, audit. Bir ANI gösterir
///   ve bu sınıfın konusu DEĞİLDİR (ADR-184 / PK-TAR-04: bunlara dokunulmaz).</item>
/// </list>
///
/// <b>🔴 Kapatılan hata sınıfı.</b> Takvim tarihi <c>new DateTimeOffset(gun)</c> gibi YEREL saat dilimi
/// uygulayan bir dönüşümle unix ms'e çevrildiğinde, TR (UTC+3) makinede <c>2 Ağustos 00:00</c> →
/// <c>1 Ağustos 21:00 UTC</c> olur ve kayıt tarih filtreli her raporda <b>BİR GÜN ERKEN</b> görünür.
/// ARA İŞ 3 analizinde bu hata masaüstünde 19, web'de 1 yazım noktasında kanıtlandı (ADR-184).
///
/// <b>Kural.</b> Gün bileşeni alınır, <c>Kind</c> nötrlenir ve <b>UTC 00:00</b> olarak yorumlanır →
/// sonuç makinenin saat diliminden BAĞIMSIZDIR. Bu, RPR-06'nın rapor gün sınırı kuralıyla ve web'deki
/// <c>DepoWise.Web.Services.FieldChecks.ToUnixMs</c> ile BİREBİR aynıdır (parite testle kilitli).
///
/// <b>Tek kaynak (PK-TAR-03=A).</b> Ekranlar bu dönüşümü KENDİ İÇİNDE yazmaz; hepsi buraya bağlanır.
/// <see cref="DepoWise.Application.Reports.ReportDateRange"/> de rapor gün sınırlarını buradan alır →
/// yazma ve okuma yolları aynı tanımı paylaşır. Web ayrı bir projedir ve iş katmanına derleme-zamanı
/// referans VERMEZ (bilinçli mimari sınır); orada aynı kuralın aynası <c>FieldChecks.ToUnixMs</c>'tir.
/// </summary>
public static class IsGunuTarihi
{
    /// <summary>Seçilen günün <b>başlangıcı</b> (UTC 00:00:00.000). <c>null</c> → <c>null</c>.</summary>
    public static long? Ms(DateTimeOffset? d) => d is null ? null : Cevir(d.Value.Date, sonu: false);

    /// <summary>Seçilen günün <b>başlangıcı</b> (UTC 00:00:00.000). <c>null</c> → <c>null</c>.</summary>
    public static long? Ms(DateTime? d) => d is null ? null : Cevir(d.Value.Date, sonu: false);

    /// <summary>Seçilen günün <b>sonu</b> (UTC 23:59:59.999) — kapsayıcı bitiş sınırı için.</summary>
    public static long? GunSonuMs(DateTimeOffset? d) => d is null ? null : Cevir(d.Value.Date, sonu: true);

    /// <summary>Seçilen günün <b>sonu</b> (UTC 23:59:59.999) — kapsayıcı bitiş sınırı için.</summary>
    public static long? GunSonuMs(DateTime? d) => d is null ? null : Cevir(d.Value.Date, sonu: true);

    private static long Cevir(DateTime gun, bool sonu)
    {
        var t = DateTime.SpecifyKind(gun, DateTimeKind.Unspecified);
        var an = sonu ? t.AddDays(1).AddMilliseconds(-1) : t;
        return new DateTimeOffset(an, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}
