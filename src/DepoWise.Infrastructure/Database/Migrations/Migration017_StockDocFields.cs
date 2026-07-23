using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Stok belgesine evrak alanları: Fatura/İrsaliye No, Sipariş Fişi No, Veresiye Fişi No (Malzeme Giriş-Çıkış).
/// </summary>
public sealed class Migration017_StockDocFields : IMigration
{
    public int Version => 17;
    public string Name => "stock_doc_fields";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE stock_documents ADD COLUMN invoice_no TEXT NULL;
ALTER TABLE stock_documents ADD COLUMN order_slip_no TEXT NULL;
ALTER TABLE stock_documents ADD COLUMN credit_slip_no TEXT NULL;";
        cmd.ExecuteNonQuery();
    }
}
