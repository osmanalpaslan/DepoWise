using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DepoWise.Desktop;

/// <summary>
/// Menü grubu başlığı → Themes/Icons.axaml içindeki StreamGeometry anahtarı.
///
/// Neden ViewModel'de sabit geometri tutulmuyor: ikonlar bir KAYNAK sözlüğünde durur, burada
/// yalnız "hangi grup hangi anahtarı kullanır" bilgisi vardır. Anahtar bulunamazsa (yeni grup
/// eklenmiş, ikon henüz çizilmemiş) null döner ve o grup ikonsuz görünür — akış bozulmaz.
///
/// ⚠ Anahtarlar AppScreens.Groups içindeki BAŞLIKLARDIR (ModuleKey DEĞİL): "Operasyon Raporları"
///   ve "Yönetici Raporları" aynı modül anahtarını ("reports") paylaşır, başlıkları farklıdır.
/// </summary>
public static class DesktopIcons
{
    private static readonly Dictionary<string, string> ByGroup = new(StringComparer.Ordinal)
    {
        ["Uyarılar"]            = "IconAlerts",
        ["Malzemeler"]          = "IconMaterials",
        ["Araçlar"]             = "IconVehicles",
        ["Günlük Faaliyet"]     = "IconDailyActivity",
        ["Bakım Takibi"]        = "IconMaintenance",
        ["Yakıt"]               = "IconFuel",
        ["Talepler"]            = "IconRequests",
        ["Ön Muhasebe"]         = "IconAccounting",
        ["Operasyon Raporları"] = "IconReportsOps",
        ["Yönetici Raporları"]  = "IconReportsManager",
        ["Şube ve Personel"]    = "IconBranches",
        ["Kullanıcı Yönetimi"]  = "IconUsers",
        ["Denetim"]             = "IconAudit",
        ["Web Yönetimi"]        = "IconWebAdmin",
        ["Yedekleme"]           = "IconBackup",
        ["Çöp Kutusu"]          = "IconTrash",
        ["Ayarlar"]             = "IconSettings",
    };

    /// <summary>Menünün ÜST GRUPLARI (1. seviye) — AppScreens.Sections başlıkları.
    /// Alt grup ikonlarından kasten farklı geometriler: aynı ikonu iki seviyede tekrarlamak
    /// hiyerarşiyi okunmaz yapar ("Malzeme ve Stok" > "Malzemeler").</summary>
    private static readonly Dictionary<string, string> BySection = new(StringComparer.Ordinal)
    {
        ["Malzeme ve Stok"]   = "IconSectionStock",
        ["Operasyon"]         = "IconSectionOperations",
        ["Finans"]            = "IconSectionFinance",
        ["Raporlar"]          = "IconSectionReports",
        ["Kurumsal Yönetim"]  = "IconSectionCorporate",
        ["Sistem Yönetimi"]   = "IconSectionSystem",
    };

    /// <summary>Ana ekran özet kartı → ikon. Kart etiketinden değil NavKey + etiketten türetilir;
    /// eşleşme yoksa null döner ve kart ikonsuz çizilir (çökmez).</summary>
    public static Geometry? ForKpi(string? iconKey) => iconKey is null ? null : ByKey(iconKey);

    /// <summary>Ana ekran uyarısı → tipine göre ikon. Bilinmeyen tip genel uyarı ikonunu alır.</summary>
    public static Geometry? ForAlert(DepoWise.Application.Reports.AlertKind kind) => ByKey(kind switch
    {
        DepoWise.Application.Reports.AlertKind.Maintenance => "IconMaintenance",
        DepoWise.Application.Reports.AlertKind.Inspection  => "IconWebAdmin",   // kalkan
        DepoWise.Application.Reports.AlertKind.LowStock    => "IconMaterials",
        DepoWise.Application.Reports.AlertKind.Fuel        => "IconFuel",
        _ => "IconWarning",
    });

    /// <summary>Üst grup başlığı → ikon. Bulunamazsa null (üst grup ikonsuz çizilir).</summary>
    public static Geometry? ForSection(string? sectionTitle)
        => string.IsNullOrEmpty(sectionTitle) ? null
         : BySection.TryGetValue(sectionTitle, out var k) ? ByKey(k) : null;
    public static Geometry? ForGroup(string? groupTitle)
    {
        if (string.IsNullOrEmpty(groupTitle)) return null;
        if (!ByGroup.TryGetValue(groupTitle, out var key)) return null;
        return ByKey(key);
    }

    /// <summary>
    /// Masaüstü gezinme anahtarı (ör. <c>materials.list</c>) → ikon. Sekme şeridi kullanır:
    /// ekranın kendi ikonu yoktur, AİT OLDUĞU GRUBUN ikonunu alır (menüdeki görüntüyle aynı dil).
    /// Katalogda olmayan anahtar ya da ikonu çizilmemiş grup → null; sekme ikonsuz çizilir, akış bozulmaz.
    /// </summary>
    public static Geometry? ForScreen(string? desktopNavKey)
    {
        if (string.IsNullOrEmpty(desktopNavKey)) return null;
        var ekran = DepoWise.Application.Security.AppScreens.All
            .FirstOrDefault(s => string.Equals(s.DesktopNavKey, desktopNavKey, StringComparison.Ordinal));
        return ForGroup(ekran?.Group);
    }

    public static Geometry? ByKey(string key)
    {
        // DIKKAT: "Application" adi DepoWise.Application ad alaniyla cakisir -> tam nitelikli yazilir.
        var app = Avalonia.Application.Current;
        if (app is null) return null;
        return app.TryFindResource(key, out var res) && res is Geometry g ? g : null;
    }
}
