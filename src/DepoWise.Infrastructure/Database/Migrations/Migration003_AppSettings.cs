using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// app_settings key/value tablosu — tema, branding ve dinamik alan ayarları.
/// company_id NULL = global varsayılan; dolu = firmaya özel override.
/// </summary>
public sealed class Migration003_AppSettings : IMigration
{
    public int Version => 3;
    public string Name => "app_settings";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE app_settings (
    id TEXT PRIMARY KEY,
    company_id TEXT NULL,
    setting_key TEXT NOT NULL,
    setting_value TEXT NULL,
    updated_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX ux_app_settings ON app_settings(COALESCE(company_id,''), setting_key);";
        cmd.ExecuteNonQuery();
    }
}
