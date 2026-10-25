using System;
using System.Text.RegularExpressions;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ M-62 (2/4) (AR-GE 2026-10-25, F-095) — "KAYDEDİLDİ" BİLDİRİMİ (toast) ═══
///
/// Başarı mesajı yalnız ekranın en altındaki küçük durum satırına yazılıyordu ("Kaydedildi mi?" belirsizliği).
/// 48 ekranın HER BİRİNİ değiştirmek yerine kabuk, açık ekranın <c>Status</c> değişimini dinler
/// (<c>ShellViewModel</c>) ve BAŞARI kalıbındaki mesajı buraya iletir; ana pencere sağ altta 4 sn'lik bildirim
/// gösterir. Hata mesajları bildirime çevrilmez — ekranda (durum satırı / hata paneli) kalıcı kalır.
/// Kalıp bilinçli olarak dar tutulur: "kaydedilemedi", "silinemedi", "Hata: …", "yetkiniz yok" gibi metinler eşleşmez.
/// </summary>
public static class Bildirimler
{
    /// <summary>Ana pencere abone olur; test ortamında abone yoktur → hiçbir şey olmaz.</summary>
    public static event Action<string>? BasariGoster;

    private static readonly Regex Basari = new(
        @"\b(kaydedildi|eklendi|güncellendi|silindi|iptal edildi|oluşturuldu|aktarıldı|onaylandı|reddedildi|gönderildi|teslim edildi|tamamlandı|geri alındı)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Olumsuz = new(
        @"^\s*hata\b|yetkiniz yok|başarısız|edilemedi|yapılamadı|bulunamadı|gönderilemedi",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Durum metni bir BAŞARI bildirimi mi?</summary>
    public static bool BasariMi(string? durum)
        => !string.IsNullOrWhiteSpace(durum) && !Olumsuz.IsMatch(durum) && Basari.IsMatch(durum);

    /// <summary>Ekranın durum metni değişti: başarıysa bildirim gösterilir.</summary>
    public static void Durum(string? durum)
    {
        if (BasariMi(durum)) BasariGoster?.Invoke(durum!.Trim());
    }
}
