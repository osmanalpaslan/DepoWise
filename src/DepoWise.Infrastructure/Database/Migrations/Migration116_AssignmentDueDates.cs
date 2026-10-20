using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-46 (AR-GE 2026-10-19, 05 F-081) — ZİMMET BEKLENEN İADE TARİHİ ═══
/// <c>assignment_due_dates</c>: (personel, varlık) başına beklenen iade tarihi. Zimmet DEFTERİ değişmez kalır (satır güncellenmez);
/// beklenti ayrı bir üst bilgi olarak tutulur, "kimde ne var" hesabına eklenir → gecikmiş iade listesi ve uyarısı. Boş doğar.
/// </summary>
public sealed class Migration116_AssignmentDueDates : IMigration
{
    public int Version => 116;
    public string Name => "assignment_due_dates";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.TableExists(conn, tx, "assignment_due_dates")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"
CREATE TABLE assignment_due_dates (
    company_id TEXT NOT NULL,
    personnel_id TEXT NOT NULL,
    asset_type TEXT NOT NULL,
    asset_id TEXT NOT NULL,
    expected_return BIGINT NOT NULL,
    updated_by TEXT NULL,
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (company_id, personnel_id, asset_type, asset_id)
);";
        c.ExecuteNonQuery();
    }
}
