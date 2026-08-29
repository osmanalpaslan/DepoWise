namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ GÜNLÜK FAALİYET "KAYIT TİPİ" KATALOĞU ═══ (ADR-182 · ARA İŞ 2 / S4, 2026-08-29)
///
/// <b>Neden bu sınıf var.</b> Kayıt tipi veritabanında <b>İKİ</b> sütunla kodlanır
/// (<c>daily_activities.activity_type</c> + <c>movement_kind</c>) ve kullanıcıya dönük Türkçe etiket
/// bugün İKİ ayrı yerde tekrarlanıyordu (servisin satır modeli + liste ekranının SQL <c>CASE</c>'i).
/// Yeni rapor bunu ÜÇÜNCÜ kez kopyalasaydı etiketler kaçınılmaz olarak ıraksardı — <c>MovementTypeOptions</c>
/// (STK-B1) ile aynı gerekçe. Bu yüzden filtre seçenekleri ve etiketler TEK kaynaktan gelir.
///
/// <b>Anahtar = FİLTRE anahtarı</b> (ham sütun değeri değil): <c>movement</c> ve <c>transfer</c> aynı
/// <c>activity_type='movement'</c> satırlarının <c>movement_kind</c> ile ayrılmış iki hâlidir. SQL'e
/// çevirme işi Infrastructure'dadır (rapor sorgusu); burada yalnız SAF katalog durur — dosya web
/// projesinde de derlenir ve hiçbir bağımlılığı yoktur.
/// </summary>
public static class DailyActivityTypeOptions
{
    /// <summary>Bakım kaydı (<c>activity_type='maintenance'</c>).</summary>
    public const string Maintenance = "maintenance";
    /// <summary>İlave yağ (<c>activity_type='extra_oil'</c>).</summary>
    public const string ExtraOil = "extra_oil";
    /// <summary>İlave filtre (<c>activity_type='extra_filter'</c>).</summary>
    public const string ExtraFilter = "extra_filter";
    /// <summary>Tamir (<c>activity_type='repair'</c>).</summary>
    public const string Repair = "repair";
    /// <summary>Hareket (<c>activity_type='movement'</c> ve <c>movement_kind</c> transfer DEĞİL).</summary>
    public const string Movement = "movement";
    /// <summary>Transfer (<c>activity_type='movement'</c> ve <c>movement_kind='transfer'</c>).</summary>
    public const string Transfer = "transfer";

    /// <summary>Filtre seçenekleri — sıra kullanıcıya gösterilen sıradır. Yeni tip eklenirse
    /// buraya + rapor sorgusundaki eşlemeye BİRLİKTE eklenir (ikisi testle bağlıdır).</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Maintenance, "Bakım"),
        (ExtraOil, "İlave Yağ"),
        (ExtraFilter, "İlave Filtre"),
        (Repair, "Tamir"),
        (Movement, "Hareket"),
        (Transfer, "Transfer"),
    };

    /// <summary>Filtre anahtarı → Türkçe etiket. Bilinmeyen değer OLDUĞU GİBİ döner (sessiz kaybolma yok).</summary>
    public static string Label(string key)
    {
        foreach (var (k, l) in All) if (k == key) return l;
        return key;
    }
}
