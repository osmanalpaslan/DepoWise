using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-26 (AR-GE 2026-10-18, 05 F-071) — STOK BELGESİ ↔ TALEP BAĞI ═══
/// <c>stock_documents.request_id</c>: onaylı talepten "Stoktan Gönder" ile oluşan transfer belgesi hangi talebi karşıladı.
/// Talebin "gönderilen" miktarı bu bağdan hesaplanır (iptal edilen belge sayılmaz). NULL = bağsız belge. Yalnız ekleme.
/// </summary>
public sealed class Migration108_StockDocRequest : IMigration
{
    public int Version => 108;
    public string Name => "stock_doc_request";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.ColumnExists(conn, tx, "stock_documents", "request_id"))
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "ALTER TABLE stock_documents ADD COLUMN request_id TEXT NULL;";
            c.ExecuteNonQuery();
        }
        using var i = conn.CreateCommand();
        i.Transaction = tx;
        i.CommandText = "CREATE INDEX IF NOT EXISTS ix_stock_documents_request ON stock_documents(company_id, request_id);";
        i.ExecuteNonQuery();
    }
}
