using System.Data.Common;

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

    public void Up(DbConnection conn, DbTransaction tx)
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
    is_global BIGINT NOT NULL DEFAULT 0,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE INDEX ix_material_templates_company ON material_templates(company_id, is_deleted);");

        // 2) vehicle_templates: created_by + is_global (mevcutlar global=1)
        if (!ColumnExists(conn, tx, "vehicle_templates", "created_by"))
            Exec(conn, tx, "ALTER TABLE vehicle_templates ADD COLUMN created_by TEXT;");
        if (!ColumnExists(conn, tx, "vehicle_templates", "is_global"))
            Exec(conn, tx, "ALTER TABLE vehicle_templates ADD COLUMN is_global BIGINT NOT NULL DEFAULT 1;");
    }

    private static bool TableExists(DbConnection conn, DbTransaction tx, string table)
        => DbIntrospect.TableExists(conn, tx, table);

    private static bool ColumnExists(DbConnection conn, DbTransaction tx, string table, string column)
        => DbIntrospect.ColumnExists(conn, tx, table, column);

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
