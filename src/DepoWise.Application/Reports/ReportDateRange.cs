using System;

namespace DepoWise.Application.Reports;

/// <summary>
/// ⭐ RPR-06 (denetim 2026-08-25) — RAPOR TARİH ARALIĞI DÖNÜŞÜMÜ (tek doğru kaynak).
///
/// <b>Bulunan hata:</b> masaüstü Raporlar ekranı bitiş tarihini <c>DateTimeOffset.ToUnixTimeMilliseconds()</c>
/// ile HAM olarak gönderiyordu. Avalonia <c>DatePicker</c> seçilen günü <b>gece yarısı (00:00)</b> olarak
/// verir; SQL koşulu <c>tarih &lt;= @to</c> olduğu için <b>bitiş gününün TAMAMI rapordan düşüyordu</b>.
/// "01.08 – 25.08" raporunda 25.08'de girilen hiçbir kayıt görünmüyordu.
///
/// Aynı hata web'de 2026-08-13'te bulunup düzeltilmişti (<c>FieldChecks.ToUnixMs(endOfDay: true)</c>);
/// masaüstü atlanmıştı → <b>iki platform aynı filtreyle FARKLI sonuç üretiyordu</b>.
///
/// <b>İkinci incelik — SAAT DİLİMİ:</b> veriler UTC Unix ms olarak saklanır. Seçilen gün YEREL saat
/// diliminde yorumlanırsa (TR = UTC+3) sınır 3 saat kayar ve gün başı/sonu yanlış olur. Bu yüzden
/// gün bileşeni alınır, <c>Kind</c> nötrlenir ve <b>UTC</b> olarak yorumlanır — web ile birebir aynı kural.
///
/// Masaüstünün diğer ekranları (Sistem Logu · Stok Değişiklik Kaydı · Stok Hareketleri) bu deseni
/// zaten satır içinde uyguluyordu; yalnız Raporlar ekranı dışarıda kalmıştı.
/// </summary>
public static class ReportDateRange
{
    /// <summary>Başlangıç sınırı: seçilen günün UTC 00:00.000'ı. <c>null</c> → filtre yok.</summary>
    public static long? StartMs(DateTimeOffset? d) => ToMs(d, endOfDay: false);

    /// <summary>Bitiş sınırı: seçilen günün UTC 23:59:59.999'u. <c>null</c> → filtre yok.</summary>
    public static long? EndMs(DateTimeOffset? d) => ToMs(d, endOfDay: true);

    /// <summary>
    /// Ortak dönüşüm. <paramref name="endOfDay"/> ise günün SONU (23:59:59.999) döner.
    /// Web'deki <c>DepoWise.Web.Services.FieldChecks.ToUnixMs</c> ile AYNI kuraldır
    /// (web projesi referans veremediği için orada aynası tutulur; parite testle kilitlidir).
    /// </summary>
    /// <remarks>
    /// ⭐ ARA İŞ 3 / ADR-184 (PK-TAR-03=A): kuralın GÖVDESİ artık
    /// <see cref="DepoWise.Application.Common.IsGunuTarihi"/>'dedir — takvim tarihi → UTC gün sınırı
    /// dönüşümü tüm projede TEK kaynaktan gelir (rapor OKUMA sınırları + ekranların YAZMA yolları).
    /// Davranış birebir aynıdır; bu yalnız yönlendirmedir (mevcut RPR-06 testleri aynen kilitler).
    /// </remarks>
    public static long? ToMs(DateTimeOffset? d, bool endOfDay)
        => endOfDay ? Common.IsGunuTarihi.GunSonuMs(d) : Common.IsGunuTarihi.Ms(d);
}
