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
