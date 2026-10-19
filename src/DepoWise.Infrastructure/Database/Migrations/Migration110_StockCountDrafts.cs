using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-28 (AR-GE 2026-10-18, 05 F-075) — SAYIM TASLAĞI + FARK TOLERANSI ═══
/// <list type="bullet">
///   <item><c>stock_count_drafts</c>: yarım kalan sayım kaydedilip sonra sürdürülür (stoğa hiçbir şey yazmaz). Cihaza/sunucuya
///   yereldir (eşitlenmez) — taslak, kesinleşmemiş çalışma kâğıdıdır.</item>
///   <item><c>companies.count_tolerance_pct</c>: sayım farkı bu yüzdeyi aşan satır varsa sayımı yalnız yetkili (ters kayıt yetkisi)
///   kesinleştirir. NULL = kontrol yok (bugünkü davranış). Sunucu otoriteli; masaüstüne giriş paketiyle iner.</item>
/// </list>
/// Yalnız ekleme; boş doğar.
/// </summary>
public sealed class Migration110_StockCountDrafts : IMigration
{
    public int Version => 110;
    public string Name => "stock_count_drafts";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Calistir(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }
        if (!DbIntrospect.TableExists(conn, tx, "stock_count_drafts"))
            Calistir(@"
CREATE TABLE stock_count_drafts (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    branch_id TEXT NULL,
    created_by TEXT NULL,
    blind BIGINT NOT NULL DEFAULT 0,
    reason TEXT NULL,
    lines_json TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0
);");
        Calistir("CREATE INDEX IF NOT EXISTS ix_stock_count_drafts ON stock_count_drafts(company_id, is_deleted);");
        if (!DbIntrospect.ColumnExists(conn, tx, "companies", "count_tolerance_pct"))
            Calistir("ALTER TABLE companies ADD COLUMN count_tolerance_pct TEXT NULL;");
    }
}
