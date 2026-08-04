using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Files;

public sealed record BackupInfo(string Path, long SizeBytes, long CreatedAt);

/// <summary>
/// Masaüstü SQLite yedeği: tutarlı tek dosya (`VACUUM INTO`), 30 gün saklama, bütünlük kontrolü
/// (PRAGMA integrity_check) ve gerçek geri yükleme. Yedek klasörü uygulama dışı (Belgeler\Alpnex_Yedekler).
/// </summary>
public sealed class BackupService
{
    public const int RetentionDays = 30;
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly string _folder;

    public BackupService(IDbConnectionFactory factory, IClock? clock = null, string? backupFolder = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _folder = backupFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Alpnex_Yedekler");
        Directory.CreateDirectory(_folder);
    }

    public string GetBackupFolder() => _folder;

    /// <summary>Tutarlı yedek alır (VACUUM INTO); eski yedekleri (30 gün) temizler. Yedek yolunu döndürür.</summary>
    public string Backup()
    {
        var date = _clock.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var path = Path.Combine(_folder, $"depowise_yedek_{date}.db");
        if (File.Exists(path)) File.Delete(path);

        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "VACUUM INTO @p;";
            cmd.AddWithValue("@p", path);
            cmd.ExecuteNonQuery();
        }
        PurgeOld();
        return path;
    }

    /// <summary>Yedek dosyasının bütünlüğünü doğrular (integrity_check = ok).</summary>
    public bool IntegrityCheck(string backupPath)
    {
        if (!File.Exists(backupPath)) return false;
        var cs = new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return string.Equals(cmd.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Yedeği canlı DB üzerine geri yükler (-wal/-shm temizlenir). Admin + reauth zorunlu.</summary>
    public void Restore(SessionContext s, string backupPath, bool reauthenticated)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Geri yükleme yalnız admin yetkisindedir.");
        if (!reauthenticated) throw new ForbiddenException("Geri yükleme için yeniden kimlik doğrulama gerekli.");
        if (!IntegrityCheck(backupPath)) throw new InvalidOperationException("Yedek bütünlük kontrolünden geçemedi.");

        var target = _factory.DatabasePath;
        // Bağlantı havuzunu boşalt → dosya kilidi kalmaz (aksi halde File.Copy "kullanımda" hatası)
        SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "-wal", "-shm" })
            if (File.Exists(target + ext)) File.Delete(target + ext);
        File.Copy(backupPath, target, overwrite: true);
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        return Directory.GetFiles(_folder, "depowise_yedek_*.db")
            .Select(p => new FileInfo(p))
            .Select(fi => new BackupInfo(fi.FullName, fi.Length, new DateTimeOffset(fi.CreationTimeUtc).ToUnixTimeMilliseconds()))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    private void PurgeOld()
    {
        var cutoff = _clock.UtcNow.AddDays(-RetentionDays);
        foreach (var p in Directory.GetFiles(_folder, "depowise_yedek_*.db"))
        {
            try { if (File.GetCreationTimeUtc(p) < cutoff.UtcDateTime) File.Delete(p); }
            catch { /* yoksay */ }
        }
    }
}
