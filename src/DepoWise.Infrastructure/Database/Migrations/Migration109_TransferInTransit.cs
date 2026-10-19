using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-27 (AR-GE 2026-10-18, 05 F-070) — İKİ ADIMLI TRANSFER ("YOLDA") ═══
/// <c>stock_documents.transit_status</c>: NULL = tek adımlı (eski) transfer · <c>yolda</c> = gönderildi, teslim bekliyor ·
/// <c>eksik</c> = kısmen teslim alındı · <c>teslim</c> = tamamı teslim alındı · <c>kapandi</c> = eksik kısım kayıp olarak kapatıldı.
/// <c>received_at</c>: son teslim anı. Yoldaki mal stok defterinde ayrı lokasyondadır (<c>StockBalanceWriter.Transit</c>) →
/// firma toplamı değişmez, kaynak ve hedef depo doğru görünür, kayıp fark edilir. Yalnız ekleme.
/// </summary>
public sealed class Migration109_TransferInTransit : IMigration
{
    public int Version => 109;
    public string Name => "transfer_in_transit";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Ekle(string kolon, string tip)
        {
            if (DbIntrospect.ColumnExists(conn, tx, "stock_documents", kolon)) return;
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = $"ALTER TABLE stock_documents ADD COLUMN {kolon} {tip} NULL;";
            c.ExecuteNonQuery();
        }
        Ekle("transit_status", "TEXT");
        Ekle("received_at", "BIGINT");
        using var i = conn.CreateCommand();
        i.Transaction = tx;
        i.CommandText = "CREATE INDEX IF NOT EXISTS ix_stock_documents_transit ON stock_documents(company_id, transit_status);";
        i.ExecuteNonQuery();
    }
}
