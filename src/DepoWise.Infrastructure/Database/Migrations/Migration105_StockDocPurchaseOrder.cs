using System.Data.Common;
using System.Text.RegularExpressions;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-32 (AR-GE 2026-10-18, 05 F-113) — MAL KABUL BELGESİ ↔ SİPARİŞ BAĞI ═══
///
/// <b>Sorun.</b> Satın alma mal kabulü stok belgesi üretiyordu ama belge hangi siparişten geldiğini TUTMUYORDU. Stok ekranı bu
/// belgenin düzeltmesini "Satın Alma ekranından yapın" diye reddediyor, Satın Alma ekranında ise kabulü geri alma yoktu; stok
/// ekranından doğrudan iptal edilirse siparişin "teslim alınan" miktarı düşmüyordu (sipariş ile defter ayrışıyordu).
///
/// <b>Çözüm.</b> <c>stock_documents.purchase_order_id</c> (yalnız ekleme, NULL). Yeni kabuller bağı yazar; mevcut kabuller,
/// işlem geçmişindeki kabul kaydından (<c>purchase_order</c> + <c>{"receive":"po:…","doc":"&lt;belge&gt;"}</c>) eşlenir.
/// Eşlenemeyen eski belge NULL kalır (eski davranış). Tek tarama + belge başına tek UPDATE (büyük geçmişte de hızlı).
/// </summary>
public sealed class Migration105_StockDocPurchaseOrder : IMigration
{
    public int Version => 105;
    public string Name => "stock_doc_purchase_order";

    private static readonly Regex BelgeRe = new("\"doc\":\"([^\"]+)\"", RegexOptions.CultureInvariant);

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Exec(string sql)
        {
            using var c = conn.CreateCommand(); c.Transaction = tx; c.CommandText = sql; c.ExecuteNonQuery();
        }
        if (!DbIntrospect.ColumnExists(conn, tx, "stock_documents", "purchase_order_id"))
            Exec("ALTER TABLE stock_documents ADD COLUMN purchase_order_id TEXT NULL;");
        Exec("CREATE INDEX IF NOT EXISTS ix_stock_documents_po ON stock_documents(company_id, purchase_order_id);");

        if (!DbIntrospect.TableExists(conn, tx, "audit_logs")) return;
        var eslesme = new List<(string Belge, string Siparis, string Firma)>();
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT entity_id, company_id, after_json FROM audit_logs WHERE entity_type='purchase_order' " +
                            "AND after_json LIKE '%\"receive\":\"po:%';";
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(2)) continue;
                var m = BelgeRe.Match(r.GetString(2));
                if (m.Success) eslesme.Add((m.Groups[1].Value, r.GetString(0), r.GetString(1)));
            }
        }
        foreach (var (belge, siparis, firma) in eslesme)
        {
            using var u = conn.CreateCommand();
            u.Transaction = tx;
            u.CommandText = "UPDATE stock_documents SET purchase_order_id=@p WHERE id=@d AND company_id=@c AND purchase_order_id IS NULL AND doc_type='in';";
            u.AddWithValue("@p", siparis);
            u.AddWithValue("@d", belge);
            u.AddWithValue("@c", firma);
            u.ExecuteNonQuery();
        }
    }
}
