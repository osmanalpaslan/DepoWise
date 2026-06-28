using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DepoWise.Desktop;

/// <summary>Masaüstü dosya seçici (Avalonia StorageProvider). Foto ekleme için yerel yolları döndürür.</summary>
public static class FilePickerService
{
    public static async Task<IReadOnlyList<string>> PickImagesAsync(bool allowMultiple = true)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null)
            return Array.Empty<string>();

        var files = await d.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Fotoğraf Seç",
            AllowMultiple = allowMultiple,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Görseller")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                }
            }
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    /// <summary>PDF kaydetme yeri seçtirir; yerel yol döner (iptal → null).</summary>
    public static async Task<string?> SavePdfAsync(string suggestedName)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null)
            return null;

        var file = await d.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "PDF Kaydet",
            SuggestedFileName = suggestedName,
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF Dosyası") { Patterns = new[] { "*.pdf" } } }
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>Tek dosya seçtirir (genel). Yerel yol döner (iptal → null).</summary>
    public static async Task<string?> PickFileAsync(string title, string patternLabel, params string[] patterns)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null)
            return null;

        var files = await d.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(patternLabel) { Patterns = patterns } }
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Dosyayı sistem varsayılan uygulamasıyla açar (sessiz başarısızlık).</summary>
    public static void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }
}
