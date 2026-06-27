using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

/// <summary>Fotoğrafı ayrı pencerede orijinal boyutta açar (tekerlekle zoom).</summary>
public static class PhotoViewer
{
    public static void Show(Bitmap? bitmap)
    {
        if (bitmap is null) return;
        var win = new PhotoViewerWindow(bitmap);
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            && d.MainWindow is not null)
            win.Show(d.MainWindow);
        else
            win.Show();
    }
}
