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

/// <summary>Rapor KATEGORİSİ (kullanıcı isteği 2026-08-07): ileride çok sayıda rapor eklendiğinde temaya göre
/// gruplamak için (UI'da alt-başlık/klasör olarak kullanılabilir — mimari şimdiden hazır). Yeni kategori
/// eklemek için buraya değer + <see cref="ReportCatalog.CategoryLabel"/>'a etiket eklenir.</summary>
public enum ReportCategory { Vehicle, Material, Fuel, Maintenance, Requests, Purchasing, Stock, Management }

/// <summary>
/// TEK doğru kaynak rapor tanımı (kullanıcı isteği 2026-08-07 — ortak rapor mimarisi). Hem masaüstü hem web
/// hem API bu kataloğdan beslenir: yeni rapor eklemek = kataloğa 1 satır + ReportService metodu; filtre/kolon/
/// yetki UI'si otomatik gelir (madde 2/10 — geleceğe hazırlık). Hesaplama BU FAZDA değişmez.
/// </summary>
public sealed record ReportDescriptor(
    string Key,               // kanonik id: "general", "stock", ... (API tipi + katalog anahtarı)
    string Name,              // ekran adı: "Genel Rapor"
    string Description,       // KULLANICIYA-DÖNÜK kısa açıklama — rapor seçicide alt-başlık + ileride "rapor
                              // hakkında bilgi" ipucu/tooltip olarak gösterilir (teknik amaçlı DEĞİL; UI metni).
    ReportCategory Category,  // temaya göre gruplama (Araç/Malzeme/Yakıt/... — UI'da alt-başlık/klasör)
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
        new ReportDescriptor("general", "Genel Rapor", "Araç ve şantiye bazlı birleşik maliyet dökümü",
            ReportCategory.Management, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("stock", "Stok Durumu", "Mevcut / minimum / kritik kalemler",
            ReportCategory.Stock, ReportGroup.Standard, ReportFilters.None, false, ExportStandard),
        new ReportDescriptor("stock-count", "Stok Sayım", "Sistem / sayılan / fark dökümü",
            ReportCategory.Stock, ReportGroup.Standard, ReportFilters.Date, true, ExportStandard),
        new ReportDescriptor("fuel", "Yakıt Tüketim", "Araç bazlı tüketim ve ortalama (L/km)",
            ReportCategory.Fuel, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("maintenance", "Bakım Raporu", "Yapılan bakım kayıtları ve malzeme maliyeti",
            ReportCategory.Maintenance, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("fuel-depot", "Depo Girişi", "Depoya alınan yakıt hareketleri",
            ReportCategory.Fuel, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("requests", "Talep Raporu", "Malzeme taleplerinin durum dökümü",
            ReportCategory.Requests, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Branch, true, ExportStandard),
        new ReportDescriptor("materials-template", "Malzeme — Şablonlu", "Şablona bağlı malzeme kayıtları",
            ReportCategory.Material, ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("materials-nontemplate", "Malzeme — Şablon Dışı", "Şablonsuz girilen malzemeler (incele/düzelt)",
            ReportCategory.Material, ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("vehicles-template", "Araç — Şablonlu", "Şablona bağlı araç kayıtları",
            ReportCategory.Vehicle, ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("vehicles-nontemplate", "Araç — Şablon Dışı", "Şablonsuz girilen araçlar (incele/düzelt)",
            ReportCategory.Vehicle, ReportGroup.Manager, ReportFilters.None, false, ExportManager),
        new ReportDescriptor("status", "Durum Rapor", "Şube bazlı sayısal özet (modül başına kayıt)",
            ReportCategory.Management, ReportGroup.Manager, ReportFilters.Date, true, ExportManager),
    };

    public static ReportDescriptor? ByKey(string key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Kategori → kullanıcıya-dönük Türkçe etiket (UI'da alt-başlık). Yeni kategori eklenince buraya da eklenir.</summary>
    public static string CategoryLabel(ReportCategory c) => c switch
    {
        ReportCategory.Vehicle => "Araç Raporları",
        ReportCategory.Material => "Malzeme Raporları",
        ReportCategory.Fuel => "Yakıt Raporları",
        ReportCategory.Maintenance => "Bakım Raporları",
        ReportCategory.Requests => "Talep Raporları",
        ReportCategory.Purchasing => "Satın Alma",
        ReportCategory.Stock => "Stok",
        ReportCategory.Management => "Yönetim",
        _ => c.ToString(),
    };

    /// <summary>Varsayılan tarih aralığı = BU AY (ayın 1'i 00:00 → şimdi). RequiresDate raporlarında UI ön-dolu
    /// gelir; sunucu tarih gelmezse buna düşürür (kullanıcı isteği 2026-08-07: aylık ERP takibi).</summary>
    public static (long From, long To) CurrentMonthRange()
    {
        var now = System.DateTimeOffset.Now;
        var monthStart = new System.DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        return (monthStart.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());
    }
}
