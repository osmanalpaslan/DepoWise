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
    // G4-4b: CARİ filtresi (ön muhasebe raporları). Değerler `parties.id`. Seçenekler ÖNCEDEN
    // YÜKLENMEZ — Material (STK-10b-3) ile AYNI desen: yaz → sunucu tarafı arama → seç.
    // Binlerce cariyi rapor açılışında indirmek performans kuralına aykırı olurdu.
    Party = 8192,
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
public enum ReportCategory { Vehicle, Material, Fuel, Maintenance, Requests, Purchasing, Stock, Management, Accounting }

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
    string? InfoNote = null,  // GENEL AMAÇLI: rapor üstünde gösterilecek küçük bilgi/metodoloji notu (katalog-sürümlü; UI kodu değil)
    // ⭐ RPR-12 (denetim 2026-08-26) — RAPORUN DAYANDIĞI MODÜL İZNİ.
    //
    // Raporların çoğu "reports" izniyle çalışır. Ama bazıları BAŞKA bir ekranın verisini gösterir ve
    // servisleri zaten O ekranın iznini ister (ör. Cari Ekstre → parties, Fatura Özeti → invoices,
    // Personel Listesi → personnel). Bu bilgi bugüne kadar YALNIZ servis kodunda duruyordu; katalog
    // bilmediği için rapor listesi izni OLMAYAN kullanıcıya da gösteriliyor, kullanıcı "Sorgula"ya
    // basınca 403 alıyordu. Alan doldurulduğunda liste bu izne göre süzülür (deny-by-default ile
    // tutarlı: göremeyeceğin raporu listede de görme). null → yalnız "reports" yeterlidir.
    string? RequiredModule = null,
    // ⭐ RPR-15 (denetim 2026-08-26) — RAPORUN OKUDUĞU EKRAN ("veri evi").
    //
    // <b>RequiredModule'den FARKI:</b> RequiredModule "bu raporu görmek için o ekranın TAM iznini iste"
    // demektir. DataModule ise yalnız <b>"Rol Yetki Kontrol" ile o ekran role KAPATILMIŞSA</b> raporu da
    // kapatır. Aradaki fark bilinçlidir:
    //
    //  • <c>RoleGrantService</c> sözleşmesi: role kapatılan modül için "admin bypass'ı dahil API/UI
    //    erişimi kapanır". Rapor yolu bu güvenceyi deliyordu — kapalı ekranın verisi rapordan satır
    //    satır okunabiliyordu (hatta Excel'e aktarılabiliyordu).
    //  • Buna karşılık bu raporlarda TAM izin istemek, bugün yalnız "Raporlar" yetkisi verilmiş
    //    kullanıcıların erişimini KESERDİ. Bu yüzden kural dar tutulmuştur: yalnız AÇIKÇA KAPATILMIŞ
    //    modülün verisi engellenir; kapatma yoksa davranış hiç değişmez.
    //
    // null → bu raporun tek bir "veri evi" yoktur (ör. Durum Rapor firma geneli sayısal özettir).
    string? DataModule = null)
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
    public bool UsesParty => Filters.HasFlag(ReportFilters.Party);   // G4-4b: cari (arama ile seçilir)
    public bool IsManager => Group == ReportGroup.Manager;
}

/// <summary>Kayıtlı rapor kataloğu — 12 rapor. Filtre bayrakları MEVCUT davranışı yansıtır (bu faz hesaplama
/// değiştirmez): yalnız hâlihazırda şube-kapsamlı raporlar Branch; tarih kullanan raporlar Date işaretlidir.</summary>
public static class ReportCatalog
{
    public const string ExportStandard = "btn-export-reports";
    public const string ExportManager = "btn-export-mgr-reports";

    /// <summary>⭐ RPT-YETKI (2026-08-29, PK-R2=A) — kategori → yetki modülü eşlemesi, TEK MERKEZ.
    /// "reports" ÜST KAPI olarak kalır; bu eşlemenin döndürdüğü anahtar İKİNCİ kapıdır ve
    /// katalog süzmesi (API + masaüstü) ile ReportService.Run AYNI eşlemeyi kullanır — rapor tür
    /// adı değiştirilerek atlatılamaz. Purchasing kategorisinde bugün rapor YOK: ileride eklenirse
    /// bilinçli olarak burada patlar (sessizce yetkisiz kalması yerine) ve yeni anahtar açılır.</summary>
    public static string CategoryModule(ReportCategory c) => c switch
    {
        ReportCategory.Vehicle => "report_vehicle",
        ReportCategory.Stock => "report_stock",
        ReportCategory.Fuel => "report_fuel",
        ReportCategory.Maintenance => "report_maintenance",
        ReportCategory.Requests => "report_requests",
        ReportCategory.Management => "report_management",
        ReportCategory.Material => "report_material",
        ReportCategory.Accounting => "report_accounting",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c,
            "Bu rapor kategorisi için yetki modülü tanımlanmadı — AppModules + CategoryModule birlikte güncellenmeli."),
    };

    public static readonly IReadOnlyList<ReportDescriptor> All = new[]
    {
        // Araç Raporu — "Genel Rapor"un YERİNE geçti (kullanıcı isteği 2026-08-07): araç başına yakıt + bakım
        // malzemesi + doğrudan parça maliyeti, sayaç birimine (km/saat) duyarlı, tek-geçiş (N+1 yok). Karar
        // destek raporu. Filtreler: Tarih + Şube(yetkili) + Araç(çoklu) + Araç Türü.
        new ReportDescriptor("vehicle", "Araç Raporu", "Araç başına yakıt + bakım + parça maliyeti ve birim maliyet",
            ReportCategory.Vehicle, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType, true, ExportStandard,
            InfoNote: "Yakıt tüketimi ve mesafe, yakıt fişleri arasındaki sayaç farkına göre hesaplanır. Tutarlar seçili tarih aralığındaki maliyetleri kapsar.",
            DataModule: "vehicles"),
        // ⭐ RPT-GUNLUK (2026-08-29, PK-R1=A): Araç Raporu'nun GÜN BAZLI kırılımı — AYRI rapor türü
        // (mevcut "vehicle" toplam raporuna DOKUNULMADI). Aralıktaki HER GÜN gösterilir (boş gün = 0);
        // amaç afaki/hatalı günlük girişin görünürlüğü. Filtre/kapsam/tarih semantiği dönem raporuyla aynı.
        new ReportDescriptor("vehicle-daily", "Araç Raporu — Günlük", "Araç maliyetlerinin gün gün kırılımı (boş günler dahil)",
            ReportCategory.Vehicle, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType, true, ExportStandard,
            InfoNote: "Her satır bir GÜN×ARAÇ'tır; veri girilmeyen günler 0 (-) olarak görünür. Değerler dönem raporuyla aynı kaynaklardan hesaplanır ve günlerin toplamı dönem toplamına eşittir. Uzun tarih aralığında araç filtresi kullanmanız önerilir.",
            DataModule: "vehicles"),
        // STK-06: depo bazlı stoktan sonra bu raporun iki modu var — filtre BOŞken firma geneli toplam
        // (eski davranış birebir), depo seçilince o deponun kırılımı + "Depo" kolonu.
        new ReportDescriptor("stock", "Stok Durumu", "Mevcut / minimum / kritik kalemler",
            ReportCategory.Stock, ReportGroup.Standard, ReportFilters.Location, false, ExportStandard,
            InfoNote: "Depo seçilmezse TÜM depoların toplamı gösterilir («Atanmamış» stok dahil). Depo seçilirse yalnız o depodaki kalemler listelenir. «Atanmamış» bir depo değildir: geçmişte deposu girilmemiş stoktur.",
            DataModule: "stock"),
        // STK-10a (2026-08-11): hareket defterinin kataloglanmış dökümü. Daha önce yalnız bir EKRAN vardı
        // (katalogda rapor olmadığı için Excel'e aktarımı yoktu). Bu artımda YALNIZ Date + Location
        // filtreleri açıktı; STK-10b-1 ile HAREKET TÜRÜ, STK-10b-2 ile ARAMA, STK-10b-3 ile MALZEME eklendi.
        // RequiresDate: defter sürekli büyür, tarihsiz tam tarama yapılmaz (ağır rapor kuralı).
        new ReportDescriptor("stock-movements", "Stok Hareketleri", "Giriş/çıkış/transfer/sayım/bakım hareketleri — Kaynak → Hedef",
            ReportCategory.Stock, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Location | ReportFilters.MovementType | ReportFilters.Search | ReportFilters.Material, true, ExportStandard,
            InfoNote: "Her satır bir stok hareketidir. Transfer defterde İKİ satırdır (kaynaktan çıkış, hedefe giriş) ve öyle gösterilir. Depo filtresi, hareketin KAYNAĞI ya da HEDEFİ seçilen depo olan satırları getirir; şube kapsamınız dışındaki hareketler görünmez. «Atanmamış» bir depo değildir: lokasyonu girilmemiş harekettir.",
            DataModule: "stock"),
        new ReportDescriptor("stock-count", "Stok Sayım", "Sistem / sayılan / fark dökümü",
            ReportCategory.Stock, ReportGroup.Standard, ReportFilters.Date | ReportFilters.Location, true, ExportStandard,
            InfoNote: "Sayım tek bir depoya/şantiyeye aittir. «Sistem» sütunu firma toplamını değil, SAYILAN DEPONUN o andaki miktarını gösterir.",
            DataModule: "stock"),
        // Yakıt Tüketim — Araç Raporu standardına taşındı (kullanıcı isteği 2026-08-08): araç başına işlem/mesafe/
        // litre/ortalama tüketim/ağırlıklı ort. fiyat/toplam maliyet/birim maliyet; sayaç birimine (km/saat) duyarlı,
        // tek-geçiş (N+1 yok), tam filo. Filtreler: Tarih + Şube(yetkili) + Araç(çoklu) + Araç Türü.
        new ReportDescriptor("fuel", "Yakıt Tüketim", "Araç başına tüketim, ortalama fiyat ve birim maliyet (km/saat duyarlı)",
            ReportCategory.Fuel, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType, true, ExportStandard,
            InfoNote: "Yakıt tüketimi ve mesafe, seçilen tarih aralığındaki yakıt fişleri arasında oluşan sayaç farklarına göre hesaplanır (saat bazlı araçlarda km yerine Saat üzerinden). Tutarlar işlem para biriminde toplanır; farklı para birimleri kur ile dönüştürülmez.",
            DataModule: "fuel"),
        // Bakım Raporu — ortak standarda taşındı (kullanıcı isteği 2026-08-08): her bakım kaydı TEK satır (detay/işlem
        // listesi), işlenen şube (op_branch_id), km/saat duyarlı sayaç, malzeme maliyeti + kalem sayısı derived-table'dan
        // (correlated subquery YOK). Filtreler: Tarih + Şube(yetkili) + Araç(çoklu) + Araç Türü + Bakım Tanımı + Teknisyen.
        new ReportDescriptor("maintenance", "Bakım Raporu", "Bakım kayıtları: tanım/alt bakım, sayaç, teknisyen, malzeme maliyeti",
            ReportCategory.Maintenance, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle | ReportFilters.VehicleType
            | ReportFilters.MaintenanceDef | ReportFilters.Technician, true, ExportStandard,
            InfoNote: "Her satır bir bakım kaydıdır (iptal edilenler hariç). Şube, bakımın işlendiği şubedir. Sayaç, bakımın yapıldığı andaki değerdir (araç birimi km ya da saat). Maliyet yalnızca bakım malzemelerini kapsar; işçilik/servis dâhil değildir.",
            DataModule: "maintenance"),
        // Depo Girişi — ortak standarda taşındı (kullanıcı isteği 2026-08-08): depoya alınan yakıt alım kayıtları;
        // Şube (op_branch_id) + Tedarikçi + Litre/Birim Fiyat/Tutar (NumCell) + pinned toplam (litre+tutar+ağırlıklı
        // ort. fiyat). Filtreler: Tarih + Şube(yetkili) + Tedarikçi.
        // ⭐ RPR-V3 (kullanıcı bildirimi 2026-08-27) — AD DÜZELTİLDİ: "Depo Girişi" → "Yakıt Depo Girişi".
        // Bu rapor YALNIZ `fuel_depot_entries` okur, yani yakıt deposuna alınan yakıttır. Kullanıcı
        // MALZEME deposuna giriş yapıp bu rapora baktı ve boş buldu — ad yanıltıyordu. Uygulamanın
        // geri kalanı (Excel sayfa adı, İçe/Dışa Aktarım ekranı, Yakıt ekranı) zaten "Yakıt Depo Girişi"
        // diyordu; tutarsız olan yalnız katalogdu. Açıklama artık malzeme girişlerinin hangi raporda
        // olduğunu da SÖYLER, böylece aynı arayış tekrar boşa çıkmaz.
        new ReportDescriptor("fuel-depot", "Yakıt Depo Girişi", "Yakıt deposuna alınan yakıt: tedarikçi, litre, birim fiyat, tutar",
            ReportCategory.Fuel, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Supplier, true, ExportStandard,
            InfoNote: "Yakıt deposuna alınan yakıt giriş kayıtları. Şube, girişin işlendiği şubedir. Tutar = litre × birim fiyat. Tutarlar işlem para biriminde toplanır; farklı para birimleri kur ile dönüştürülmez. ⚠️ MALZEME deposuna yapılan giriş/çıkışlar bu raporda DEĞİL, «Stok Hareketleri» raporundadır.",
            DataModule: "fuel"),
        // Talep Raporu — ortak standarda taşındı (kullanıcı isteği 2026-08-08): her talep TEK satır (belge listesi);
        // şube/talep eden/onaylayan/açıklama gösterilir, kalem sayısı derived-table'dan (correlated subquery YOK).
        // Reddedilen/iptal talepler LİSTEDE KALIR (Durum filtresiyle daraltılır). Para/araç kolonu yoktur.
        new ReportDescriptor("requests", "Talep Raporu", "Malzeme talepleri: şube, talep eden, onaylayan, durum, kalem",
            ReportCategory.Requests, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Requester | ReportFilters.Status, true, ExportStandard,
            InfoNote: "Her satır bir malzeme talebidir. Kalem sayısı, talepteki malzeme satırı adedidir (miktar toplamı değildir). Reddedilen ve iptal edilen talepler de listelenir; Durum filtresiyle daraltabilirsiniz.",
            DataModule: "requests"),
        // ⭐ RPR-10 (denetim 2026-08-26) — MUAYENE / SİGORTA RAPORU.
        // Veri modeli ve iş kuralı ZATEN vardı (vehicle_inspections + InspectionService); eksik olan yalnız
        // raporu. Kolonlar mevcut "Muayene/Sigorta" ekranından BİREBİR alındı (ARAÇ · BELGE · SON · SONRAKİ ·
        // YER · DURUM); şube ve kalan gün rapora özgü eklemelerdir. Durum eşiği uydurulmadı — ekranla AYNI
        // sabit kullanılır (InspectionService.ApproachingDays = 30 gün).
        new ReportDescriptor("inspection", "Muayene / Sigorta", "Araç belgeleri: muayene, sigorta, kasko, kalibrasyon — son/sonraki tarih ve durum",
            ReportCategory.Vehicle, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Vehicle, false, ExportStandard,
            InfoNote: "Tarih aralığı SONRAKİ tarihe uygulanır («bu aralıkta süresi dolacak belgeler»). " +
                      "Durum: sonraki tarih geçmişse «Süresi geçti», 30 günden az kalmışsa «Yaklaşıyor», " +
                      "aksi halde «Normal» — Muayene/Sigorta ekranıyla aynı kural. İptal edilen belgeler listelenmez.",
            RequiredModule: "inspection"),
        // ⭐ RPR-11 (denetim 2026-08-26) — PERSONEL RAPORU. Kolonlar mevcut Personel ekranından alındı
        // (AD SOYAD · UNVAN · TELEFON · ERİŞİM · DURUM) + şube. "Erişim" rozeti de ekranla aynı kuraldır.
        new ReportDescriptor("personnel", "Personel Listesi", "Şube bazlı personel: unvan, telefon, uygulama erişimi ve durum",
            ReportCategory.Management, ReportGroup.Standard, ReportFilters.Branch, false, ExportStandard,
            InfoNote: "Erişim kolonu personelin uygulama hesabını gösterir: «Admin»/«Kullanıcı» (bağlı hesap), " +
                      "«Saha personeli» (hesabı yok ama saha personeli işaretli) veya «Kullanıcı yok». " +
                      "Silinen personel listelenmez.",
            RequiredModule: "personnel"),
        new ReportDescriptor("materials-template", "Malzeme — Şablonlu", "Şablona bağlı malzeme kayıtları",
            ReportCategory.Material, ReportGroup.Manager, ReportFilters.None, false, ExportManager,
            DataModule: "materials"),
        new ReportDescriptor("materials-nontemplate", "Malzeme — Şablon Dışı", "Şablonsuz girilen malzemeler (incele/düzelt)",
            ReportCategory.Material, ReportGroup.Manager, ReportFilters.None, false, ExportManager,
            DataModule: "materials"),
        new ReportDescriptor("vehicles-template", "Araç — Şablonlu", "Şablona bağlı araç kayıtları",
            ReportCategory.Vehicle, ReportGroup.Manager, ReportFilters.None, false, ExportManager,
            DataModule: "vehicles"),
        new ReportDescriptor("vehicles-nontemplate", "Araç — Şablon Dışı", "Şablonsuz girilen araçlar (incele/düzelt)",
            ReportCategory.Vehicle, ReportGroup.Manager, ReportFilters.None, false, ExportManager,
            DataModule: "vehicles"),
        new ReportDescriptor("status", "Durum Rapor", "Şube bazlı sayısal özet (modül başına kayıt)",
            ReportCategory.Management, ReportGroup.Manager, ReportFilters.Date, true, ExportManager),

        // ═══ G4-4 — ÖN MUHASEBE RAPORLARI (kullanıcı isteği 2026-08-12) ═══════════════════════
        // Hepsi ŞUBE KAPSAMLIDIR: ReportScope.BranchSql → BranchAccess (izinli ∩ istenen).
        // ⚠️ İKİNCİ FİNANSAL GERÇEKLİK YOK: raporlar mevcut defterlerden OKUR (party_ledger,
        //    invoices, finance_transactions, invoice_allocations). Özet/bakiye tablosu SAKLANMAZ.
        // ⚠️ "Firma toplamı" = kullanıcının ERİŞEBİLDİĞİ şubelerin toplamı; erişemediği şube GİRMEZ.

        new ReportDescriptor("acc-statement", "Cari Ekstre",
            "Seçili carinin hareket dökümü ve yürüyen bakiyesi (şube kapsamlı)",
            ReportCategory.Accounting, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Party, true, ExportStandard,
            InfoNote: "Yürüyen bakiye seçili ŞUBE KAPSAMINA göre hesaplanır: yalnız seçtiğiniz (ve yetkili olduğunuz) şubelerin hareketleri toplanır. İptal edilen hareketler listede görünür ama bakiyeye girmez. Cari kartı firma genelinde tekildir; ayrışma HAREKET düzeyindedir.",
            RequiredModule: "parties"),

        new ReportDescriptor("acc-balances", "Cari Bakiye Özeti",
            "Cari başına borç / alacak / bakiye (şube kapsamlı)",
            ReportCategory.Accounting, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Party, false, ExportStandard,
            InfoNote: "Bakiye = Borç − Alacak. Pozitif: cari size borçlu. Negatif: siz cariye borçlusunuz. Bakiye SAKLANMAZ, her seferinde hareketlerden hesaplanır. Şube seçilirse yalnız o şubelerin hareketleri toplanır.",
            RequiredModule: "parties"),

        new ReportDescriptor("acc-invoices", "Fatura Özeti",
            "Alış / satış faturaları, tutar ve kalan (şube kapsamlı)",
            ReportCategory.Accounting, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Party, true, ExportStandard,
            InfoNote: "Kalan tutar SAKLANMAZ: genel toplamdan iptal edilmemiş tahsilat/ödeme tahsisleri düşülerek hesaplanır. İptal edilmiş faturalar 'İptal' olarak görünür ve kalan hesabına girmez.",
            RequiredModule: "invoices"),

        new ReportDescriptor("acc-open-invoices", "Açık Faturalar / Vade",
            "Kapanmamış faturalar, kalan tutar ve vade durumu (şube kapsamlı)",
            ReportCategory.Accounting, ReportGroup.Standard,
            ReportFilters.Branch | ReportFilters.Party, false, ExportStandard,
            InfoNote: "Yalnız YÜRÜRLÜKTEKİ ve kalanı sıfırdan büyük faturalar listelenir. 'Gecikme' vadesi geçmiş gün sayısıdır; vadesiz faturada boştur.",
            RequiredModule: "invoices"),

        new ReportDescriptor("acc-payments", "Tahsilat / Ödeme Özeti",
            "Cari tahsilat ve ödemeleri, yöntem ve hesap kırılımı (şube kapsamlı)",
            ReportCategory.Accounting, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch | ReportFilters.Party, true, ExportStandard,
            InfoNote: "Yalnız cari etkileyen hareketler (tahsilat/ödeme) listelenir; iç transfer ve açılış hareketleri Kasa/Banka raporundadır. İptal edilen işlemler görünür ama toplama girmez.",
            RequiredModule: "finance"),

        new ReportDescriptor("acc-cash", "Kasa / Banka Özeti",
            "Hesap başına giriş / çıkış / bakiye (şube kapsamlı)",
            ReportCategory.Accounting, ReportGroup.Standard,
            ReportFilters.Date | ReportFilters.Branch, false, ExportStandard,
            InfoNote: "Bakiye = Σ giriş − Σ çıkış; SAKLANMAZ. Tarih aralığı verilirse giriş/çıkış o aralıktan, bakiye ise TÜM hareketlerden hesaplanır (dönem hareketi ile güncel bakiye ayrı okunur). İptal edilen hareketler hiçbirine girmez.",
            RequiredModule: "finance"),
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
        ReportCategory.Accounting => "Ön Muhasebe",
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
