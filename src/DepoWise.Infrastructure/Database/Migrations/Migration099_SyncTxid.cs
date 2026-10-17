using System.Data.Common;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-03 (AR-GE 2026-10-17, B1 adım 2) — SUNUCU TARAFLI EŞİTLEME İMLECİ: İŞLEM KİMLİĞİ UFKU ═══
///
/// <b>Sorun (07 §2 R1, K3, K4; canlı ölçüm 07 §7).</b> Çekme imleci istemci saatli <c>updated_at</c>'e dayanıyordu:
/// çevrimdışı makinenin geç gelen (geriye damgalı) satırı başka makinelerin imlecinin gerisinde kalıp HİÇ inmeyebiliyordu;
/// saati yanlış makine imleci bozuyordu. Ayrıca istemcideki imleç hiç atanmadığı için her tur tam paket iniyordu
/// (canlıda ≈4–6 GB/ay veritabanı trafiği).
///
/// <b>Çözüm — değişiklik yakalamada kanıtlanmış "işlem kimliği ufku" yöntemi.</b> Her eşitlenen satıra, onu SON YAZAN
/// veritabanı işleminin kimliği (<c>server_txid</c>) yazılır (tetikleyici). Çekme "önceki ufkumdan bu yana BİTMİŞ
/// işlemlerin yazdığı her şey" diye sorar: <c>server_txid &gt;= önceki_ufuk AND server_txid &lt; şu_anki_ufuk</c>.
/// PostgreSQL'de ufuk = <c>pg_snapshot_xmin(pg_current_snapshot())</c> (hâlâ süren en eski işlem) → süren uzun bir
/// işlemin satırları bir sonraki tura kalır, ASLA atlanmaz; saat ve damga artık önemli değildir; hiçbir yazma kilitlenmez.
/// SQLite'ta (yalnız sunucu rolünde: testler / API'nin SQLite yedek modu) yazımlar zaten sıralı olduğundan artan bir sayaç
/// aynı garantiyi verir.
///
/// <b>Masaüstü güvenliği:</b> migration'lar masaüstü SQLite'ına da uygulanır. SQLite tetikleyicileri yalnız
/// <c>sync_server_role</c> satırı VARSA çalışır; bu satırı yalnız API (ServerServices) oluşturur → masaüstünde tetikleyiciler
/// etkisizdir, çekilen satırın sunucu değeri olduğu gibi saklanır.
///
/// <b>Canlı veri güvenliği:</b> yalnız ekleme (ADD COLUMN NULL, CREATE INDEX/TRIGGER/FUNCTION/TABLE). Mevcut satırlar bir kez
/// damgalanır (PG: bu migration'ın işlem kimliği; SQLite: 1) → ilk v2 çekiminde hepsi gelir. Eski uç (<c>business-pull</c>)
/// ve eski istemciler değişmeden çalışır (kolon kesişimiyle taşınır).
/// </summary>
public sealed class Migration099_SyncTxid : IMigration
{
    public int Version => 99;
    public string Name => "sync_txid";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        var sqlite = SqlDialect.IsSqlite(conn);
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        if (sqlite)
        {
            Exec("CREATE TABLE IF NOT EXISTS sync_server_role (id INTEGER PRIMARY KEY CHECK (id = 1));");
            Exec("CREATE TABLE IF NOT EXISTS sync_txid_counter (id INTEGER PRIMARY KEY CHECK (id = 1), next INTEGER NOT NULL);");
            Exec("INSERT OR IGNORE INTO sync_txid_counter(id, next) VALUES (1, 2);");   // 1 = migration öncesi satırlar
        }
        else
        {
            Exec("""
                CREATE OR REPLACE FUNCTION dw_set_server_txid() RETURNS trigger AS $$
                BEGIN
                    -- Değeri hiç değişmeyen güncelleme (ör. masaüstünün aynı satırı aynen yeniden göndermesi) satırı
                    -- "değişti" saydırmaz → bütün bilgisayarlar onu boşuna yeniden indirmez.
                    IF TG_OP = 'UPDATE' AND (to_jsonb(NEW) - 'server_txid') = (to_jsonb(OLD) - 'server_txid') THEN
                        NEW.server_txid := OLD.server_txid;
                        RETURN NEW;
                    END IF;
                    NEW.server_txid := pg_current_xact_id()::text::bigint;
                    RETURN NEW;
                END
                $$ LANGUAGE plpgsql;
                """);
        }

        foreach (var table in BusinessSyncService.Tables)
        {
            if (!DbIntrospect.TableExists(conn, tx, table)) continue;
            if (BusinessSyncService.YalnizGonderim.Contains(table)) continue;   // M-06: sunucudan inmeyen tabloda ufuk gerekmez
            if (!DbIntrospect.ColumnExists(conn, tx, table, "server_txid"))
                Exec($"ALTER TABLE {table} ADD COLUMN server_txid BIGINT NULL;");

            // Yalnız server_txid: delta aralığı dar olduğundan yeterli; company_id'yi indekse KATMAK, Migration062'nin
            // belgelenmiş geri alma adımını (company_id kolonunu düşürme) bozuyordu (CompanyIdMigrationTests yakaladı).
            Exec($"CREATE INDEX IF NOT EXISTS ix_{table}_txid ON {table}(server_txid);");

            if (sqlite)
            {
                // Bir kez damga (rol satırı migration sırasında YOK → tetikleyici yazmaz; sabit 1 = "ilk çekimde gelsin").
                Exec($"UPDATE {table} SET server_txid = 1 WHERE server_txid IS NULL;");
                var govde = $"BEGIN UPDATE sync_txid_counter SET next = next + 1 WHERE id = 1; " +
                            $"UPDATE {table} SET server_txid = (SELECT next - 1 FROM sync_txid_counter WHERE id = 1) WHERE rowid = NEW.rowid; END;";
                Exec($"CREATE TRIGGER IF NOT EXISTS trg_{table}_txid_ins AFTER INSERT ON {table} " +
                     $"WHEN EXISTS (SELECT 1 FROM sync_server_role) {govde}");
                Exec($"CREATE TRIGGER IF NOT EXISTS trg_{table}_txid_upd AFTER UPDATE ON {table} " +
                     $"WHEN EXISTS (SELECT 1 FROM sync_server_role) {govde}");
            }
            else
            {
                Exec($"DROP TRIGGER IF EXISTS trg_{table}_txid ON {table};");
                Exec($"CREATE TRIGGER trg_{table}_txid BEFORE INSERT OR UPDATE ON {table} " +
                     "FOR EACH ROW EXECUTE FUNCTION dw_set_server_txid();");
                Exec($"UPDATE {table} SET server_txid = pg_current_xact_id()::text::bigint WHERE server_txid IS NULL;");
            }
        }
    }
}
