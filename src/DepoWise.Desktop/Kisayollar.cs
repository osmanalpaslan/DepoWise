using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DepoWise.Desktop.Controls;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ M-62 (3/4) (AR-GE 2026-10-25, F-093) — PENCERE DÜZEYİ KLAVYE KISAYOLLARI ═══
/// Ekranlar DEĞİŞMEDEN çalışır: açık ekranın ortak parçaları kullanılır (araç çubuğunun ana eylemi, "Search" sınıflı arama
/// kutusu, ekranın Yükle/Yenile komutu). Bir ekranda karşılığı yoksa kısayol hiçbir şey yapmaz (yanlış işlem tetiklenmez).
///   F5            → yenile (IRefreshable ya da ekranın Load/Yenile/Refresh komutu)
///   Ctrl+N        → araç çubuğunun ana eylemi ("Yeni …") — yetki/durum düğmeyle aynı (CanExecute + görünürlük)
///   Ctrl+F        → ekrandaki arama kutusuna odak
///   Ctrl+W        → aktif sekmeyi kapat · Ctrl+Tab / Ctrl+Shift+Tab → sonraki / önceki sekme · F1 → ekran bilgisi
/// Kayıt/silme kısayolu (Ctrl+S, Del) BİLİNÇLİ OLARAK YOK: onay akışı ekrana göre değişir; tek tuşla kayda dokunulmaz.
/// </summary>
public static class Kisayollar
{
    private static readonly string[] YenilemeKomutlari = { "LoadCommand", "YenileCommand", "RefreshCommand" };

    /// <summary>F5: ekranı yenile. Yenilendiyse true.</summary>
    public static bool Yenile(object? sayfa)
    {
        if (sayfa is IRefreshable r) { r.RefreshData(); return true; }
        if (sayfa is null) return false;
        foreach (var ad in YenilemeKomutlari)
            if (sayfa.GetType().GetProperty(ad)?.GetValue(sayfa) is ICommand k && k.CanExecute(null)) { k.Execute(null); return true; }
        return false;
    }

    /// <summary>Ctrl+N: ekrandaki görünür araç çubuğunun ana eylemi. Çalıştıysa true.</summary>
    public static bool AnaEylem(Control? icerik)
    {
        var cubuk = icerik?.GetVisualDescendants().OfType<Toolbar>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.PrimaryActionText) && t.PrimaryActionCommand is not null);
        if (cubuk?.PrimaryActionCommand is not { } k || !k.CanExecute(null)) return false;
        k.Execute(null);
        return true;
    }

    /// <summary>Ctrl+F: ekrandaki ilk görünür arama kutusuna odak. Odaklandıysa true.</summary>
    public static bool AramayaOdaklan(Control? icerik)
    {
        var kutu = icerik?.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains("Search") && t.IsEffectivelyVisible && t.IsEffectivelyEnabled);
        if (kutu is null) return false;
        kutu.Focus();
        kutu.SelectAll();
        return true;
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: sekmeler arasında döner (yon = +1 / −1). Geçildiyse true.</summary>
    public static bool SekmeGec(ShellViewModel? kabuk, int yon)
    {
        if (kabuk is null || kabuk.OpenTabs.Count < 2) return false;
        var i = kabuk.OpenTabs.ToList().FindIndex(t => t.IsActive);
        var hedef = kabuk.OpenTabs[((i < 0 ? 0 : i) + yon + kabuk.OpenTabs.Count) % kabuk.OpenTabs.Count];
        kabuk.ActivateTabCommand.Execute(hedef);
        return true;
    }

    /// <summary>Ctrl+W: aktif sekmeyi kapatır. Kapattıysa true.</summary>
    public static bool SekmeKapat(ShellViewModel? kabuk)
    {
        var aktif = kabuk?.OpenTabs.FirstOrDefault(t => t.IsActive);
        if (aktif is null) return false;
        kabuk!.CloseTabCommand.Execute(aktif);
        return true;
    }
}
