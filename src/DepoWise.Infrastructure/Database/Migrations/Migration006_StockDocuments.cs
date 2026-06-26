using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Stok belgeleri (giriş/çıkış/transfer/sayım) + hareket↔belge bağı + ters kayıt izi + sayım satırları.
/// Hareket ana kaynak; bakiye yalnız hareketle değişir. İptal = ters hareket (fiziksel silme yok).
/// </summary>
public sealed class Migration006_StockDocuments : IMigration
{
    public int Version => 6;
    public string Name => "stock_documents";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE stock_documents (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    doc_type TEXT NOT NULL,                 -- in | out | transfer | count
    doc_no TEXT NOT NULL,
    doc_date INTEGER NOT NULL,
    from_branch_id TEXT NULL,
    to_branch_id TEXT NULL,
    personnel_id TEXT NULL,
    vehicle_id TEXT NULL,                   -- vehicles FK Faz 08
    note TEXT NULL,
    status TEXT NOT NULL DEFAULT 'active',  -- active | cancelled
    group_id TEXT NULL,                     -- transfer çiftini gruplar
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE UNIQUE INDEX ux_stock_documents_no ON stock_documents(company_id, doc_type, doc_no);
CREATE INDEX ix_stock_documents_company ON stock_documents(company_id, doc_type, created_at);

-- stock_movements'a belge/grup/ters-kayıt bağlantısı
ALTER TABLE stock_movements ADD COLUMN document_id TEXT NULL;
ALTER TABLE stock_movements ADD COLUMN branch_from_id TEXT NULL;
ALTER TABLE stock_movements ADD COLUMN is_reversed INTEGER NOT NULL DEFAULT 0;
ALTER TABLE stock_movements ADD COLUMN reverses_movement_id TEXT NULL;

CREATE TABLE stock_count_lines (
    id TEXT PRIMARY KEY,
    document_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    system_qty TEXT NOT NULL,               -- sayım anındaki sistem bakiyesi (snapshot)
    counted_qty TEXT NOT NULL,
    diff_qty TEXT NOT NULL,
    reason TEXT NULL,
    FOREIGN KEY (document_id) REFERENCES stock_documents(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
CREATE INDEX ix_stock_count_lines_doc ON stock_count_lines(document_id);";
        cmd.ExecuteNonQuery();
    }
}
