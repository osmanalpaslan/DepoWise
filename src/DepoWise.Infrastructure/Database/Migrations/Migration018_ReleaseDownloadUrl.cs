using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Güncelleme paketinin indirme adresi (web'e yüklenen paket dosyası). Masaüstü bu URL'den paketi
/// indirir, checksum doğrular ve kurar. Additive — mevcut yayınlar NULL kalır (URL yoksa indirme yapılmaz).
/// </summary>
public sealed class Migration018_ReleaseDownloadUrl : IMigration
{
    public int Version => 18;
    public string Name => "app_releases.download_url";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE app_releases ADD COLUMN download_url TEXT NULL;";
        cmd.ExecuteNonQuery();
    }
}
