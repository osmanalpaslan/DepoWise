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

/// <summary>Detayda gösterilen kayıtlı fotoğraf — silme için FileId taşır.</summary>
public sealed class DetailPhoto
{
    public string FileId { get; }
    public Bitmap? Image { get; }
    public DetailPhoto(string fileId, Bitmap? image) { FileId = fileId; Image = image; }
}
