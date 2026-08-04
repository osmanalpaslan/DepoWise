using System;
using System.IO;
using System.Text;

namespace DepoWise.Desktop;

/// <summary>
/// Z2 (2026-07-19): Eşitleme olaylarının KALICI günlüğü. Amaç: destek verirken YALNIZ log dosyasına
/// bakarak "hangi kayıt gitti/gitmedi, neden" anlaşılabilsin (sessiz başarısızlık görünür olsun).
///
/// Dosya: %LOCALAPPDATA%\DepoWise\logs\sync.log — ~2 MB'ı geçince yarısı kırpılır (sonsuz büyümez).
/// En az kod: harici bağımlılık yok, thread-safe (lock), hata olursa sessizce yutar (log yüzünden uygulama bozulmaz).
/// </summary>
public static class SyncLog
{
    private static readonly object _lock = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    private static string? PathOrNull()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alpnex", "logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "sync.log");
        }
        catch { return null; }
    }

    /// <summary>Log klasörü (kullanıcıya "şu dosyaya bak" demek için).</summary>
    public static string? FilePath => PathOrNull();

    public static void Write(string @event, string? detail = null)
    {
        var path = PathOrNull();
        if (path is null) return;
        var machine = Environment.MachineName;
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{machine}] {@event}" +
                   (string.IsNullOrWhiteSpace(detail) ? "" : " | " + detail) + Environment.NewLine;
        lock (_lock)
        {
            try
            {
                TrimIfLarge(path);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch { /* log best-effort; asla uygulamayı bozmaz */ }
        }
    }

    private static void TrimIfLarge(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < MaxBytes) return;
            var all = File.ReadAllText(path, Encoding.UTF8);
            // Son yarıyı koru (yeni olaylar önemli)
            File.WriteAllText(path, all.Substring(all.Length / 2), Encoding.UTF8);
        }
        catch { }
    }
}
