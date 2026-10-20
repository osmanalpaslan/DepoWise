using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-50 (AR-GE 2026-10-19, 05 F-083) — ZİMMET KİT (SET) ŞABLONU ═══
/// <c>assignment_kits</c>: "yeni işçi seti", "kaynakçı seti" gibi adlandırılmış kalem listesi (JSON: tür, kayıt, miktar).
/// Kit yalnız ŞABLONDUR — stok/zimmet hareketi yazmaz; teslimde kalemler mevcut toplu teslim listesine yüklenir ve teslim
/// her zamanki tek işlemli yoldan (AssignmentService.IssueMany) geçer. Boş doğar; yalnız CREATE TABLE.
/// </summary>
public sealed class Migration119_AssignmentKits : IMigration
{
    public int Version => 119;
    public string Name => "assignment_kits";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.TableExists(conn, tx, "assignment_kits")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"
CREATE TABLE assignment_kits (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    lines_json TEXT NOT NULL,
    created_by TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    is_deleted INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_assignment_kits ON assignment_kits(company_id, is_deleted, name);";
        c.ExecuteNonQuery();
    }
}
