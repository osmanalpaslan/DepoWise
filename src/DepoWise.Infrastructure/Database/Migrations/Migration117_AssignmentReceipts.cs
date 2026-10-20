using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-47 (AR-GE 2026-10-19, 05 F-082) — ZİMMET KABUL TUTANAĞI ═══
/// <c>assignment_receipts</c>: kişi bazlı zimmet tutanağı. Oluşturulduğu andaki zimmet listesi DONDURULUR
/// (<c>snapshot_json</c>) — sonradan teslim/iade olsa da tutanak değişmez ("almadım" itirazına kanıt). Durum:
/// <c>bekliyor</c> → <c>kabul</c> | <c>ret</c> (gerekçeli). Boş doğar; yalnız CREATE TABLE.
/// </summary>
public sealed class Migration117_AssignmentReceipts : IMigration
{
    public int Version => 117;
    public string Name => "assignment_receipts";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.TableExists(conn, tx, "assignment_receipts")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"
CREATE TABLE assignment_receipts (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    receipt_no TEXT NOT NULL,
    personnel_id TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'bekliyor',
    snapshot_json TEXT NOT NULL,
    created_by TEXT NULL,
    created_at BIGINT NOT NULL,
    decided_by TEXT NULL,
    decided_at BIGINT NULL,
    decision_note TEXT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE INDEX ix_assignment_receipts ON assignment_receipts(company_id, personnel_id, is_deleted);";
        c.ExecuteNonQuery();
    }
}
