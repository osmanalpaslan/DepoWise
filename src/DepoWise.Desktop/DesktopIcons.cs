using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DepoWise.Application.Security;

namespace DepoWise.Desktop;

/// <summary>
/// Menü simgesi → <c>Themes/Icons.axaml</c> içindeki StreamGeometry anahtarı.
///
/// <b>⭐ MNU-IKON (2026-09-05) — eşleme artık ORTAK KATMANDAN gelir.</b>
/// Eskiden "hangi menü hangi ikonu alır" bilgisi burada VE web'de <c>NavMenu.WebIcon</c> içinde
/// ayrı ayrı elle tutuluyordu. Katalogda bir grup yeniden adlandırılınca iki tablo da sessizce
/// eskiyordu; web'de beş anahtar tam olarak böyle ölmüştü. Artık kavramlar
/// <see cref="MenuIcons"/> içindedir ve burada yalnız <b>kavram → geometri anahtarı</b> çevirisi var.
///
/// Neden ViewModel'de sabit geometri tutulmuyor: ikonlar bir KAYNAK sözlüğünde durur, burada
/// yalnız hangi kavramın hangi anahtarı kullandığı bilgisi vardır.
///
/// <b>Simgesiz menü kalmaz:</b> bilinmeyen kavram nötr bir geometri alır (<c>IconScreen</c> /
/// <c>IconGroup</c>). Kaynak hiç bulunamazsa null döner ve o satır ikonsuz çizilir — akış bozulmaz.
/// </summary>
public static class DesktopIcons
{
    /// <summary>Kavram → Icons.axaml anahtarı. Kavram listesinin sahibi <see cref="MenuIcons"/>'tur;
    /// buraya eklenmemiş bir kavram <c>MenuIkonTests</c> ile yakalanır.</summary>
    private static readonly Dictionary<string, string> ByConcept = new(StringComparer.Ordinal)
    {
        // ── üst menüler ──
        ["alerts"]           = "IconAlerts",
        ["materials"]        = "IconMaterials",
        ["vehicles"]         = "IconVehicles",
        ["equipment"]        = "IconEquipment",
        ["assignments"]      = "IconAssignments",
        ["purchasing"]       = "IconPurchasing",
        ["work-orders"]      = "IconWorkOrders",
        ["calendar"]         = "IconCalendar",
        ["daily-activity"]   = "IconDailyActivity",
        ["maintenance"]      = "IconMaintenance",
        ["fuel"]             = "IconFuel",
        ["requests"]         = "IconRequests",
        ["accounting"]       = "IconAccounting",
        ["reports"]          = "IconReportsOps",
        ["reports-manager"]  = "IconReportsManager",
        ["branches"]         = "IconBranches",
        ["users"]            = "IconUsers",
        ["documents"]        = "IconDocuments",
        ["announcements"]    = "IconAnnouncements",
        ["audit"]            = "IconAudit",
        ["web-admin"]        = "IconWebAdmin",
        ["backup"]           = "IconBackup",
        ["trash"]            = "IconTrash",
        ["settings"]         = "IconSettings",

        // ── alt menüler (ekranlar) ──
        ["list"]             = "IconList",
        ["new"]              = "IconAdd",
        ["stock-entry"]      = "IconStockEntry",
        ["movements"]        = "IconMovements",
        ["count"]            = "IconCount",
        ["template"]         = "IconTemplate",
        ["distribute"]       = "IconDistribute",
        ["inspection"]       = "IconInspection",
        ["definition"]       = "IconDefinition",
        ["fuel-depot"]       = "IconFuelDepot",
        ["summary"]          = "IconSummary",
        ["request-form"]     = "IconRequestForm",
        ["approve"]          = "IconApprove",
        ["operations"]       = "IconOperations",
        ["invoice"]          = "IconInvoice",
        ["finance"]          = "IconFinance",
        ["payment"]          = "IconPayment",
        ["cost-center"]      = "IconCostCenter",
        ["designer"]         = "IconDesigner",
        ["projects"]         = "IconProjects",
        ["personnel"]        = "IconPersonnel",
        ["teams"]            = "IconTeams",
        ["permissions"]      = "IconPermissions",
        ["log"]              = "IconLog",
        ["companies"]        = "IconCompanies",
        ["update"]           = "IconUpdate",
        ["machine"]          = "IconMachine",
        ["server"]           = "IconServer",
        ["quota"]            = "IconQuota",
        ["delete"]           = "IconDelete",
        ["reset"]            = "IconReset",
        ["screens"]          = "IconScreens",
        ["field-settings"]   = "IconFieldSettings",
        ["excel"]            = "IconImportExcel",
        ["developer"]        = "IconDeveloper",
        ["theme"]            = "IconTheme",
        ["info"]             = "IconInfo",

        // ── üst gruplar (section) ──
        ["section-stock"]      = "IconSectionStock",
        ["section-operations"] = "IconSectionOperations",
        ["section-finance"]    = "IconSectionFinance",
        ["section-reports"]    = "IconSectionReports",
        ["section-corporate"]  = "IconSectionCorporate",
        ["section-system"]     = "IconSectionSystem",

        // ── nötrler ──
        ["screen"]           = "IconScreen",
        ["group"]            = "IconGroup",
    };

    /// <summary>Kavramın geometri anahtarı (test ve tanı için). Bilinmeyen kavram → null.</summary>
    public static string? KeyForConcept(string concept)
        => ByConcept.TryGetValue(concept, out var k) ? k : null;

    private static Geometry? ByConceptGeometry(string concept)
        => ByConcept.TryGetValue(concept, out var key) ? ByKey(key) : null;

    /// <summary>Ana ekran özet kartı → ikon. Kart etiketinden değil NavKey + etiketten türetilir;
    /// eşleşme yoksa null döner ve kart ikonsuz çizilir (çökmez).</summary>
    public static Geometry? ForKpi(string? iconKey) => iconKey is null ? null : ByKey(iconKey);

    /// <summary>Ana ekran uyarısı → tipine göre ikon. Bilinmeyen tip genel uyarı ikonunu alır.</summary>
    public static Geometry? ForAlert(DepoWise.Application.Reports.AlertKind kind) => ByKey(kind switch
    {
        DepoWise.Application.Reports.AlertKind.Maintenance => "IconMaintenance",
        DepoWise.Application.Reports.AlertKind.Inspection  => "IconInspection",
        DepoWise.Application.Reports.AlertKind.LowStock    => "IconMaterials",
        DepoWise.Application.Reports.AlertKind.Fuel        => "IconFuel",
        _ => "IconWarning",
    });

    /// <summary>Üst grup başlığı → ikon.</summary>
    public static Geometry? ForSection(string? sectionTitle)
        => string.IsNullOrEmpty(sectionTitle) ? null : ByConceptGeometry(MenuIcons.ForSection(sectionTitle));

    /// <summary>Üst menü (grup) başlığı → ikon.</summary>
    public static Geometry? ForGroup(string? groupTitle)
        => string.IsNullOrEmpty(groupTitle) ? null : ByConceptGeometry(MenuIcons.ForGroup(groupTitle));

    /// <summary>
    /// ⭐ ALT MENÜ İKONU — ekranın KENDİ katalog anahtarından (<c>AppScreen.Key</c>).
    /// Kullanıcı isteği 2026-09-05: alt menülerde hiç ikon yoktu.
    /// </summary>
    public static Geometry? ForScreenKey(string? screenKey)
        => string.IsNullOrEmpty(screenKey) ? null : ByConceptGeometry(MenuIcons.ForScreen(screenKey));

    /// <summary>
    /// Masaüstü gezinme anahtarı (ör. <c>materials.list</c>) → ikon. Sekme şeridi kullanır.
    ///
    /// ⭐ 2026-09-05: artık ekranın KENDİ ikonunu döndürür (eskiden grubunkini döndürüyordu).
    /// Kural değişmedi — kural "menüde ne görülüyorsa sekmede de o görülsün"dü; menüde alt menüler
    /// artık kendi ikonlarını taşıdığı için sekme de onu göstermelidir. Aksi hâlde aynı ekran
    /// menüde bir, sekmede başka bir ikonla görünürdü.
    /// </summary>
    public static Geometry? ForScreen(string? desktopNavKey)
    {
        if (string.IsNullOrEmpty(desktopNavKey)) return null;
        var ekran = AppScreens.All
            .FirstOrDefault(s => string.Equals(s.DesktopNavKey, desktopNavKey, StringComparison.Ordinal));
        return ekran is null ? null : ForScreenKey(ekran.Key);
    }

    public static Geometry? ByKey(string key)
    {
        // DIKKAT: "Application" adi DepoWise.Application ad alaniyla cakisir -> tam nitelikli yazilir.
        var app = Avalonia.Application.Current;
        if (app is null) return null;
        return app.TryFindResource(key, out var res) && res is Geometry g ? g : null;
    }
}
