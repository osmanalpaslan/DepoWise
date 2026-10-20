using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-37 (AR-GE 2026-10-19, 05 F-079) — BAKIM PLANINDA EK TARİH PERİYODU ═══
/// <c>maintenance_definitions.interval_days</c> (decimal metin, NULL = yok): km/saat tanımına ek olarak gün periyodu
/// (ör. 10.000 km YA DA 365 gün). Uyarı hangi ölçüt önce dolarsa onunla tetiklenir. Yalnız ekleme.
/// </summary>
public sealed class Migration113_MaintenanceIntervalDays : IMigration
{
    public int Version => 113;
    public string Name => "maintenance_interval_days";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.ColumnExists(conn, tx, "maintenance_definitions", "interval_days")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "ALTER TABLE maintenance_definitions ADD COLUMN interval_days TEXT NULL;";
        c.ExecuteNonQuery();
    }
}
