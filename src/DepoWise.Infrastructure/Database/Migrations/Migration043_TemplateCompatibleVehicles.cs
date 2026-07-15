using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Malzeme şablonuna "uyumlu araçlar" alanı (madde 12): material_templates.compatible_vehicle_ids
/// (virgülle ayrılmış araç id listesi; boş = yok). Şablon fotoğrafları ayrı file_records ile taşınır
/// (entity_type = material_template / vehicle_template) — şema gerektirmez. Idempotent.
/// </summary>
public sealed class Migration043_TemplateCompatibleVehicles : IMigration
{
    public int Version => 43;
    public string Name => "template_compatible_vehicles";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        if (!ColumnExists(conn, tx, "material_templates", "compatible_vehicle_ids"))
            Exec(conn, tx, "ALTER TABLE material_templates ADD COLUMN compatible_vehicle_ids TEXT;");
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

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
