namespace DepoWise.Application.Reports;

/// <summary>Bir raporun KULLANDIĞI filtreler (bit bayrağı). UI yalnız işaretli filtreleri gösterir (madde 3);
/// sunucu da buna göre davranır (ör. RequiresDate → tarih zorunlu/varsayılan).</summary>
[System.Flags]
public enum ReportFilters
{
    None = 0,
    Date = 1,
    Branch = 2,
    Vehicle = 4,
}

/// <summary>Rapor grubu — menü/Excel-yetki ayrımı. Standart = "Raporlar", Yönetici = "Yönetici Raporları".</summary>
public enum ReportGroup { Standard, Manager }

/// <summary>
/// TEK doğru kaynak rapor tanımı (kullanıcı isteği 2026-08-07 — ortak rapor mimarisi). Hem masaüstü hem web
/// hem API bu kataloğdan beslenir: yeni rapor eklemek = kataloğa 1 satır + ReportService metodu; filtre/kolon/
/// yetki UI'si otomatik gelir (madde 2/10 — geleceğe hazırlık). Hesaplama BU FAZDA değişmez.
/// </summary>
public sealed record ReportDescriptor(
    string Key,               // kanonik id: "general", "stock", ... (API tipi + katalog anahtarı)
    string Name,              // ekran adı: "Genel Rapor"
    string Description,       // seçicide gösterilen kısa açıklama
    ReportGroup Group,
    ReportFilters Filters,    // bu raporun kullandığı filtreler
    bool RequiresDate,        // true → başlangıç/bitiş ZORUNLU + varsayılan (Bu Ay); milyonlarca kayıt taraması engellenir
    string ExportButton)      // Excel yetkisi: Rapor / Yönetici Rapor özel butonu
{
    public bool UsesDate => Filters.HasFlag(ReportFilters.Date);
    public bool UsesBranch => Filters.HasFlag(ReportFilters.Branch);
    public bool UsesVehicle => Filters.HasFlag(ReportFilters.Vehicle);
    public bool IsManager => Group == ReportGroup.Manager;
}

/// <summary>Kayıtlı rapor kataloğu — 12 rapor. Filtre bayrakları MEVCUT davranışı yansıtır (bu faz hesaplama
/// değiştirmez): yalnız hâlihazırda şube-kapsamlı raporlar Branch; tarih kullanan raporlar Date işaretlidir.</summary>
public static class ReportCatalog
{
    public const string ExportStandard = "btn-export-reports";
    public const string ExportManager = "btn-export-mgr-reports";

    public static readonly IReadOnlyList<ReportDescriptor> All = new[]
    {
        new ReportDescriptor("general", "Genel Rapor", "Araç ve şantiye bazlı genel döküm",
            ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("stock", "Stok Durumu", "Mevcut / minimum / kritik kalemler",
            ReportGroup.Standard, ReportFilters.None, false, ExportStandard),
        new ReportDescriptor("stock-count", "Stok Sayım", "Sistem / sayılan / fark dökümü",
            ReportGroup.Standard, ReportFilters.Date, true, ExportStandard),
        new ReportDescriptor("fuel", "Yakıt Tüketim", "Araç bazlı tüketim ve ortalama",
            ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("maintenance", "Bakım Raporu", "Yapılan bakım kayıtları",
            ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("fuel-depot", "Depo Girişi", "Depoya alınan yakıt hareketleri",
            ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("requests", "Talep Raporu", "Talep durumu dökümü",
            ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("materials-template", "Malzeme — Şablonlu", "Şablona bağlı malzeme kayıtları",
            ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("materials-nontemplate", "Malzeme — Şablon Dışı", "Şablonsuz girilen malzemeler",
            ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("vehicles-template", "Araç — Şablonlu", "Şablona bağlı araç kayıtları",
            ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("vehicles-nontemplate", "Araç — Şablon Dışı", "Şablonsuz girilen araçlar",
            ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("status", "Durum Rapor", "Şube bazlı sayısal özet",
            ReportGroup.Manager, ReportFilters.Date, true, ExportManager),
    };

    public static ReportDescriptor? ByKey(string key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Varsayılan tarih aralığı = BU AY (ayın 1'i 00:00 → şimdi). RequiresDate raporlarında UI ön-dolu
    /// gelir; sunucu tarih gelmezse buna düşürür (kullanıcı isteği 2026-08-07: aylık ERP takibi).</summary>
    public static (long From, long To) CurrentMonthRange()
    {
        var now = System.DateTimeOffset.Now;
        var monthStart = new System.DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        return (monthStart.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());
    }
}
