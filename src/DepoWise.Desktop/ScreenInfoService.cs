using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

/// <summary>Aktif ekranın gerçek kod bilgisini gösteren pencereyi açar (kopyalanabilir).</summary>
public static class ScreenInfoService
{
    public static async Task ShowAsync(string title, string body)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;
        var win = new ScreenInfoWindow(title, body);
        await win.ShowDialog(desktop.MainWindow);
    }
}
