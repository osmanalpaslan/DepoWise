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
    VehicleType = 8,
    MaintenanceDef = 16,   // Bakım Raporu: bakım tanımı (ana) çoklu filtre
    Technician = 32,       // Bakım Raporu: teknisyen (personel) çoklu filtre
    Supplier = 64,         // Depo Girişi: tedarikçi çoklu filtre
    Requester = 128,       // Talep Raporu: talep eden (personel) çoklu filtre
    Status = 256,          // Talep Raporu: durum çoklu filtre (sabit liste — DB tanımı değil)
    // STK-06: STOK LOKASYONU (depo/şantiye) çoklu filtre. ⚠️ Branch ile AYNI ŞEY DEĞİLDİR:
    // Branch = kaydı İŞLEYEN şube (op_branch_id) · Location = stoğun FİZİKSEL yeri (stock_balances.location_id).
    // İkisi bilinçli olarak ayrı bayraktır; birleştirilirse iki kavram karışır.
    Location = 512,
    // STK-10b-1: STOK HAREKET TÜRÜ (giriş/çıkış/transfer/sayım/bakım/iptal) çoklu filtre.
    // Seçenekler SABİT listedir ve TEK doğru kaynaktan gelir: DepoWise.Application.Ui.MovementTypeOptions
    // (STK-B1). Web bu dosyayı derliyor (paylaşılan dosya, bkz. DepoWise.Web.csproj) → seçenekler için
    // /api/reports/scope'a YENİ ALAN EKLENMEDİ; iki platform aynı sabitten besleniyor.
    MovementType = 1024,
    // STK-10b-2 (ADR-104 / KARAR-10): SERBEST METİN ARAMA. Diğer filtrelerden farklı olarak SKALER
    // (tek metin), liste değil. Semantiği mevcut Stok Hareketleri ekranından AYNEN taşınır:
    // malzeme kodu · malzeme adı · not · fatura no · belge no üzerinde OR araması.
    Search = 2048,
    // STK-10b-3: MALZEME filtresi (tek malzeme seçimi). Seçenekler ÖNCEDEN YÜKLENMEZ — mevcut
    // malzeme ARAMA deseni kullanılır (web: /api/materials?search=… + MudAutocomplete · masaüstü:
    // yerel MaterialService.List(search)). Bu yüzden /api/reports/scope'a malzeme listesi
    // EKLENMEDİ: 2461 malzemeyi rapor açılışında indirmek performans kuralına aykırı olurdu.
    Material = 4096,
}

/// <summary>Talep DURUMLARI — TEK doğru kaynak (kullanıcı isteği 2026-08-08). Filtre listesi (web scope + masaüstü
/// picker) ve rapor görüntü etiketi buradan gelir; iki platform aynı değerleri kullanır. DB değeri = <c>Key</c>.</summary>
public static class RequestStatusOptions
{
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        ("draft", "Taslak"),
        ("pending", "Beklemede"),
        ("approved", "Onaylı"),
        ("rejected", "Reddedildi"),
        ("cancelled", "İptal"),
    };

    /// <summary>DB durum değeri → kullanıcıya dönük Türkçe etiket. Bilinmeyen değer olduğu gibi döner.</summary>
    public static string Label(string key)
    {
        foreach (var (k, l) in All) if (k == key) return l;
        return key;
    }
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
    string ExportButton,      // Excel yetkisi: Rapor / Yönetici Rapor özel butonu
    string? InfoNote = null)  // GENEL AMAÇLI: rapor üstünde gösterilecek küçük bilgi/metodoloji notu (katalog-sürümlü; UI kodu değil)
{
    public bool UsesDate => Filters.HasFlag(ReportFilters.Date);
    public bool UsesBranch => Filters.HasFlag(ReportFilters.Branch);
    public bool UsesVehicle => Filters.HasFlag(ReportFilters.Vehicle);
    public bool UsesVehicleType => Filters.HasFlag(ReportFilters.VehicleType);
    public bool UsesMaintenanceDef => Filters.HasFlag(ReportFilters.MaintenanceDef);
    public bool UsesTechnician => Filters.HasFlag(ReportFilters.Technician);
    public bool UsesSupplier => Filters.HasFlag(ReportFilters.Supplier);
    public bool UsesRequester => Filters.HasFlag(ReportFilters.Requester);
    public bool UsesStatus => Filters.HasFlag(ReportFilters.Status);
    public bool UsesLocation => Filters.HasFlag(ReportFilters.Location);   // STK-06: stok deposu/şantiyesi
    public bool UsesMovementType => Filters.HasFlag(ReportFilters.MovementType);   // STK-10b-1: hareket türü
    public bool UsesSearch => Filters.HasFlag(ReportFilters.Search);   // STK-10b-2: serbest metin arama
    public bool UsesMaterial => Filters.HasFlag(ReportFilters.Material);   // STK-10b-3: malzeme (arama ile seçilir)
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
        // Araç Raporu — "Genel Rapor"un YERİNE geçti (kullanıcı isteği 2026-08-07): araç başına yakıt + bakım
        // malzemesi + doğrudan parça maliyeti, sayaç birimine (km/saat) duyarlı, tek-geçiş (N+1 yok). Karar
        // destek raporu. Filtreler: Tarih + Şube(yetkili) + Araç(çoklu) + Araç Türü.
        new ReportDescriptor("vehicle", "Araç Raporu", "Araç başına yakıt + bakım + parça maliyeti ve birim maliyet",
            ReportCategory.Vehicle, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType, true, ExportStandard,
            InfoNote: "Yakıt tüketimi ve mesafe, yakıt fişleri arasındaki sayaç farkına göre hesaplanır. Tutarlar seçili tarih aralığındaki maliyetleri kapsar."),
        // STK-06: depo bazlı stoktan sonra bu raporun iki modu var — filtre BOŞken firma geneli toplam
        // (eski davranış birebir), depo seçilince o deponun kırılımı + "Depo" kolonu.
        new ReportDescriptor("stock", "Stok Durumu", "Mevcut / minimum / kritik kalemler",
            ReportCategory.Stock, ReportGroup.Standard, ReportFilters.Location, false, ExportStandard,
            InfoNote: "Depo seçilmezse TÜM depoların toplamı gösterilir (0022Atanmamış0022 stok dahil). Depo seçilirse yalnız o depodaki kalemler listelenir. 0022Atanmamış0022 bir depo değildir: geçmişte deposu girilmemiş stoktur."),
        // STK-10a (2026-08-11): hareket defterinin kataloglanmış dökümü. Daha önce yalnız bir EKRAN vardı
        // (katalogda rapor olmadığı için Excel'e aktarımı yoktu). Bu artımda YALNIZ Date + Location
        // filtreleri açıktı; STK-10b-1 ile HAREKET TÜRÜ, STK-10b-2 ile ARAMA, STK-10b-3 ile MALZEME eklendi.
        // RequiresDate: defter sürekli büyür, tarihsiz tam tarama yapılmaz (ağır rapor kuralı).
        new ReportDescriptor("stock-movements", "Stok Hareketleri", "Giriş/çıkış/transfer/sayım/bakım hareketleri — Kaynak → Hedef",
            ReportCategory.Stock, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Location | ReportFilters.MovementType | ReportFilters.Search | ReportFilters.Material, true, ExportStandard,
            InfoNote: "Her satır bir stok hareketidir. Transfer defterde İKİ satırdır (kaynaktan çıkış, hedefe giriş) ve öyle gösterilir. Depo filtresi, hareketin KAYNAĞI ya da HEDEFİ seçilen depo olan satırları getirir; şube kapsamınız dışındaki hareketler görünmez. 0022Atanmamış0022 bir depo değildir: lokasyonu girilmemiş harekettir."),
        new ReportDescriptor("stock-count", "Stok Sayım", "Sistem / sayılan / fark dökümü",
            ReportCategory.Stock, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Location, true, ExportStandard,
            InfoNote: "Sayım tek bir depoya/şantiyeye aittir. 0022Sistem0022 sütunu firma toplamını değil, SAYILAN DEPONUN o andaki miktarını gösterir."),
        // Yakıt Tüketim — Araç Raporu standardına taşındı (kullanıcı isteği 2026-08-08): araç başına işlem/mesafe/
        // litre/ortalama tüketim/ağırlıklı ort. fiyat/toplam maliyet/birim maliyet; sayaç birimine (km/saat) duyarlı,
        // tek-geçiş (N+1 yok), tam filo. Filtreler: Tarih + Şube(yetkili) + Araç(çoklu) + Araç Türü.
        new ReportDescriptor("fuel", "Yakıt Tüketim", "Araç başına tüketim, ortalama fiyat ve birim maliyet (km/saat duyarlı)",
            ReportCategory.Fuel, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType, true, ExportStandard,
            InfoNote: "Yakıt tüketimi ve mesafe, seçilen tarih aralığındaki yakıt fişleri arasında oluşan sayaç farklarına göre hesaplanır (saat bazlı araçlarda km yerine Saat üzerinden). Tutarlar işlem para biriminde toplanır; farklı para birimleri kur ile dönüştürülmez."),
        // Bakım Raporu — ortak standarda taşındı (kullanıcı isteği 2026-08-08): her bakım kaydı TEK satır (detay/işlem
        // listesi), işlenen şube (op_branch_id), km/saat duyarlı sayaç, malzeme maliyeti + kalem sayısı derived-table'dan
        // (correlated subquery YOK). Filtreler: Tarih + Şube(yetkili) + Araç(çoklu) + Araç Türü + Bakım Tanımı + Teknisyen.
        new ReportDescriptor("maintenance", "Bakım Raporu", "Bakım kayıtları: tanım/alt bakım, sayaç, teknisyen, malzeme maliyeti",
            ReportCategory.Maintenance, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType
            | ReportFilters.MaintenanceDef | ReportFilters.Technician, true, ExportStandard,
            InfoNote: "Her satır bir bakım kaydıdır (iptal edilenler hariç). Şube, bakımın işlendiği şubedir. Sayaç, bakımın yapıldığı andaki değerdir (araç birimi km ya da saat). Maliyet yalnızca bakım malzemelerini kapsar; işçilik/servis dâhil değildir."),
        // Depo Girişi — ortak standarda taşındı (kullanıcı isteği 2026-08-08): depoya alınan yakıt alım kayıtları;
        // Şube (op_branch_id) + Tedarikçi + Litre/Birim Fiyat/Tutar (NumCell) + pinned toplam (litre+tutar+ağırlıklı
        // ort. fiyat). Filtreler: Tarih + Şube(yetkili) + Tedarikçi.
        new ReportDescriptor("fuel-depot", "Depo Girişi", "Depoya alınan yakıt: tedarikçi, litre, birim fiyat, tutar",
            ReportCategory.Fuel, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Supplier, true, ExportStandard,
            InfoNote: "Depoya alınan yakıt giriş kayıtları. Şube, girişin işlendiği şubedir. Tutar = litre × birim fiyat. Tutarlar işlem para biriminde toplanır; farklı para birimleri kur ile dönüştürülmez."),
        // Talep Raporu — ortak standarda taşındı (kullanıcı isteği 2026-08-08): her talep TEK satır (belge listesi);
        // şube/talep eden/onaylayan/açıklama gösterilir, kalem sayısı derived-table'dan (correlated subquery YOK).
        // Reddedilen/iptal talepler LİSTEDE KALIR (Durum filtresiyle daraltılır). Para/araç kolonu yoktur.
        new ReportDescriptor("requests", "Talep Raporu", "Malzeme talepleri: şube, talep eden, onaylayan, durum, kalem",
            ReportCategory.Requests, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Requester | ReportFilters.Status, true, ExportStandard,
            InfoNote: "Her satır bir malzeme talebidir. Kalem sayısı, talepteki malzeme satırı adedidir (miktar toplamı değildir). Reddedilen ve iptal edilen talepler de listelenir; Durum filtresiyle daraltabilirsiniz."),
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
