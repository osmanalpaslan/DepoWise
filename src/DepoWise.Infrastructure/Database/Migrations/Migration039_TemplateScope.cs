using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Yetki şablonları firma-kapsamlı: 'scope_all' kolonu (1 = TÜM firmalar; 0 = yalnız company_id firması).
/// Süper admin şablonu bir firmaya VEYA tüm firmalara oluşturabilir. Görünürlük: scope_all=1 VEYA
/// company_id = kullanıcının firması (ve kullanıcı-oluşturma yetkisi). Idempotent.
/// </summary>
public sealed class Migration039_TemplateScope : IMigration
{
    public int Version => 39;
    public string Name => "template_scope_all";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        if (ColumnExists(conn, tx, "permission_templates", "scope_all")) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE permission_templates ADD COLUMN scope_all INTEGER NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection conn, SqliteTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
