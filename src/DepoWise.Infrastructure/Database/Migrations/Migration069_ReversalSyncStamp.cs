using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// SNK-A1 / SNK-A2 (denetim 2026-08-18) — <b>İPTAL İŞLEMİ SENKRONA HİÇ GİRMİYORDU.</b>
///
/// <b>KÖK NEDEN.</b> Senkron deltası bir ZAMAN DAMGASI üzerinden hesaplanır
/// (<c>BusinessSyncService.StampColumn</c>): önce <c>updated_at</c>, yoksa <c>created_at</c>.
/// Bu üç tabloda <c>updated_at</c> KOLONU YOKTU → damga <c>created_at</c>'e düşüyordu. İptal ise
/// var olan satırı <b>yerinde güncelliyor</b> (<c>is_reversed=1</c> / <c>status='cancelled'</c>) ve
/// <c>created_at</c> değişmiyor → <b>güncelleme push'a HİÇ girmiyordu.</b>
///
/// <b>SOMUT SONUÇLARI.</b>
/// <list type="number">
///   <item><b>party_ledger (bakiye YANLIŞ):</b> cari bakiyesi <c>WHERE is_reversed=0</c> ile hesaplanır.
///     Masaüstünde iptal edilen hareketin bayrağı sunucuya gitmediği için asıl kayıt sunucuda hâlâ
///     <c>is_reversed=0</c> kalıyor ve bakiyeye giriyordu → <b>masaüstünde iptal edilen borç web'de
///     duruyordu.</b></item>
///   <item><b>stock_movements / stock_documents:</b> bakiye BOZULMUYORDU (sunucudaki
///     <c>RecomputeBalances</c> tüm hareketleri toplar ve ters kayıt satırı senkronla gidiyor), ama
///     web'de iptal edilmiş belge <b>hâlâ aktif</b> görünüyor ve <b>ikinci kez iptal edilebiliyordu</b>
///     (defterde gereksiz ters kayıtlar).</item>
/// </list>
///
/// <b>ÇÖZÜM.</b> Üç tabloya <c>updated_at</c> eklenir ve mevcut satırlarda <c>created_at</c> ile
/// doldurulur. Böylece:
/// • Damga <c>created_at</c>'ten <c>updated_at</c>'e geçer ama <b>değerler aynı olduğu için mevcut
///   delta davranışı BİREBİR korunur</b> (hiçbir satır kaybolmaz/tekrar gönderilmez).
/// • İptal/durum değişikliği artık damgayı tazeler → güncelleme normal delta ile taşınır.
/// İkinci bir senkron mekanizması KURULMADI; mevcut sözleşme olduğu gibi kullanıldı.
///
/// <b>Geriye uyumluluk:</b> yalnız kolon EKLENİR; hiçbir kolon/satır silinmez, tip değişmez.
/// Eski sürüm bir istemci bu kolonu görmezden gelir (SELECT * ile okunur, upsert kolon kesişimiyle çalışır).
/// İdempotent: kolon varsa hiçbir şey yapılmaz. İki lehçede de aynı (portable SQL).
/// </summary>
public sealed class Migration069_ReversalSyncStamp : IMigration
{
    public int Version => 69;
    public string Name => "reversal_sync_stamp";

    /// <summary>Damgası eksik olan ve YERİNDE GÜNCELLENEN iş tabloları.</summary>
    private static readonly string[] Tables = { "party_ledger", "stock_movements", "stock_documents" };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var table in Tables)
        {
            if (!DbIntrospect.TableExists(conn, tx, table)) continue;
            if (DbIntrospect.ColumnExists(conn, tx, table, "updated_at")) continue;   // idempotent

            using (var add = conn.CreateCommand())
            {
                add.Transaction = tx;
                // NULL yapılabilir eklenir; hemen ardından created_at ile doldurulur.
                // (SQLite'ta NOT NULL + varsayılansız ADD COLUMN yasaktır; iki adım iki lehçede de güvenli.)
                add.CommandText = $"ALTER TABLE {table} ADD COLUMN updated_at BIGINT NULL;";
                add.ExecuteNonQuery();
            }
            using (var fill = conn.CreateCommand())
            {
                fill.Transaction = tx;
                // ⭐ KRİTİK: created_at ile doldurulur. Damga created_at'ten updated_at'e geçtiğinde
                // delta penceresi AYNI kalsın diye — aksi halde tüm geçmiş "yeni değişmiş" sayılıp
                // ilk eşitlemede baştan gönderilirdi.
                fill.CommandText = $"UPDATE {table} SET updated_at = created_at WHERE updated_at IS NULL;";
                fill.ExecuteNonQuery();
            }
            using (var ix = conn.CreateCommand())
            {
                ix.Transaction = tx;
                // Delta sorgusu "updated_at > @since" ile tarar → indeks olmadan tam tablo taraması olurdu.
                ix.CommandText = $"CREATE INDEX IF NOT EXISTS ix_{table}_updated ON {table}(updated_at);";
                ix.ExecuteNonQuery();
            }
        }
    }
}
