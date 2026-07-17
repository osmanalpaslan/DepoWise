using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

/// <summary>"Kolonları Ayarla" modal penceresi yardımcısı (kullanıcı isteği 2026-07-17) — ConfirmService ile
/// AYNI desen. Owner = aktif MainWindow. Vazgeç → null; Kaydet → seçili kolon anahtarları.</summary>
public static class ColumnPickerService
{
    public static async Task<List<string>?> PickAsync(IReadOnlyList<(string Key, string Label)> available, IReadOnlyList<string> selected)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return null;

        var win = new ColumnPickerWindow(available, selected);
        return await win.ShowDialog<List<string>?>(desktop.MainWindow);
    }
}
