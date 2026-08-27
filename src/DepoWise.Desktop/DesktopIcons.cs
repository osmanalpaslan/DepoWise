using System;
using System.Collections.Generic;
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

    public static Geometry? ForGroup(string? groupTitle)
    {
        if (string.IsNullOrEmpty(groupTitle)) return null;
        if (!ByGroup.TryGetValue(groupTitle, out var key)) return null;
        return ByKey(key);
    }

    public static Geometry? ByKey(string key)
    {
        // DIKKAT: "Application" adi DepoWise.Application ad alaniyla cakisir -> tam nitelikli yazilir.
        var app = Avalonia.Application.Current;
        if (app is null) return null;
        return app.TryFindResource(key, out var res) && res is Geometry g ? g : null;
    }
}
