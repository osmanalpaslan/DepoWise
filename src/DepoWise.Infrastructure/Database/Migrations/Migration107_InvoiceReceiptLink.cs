using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-25 (AR-GE 2026-10-18, 05 F-072 / F-148) — FATURA ↔ MAL KABUL BAĞI ═══
/// <c>invoices.receipt_document_id</c>: alış faturasının oluşturulduğu siparişli mal kabul belgesi (stok giriş belgesi).
/// NULL = bağsız fatura (eski davranış). Bağlı faturada stok YAZILMAZ (mal kabulle girdi) → çift stok kökten biter; aynı
/// kabul ikinci kez faturalanamaz, faturalanan miktar kabulden fazla olamaz (servis kontrolü). Yalnız ekleme.
/// </summary>
public sealed class Migration107_InvoiceReceiptLink : IMigration
{
    public int Version => 107;
    public string Name => "invoice_receipt_link";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.ColumnExists(conn, tx, "invoices", "receipt_document_id"))
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "ALTER TABLE invoices ADD COLUMN receipt_document_id TEXT NULL;";
            c.ExecuteNonQuery();
        }
        using var i = conn.CreateCommand();
        i.Transaction = tx;
        i.CommandText = "CREATE INDEX IF NOT EXISTS ix_invoices_receipt ON invoices(company_id, receipt_document_id);";
        i.ExecuteNonQuery();
    }
}
