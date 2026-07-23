using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Malzeme talep başlığı + kalemleri + durum geçmişi. Talep belgedir; onay STOK DÜŞÜRMEZ.
/// Belge no TLP-YYYY-NNNN (tenant/yıl benzersiz).
/// </summary>
public sealed class Migration010_Requests : IMigration
{
    public int Version => 10;
    public string Name => "material_requests";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE material_requests (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    doc_no TEXT NOT NULL,
    request_date BIGINT NOT NULL,
    branch_id TEXT NULL,
    requester_id TEXT NULL,
    warehouse_id TEXT NULL,
    approver_id TEXT NULL,
    description TEXT NULL,
    status TEXT NOT NULL DEFAULT 'draft',     -- draft|pending|approved|rejected|cancelled
    approved_by TEXT NULL,
    approved_at BIGINT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE UNIQUE INDEX ux_material_requests_no ON material_requests(company_id, doc_no);
CREATE INDEX ix_material_requests ON material_requests(company_id, status, created_at);

CREATE TABLE material_request_items (
    id TEXT PRIMARY KEY,
    request_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL,                   -- decimal
    vehicle_id TEXT NULL,
    note TEXT NULL,
    FOREIGN KEY (request_id) REFERENCES material_requests(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
CREATE INDEX ix_material_request_items ON material_request_items(request_id);

CREATE TABLE request_status_history (
    id TEXT PRIMARY KEY,
    request_id TEXT NOT NULL,
    from_status TEXT NULL,
    to_status TEXT NOT NULL,
    by_user TEXT NULL,
    reason TEXT NULL,
    created_at BIGINT NOT NULL,
    FOREIGN KEY (request_id) REFERENCES material_requests(id)
);
CREATE INDEX ix_request_status_history ON request_status_history(request_id, created_at);";
        cmd.ExecuteNonQuery();
    }
}
