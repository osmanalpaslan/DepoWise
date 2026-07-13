using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Malzeme yeni-kayıt şablonları (Araç şablonuna benzer) + şablon görünürlüğü OLUŞTURAN bazlı:
/// - vehicle_templates ve material_templates'e created_by + is_global eklenir.
/// - is_global=1 (admin şablonu) → firmada herkese görünür; is_global=0 → yalnız created_by kullanıcıya.
/// - Mevcut araç şablonları is_global=1 sayılır (geriye dönük: herkese görünür kalsın).
/// Idempotent.
/// </summary>
public sealed class Migration040_MaterialTemplates : IMigration
{
    public int Version => 40;
    public string Name => "material_templates";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        // 1) material_templates tablosu
        if (!TableExists(conn, tx, "material_templates"))
            Exec(conn, tx, @"
CREATE TABLE material_templates (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    code TEXT,
    type TEXT,
    category_id TEXT,
    unit_id TEXT,
    brand_id TEXT,
    supplier_id TEXT,
    min_stock TEXT NOT NULL DEFAULT '0',
    unit_price TEXT NOT NULL DEFAULT '0',
    currency TEXT NOT NULL DEFAULT 'TRY',
    description TEXT,
    created_by TEXT,
    is_global INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_material_templates_company ON material_templates(company_id, is_deleted);");

        // 2) vehicle_templates: created_by + is_global (mevcutlar global=1)
        if (!ColumnExists(conn, tx, "vehicle_templates", "created_by"))
            Exec(conn, tx, "ALTER TABLE vehicle_templates ADD COLUMN created_by TEXT;");
        if (!ColumnExists(conn, tx, "vehicle_templates", "is_global"))
            Exec(conn, tx, "ALTER TABLE vehicle_templates ADD COLUMN is_global INTEGER NOT NULL DEFAULT 1;");
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n;";
        cmd.Parameters.AddWithValue("$n", table);
        return System.Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqliteConnection conn, SqliteTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
