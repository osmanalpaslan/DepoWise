using System;
using System.Collections.Generic;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ MNU-IKON (kullanıcı isteği 2026-09-05) — MENÜ SİMGELERİNİN TEK DOĞRU KAYNAĞI ═══
///
/// <b>Bulgu (iki ortam da ölçüldü):</b>
/// <list type="bullet">
///   <item><b>Alt menülerin (70 ekran) HİÇBİRİNDE simge yoktu</b> — eksik kalmış değil, hiç
///     tanımlanmamıştı: masaüstü şablonunda simge alanı bile yoktu, web'de <c>MudNavLink</c>'lere
///     <c>Icon</c> verilmiyordu.</item>
///   <item><b>Üst gruplarda</b> masaüstünde 7 grup simgesizdi (Ekipman · Zimmet · Satın Alma ·
///     İş Emirleri · Takvim · Evrak · Duyurular); web'de ise eşleme tablosunda <b>eskimiş
///     anahtarlar</b> vardı ("Personel", "Yönetim", "Raporlar", "İmport / Export", "Kullanıcı") —
///     grup adları değişince simge SESSİZCE genel klasöre düşmüştü.</item>
/// </list>
///
/// <b>Kök neden:</b> simge eşlemesi İKİ AYRI YERDE elle tutuluyordu (masaüstünde
/// <c>DesktopIcons</c>, web'de <c>NavMenu.WebIcon</c>). Katalogda bir grup yeniden adlandırılınca
/// iki tablo da sessizce eskiyordu — kimse fark etmiyordu, çünkü eşleşmeyen anahtar hata vermiyor,
/// yalnız simgeyi kaybediyordu.
///
/// <b>Çözüm:</b> "hangi menü hangi SİMGE KAVRAMINI kullanır" bilgisi artık BURADA, ortak katmanda.
/// Platformlar yalnız <b>kavram → kendi simge sistemi</b> çevirisini yapar
/// (masaüstü: Avalonia geometrisi · web: MudBlazor sabiti). Böylece:
/// <list type="number">
///   <item>iki menü bir daha ayrışamaz — kavram listesi tek,</item>
///   <item>Application katmanı MudBlazor'a ya da Avalonia'ya BAĞIMLI OLMAZ (yalnız string),</item>
///   <item>eksik eşleme <b>testle</b> yakalanır (bkz. <c>MenuIkonTests</c>), sessizce kaybolmaz.</item>
/// </list>
///
/// <b>Neden <c>AppScreen</c> kaydına alan eklenmedi:</b> o kayıt çok sayıda parite testiyle kilitli
/// ve her ekran satırını değiştirmek gerekirdi. Ayrı tablo, çalışan yapıya dokunmadan aynı sonucu
/// verir (geliştirme protokolü §5: en küçük doğru değişiklik).
///
/// <b>Kavram yeniden kullanılır:</b> 70 ekrana 70 ayrı çizim yapmak menüyü okunmaz yapardı —
/// aynı işi yapan ekranlar (listeler, "yeni kayıt"lar) BİLEREK aynı kavramı paylaşır. Ayırt edici
/// olan üst grubun simgesi + ekranın adıdır.
/// </summary>
public static class MenuIcons
{
    /// <summary>Eşleşme bulunamazsa dönen kavram. Platformlar bunun için nötr bir simge çizer —
    /// yani menü öğesi ASLA simgesiz kalmaz (kullanıcı şartı: "simgesi olmayan menü istemiyorum").</summary>
    public const string Fallback = "screen";

    // ═══════════ EKRAN (alt menü) → simge kavramı ═══════════
    private static readonly Dictionary<string, string> ByScreen = new(StringComparer.Ordinal)
    {
        ["alerts"]                    = "alerts",

        // Malzemeler
        ["materials.list"]            = "list",
        ["materials.new"]             = "new",
        ["stock.entry"]               = "stock-entry",
        ["stock.movements"]           = "movements",
        ["stock.count"]               = "count",
        ["material_templates"]        = "template",
        ["stock.distribute"]          = "distribute",

        // Araçlar
        ["vehicles.list"]             = "list",
        ["vehicles.new"]              = "new",
        ["vehicle_templates"]         = "template",
        ["inspection"]                = "inspection",

        // Operasyon
        ["daily_activity"]            = "daily-activity",
        ["maintenance.defs"]          = "definition",
        ["maintenance.records"]       = "maintenance",
        ["fuel.dist"]                 = "fuel",
        ["fuel.depot"]                = "fuel-depot",
        ["fuel.summary"]              = "summary",
        ["work_orders"]               = "work-orders",
        ["calendar"]                  = "calendar",
        ["purchasing"]                = "purchasing",
        ["assignments"]               = "assignments",
        ["equipment"]                 = "equipment",

        // Talepler
        ["requests.form"]             = "request-form",
        ["requests.approve"]          = "approve",
        ["approvals"]                 = "approve",
        ["request_ops"]               = "operations",

        // Ön muhasebe
        ["accounting.parties"]        = "list",
        ["accounting.parties.new"]    = "new",
        ["accounting.invoices"]       = "invoice",
        ["accounting.invoices.new"]   = "new",
        ["accounting.finance"]        = "finance",
        ["accounting.finance.new"]    = "new",
        ["accounting.payments"]       = "payment",
        ["cost_centers"]              = "cost-center",

        // Raporlar
        ["reports"]                   = "reports",
        ["reports.manager"]           = "reports-manager",
        ["reports.designer"]          = "designer",

        // Kurumsal
        ["branches"]                  = "branches",
        ["projects"]                  = "projects",
        ["personnel"]                 = "personnel",
        ["documents"]                 = "documents",
        ["announcements"]             = "announcements",
        ["users"]                     = "users",
        ["teams"]                     = "teams",
        ["permissions"]               = "permissions",
        ["permission_templates"]      = "template",
        ["audit"]                     = "audit",
        ["stock_change_log"]          = "log",

        // Sistem / web yönetimi
        ["companies"]                 = "companies",
        ["releases"]                  = "update",
        ["machines"]                  = "machine",
        ["machine_backups"]           = "backup",
        ["server_backups"]            = "backup",
        ["server_status"]             = "server",
        ["quota_monitor"]             = "quota",
        ["company_permissions"]       = "permissions",
        ["purge_company"]             = "delete",
        ["reset_company_business"]    = "reset",
        ["local_reset"]               = "reset",
        ["screen_visibility"]         = "screens",
        ["backup"]                    = "backup",
        ["trash"]                     = "trash",

        // Ayarlar
        ["definitions"]               = "definition",
        ["field_settings"]            = "field-settings",
        ["import"]                    = "excel",
        ["import_export"]             = "excel",
        ["settings.developer"]        = "developer",
        ["theme"]                     = "theme",
        ["about"]                     = "info",
    };

    // ═══════════ ÜST MENÜ (grup) → simge kavramı ═══════════
    // ⚠️ Anahtar grup BAŞLIĞIDIR (AppScreens.Groups içindeki metin). Bir grup yeniden adlandırılırsa
    // burası da güncellenmelidir — ve güncellenmezse MenuIkonTests KIRILIR (eskiden sessizce
    // genel klasöre düşüyordu; web'deki beş eskimiş anahtar tam olarak böyle oluşmuştu).
    private static readonly Dictionary<string, string> ByGroup = new(StringComparer.Ordinal)
    {
        ["Uyarılar"]            = "alerts",
        ["Malzemeler"]          = "materials",
        ["Araçlar"]             = "vehicles",
        ["Ekipman"]             = "equipment",
        ["Zimmet"]              = "assignments",
        ["Satın Alma"]          = "purchasing",
        ["İş Emirleri"]         = "work-orders",
        ["Takvim"]              = "calendar",
        ["Günlük Faaliyet"]     = "daily-activity",
        ["Bakım Takibi"]        = "maintenance",
        ["Yakıt"]               = "fuel",
        ["Talepler"]            = "requests",
        ["Ön Muhasebe"]         = "accounting",
        ["Operasyon Raporları"] = "reports",
        ["Yönetici Raporları"]  = "reports-manager",
        ["Şube ve Personel"]    = "branches",
        ["Kullanıcı Yönetimi"]  = "users",
        ["Evrak"]               = "documents",
        ["Duyurular"]           = "announcements",
        ["Denetim"]             = "audit",
        ["Web Yönetimi"]        = "web-admin",
        ["Yedekleme"]           = "backup",
        ["Çöp Kutusu"]          = "trash",
        ["Ayarlar"]             = "settings",
    };

    // ═══════════ ÜST GRUP (section) → simge kavramı ═══════════
    // Alt gruplarınkinden BİLEREK farklı kavramlar: aynı simgeyi iki seviyede tekrarlamak
    // hiyerarşiyi okunmaz yapar ("Malzeme ve Stok" > "Malzemeler").
    private static readonly Dictionary<string, string> BySection = new(StringComparer.Ordinal)
    {
        ["Malzeme ve Stok"]  = "section-stock",
        ["Operasyon"]        = "section-operations",
        ["Finans"]           = "section-finance",
        ["Raporlar"]         = "section-reports",
        ["Kurumsal Yönetim"] = "section-corporate",
        ["Sistem Yönetimi"]  = "section-system",
    };

    /// <summary>Ekran anahtarı → simge kavramı. Bilinmeyen ekran nötr kavram alır (simgesiz KALMAZ).</summary>
    public static string ForScreen(string? screenKey)
        => screenKey is not null && ByScreen.TryGetValue(screenKey, out var v) ? v : Fallback;

    /// <summary>Grup başlığı → simge kavramı. Kullanıcının kendi oluşturduğu gruplar nötr kavram alır.</summary>
    public static string ForGroup(string? groupTitle)
        => groupTitle is not null && ByGroup.TryGetValue(groupTitle, out var v) ? v : "group";

    /// <summary>Üst grup başlığı → simge kavramı.</summary>
    public static string ForSection(string? sectionTitle)
        => sectionTitle is not null && BySection.TryGetValue(sectionTitle, out var v) ? v : "group";

    /// <summary>Testlerin kapsamı ölçebilmesi için: kullanılan TÜM kavramlar (nötrler dâhil).
    /// Bir platform bu kavramlardan birini çeviremiyorsa test kırılır.</summary>
    public static IReadOnlyCollection<string> AllConcepts()
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { Fallback, "group" };
        foreach (var v in ByScreen.Values) set.Add(v);
        foreach (var v in ByGroup.Values) set.Add(v);
        foreach (var v in BySection.Values) set.Add(v);
        return set;
    }
}
