using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Sürüm/paket yönetimi (web yönetimi, süper admin): yayın kaydı + checksum + min desteklenen sürüm.
/// Bozuk/checksum'u tutmayan paket kurulmaz (updater doğrular).
/// </summary>
public sealed class Migration012_Releases : IMigration
{
    public int Version => 12;
    public string Name => "app_releases";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE app_releases (
    id TEXT PRIMARY KEY,
    version TEXT NOT NULL,
    checksum_sha256 TEXT NOT NULL,
    size_bytes INTEGER NOT NULL DEFAULT 0,
    min_supported_version TEXT NOT NULL DEFAULT '0.0.0',
    release_notes TEXT NULL,
    signed INTEGER NOT NULL DEFAULT 0,
    published_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_app_releases_version ON app_releases(version);";
        cmd.ExecuteNonQuery();
    }
}
