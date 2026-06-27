using System.Security.Cryptography;
using DepoWise.Application.Update;

namespace DepoWise.Infrastructure.Update;

public sealed class UpdateFailedException : Exception
{
    public UpdateFailedException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Masaüstü updater: sürüm kontrol + checksum doğrulama (bozuk paket KURULMAZ) + indirme/kurulum yüzdesi
/// (0-100) + başarısız güncellemede ESKİ SÜRÜME dönüş (rollback) + hata logu. Dosya tabanlı, test edilebilir.
/// </summary>
public sealed class UpdateService
{
    private readonly string _installRoot;   // current.txt + app/ burada
    private readonly string _logPath;

    public UpdateService(string installRoot)
    {
        _installRoot = installRoot;
        Directory.CreateDirectory(Path.Combine(_installRoot, "app"));
        _logPath = Path.Combine(_installRoot, "update.log");
        if (!File.Exists(CurrentFile)) File.WriteAllText(CurrentFile, "0.0.0");
    }

    private string CurrentFile => Path.Combine(_installRoot, "current.txt");
    private string AppDir => Path.Combine(_installRoot, "app");
    private string BackupDir => Path.Combine(_installRoot, "backup");

    public string CurrentVersion() => File.ReadAllText(CurrentFile).Trim();

    public UpdateCheckResult Check(UpdatePackage? latest)
    {
        var current = CurrentVersion();
        if (latest is null || !SemVer.TryParse(latest.Version, out var lv) || !SemVer.TryParse(current, out var cv))
            return new UpdateCheckResult(false, current, latest?.Version, false, false);

        var available = lv.CompareTo(cv) > 0;
        bool belowMin = SemVer.TryParse(latest.MinSupportedVersion, out var minV) && cv.CompareTo(minV) < 0;
        return new UpdateCheckResult(available, current, latest.Version, belowMin, !latest.Signed);
    }

    public static bool VerifyChecksum(byte[] content, string expectedSha256Hex)
    {
        var actual = Convert.ToHexString(SHA256.HashData(content));
        return string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Paketi uygular. installStep: gerçek kurulum adımı (test için enjekte; false/exception → başarısız → rollback).
    /// Hata olursa eski sürüm geri yüklenir ve UpdateFailedException atılır.
    /// </summary>
    public void ApplyUpdate(UpdatePackage pkg, byte[] content, Action<int>? progress = null, Func<bool>? installStep = null)
    {
        void Report(int p) => progress?.Invoke(p);
        Report(0);

        // 1) Checksum — bozuk paket kurulmaz (hiçbir değişiklik yapılmaz)
        if (!VerifyChecksum(content, pkg.ChecksumSha256))
        {
            Log($"Checksum hatası, kurulum iptal: {pkg.Version}");
            throw new UpdateFailedException("Paket checksum doğrulamasını geçemedi (bozuk paket).");
        }
        Report(30);

        var previousVersion = CurrentVersion();
        BackupCurrent();         // eski sürümü yedekle
        Report(50);

        try
        {
            // 2) Stage + kurulum (gerçekte dosya değişimi). Test installStep ile başarısızlık simüle edebilir.
            File.WriteAllBytes(Path.Combine(AppDir, "payload.bin"), content);
            Report(80);
            if (installStep is not null && !installStep())
                throw new InvalidOperationException("Kurulum adımı başarısız.");

            File.WriteAllText(CurrentFile, pkg.Version);
            Report(100);
            Log($"Güncelleme başarılı: {previousVersion} -> {pkg.Version}");
        }
        catch (Exception ex)
        {
            RestoreBackup();     // ROLLBACK: eski sürüme dön
            Log($"Güncelleme başarısız, eski sürüme dönüldü ({previousVersion}): {ex.Message}");
            throw new UpdateFailedException($"Güncelleme başarısız; {previousVersion} sürümüne dönüldü.", ex);
        }
    }

    private void BackupCurrent()
    {
        if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true);
        Directory.CreateDirectory(BackupDir);
        File.Copy(CurrentFile, Path.Combine(BackupDir, "current.txt"), true);
        foreach (var f in Directory.GetFiles(AppDir))
            File.Copy(f, Path.Combine(BackupDir, Path.GetFileName(f)), true);
    }

    private void RestoreBackup()
    {
        if (!Directory.Exists(BackupDir)) return;
        File.Copy(Path.Combine(BackupDir, "current.txt"), CurrentFile, true);
        // app/ dizinini yedekten tazele
        foreach (var f in Directory.GetFiles(AppDir)) File.Delete(f);
        foreach (var f in Directory.GetFiles(BackupDir))
        {
            var name = Path.GetFileName(f);
            if (name == "current.txt") continue;
            File.Copy(f, Path.Combine(AppDir, name), true);
        }
    }

    private void Log(string message)
    {
        try { File.AppendAllText(_logPath, $"{DateTimeOffset.UtcNow:O}\t{message}{Environment.NewLine}"); }
        catch { /* log hatası akışı durdurmaz */ }
    }
}
