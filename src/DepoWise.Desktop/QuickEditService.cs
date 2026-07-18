using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using DepoWise.Application.Security;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

/// <summary>Çift-tık "hızlı düzenle" penceresi yardımcısı (kullanıcı isteği 2026-07-19) — ColumnPickerService
/// ile AYNI desen. Owner = aktif MainWindow. Dönüş: "saved" / "deleted" / null (Kapat).</summary>
public static class QuickEditService
{
    public static async Task<string?> ShowMaterialAsync(SessionContext session, string materialId)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null) return null;
        var win = new MaterialQuickEditWindow(session, materialId);
        return await win.ShowDialog<object?>(desktop.MainWindow) as string;
    }

    public static async Task<string?> ShowVehicleAsync(SessionContext session, string vehicleId)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null) return null;
        var win = new VehicleQuickEditWindow(session, vehicleId);
        return await win.ShowDialog<object?>(desktop.MainWindow) as string;
    }
}
