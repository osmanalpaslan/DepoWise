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
public sealed record AppScreenGroup(string Title, string DesktopIcon, string ModuleKey);

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
    /// <summary>Menü grupları — iki platformda AYNI başlık ve sıra.</summary>
    public static readonly IReadOnlyList<AppScreenGroup> Groups = new[]
    {
        new AppScreenGroup("Uyarılar", "🔔", "alerts"),
        new AppScreenGroup("Malzemeler", "📦", "materials"),
        new AppScreenGroup("Araçlar", "🚚", "vehicles"),
        new AppScreenGroup("Personel", "🧑‍🔧", "personnel"),
        new AppScreenGroup("Günlük Faaliyet", "📋", "daily_activity"),
        new AppScreenGroup("Bakım Takibi", "🔧", "maintenance"),
        new AppScreenGroup("Yakıt", "⛽", "fuel"),
        new AppScreenGroup("Yönetim", "👤", "branches"),
        new AppScreenGroup("Talepler", "📄", "requests"),
        new AppScreenGroup("Raporlar", "📊", "reports"),
        new AppScreenGroup("Yönetici Raporları", "📈", "reports"),
        new AppScreenGroup("İmport / Export", "🔁", "import_export"),
        // G4-1 (2026-08-12): ÖN MUHASEBE. Şimdilik yalnız Cari; G4-2/3/4 kendi ekranlarını buraya ekler.
        new AppScreenGroup("Ön Muhasebe", "🧾", "parties"),
        new AppScreenGroup("Kullanıcı", "👥", "users"),
        new AppScreenGroup("Ayarlar", "🛠️", "settings"),
        new AppScreenGroup("Web Yönetimi", "🛡️", "companies"),
        new AppScreenGroup("Çöp Kutusu", "🗑️", "trash"),
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
        // ── Uyarılar ────────────────────────────────────────────────────────────────────────
        new AppScreen("alerts", "alerts", "Uyarılar", "Uyarılar", Both, "alerts", "alerts", WebPermOverride: ""),

        // ── Malzemeler ──────────────────────────────────────────────────────────────────────
        new AppScreen("materials.list", "materials", "Malzemeler", "Malzeme Listesi", Both, "materials", "materials"),
        new AppScreen("materials.new", "materials", "Malzemeler", "Yeni Kayıt", Both, "materials/new", "materials:new"),
        // Malzeme Şablonları menüde YALNIZ masaüstünde (web'de ekran var ama menüde listelenmiyordu).
        new AppScreen("material_templates", "material_templates", "Malzemeler", "Malzeme Şablonları", D, null, "material_templates:templates"),
        new AppScreen("stock.entry", "stock", "Malzemeler", "Giriş-Çıkış", Both, "stock", "stock"),
        new AppScreen("stock.movements", "stock", "Malzemeler", "Stok Hareketleri", Both, "stock/movements", "stock:movements"),
        new AppScreen("stock.count", "stock", "Malzemeler", "Stok Sayım", Both, "stock/count", "stock:count"),
        // STK-08 — web'de Stok İşlemleri ekranından açılır, menüde listelenmez.
        new AppScreen("stock.distribute", "stock", "Malzemeler", "Atanmamış Stok Dağıtımı", D, null, "stock:distribute"),

        // ── Araçlar ─────────────────────────────────────────────────────────────────────────
        new AppScreen("vehicles.list", "vehicles", "Araçlar", "Araç Listesi", Both, "vehicles", "vehicles"),
        new AppScreen("vehicles.new", "vehicles", "Araçlar", "Yeni Araç Ekle", Both, "vehicles/new", "vehicles:new"),
        new AppScreen("vehicle_templates", "vehicle_templates", "Araçlar", "Şablonlar", Both, "vehicle-templates", "vehicle_templates:templates"),
        new AppScreen("inspection", "inspection", "Araçlar", "Muayene / Sigorta", Both, "inspection", "inspection"),

        // ── Personel ────────────────────────────────────────────────────────────────────────
        new AppScreen("personnel", "personnel", "Personel", "Personel Girişi", Both, "personnel", "personnel"),

        // ── Günlük Faaliyet ─────────────────────────────────────────────────────────────────
        new AppScreen("daily_activity", "daily_activity", "Günlük Faaliyet", "Günlük Faaliyet Girişi", Both, "daily", "daily_activity"),

        // ── Bakım Takibi ────────────────────────────────────────────────────────────────────
        new AppScreen("maintenance.defs", "maintenance", "Bakım Takibi", "Bakım Tanımları Girişi", Both, "maintenance/defs", "maintenance:defs"),
        new AppScreen("maintenance.records", "maintenance", "Bakım Takibi", "Araç Bakımları Girişi", Both, "maintenance/records", "maintenance:records"),

        // ── Yakıt ───────────────────────────────────────────────────────────────────────────
        new AppScreen("fuel.dist", "fuel", "Yakıt", "Yakıt Dağıtımları", Both, "fuel/dist", "fuel:dist"),
        new AppScreen("fuel.depot", "fuel", "Yakıt", "Depo Girişleri", Both, "fuel/depot", "fuel:depot"),
        new AppScreen("fuel.summary", "fuel", "Yakıt", "Özet", Both, "fuel/summary", "fuel:summary"),

        // ── Yönetim ─────────────────────────────────────────────────────────────────────────
        new AppScreen("branches", "branches", "Yönetim", "Şube / Şantiye", Both, "branches", "branches"),
        new AppScreen("audit", "audit", "Yönetim", "Sistem Logu", Both, "audit", "audit"),
        new AppScreen("stock_change_log", "stock_change_log", "Yönetim", "Stok Değişiklik Kaydı", Both, "stock-change-log", "stock_change_log"),
        // Yedek Yönetimi masaüstünden 2026-07-26'da KALDIRILDI; web'de süper + kısıtlı süper admin.
        new AppScreen("backup", "backup", "Yönetim", "Yedek Yönetimi", W, "backup", null, WebPermOverride: "@superr"),

        // ── Talepler ────────────────────────────────────────────────────────────────────────
        new AppScreen("requests.form", "requests", "Talepler", "Talep Formu", Both, "requests", "requests:form"),
        new AppScreen("requests.approve", "request_approval", "Talepler", "Talep Onaylama", Both, "requests/approve", "requests:approve", WebPermOverride: "requests"),
        new AppScreen("request_ops", "request_ops", "Talepler", "Talep Operasyonları", Both, "request-operations", "request_ops:board"),

        // ── Raporlar ────────────────────────────────────────────────────────────────────────
        new AppScreen("reports", "reports", "Raporlar", "Raporlar", Both, "reports", "reports"),
        // Yönetici Raporları: aynı ekran, ayrı menü girişi (web'de admin kapısı).
        new AppScreen("reports.manager", "reports", "Yönetici Raporları", "Raporlar", Both, "reports", "reports", WebPermOverride: "@admin"),

        // ── İmport / Export ─────────────────────────────────────────────────────────────────
        // Masaüstünde kendi grubu; web'de Ayarlar altında "Excel İçe Aktarım" olarak duruyordu.
        new AppScreen("import_export", "import_export", "İmport / Export", "İmport / Export", D, null, "import_export"),

        // ── Ön Muhasebe (G4-1) ──────────────────────────────────────────────────────────────
        // Cari KARTI ayrı bir menü girişi DEĞİLDİR: listeden açılır (web: parties/{Id}).
        // Menüyü kalabalıklaştırmamak için yalnız liste + yeni kayıt girişleri var.
        new AppScreen("accounting.parties", "parties", "Ön Muhasebe", "Cari Listesi", Both, "parties", "parties"),
        new AppScreen("accounting.parties.new", "parties", "Ön Muhasebe", "Yeni Cari", Both, "parties/new", "parties:new"),

        // ── Ön Muhasebe / Fatura (G4-2) ────────────────────────────────────────────────────
        // Fatura DETAYI ayrı menü girişi değildir: listeden açılır (web: invoices/{Id}).
        // "invoices" modülü cariden AYRIDIR — fatura kesme yetkisi ayrı verilebilir.
        new AppScreen("accounting.invoices", "invoices", "Ön Muhasebe", "Fatura Listesi", Both, "invoices", "invoices"),
        new AppScreen("accounting.invoices.new", "invoices", "Ön Muhasebe", "Yeni Fatura", Both, "invoices/new", "invoices:new"),

        // ── Ön Muhasebe / Kasa-Banka (G4-3) ────────────────────────────────────────────────
        // Kasa ve banka AYRI ekran DEĞİL: aynı defter, aynı ekran, tür filtresiyle ayrılır.
        // Tahsilat/ödeme kendi ekranındadır — para hareketi hesap tanımından ayrı bir iştir.
        new AppScreen("accounting.finance", "finance", "Ön Muhasebe", "Kasa / Banka", Both, "finance", "finance"),
        new AppScreen("accounting.finance.new", "finance", "Ön Muhasebe", "Yeni Hesap", Both, "finance/new", "finance:new"),
        new AppScreen("accounting.payments", "finance", "Ön Muhasebe", "Tahsilat / Ödeme", Both, "payments", "payments"),

        // ── Kullanıcı ───────────────────────────────────────────────────────────────────────
        new AppScreen("users", "users", "Kullanıcı", "Kullanıcı Tanım", Both, "users", "users"),
        new AppScreen("permissions", "permissions", "Kullanıcı", "Yetkiler", Both, "permissions", "permissions"),
        new AppScreen("permission_templates", "permission_templates", "Kullanıcı", "Yetki Şablonları", Both, "permission-templates", "permission_templates"),

        // ── Ayarlar ─────────────────────────────────────────────────────────────────────────
        new AppScreen("definitions", "definitions", "Ayarlar", "Tanım Düzenle", Both, "definitions", "definitions"),
        new AppScreen("import", "import_export", "Ayarlar", "Excel İçe Aktarım", W, "import", null),
        new AppScreen("settings.developer", "settings", "Ayarlar", "Geliştirici Modu", Both, "developer", "settings:developer"),
        new AppScreen("theme", "theme", "Ayarlar", "Tema", Both, "theme", "theme", WebPermOverride: ""),
        new AppScreen("about", "about", "Ayarlar", "Hakkında", Both, "soon/about", "about", WebPermOverride: ""),

        // ── Web Yönetimi (süper admin) ──────────────────────────────────────────────────────
        new AppScreen("companies", "companies", "Web Yönetimi", "Firma Tanım", Both, "companies", "companies"),
        new AppScreen("releases", "releases", "Web Yönetimi", "Güncelleme Yönetimi", Both, "releases", "releases"),
        new AppScreen("machines", "machines", "Web Yönetimi", "Makine Yönetimi", Both, "machines", "machines"),
        new AppScreen("machine_backups", "machine_backups", "Web Yönetimi", "Makine Yedekleri", W, "machine-backups", null),
        new AppScreen("server_backups", "server_backups", "Web Yönetimi", "Sunucu Yedekleri", Both, "server-backups", "server_backups"),
        new AppScreen("server_status", "server_status", "Web Yönetimi", "Canlı Sunucu", W, "server-status", null),
        new AppScreen("quota_monitor", "quota_monitor", "Web Yönetimi", "Kota İzleme", W, "quota-monitor", null),
        new AppScreen("company_permissions", "companies", "Web Yönetimi", "Firma Yetki Kontrol", W, "company-permissions", null),
        new AppScreen("role_permissions", "role_permissions", "Web Yönetimi", "Rol Yetki Kontrol", W, "role-permissions", null),
        new AppScreen("purge_company", "purge_company", "Web Yönetimi", "Kalıcı Silme", W, "purge-company", null),
        new AppScreen("reset_company_business", "purge_company", "Web Yönetimi", "Firma İş Verisini Sıfırla", W, "reset-company-business", null, WebPermOverride: "@super"),
        // G5 (2026-08-12): ekranların hangi platformda açık olacağını yönetir. Süper admin ekranıdır;
        // diğer süper admin ekranları gibi (Rol Yetki Kontrol, Kota İzleme…) YALNIZ WEB'de sunulur.
        new AppScreen("screen_visibility", "screen_visibility", "Web Yönetimi", "Ekran Platform Yönetimi", W, "screen-visibility", null),

        // ── Çöp Kutusu ──────────────────────────────────────────────────────────────────────
        // G2-B1 DÜZELTMESİ (2026-08-12): "trash" artık AppModules kataloğunda da var → yetki ağacından
        // yönetilebilir. Eskiden katalog dışıydı; masaüstünde yalnız admin bypass'ı, web'de "@admin"
        // sözde-anahtarı sayesinde çalışıyordu ve HİÇ KİMSEYE devredilemiyordu.
        new AppScreen("trash", "trash", "Çöp Kutusu", "Çöp Kutusu Listesi", Both, "trash", "trash"),
    };

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

    /// <summary>Bir grubun o platformdaki ekranları, katalog sırasında.</summary>
    public static IEnumerable<AppScreen> ScreensOf(string groupTitle, ScreenPlatform platform)
        => All.Where(s => s.Group == groupTitle && s.Platforms.HasFlag(platform));
}
