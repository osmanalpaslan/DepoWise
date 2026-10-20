using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-35 (AR-GE 2026-10-19, 05 F-077) — ARIZA → İŞ EMRİ BAĞI ═══
/// <c>work_orders.fault_vehicle_id</c>: iş emri bir ARIZA bildiriminden açıldıysa arızalı araç. Bildirim anı = iş emrinin
/// <c>created_at</c>'ı; tamamlanınca arıza süresi ölçülür ve araç (hâlâ Arızalı ise) "Aktif"e döner. NULL = normal iş emri.
/// Yalnız ekleme.
/// </summary>
public sealed class Migration111_WorkOrderFault : IMigration
{
    public int Version => 111;
    public string Name => "work_order_fault";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.ColumnExists(conn, tx, "work_orders", "fault_vehicle_id"))
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "ALTER TABLE work_orders ADD COLUMN fault_vehicle_id TEXT NULL;";
            c.ExecuteNonQuery();
        }
        using var i = conn.CreateCommand();
        i.Transaction = tx;
        i.CommandText = "CREATE INDEX IF NOT EXISTS ix_work_orders_fault ON work_orders(company_id, fault_vehicle_id);";
        i.ExecuteNonQuery();
    }
}
