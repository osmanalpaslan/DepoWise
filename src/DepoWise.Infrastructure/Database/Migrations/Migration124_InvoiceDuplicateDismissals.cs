using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-82 (AR-GE 2026-10-27, F-183) — "MÜKERRER DEĞİL" KARARLARI ═══
/// Olası mükerrer fatura listesinde kullanıcının "bu ikisi farklı faturalar" kararı. Firma genelidir ve iki ortamda aynı görünmeli →
/// eşitlenir (bilgisayara özel ayar DEĞİL). Kimlik <c>{küçük fatura id}:{büyük fatura id}</c> → iki bilgisayarda aynı karar kopya üretmez.
/// Boş doğar; yalnız CREATE TABLE (+ eşitleme altyapısı).
/// </summary>
public sealed class Migration124_InvoiceDuplicateDismissals : IMigration
{
    public int Version => 124;
    public string Name => "invoice_duplicate_dismissals";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.TableExists(conn, tx, "invoice_duplicate_dismissals"))
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = @"
CREATE TABLE invoice_duplicate_dismissals (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    invoice_a TEXT NOT NULL,
    invoice_b TEXT NOT NULL,
    created_by TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE INDEX ix_invoice_duplicate_dismissals ON invoice_duplicate_dismissals(company_id, is_deleted);";
            c.ExecuteNonQuery();
        }
        Migration121_SyncB6Tables.SunucuDamgasiKur(conn, tx, "invoice_duplicate_dismissals");
        Migration100_LocalChangeSeq.YerelSayacKur(conn, tx, "invoice_duplicate_dismissals");
    }
}
