using DepoWise.Application.Ui;

namespace DepoWise.Application.Reports;

/// <summary>
/// ═══ ARA İŞ 4 (Custom Rapor) — MERKEZÎ KAYNAK/KOLON BEYAZ LİSTESİ (ADR-186) ═══
///
/// <b>PK-CR-05=A:</b> kullanıcıdan gelen HİÇBİR metin SQL'e girmez. Kullanıcı yalnız buradaki
/// <see cref="CustomReportSource.Key"/> ve <see cref="ListColumn.Key"/> değerlerinden SEÇİM yapar;
/// tablo adı · kolon adı · SQL ifadesi · JOIN · ORDER BY parçası · aggregate parçası GÖNDEREMEZ.
/// Bu listede olmayan hiçbir kaynak/kolon çalıştırılmaz.
///
/// <b>PK-CR-09=A — v1 KAYNAKLARI YALNIZ ÜÇ TANEDİR</b> ve hepsi mevcut, güvenli, testli
/// <c>SearchGrid</c> yollarına bağlanır (yeni sorgu yüzeyi açılmaz):
/// Malzemeler · Araçlar · Günlük Faaliyet. Yakıt · Bakım · Stok Hareketleri · Faturalar v1 DIŞIDIR.
///
/// <b>PK-CR-10=A — TARİH ZORUNLULUĞU KAYNAK BAZLIDIR:</b>
///  • <b>Olay verisi</b> (Günlük Faaliyet — <c>activity_date</c> gerçek İŞ GÜNÜ alanı taşır):
///    tarih aralığı ZORUNLU ve SQL'e iner (<see cref="CustomReportSource.RequiresDate"/>).
///  • <b>Ana veri</b> (Malzeme, Araç — tabloda İŞ GÜNÜ alanı YOKTUR; yalnız <c>created_at</c>/
///    <c>updated_at</c> vardır): tarih filtresi YOKTUR. <c>created_at</c> iş günü yerine
///    KESİNLİKLE kullanılmaz (ADR-184'ün "iş günü ↔ kayıt anı" ayrımı korunur). Sınırsız sorguyu
///    engellemek için bunun yerine <see cref="CustomReportSource.RequiresFilter"/> = en az bir
///    beyaz-listeli filtre ZORUNLUDUR; satır tavanı her kaynakta SQL'e iner.
///
/// <b>Yetki:</b> her kaynak, raporun geçmek zorunda olduğu mevcut kapıların meta verisini taşır —
/// <see cref="DataModule"/> (RPR-15), <see cref="Category"/> (ADR-181 kategori izni),
/// <see cref="IsManager"/> (yönetici kapısı). Bunlar tanımdan DEĞİL kaynaktan gelir; kullanıcı
/// tanımı düzenleyerek güvenlik kapısını gevşetemez.
/// </summary>
public sealed record CustomReportSource(
    string Key,
    string Label,
    string DataModule,
    ReportCategory Category,
    bool IsManager,
    bool RequiresDate,
    bool RequiresFilter,
    IReadOnlyList<ListColumn> Columns)
{
    /// <summary>Kolon anahtarı beyaz listede mi?</summary>
    public bool HasColumn(string key) => Columns.Any(c => c.Key == key);

    /// <summary>Kolonun kullanıcıya görünen Türkçe başlığı (beyaz liste dışıysa null).</summary>
    public string? LabelOf(string key) => Columns.FirstOrDefault(c => c.Key == key)?.Label;

    /// <summary>Kolon sayısal mı (rapor tablosunda sağa hizalama/toplam için).</summary>
    public bool IsNumeric(string key) => Columns.FirstOrDefault(c => c.Key == key)?.IsNumeric ?? false;
}

/// <summary>v1 kaynak kayıt defteri — TEK doğru kaynak (masaüstü + web + API aynı listeden beslenir).</summary>
public static class CustomReportSources
{
    public const string Materials = "materials";
    public const string Vehicles = "vehicles";
    public const string DailyActivity = "daily_activity";

    public static readonly IReadOnlyList<CustomReportSource> All = new[]
    {
        // ANA VERİ — iş günü tarihi YOK → tarih filtresi yok, en az bir filtre ZORUNLU (PK-CR-10=A).
        new CustomReportSource(Materials, "Malzemeler", DataModule: "materials",
            ReportCategory.Material, IsManager: false, RequiresDate: false, RequiresFilter: true,
            MaterialListColumns.All),

        new CustomReportSource(Vehicles, "Araçlar", DataModule: "vehicles",
            ReportCategory.Vehicle, IsManager: false, RequiresDate: false, RequiresFilter: true,
            VehicleListColumns.All),

        // OLAY VERİSİ — activity_date gerçek iş günü alanıdır → tarih aralığı ZORUNLU (PK-CR-10=A).
        new CustomReportSource(DailyActivity, "Günlük Faaliyet", DataModule: "daily_activity",
            ReportCategory.DailyActivity, IsManager: false, RequiresDate: true, RequiresFilter: false,
            DailyActivityListColumns.All),
    };

    /// <summary>Bilinmeyen kaynak → null (çağıran REDDEDER; istisna ile kapı atlatılamaz).</summary>
    public static CustomReportSource? ByKey(string? key)
        => key is null ? null : All.FirstOrDefault(s => s.Key == key);
}
