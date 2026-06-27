using Avalonia.Media.Imaging;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Forma eklenen (henüz kaydedilmemiş) fotoğraf — yerel yol + önizleme.</summary>
public sealed class PhotoStage
{
    public string LocalPath { get; }
    public Bitmap? Image { get; }

    public PhotoStage(string localPath)
    {
        LocalPath = localPath;
        try { Image = new Bitmap(localPath); } catch { Image = null; }
    }
}
