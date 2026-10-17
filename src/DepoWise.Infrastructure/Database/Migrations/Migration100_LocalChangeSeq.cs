using System.Data.Common;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-03 (AR-GE 2026-10-17, B1 adım 3) — GÖNDERME İMLECİ: YEREL DEĞİŞİKLİK SAYACI (yalnız masaüstü SQLite) ═══
///
/// <b>Sorun (05 F-032, A3a B2-1, A3b K4).</b> Masaüstünün "neyi gönderdim" imleci istemci saatli <c>updated_at</c>'ti
/// (<c>sync_push_watermark</c> = yerel en büyük damga). Bilgisayarın saati geri alınırsa (pil, elle düzeltme, saat dilimi
/// hatası) yeni kaydın damgası imlecin GERİSİNDE kalır ve kayıt sunucuya HİÇ gönderilmez — sessiz veri kaybı. Ayrıca
/// sunucudan inen satırlar da damgaları yeni olduğu için bir sonraki gönderimde sunucuya geri yollanıyordu (yankı trafiği).
///
/// <b>Çözüm.</b> Her eşitlenen tabloya <c>local_change_seq</c>: bu makinedeki her YEREL yazımda artan bir sayaçtan numara
/// alır (tetikleyici). İmleç = sunucunun kabul ettiği son numara → saat önemsizdir. Çekme (ApplyPull) uygulanırken
/// <c>sync_apply_guard</c> satırı aynı işlem içinde durur; tetikleyici o sırada çalışmaz → inen satır yeniden gönderilmez.
///
/// <b>Kapsam:</b> yalnız sunucu rolü OLMAYAN SQLite (masaüstü). Sunucu rolünde (sync_server_role) tetikleyici etkisizdir;
/// PostgreSQL'de bu migration hiçbir şey yapmaz (sunucunun gönderme imleci yoktur).
/// <b>Canlı veri güvenliği:</b> yalnız ekleme; mevcut satırlara dokunulmaz (NULL kalır). Geçiş, istemcideki tek seferlik
/// "eski damga imleci + yeni sayaç" birleşik gönderimiyle yapılır (BusinessSyncPushService) → bekleyen kayıt kaybolmaz.
/// </summary>
public sealed class Migration100_LocalChangeSeq : IMigration
{
    public int Version => 100;
    public string Name => "local_change_seq";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!SqlDialect.IsSqlite(conn)) return;
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        Exec("CREATE TABLE IF NOT EXISTS sync_local_counter (id INTEGER PRIMARY KEY CHECK (id = 1), next INTEGER NOT NULL);");
        Exec("INSERT OR IGNORE INTO sync_local_counter(id, next) VALUES (1, 1);");
        // Çekme uygulanırken (aynı işlem içinde) dolu; o sırada yerel sayaç tetikleyicisi çalışmaz (yankı kesme).
        Exec("CREATE TABLE IF NOT EXISTS sync_apply_guard (id INTEGER PRIMARY KEY CHECK (id = 1));");

        foreach (var table in BusinessSyncService.Tables)
        {
            if (!DbIntrospect.TableExists(conn, tx, table)) continue;
            if (!DbIntrospect.ColumnExists(conn, tx, table, "local_change_seq"))
                Exec($"ALTER TABLE {table} ADD COLUMN local_change_seq INTEGER NULL;");
            Exec($"CREATE INDEX IF NOT EXISTS ix_{table}_lseq ON {table}(local_change_seq);");

            var kosul = "WHEN NOT EXISTS (SELECT 1 FROM sync_server_role) AND NOT EXISTS (SELECT 1 FROM sync_apply_guard)";
            var govde = $"BEGIN UPDATE sync_local_counter SET next = next + 1 WHERE id = 1; " +
                        $"UPDATE {table} SET local_change_seq = (SELECT next - 1 FROM sync_local_counter WHERE id = 1) WHERE rowid = NEW.rowid; END;";
            Exec($"CREATE TRIGGER IF NOT EXISTS trg_{table}_lseq_ins AFTER INSERT ON {table} {kosul} {govde}");
            Exec($"CREATE TRIGGER IF NOT EXISTS trg_{table}_lseq_upd AFTER UPDATE ON {table} {kosul} {govde}");
        }
    }
}
