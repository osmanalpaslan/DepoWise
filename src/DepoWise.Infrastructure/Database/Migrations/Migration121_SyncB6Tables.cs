using System.Data.Common;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ AR-GE 2026-10-26 (M-31 hazırlığında bulundu) — B6 TABLOLARI EŞİTLEMEYE ═══
///
/// <b>Sorun.</b> B6'da eklenen tablolar (116 zimmet iade tarihi, 117 zimmet tutanağı, 118 ekipman taşıma geçmişi, 119 zimmet kiti,
/// 120 günlük kontrol + madde listesi) eşitleme listesinde (<see cref="BusinessSyncService.Tables"/>) YOKTU: masaüstünde girilen
/// kayıt yalnız o bilgisayarda kalıyor, web'de girilen masaüstüne inmiyordu. B6 testleri tek veritabanında koştuğu için görülmedi.
///
/// <b>Çözüm.</b> Tablolar eşitleme listesine eklendi. Bu migration yalnız ALTYAPI kurar (yalnız ekleme):
/// sunucu damgası <c>server_txid</c> (Migration099'un yaptığının aynısı — 099 bu tablolar yokken koştu) ve masaüstünde yerel
/// değişiklik sayacı (<see cref="Migration100_LocalChangeSeq.YerelSayacKur"/>). Mevcut satırlar bir kez damgalanır (ilk çekimde gelir);
/// PG'de damga TETİKLEYİCİDEN ÖNCE yazılır (099'da bulunan sıra hatası tekrarlanmasın). Sayım taslakları (110) BİLİNÇLİ olarak
/// eşitlenmez: taslak, sayımı yapan bilgisayardaki yarım iştir.
/// </summary>
public sealed class Migration121_SyncB6Tables : IMigration
{
    public int Version => 121;
    public string Name => "sync_b6_tables";

    /// <summary>Bu migration'ın eşitlemeye açtığı tablolar (BusinessSyncService.Tables ile aynı — test kilitler).</summary>
    public static readonly string[] Tablolar =
    {
        "assignment_due_dates", "assignment_receipts", "assignment_kits", "equipment_moves", "daily_check_templates", "daily_checks",
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var tablo in Tablolar)
        {
            if (!DbIntrospect.TableExists(conn, tx, tablo)) continue;
            SunucuDamgasiKur(conn, tx, tablo);
            Migration100_LocalChangeSeq.YerelSayacKur(conn, tx, tablo);
        }
    }

    /// <summary>Bir tabloya sunucu damgasını kurar (099 ile aynı sonuç). SQLite: kolon + bir kez damga 1 (indeks/tetikleyici yalnız
    /// sunucu rolünde, <see cref="Migration099_SyncTxid.SunucuRoluKur"/> açılışta listeye göre kurar). PG: kolon + indeks + damga +
    /// tetikleyici (099'un <c>dw_set_server_txid</c> işlevi).</summary>
    internal static void SunucuDamgasiKur(DbConnection conn, DbTransaction tx, string tablo)
    {
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }
        if (!DbIntrospect.ColumnExists(conn, tx, tablo, "server_txid"))
            Exec($"ALTER TABLE {tablo} ADD COLUMN server_txid BIGINT NULL;");
        if (SqlDialect.IsSqlite(conn))
        {
            Exec($"UPDATE {tablo} SET server_txid = 1 WHERE server_txid IS NULL;");
            return;
        }
        Exec($"CREATE INDEX IF NOT EXISTS ix_{tablo}_txid ON {tablo}(server_txid);");
        Exec($"UPDATE {tablo} SET server_txid = pg_current_xact_id()::text::bigint WHERE server_txid IS NULL;");   // tetikleyiciden ÖNCE
        Exec($"DROP TRIGGER IF EXISTS trg_{tablo}_txid ON {tablo};");
        Exec($"CREATE TRIGGER trg_{tablo}_txid BEFORE INSERT OR UPDATE ON {tablo} FOR EACH ROW EXECUTE FUNCTION dw_set_server_txid();");
    }
}
