namespace DepoWise.Application.Security;

/// <summary>
/// G5 hazırlığı — bir ekranın hangi PLATFORMLARDA var olduğu. Bayrak birleşimi kullanılır
/// (<c>Desktop | Web</c>). ⚠️ Bu, YETKİ DEĞİLDİR: "bu ekran bu platformda var mı" sorusunu yanıtlar.
/// Erişim kuralı ikisinin BİRLEŞİMİDİR: <c>ERİŞİM = PLATFORM_AKTİF &amp;&amp; YETKİ_VAR</c>.
/// </summary>
[Flags]
public enum ScreenPlatform
{
    None = 0,
    Desktop = 1,
    Web = 2,
    Both = Desktop | Web,
}

/// <summary>Menü grubu (iki platformda AYNI başlık ve sıra). Masaüstü ikon olarak emoji kullanır;
/// web ikonu MudBlazor sabitidir ve yalnız web projesinde çözülür (Application katmanı MudBlazor'a bağımlı değildir).</summary>
public sealed record AppScreenGroup(string Title, string DesktopIcon, string ModuleKey, string? Section = null);

/// <summary>
/// SEC — menünün ÜÇÜNCÜ seviyesi: <b>ÜST GRUP</b>. Ekran taşımaz; altında üst menüler bulunur.
/// Anahtarı <c>section:</c> önekiyle başlar (firma bazlı üst gruplarla AYNI anahtar biçimi — böylece
/// katalog varsayılanı ile firma tercihi tek kod yolunda buluşur).
/// </summary>
public sealed record AppScreenSection(string Key, string Title);

/// <summary>
/// TEK EKRAN TANIMI (G2/G6). Menüler, gezinme ve platform görünürlüğü BUNDAN türetilir.
/// </summary>
/// <param name="Key">Benzersiz ekran anahtarı (ör. <c>materials.new</c>). Kod içinde referans verilir.</param>
/// <param name="ModuleKey">Yetki modülü — <see cref="AppModules.All"/> içindeki anahtar.</param>
/// <param name="Group">Menü grubu başlığı (<see cref="AppScreens.Groups"/>).</param>
/// <param name="Label">Menüde görünen etiket.</param>
/// <param name="Platforms">Ekranın var olduğu platformlar (G5 bunu yönetilebilir hâle getirecek).</param>
/// <param name="WebRoute">Web adresi (başında '/' YOK). <see cref="ScreenPlatform.Web"/> varsa zorunlu.</param>
/// <param name="DesktopNavKey">Masaüstü gezinme anahtarı. <see cref="ScreenPlatform.Desktop"/> varsa zorunlu.</param>
/// <param name="WebPermOverride">
/// GEÇİŞ DÖNEMİ: web menüsündeki sözde-yetki anahtarları (<c>@admin</c> / <c>@super</c> / <c>@superr</c>)
/// ya da "" (herkese açık). <see cref="AppModules"/> kataloğunda karşılığı olmayan bu kurallar bilinçli
/// olarak KORUNUR — bugünkü davranış birebir aynı kalsın diye. Boşsa <paramref name="ModuleKey"/> kullanılır.
/// </param>
public sealed record AppScreen(
    string Key,
    string ModuleKey,
    string Group,
    string Label,
    ScreenPlatform Platforms,
    string? WebRoute = null,
    string? DesktopNavKey = null,
    string? WebPermOverride = null)
{
    public bool OnDesktop => Platforms.HasFlag(ScreenPlatform.Desktop);
    public bool OnWeb => Platforms.HasFlag(ScreenPlatform.Web);

    /// <summary>Web menüsünün kullanacağı yetki anahtarı (sözde anahtar varsa o).</summary>
    public string WebPermKey => WebPermOverride ?? ModuleKey;
}

/// <summary>
/// ═══ EKRAN KATALOĞU — TEK DOĞRU KAYNAK (G2/G6, 2026-08-12) ═══
///
/// <b>NEDEN VAR:</b> önceden bir ekran eklemek <b>5 ayrı yerde</b> elle iş gerektiriyordu
/// (modül kataloğu · masaüstü menüsü · masaüstü <c>Navigate</c> · web menüsü · web route). Biri
/// unutulunca ekran ya menüde çıkmıyor, ya yetki ağacında görünmüyor, ya tek platformda kalıyordu.
/// Web menüsü, masaüstü menüsünün ELLE tutulan aynasıydı ve sayılar zaten ayrışmıştı.
///
/// <b>ARTIK:</b> yeni ekran = buraya <b>TEK SATIR</b>. Menüler (<c>ShellViewModel.BuildGroups</c> ve
/// <c>NavMenu.razor</c>) bu listeden ÜRETİLİR; <c>AppScreensParityTests</c> her katmanın gerçekten
/// beslendiğini ve hiçbir ekranın yetim kalmadığını doğrular.
///
/// <b>REFLECTION KULLANILMAZ (bilinçli):</b> route'ları tarayarak otomatik keşif, yeni bir ekranın
/// sessizce yetkisiz açılmasına yol açabilir ve deny-by-default'u zayıflatır. Bunun yerine
/// <b>açık bildirim + derleme/test zamanı zorlama</b> tercih edilmiştir.
///
/// <b>SIRA ÖNEMLİDİR:</b> menüler bu listedeki sırayla üretilir — mevcut kullanıcı alışkanlığı bozulmasın
/// diye taşıma sırasında iki menünün sırası BİREBİR korunmuştur.
///
/// <b>PLATFORM FARKLARI GERÇEKTİR:</b> bazı ekranlar bugün yalnız bir platformda var (ör. Kota İzleme
/// yalnız web, Atanmamış Stok Dağıtımı menüde yalnız masaüstü). Bunlar <see cref="ScreenPlatform"/>
/// ile KAYIT ALTINA alınmıştır; G5 bu alanı çalışma zamanında yönetilebilir yapacaktır.
/// </summary>
public static class AppScreens
{
    /// <summary>
    /// SEC — ÜST GRUPLAR (menünün en üst seviyesi). Kullanıcının 2026-08-19'da ilettiği NİHAİ ŞEMA
    /// artık projenin VARSAYILAN menüsüdür: hiçbir firma kaydı olmadan da menü bu düzende çıkar.
    /// Sıra buradaki sıradır; bir üst grup, ilk üyesinin bulunduğu yerde açılır.
    /// </summary>
    public static readonly IReadOnlyList<AppScreenSection> Sections = new[]
    {
        new AppScreenSection("section:malzemestok", "Malzeme ve Stok"),
        new AppScreenSection("section:operasyon",   "Operasyon"),
        new AppScreenSection("section:finans",      "Finans"),
        new AppScreenSection("section:raporlar",    "Raporlar"),
        new AppScreenSection("section:kurumsal",    "Kurumsal Yönetim"),
        new AppScreenSection("section:sistem",      "Sistem Yönetimi"),
    };

    /// <summary>Menü grupları — iki platformda AYNI başlık ve sıra. Dördüncü alan bağlı olduğu ÜST GRUP.</summary>
    public static readonly IReadOnlyList<AppScreenGroup> Groups = new[]
    {
        new AppScreenGroup("Uyarılar",            "🔔", "alerts"),
        new AppScreenGroup("Malzemeler",          "📦", "materials",       "section:malzemestok"),
        new AppScreenGroup("Araçlar",             "🚚", "vehicles",        "section:operasyon"),
        new AppScreenGroup("Ekipman",             "⚙️", "equipment",       "section:operasyon"),   // EKP-01 (ADR-166)
        new AppScreenGroup("Zimmet",              "🧰", "assignments",     "section:operasyon"),   // ZMT-01 (ADR-167)
        new AppScreenGroup("Satın Alma",          "🛒", "purchasing",      "section:operasyon"),   // STN-01 (ADR-169)
        new AppScreenGroup("Günlük Faaliyet",     "📋", "daily_activity",  "section:operasyon"),
        new AppScreenGroup("Bakım Takibi",        "🔧", "maintenance",     "section:operasyon"),
        new AppScreenGroup("Yakıt",               "⛽", "fuel",            "section:operasyon"),
        new AppScreenGroup("Talepler",            "📄", "requests"),
        new AppScreenGroup("Ön Muhasebe",         "🧾", "parties",         "section:finans"),
        new AppScreenGroup("Operasyon Raporları", "📊", "reports",         "section:raporlar"),
        new AppScreenGroup("Yönetici Raporları",  "📈", "reports",         "section:raporlar"),
        new AppScreenGroup("Şube ve Personel",    "🏗️", "branches",        "section:kurumsal"),
        new AppScreenGroup("Kullanıcı Yönetimi",  "👥", "users",           "section:kurumsal"),
        new AppScreenGroup("Evrak",               "📁", "files",           "section:kurumsal"),   // EVR-01 (ADR-165)
        new AppScreenGroup("Denetim",             "🔍", "audit",           "section:kurumsal"),
        new AppScreenGroup("Web Yönetimi",        "🛡️", "companies",       "section:sistem"),
        new AppScreenGroup("Yedekleme",           "💾", "backup",          "section:sistem"),
        new AppScreenGroup("Çöp Kutusu",          "🗑️", "trash",           "section:sistem"),
        new AppScreenGroup("Ayarlar",             "🛠️", "settings"),
    };

    private const ScreenPlatform Both = ScreenPlatform.Both;
    private const ScreenPlatform D = ScreenPlatform.Desktop;
    private const ScreenPlatform W = ScreenPlatform.Web;

    /// <summary>
    /// TÜM EKRANLAR. Sıra = menü sırası. Yeni ekran buraya eklenir; başka hiçbir yere kayıt gerekmez
    /// (masaüstünde ayrıca <c>ShellViewModel.Navigate</c> içine ekranı AÇAN <c>case</c> yazılır —
    /// bu bir VERİ değil KOD eşlemesidir ve parity testi eksikse uyarır).
    /// </summary>
    public static readonly IReadOnlyList<AppScreen> All = new[]
    {
        // ── Uyarılar (en üst seviye, üst grubu yok) ──────────────────────────────────────────
        new AppScreen("alerts", "alerts", "Uyarılar", "Uyarılar", Both, "alerts", "alerts", WebPermOverride: ""),

        // ═══ MALZEME VE STOK ════════════════════════════════════════════════════════════════
        // ── Malzemeler ──────────────────────────────────────────────────────────────────────
        new AppScreen("materials.list", "materials", "Malzemeler", "Malzeme Listesi", Both, "materials", "materials"),
        new AppScreen("materials.new", "materials", "Malzemeler", "Yeni Kayıt", Both, "materials/new", "materials:new"),
        new AppScreen("stock.entry", "stock", "Malzemeler", "Giriş-Çıkış", Both, "stock", "stock"),
        new AppScreen("stock.movements", "stock", "Malzemeler", "Stok Hareketleri", Both, "stock/movements", "stock:movements"),
        new AppScreen("stock.count", "stock", "Malzemeler", "Stok Sayım", Both, "stock/count", "stock:count"),
        // Malzeme Şablonları menüde YALNIZ masaüstünde (web'de ekran var ama menüde listelenmiyordu).
        new AppScreen("material_templates", "material_templates", "Malzemeler", "Malzeme Şablonları", D, null, "material_templates:templates"),
        // STK-08 — web'de Stok İşlemleri ekranından açılır, menüde listelenmez.
        new AppScreen("stock.distribute", "stock", "Malzemeler", "Atanmamış Stok Dağıtımı", D, null, "stock:distribute"),

        // ═══ OPERASYON ══════════════════════════════════════════════════════════════════════
        // ── Araçlar ─────────────────────────────────────────────────────────────────────────
        new AppScreen("vehicles.list", "vehicles", "Araçlar", "Araç Listesi", Both, "vehicles", "vehicles"),
        new AppScreen("vehicles.new", "vehicles", "Araçlar", "Yeni Araç Ekle", Both, "vehicles/new", "vehicles:new"),
        new AppScreen("vehicle_templates", "vehicle_templates", "Araçlar", "Şablonlar", Both, "vehicle-templates", "vehicle_templates:templates"),
        new AppScreen("inspection", "inspection", "Araçlar", "Muayene / Sigorta", Both, "inspection", "inspection"),

        // ── Günlük Faaliyet ─────────────────────────────────────────────────────────────────
        new AppScreen("daily_activity", "daily_activity", "Günlük Faaliyet", "Günlük Faaliyet Girişi", Both, "daily", "daily_activity"),

        // ── Bakım Takibi ────────────────────────────────────────────────────────────────────
        new AppScreen("maintenance.defs", "maintenance", "Bakım Takibi", "Bakım Tanımları Girişi", Both, "maintenance/defs", "maintenance:defs"),
        new AppScreen("maintenance.records", "maintenance", "Bakım Takibi", "Araç Bakımları Girişi", Both, "maintenance/records", "maintenance:records"),

        // ── Yakıt ───────────────────────────────────────────────────────────────────────────
        new AppScreen("fuel.dist", "fuel", "Yakıt", "Yakıt Dağıtımları", Both, "fuel/dist", "fuel:dist"),
        new AppScreen("fuel.depot", "fuel", "Yakıt", "Depo Girişleri", Both, "fuel/depot", "fuel:depot"),
        new AppScreen("fuel.summary", "fuel", "Yakıt", "Özet", Both, "fuel/summary", "fuel:summary"),

        // ── Talepler (en üst seviye, üst grubu yok — kullanıcı şeması 2026-08-19) ────────────
        new AppScreen("requests.form", "requests", "Talepler", "Talep Formu", Both, "requests", "requests:form"),
        new AppScreen("requests.approve", "request_approval", "Talepler", "Talep Onaylama", Both, "requests/approve", "requests:approve", WebPermOverride: "requests"),
        new AppScreen("request_ops", "request_ops", "Talepler", "Talep Operasyonları", Both, "request-operations", "request_ops:board"),

        // ═══ FİNANS ═════════════════════════════════════════════════════════════════════════
        // ── Ön Muhasebe (G4-1 cari · G4-2 fatura · G4-3 kasa/banka) ─────────────────────────
        // Cari KARTI ve fatura DETAYI ayrı menü girişi DEĞİLDİR: listeden açılır.
        // "invoices" modülü cariden AYRIDIR — fatura kesme yetkisi ayrı verilebilir.
        new AppScreen("accounting.parties", "parties", "Ön Muhasebe", "Cari Listesi", Both, "parties", "parties"),
        new AppScreen("accounting.parties.new", "parties", "Ön Muhasebe", "Yeni Cari", Both, "parties/new", "parties:new"),
        new AppScreen("accounting.invoices", "invoices", "Ön Muhasebe", "Fatura Listesi", Both, "invoices", "invoices"),
        new AppScreen("accounting.invoices.new", "invoices", "Ön Muhasebe", "Yeni Fatura", Both, "invoices/new", "invoices:new"),
        // Kasa ve banka AYRI ekran DEĞİL: aynı defter, aynı ekran, tür filtresiyle ayrılır.
        new AppScreen("accounting.finance", "finance", "Ön Muhasebe", "Kasa / Banka", Both, "finance", "finance"),
        new AppScreen("accounting.finance.new", "finance", "Ön Muhasebe", "Yeni Hesap", Both, "finance/new", "finance:new"),
        new AppScreen("accounting.payments", "finance", "Ön Muhasebe", "Tahsilat / Ödeme", Both, "payments", "payments"),

        // ═══ RAPORLAR ═══════════════════════════════════════════════════════════════════════
        // ⭐ RPR-07 (2026-08-25) — İKİ RAPOR EKRANI ARTIK GERÇEKTEN AYRI.
        //
        // Önceden iki menü girişi AYNI route ve AYNI gezinme anahtarını kullanıyordu → "Operasyon
        // Raporları" ile "Yönetici Raporları" birebir aynı ekranı açıyordu; ayrım yalnız web menüsünde
        // görünürlük kapısıydı. Artık ayrım İŞLEVSELDİR ve ŞUBE KAPSAMINDADIR:
        //
        //   • Operasyon Raporları → ÇALIŞMA ŞUBESİ (girişte seçilen şube). Şube seçici YOK; sunucu
        //     kapsamı ayrıca doğrular. Depo personelinin ekranı budur.
        //   • Yönetici Raporları  → mevcut davranış: izinli şubeler + (yetkisi varsa) şube seçici.
        //
        // ⚠️ Ekran ANAHTARLARI (reports / reports.manager) DEĞİŞMEDİ → firmaların kayıtlı menü düzeni
        // ve platform görünürlük satırları aynen çalışır. Rapor LİSTESİ iki ekranda da aynıdır;
        // kimseden rapor erişimi ALINMADI.
        new AppScreen("reports", "reports", "Operasyon Raporları", "Raporlar", Both, "reports", "reports"),
        new AppScreen("reports.manager", "reports", "Yönetici Raporları", "Raporlar", Both, "reports/manager", "reports:manager", WebPermOverride: "@admin"),

        // ═══ KURUMSAL YÖNETİM ═══════════════════════════════════════════════════════════════
        // ── Şube ve Personel ────────────────────────────────────────────────────────────────
        new AppScreen("branches", "branches", "Şube ve Personel", "Şube / Şantiye", Both, "branches", "branches"),
        // PRJ-01 (ADR-164): Projeler — yetki modülü branches (PK-C4: ayrı kapı yok).
        new AppScreen("projects", "branches", "Şube ve Personel", "Projeler", Both, "projects", "projects"),
        // STN-01 (ADR-169): Satın Alma — sipariş + mal kabul (tek ekran).
        new AppScreen("purchasing", "purchasing", "Satın Alma", "Satın Alma", Both, "purchasing", "purchasing"),
        // MLY-01 (ADR-168): Maliyet Merkezleri — Ön Muhasebe altında alt menü (tanım + özet tek ekran).
        new AppScreen("cost_centers", "cost_centers", "Ön Muhasebe", "Maliyet Merkezleri", Both, "cost-centers", "cost_centers"),
        // ZMT-01 (ADR-167): Zimmet — kimde ne var + hareket defteri (tek ekran).
        new AppScreen("assignments", "assignments", "Zimmet", "Zimmet", Both, "assignments", "assignments"),
        // EKP-01 (ADR-166): Ekipman — araçtan ayrı varlık kartları.
        new AppScreen("equipment", "equipment", "Ekipman", "Ekipman Listesi", Both, "equipment", "equipment"),
        // EVR-01 (ADR-165): merkezi Evrak/Belge ekranı — yetki modülü files ("Dosya / Fotoğraf").
        new AppScreen("documents", "files", "Evrak", "Evrak / Belgeler", Both, "documents", "documents"),
        new AppScreen("personnel", "personnel", "Şube ve Personel", "Personel Girişi", Both, "personnel", "personnel"),

        // ── Kullanıcı Yönetimi ──────────────────────────────────────────────────────────────
        new AppScreen("users", "users", "Kullanıcı Yönetimi", "Kullanıcı Tanım", Both, "users", "users"),
        new AppScreen("permissions", "permissions", "Kullanıcı Yönetimi", "Yetkiler", Both, "permissions", "permissions"),
        new AppScreen("permission_templates", "permission_templates", "Kullanıcı Yönetimi", "Yetki Şablonları", Both, "permission-templates", "permission_templates"),

        // ── Denetim ─────────────────────────────────────────────────────────────────────────
        new AppScreen("audit", "audit", "Denetim", "Sistem Logu", Both, "audit", "audit"),
        new AppScreen("stock_change_log", "stock_change_log", "Denetim", "Stok Değişiklik Kaydı", Both, "stock-change-log", "stock_change_log"),

        // ═══ SİSTEM YÖNETİMİ ════════════════════════════════════════════════════════════════
        // ── Web Yönetimi (süper admin) ──────────────────────────────────────────────────────
        new AppScreen("companies", "companies", "Web Yönetimi", "Firma Tanım", Both, "companies", "companies"),
        new AppScreen("releases", "releases", "Web Yönetimi", "Güncelleme Yönetimi", Both, "releases", "releases"),
        new AppScreen("machines", "machines", "Web Yönetimi", "Makine Yönetimi", Both, "machines", "machines"),
        new AppScreen("machine_backups", "machine_backups", "Web Yönetimi", "Makine Yedekleri", W, "machine-backups", null),
        new AppScreen("server_backups", "server_backups", "Web Yönetimi", "Sunucu Yedekleri", Both, "server-backups", "server_backups"),
        new AppScreen("server_status", "server_status", "Web Yönetimi", "Canlı Sunucu", W, "server-status", null),
        new AppScreen("quota_monitor", "quota_monitor", "Web Yönetimi", "Kota İzleme", W, "quota-monitor", null),
        // A2 (ADR-116, 2026-08-19): "Firma Yetki Kontrol" + "Rol Yetki Kontrol" TEK EKRANDA birleşti.
        // İkisi de aynı soruyu soruyordu ("bu ekran kime verilebilir?"); biri firma bazlı, diğeri tüm
        // firmalar için ortaktı. Artık ikisi de firma bazlı ve tek ekranda iki sekme.
        // ⚠️ "role_permissions" MODÜL anahtarı KATALOGDA KALIR (AppModules) — yalnız EKRAN kalktı.
        new AppScreen("company_permissions", "companies", "Web Yönetimi", "Firma Yetki Paketi", W, "company-permissions", null),
        new AppScreen("purge_company", "purge_company", "Web Yönetimi", "Kalıcı Silme", W, "purge-company", null),
        new AppScreen("reset_company_business", "purge_company", "Web Yönetimi", "Firma İş Verisini Sıfırla", W, "reset-company-business", null, WebPermOverride: "@super"),
        // YET (2026-08-18, kullanıcı isteği): makinelerin YEREL verisini sıfırlama isteği. Eskiden
        // Firmalar ekranının içinde gömülü bir düğmeydi → devredilemiyordu. Artık kendi ekranı + kendi
        // modülü var; "açık-verilir" (AppModules.IsExplicitOnly) olduğu için admin bypass ile kimseye
        // örtük açılmaz. WebPermOverride YOK: yetki normal modül kapısından geçer.
        new AppScreen("local_reset", "local_reset", "Web Yönetimi", "Yerel Veri Sıfırlama", W, "local-reset", null),
        // G5 (2026-08-12): ekranların hangi platformda açık olacağını yönetir. Süper admin ekranıdır;
        // diğer süper admin ekranları gibi (Rol Yetki Kontrol, Kota İzleme…) YALNIZ WEB'de sunulur.
        new AppScreen("screen_visibility", "screen_visibility", "Web Yönetimi", "Menü / Ekran Yönetimi", W, "screen-visibility", null),

        // ── Yedekleme ───────────────────────────────────────────────────────────────────────
        // Yedek Yönetimi masaüstünden 2026-07-26'da KALDIRILDI; web'de süper + kısıtlı süper admin.
        new AppScreen("backup", "backup", "Yedekleme", "Yedek Yönetimi", W, "backup", null, WebPermOverride: "@superr"),

        // ── Çöp Kutusu ──────────────────────────────────────────────────────────────────────
        // G2-B1 DÜZELTMESİ (2026-08-12): "trash" artık AppModules kataloğunda da var → yetki ağacından
        // yönetilebilir. Eskiden katalog dışıydı; masaüstünde yalnız admin bypass'ı, web'de "@admin"
        // sözde-anahtarı sayesinde çalışıyordu ve HİÇ KİMSEYE devredilemiyordu.
        new AppScreen("trash", "trash", "Çöp Kutusu", "Çöp Kutusu Listesi", Both, "trash", "trash"),

        // ── Ayarlar (en üst seviye, üst grubu yok) ──────────────────────────────────────────
        // "Excel'e Aktarım": web'de `import`, masaüstünde `import_export` ekranıdır. İkisi de TEK
        // platformda bulunduğu için her platformda menüde YALNIZ BİR giriş görünür.
        new AppScreen("definitions", "definitions", "Ayarlar", "Tanım Düzenle", Both, "definitions", "definitions"),
        new AppScreen("import", "import_export", "Ayarlar", "Excel'e Aktarım", W, "import", null),
        new AppScreen("import_export", "import_export", "Ayarlar", "Excel'e Aktarım", D, null, "import_export"),
        // ⭐ SEC-03 (2026-08-25): geliştirici modu süper admin yetkilerini taklit eder → menüde de
        // yalnız süper admine görünür. Modülü "settings" olarak KALIR (yeni modül açmak yetki ağacını
        // ve rol tavanı matrisini değiştirirdi); ekran düzeyindeki kapı sözde-anahtarla verilir.
        new AppScreen("settings.developer", "settings", "Ayarlar", "Geliştirici Modu", Both, "developer", "settings:developer", WebPermOverride: "@super"),
        new AppScreen("theme", "theme", "Ayarlar", "Tema", Both, "theme", "theme", WebPermOverride: ""),
        new AppScreen("about", "about", "Ayarlar", "Hakkında", Both, "soon/about", "about", WebPermOverride: ""),
    };

    /// <summary>
    /// ═══ KORUMALI EKRANLAR (MNU-B2, 2026-08-18) ═══
    ///
    /// Bu ekranlar <b>HER PLATFORMDA BİRDEN</b> kapatılamaz. Liste keyfî değildir; her biri
    /// kapatıldığında geri dönüşü olmayan bir kilitlenme üretir:
    /// <list type="bullet">
    ///   <item><c>screen_visibility</c> — platform kararlarını geri alabilen <b>TEK</b> ekran.
    ///   Yalnız web'de var (<c>W</c>); web'de kapatılırsa menüden düşer <b>ve</b>
    ///   <c>MainLayout</c> route korumasına takılır → adresi elle yazarak da açılamaz. Masaüstü
    ///   karşılığı olmadığı için kurtarma yolu kalmaz (veritabanına elle müdahale gerekir).</item>
    ///   <item><c>users</c> / <c>permissions</c> — iki platformda da kapatılırsa firmada bir daha
    ///   kullanıcı açılamaz ve yetki verilemez.</item>
    /// </list>
    ///
    /// Kural DAR tutulmuştur: tek bir platformda kapatmak SERBESTTİR (diğer platform kurtarma yolu
    /// olarak kalır). Yalnız "hepsi kapalı" hâli engellenir.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Protected = new HashSet<string>(StringComparer.Ordinal)
    {
        "screen_visibility",
        "users",
        "permissions",
    };

    /// <summary>Bu ekran tümüyle kapatılmaya karşı korumalı mı?</summary>
    public static bool IsProtected(string screenKey) => Protected.Contains(screenKey);

    /// <summary>Belirtilen platformdaki ekranlar (menü üretimi bunu kullanır).</summary>
    public static IEnumerable<AppScreen> For(ScreenPlatform platform)
        => All.Where(s => s.Platforms.HasFlag(platform));

    /// <summary>Anahtardan ekran (yoksa null).</summary>
    public static AppScreen? ByKey(string key)
        => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

    /// <summary>Masaüstü gezinme anahtarından ekran (yoksa null).</summary>
    public static AppScreen? ByDesktopNavKey(string navKey)
        => All.FirstOrDefault(s => string.Equals(s.DesktopNavKey, navKey, StringComparison.Ordinal));

    /// <summary>Web route'undan ekran (yoksa null). Route başında '/' olmadan verilir.</summary>
    public static AppScreen? ByWebRoute(string route)
        => All.FirstOrDefault(s => string.Equals(s.WebRoute, route, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bir platformdaki menü grupları — o platformda EN AZ BİR ekranı olanlar, katalog sırasında.</summary>
    public static IEnumerable<AppScreenGroup> GroupsFor(ScreenPlatform platform)
        => Groups.Where(g => All.Any(s => s.Group == g.Title && s.Platforms.HasFlag(platform)));

    /// <summary>Bu anahtar KATALOG üst grubu mu? (firma bazlı üst gruplar bunun dışındadır)</summary>
    public static bool IsCatalogSection(string key)
        => Sections.Any(x => string.Equals(x.Key, key, StringComparison.Ordinal));

    /// <summary>Katalog üst grubunun başlığı (yoksa null).</summary>
    public static string? SectionTitleOf(string key)
        => Sections.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal))?.Title;

    /// <summary>Bir üst menünün KATALOG varsayılanı olan üst grubu (yoksa null = en üst seviye).</summary>
    public static string? SectionOfGroup(string groupTitle)
        => Groups.FirstOrDefault(g => string.Equals(g.Title, groupTitle, StringComparison.Ordinal))?.Section;

    /// <summary>Katalogdaki üst grup sırası (yoksa listenin sonuna).</summary>
    public static int SectionIndex(string key)
    {
        for (int i = 0; i < Sections.Count; i++)
            if (string.Equals(Sections[i].Key, key, StringComparison.Ordinal)) return i;
        return int.MaxValue / 2;
    }

    /// <summary>Bir grubun o platformdaki ekranları, katalog sırasında.</summary>
    public static IEnumerable<AppScreen> ScreensOf(string groupTitle, ScreenPlatform platform)
        => All.Where(s => s.Group == groupTitle && s.Platforms.HasFlag(platform));
}
