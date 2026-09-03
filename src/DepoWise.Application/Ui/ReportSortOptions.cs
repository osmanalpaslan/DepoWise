using System.Collections.Generic;

namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ RAPOR SIRALAMA SEÇENEKLERİ — TEK DOĞRU KAYNAK (kullanıcı isteği 2026-09-02) ═══
///
/// Kullanıcı: "günlük rapor sıralamasını değiştirebileceğim bir alan veya ekran olmalı."
///
/// <b>Güvenlik:</b> kullanıcıdan gelen metin ASLA <c>ORDER BY</c>'a yazılmaz. Arayüz yalnız buradaki
/// ANAHTARLARI gönderir; rapor servisi anahtarı kendi BEYAZ LİSTESİNDEN sabit bir SQL parçasına
/// çevirir. Bilinmeyen anahtar sessizce VARSAYILANA düşer (fail-safe; enjeksiyon yüzeyi yok).
///
/// <b>Desen:</b> <see cref="DailyActivityTypeOptions"/> ile aynı — sabit liste, iki platform da bu
/// dosyayı derler, <c>/api/reports/scope</c>'a yeni alan eklenmez.
/// </summary>
public static class ReportSortOptions
{
    // ── Günlük Faaliyet — Detay (gün gün döküm) ──────────────────────────────────────────────────
    public const string DateDesc = "date_desc";
    public const string DateAsc = "date_asc";
    public const string Vehicle = "vehicle";
    public const string Type = "type";
    public const string CostDesc = "cost_desc";

    // ── Günlük Faaliyet — Dönem (araç bazında toplam) ────────────────────────────────────────────
    public const string CountDesc = "count_desc";
    public const string DaysDesc = "days_desc";

    /// <summary>Detay raporunun sıralama seçenekleri. İlk öğe VARSAYILANDIR.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> DailyActivityDetail = new[]
    {
        (DateDesc, "Tarih (yeniden eskiye)"),
        (DateAsc, "Tarih (eskiden yeniye)"),
        (Vehicle, "Araç kodu"),
        (Type, "Kayıt tipi"),
        (CostDesc, "Parça maliyeti (çoktan aza)"),
    };

    /// <summary>Dönem (toplam) raporunun sıralama seçenekleri. İlk öğe VARSAYILANDIR.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> DailyActivitySummary = new[]
    {
        (Vehicle, "Araç kodu"),
        (CountDesc, "Kayıt sayısı (çoktan aza)"),
        (CostDesc, "Parça maliyeti (çoktan aza)"),
        (DaysDesc, "Toplam süre (çoktan aza)"),
    };

    /// <summary>Anahtar → Türkçe etiket. Bilinmeyen değer OLDUĞU GİBİ döner (sessiz kaybolma yok).</summary>
    public static string Label(IReadOnlyList<(string Key, string Label)> options, string? key)
    {
        if (key is null) return options.Count > 0 ? options[0].Label : "";
        foreach (var (k, l) in options) if (k == key) return l;
        return key;
    }

    /// <summary>Anahtar listede var mı? Rapor servisi bunu geçmeyen anahtarı VARSAYILANA düşürür.</summary>
    public static bool IsValid(IReadOnlyList<(string Key, string Label)> options, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        foreach (var (k, _) in options) if (k == key) return true;
        return false;
    }
}
