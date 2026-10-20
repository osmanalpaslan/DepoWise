using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-48 (AR-GE 2026-10-19, 05 F-058) — EKİPMAN TAŞIMA HAREKETİ / KONUM GEÇMİŞİ ═══
/// <c>equipment_moves</c>: ekipman bir şubeden/şantiyeden diğerine taşındığında değişmez satır (kimden, kime, ne zaman, kim,
/// not). Kartın şube alanı aynı işlemde güncellenir; geçmiş SİLİNMEZ → "kayıpta son konum" ve "ne zaman taşındı" belli.
/// Boş doğar; yalnız CREATE TABLE.
/// </summary>
public sealed class Migration118_EquipmentMoves : IMigration
{
    public int Version => 118;
    public string Name => "equipment_moves";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.TableExists(conn, tx, "equipment_moves")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"
CREATE TABLE equipment_moves (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    equipment_id TEXT NOT NULL,
    from_branch_id TEXT NULL,
    to_branch_id TEXT NULL,
    from_location TEXT NULL,
    to_location TEXT NULL,
    move_date BIGINT NOT NULL,
    note TEXT NULL,
    operation_id TEXT NULL,
    created_by TEXT NULL,
    created_at BIGINT NOT NULL
);
CREATE INDEX ix_equipment_moves ON equipment_moves(company_id, equipment_id, move_date);";
        c.ExecuteNonQuery();
    }
}
