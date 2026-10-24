using System.Collections.Generic;

namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ SAYAÇ BİRİMİ ETİKETLERİ — TEK DOĞRU KAYNAK (kullanıcı isteği 2026-09-03) ═══
///
/// Kullanıcı: "saat sayacı ortamlarda kayıt yaparken hour olarak geçiyor. uygulamada ingilizce terim
/// istemiyorum. doğru ad 'saat' olmalı."
///
/// ⚠️ VERİTABANI DEĞERİ DEĞİŞMEZ: kayıtlar "hour" koduyla durur (canlı veri + senkron + raporlar buna
/// bağlı). Bu sınıf yalnız EKRANDA gösterilen etiketi çevirir — DailyActivityTypeOptions ile aynı desen
/// (iki platform da bu dosyayı derler; ikinci bir eşleme kurulmaz).
/// </summary>
public static class MeterUnitOptions
{
    public const string Km = "km";
    public const string Hour = "hour";

    /// <summary>Seçim listeleri için (Key = DB kodu, Label = kullanıcıya görünen ad).</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Km, "km"),
        (Hour, "saat"),
    };

    /// <summary>DB kodu → Türkçe etiket. Bilinmeyen/boş değer OLDUĞU GİBİ döner (sessiz kaybolma yok).</summary>
    public static string Label(string? key) => key switch
    {
        Hour => "saat",
        null or "" => "km",
        _ => key,
    };
}

/// <summary>
/// ═══ KİRA BEDELİ BİRİMİ (kullanıcı isteği 2026-10-10 — Kiralık Araçlar) ═══
/// Kiralık iş makinesi saatlik, günlük ya da aylık kiralanır. DB'de kod (hour/day/month) durur; ekranda
/// Türkçe etiket görünür. Bu dosya web projesinde de derlenir (MeterUnitOptions ile aynı bağ) → iki platform
/// aynı listeyi kullanır.
/// </summary>
public static class RentalPriceUnits
{
    public const string Hour = "hour";
    public const string Day = "day";
    public const string Month = "month";

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Day, "Gün"),
        (Month, "Ay"),
        (Hour, "Saat"),
    };

    public static string Label(string? key) => key switch
    {
        Hour => "Saat",
        Month => "Ay",
        Day or null or "" => "Gün",
        _ => key,
    };

    /// <summary>Bilinmeyen/boş birim → "day" (varsayılan günlük kira).</summary>
    public static string Normalize(string? key) => key is Hour or Month ? key : Day;
}

/// <summary>
/// ═══ M-41 (AR-GE 2026-10-19, 05 F-116) — MUAYENE / BELGE TÜRLERİ (tek kaynak) ═══
/// Araç ve ekipman belgelerinin türleri ve Türkçe etiketleri. Eskiden beş ayrı yerde elle yazılıyordu; yeni tür
/// "periyodik kontrol" (İş Ekipmanları Yönetmeliği — vinç, forklift, kaldırma araçları) eklenirken tek listeye toplandı.
/// Bu dosya web projesinde de derlenir → iki platform aynı listeyi kullanır.
/// </summary>
/// <summary>⭐ M-34 (denetim B5+B6): toplu yakıt dağıtımının tek onay metni — masaüstü (FuelService) ve web aynı kaynaktan.</summary>
public static class TopluYakitMetni
{
    public static string Onay(int satir, decimal litre, decimal tutar, int uyari)
        => $"{satir} araca toplam {litre:0.##} L yakıt dağıtımı kaydedilsin mi? (Toplam {tutar:0.##} ₺)" +
           (uyari > 0 ? $"\n⚠ {uyari} satırda uyarı var (sayaç geri / şüpheli sıçrama / çok büyük litre) — kontrol ettiyseniz devam edin." : "") +
           "\nTek işlemde kaydedilir: biri bile hatalıysa hiçbiri yazılmaz.";
}

public static class InspectionDocTypes
{
    public const string Inspection = "inspection";
    public const string Insurance = "insurance";
    public const string Kasko = "kasko";
    public const string Calibration = "calibration";
    /// <summary>Yasal periyodik kontrol (yetkili kişi/firma + rapor no ile).</summary>
    public const string Periodic = "periodic";

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Inspection, "Muayene"),
        (Insurance, "Sigorta"),
        (Kasko, "Kasko"),
        (Calibration, "Kalibrasyon"),
        (Periodic, "Periyodik Kontrol"),
    };

    public static bool IsValid(string? key)
    {
        foreach (var (k, _) in All) if (k == key) return true;
        return false;
    }

    public static string Label(string? key)
    {
        foreach (var (k, l) in All) if (k == key) return l;
        return key ?? "";
    }

    /// <summary>Etiketten koda (masaüstü seçim kutusu, Excel içe aktarma). Bilinmeyen → muayene.</summary>
    public static string FromLabel(string? label)
    {
        var t = (label ?? "").Trim().ToLowerInvariant();
        foreach (var (k, l) in All) if (l.ToLowerInvariant() == t || k == t) return k;
        return t.StartsWith("periyodik") ? Periodic : Inspection;
    }
}
