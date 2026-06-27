using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

/// <summary>Türkçe modal onay penceresi yardımcısı. Owner = aktif MainWindow.</summary>
public static class ConfirmService
{
    public static async Task<bool> AskAsync(string message, string title = "Onay",
        string okText = "Evet", string cancelText = "Vazgeç", bool danger = false)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return false;

        var win = new ConfirmWindow(title, message, okText, cancelText, danger);
        return await win.ShowDialog<bool>(desktop.MainWindow);
    }
}
