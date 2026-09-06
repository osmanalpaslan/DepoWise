using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ UYGULAMA İÇİ SOHBET (kullanıcı isteği 2026-09-06) ═══
///
/// <b>Kullanıcının isteği:</b> <i>"uygulama içi chat bölümü olsun… mevcut kullanıcıların çevrimiçi
/// olduğu görünebilsin ve mesaj atabileyim… chat i normal eşitleme sürecinin dışında tutalım ve
/// sadece makine çevrimiçiyse çalışsın."</i>
///
/// <para><b>Neden şema değişikliği ZORUNLU.</b> Projede hiçbir mesajlaşma altyapısı yoktu (arama
/// sonucu sıfır dosya). Mesaj saklanacak bir tablo ve "kim çevrimiçi" bilgisini tutacak bir alan
/// olmadan istek teknik olarak karşılanamaz.</para>
///
/// <para><b>chat_messages — birebir mesaj.</b> Her satır bir mesajdır: kimden, kime, gövde, zaman.
/// <c>company_id</c> zorunludur ve sorguların tamamı bununla süzülür → bir firmanın mesajı başka
/// firmaya SIZAMAZ. <c>read_at</c> okundu bilgisidir (okunmamış sayacı bundan hesaplanır).
/// <c>is_deleted</c> yumuşak silme içindir; operasyonel kayıt gibi mesaj da fiziksel silinmez
/// (CLAUDE.md §4) — saklama süresi dolduğunda toplu temizlik AYRI bir iştir.</para>
///
/// <para><b>SENKRON KAPSAMI DIŞINDA — bilinçli.</b> Kullanıcı sohbetin normal eşitlemenin dışında
/// kalmasını ve yalnız çevrimiçiyken çalışmasını istedi. Bu yüzden <c>chat_messages</c> iş verisi
/// senkron kataloğuna EKLENMEZ: masaüstü mesajları yerel SQLite'a yazmaz, doğrudan sunucudan okur ve
/// sunucuya yazar. Sonuç: çevrimdışı kuyruk yok, çakışma yok, LWW tartışması yok. Çevrimdışı makinede
/// sohbet sadece çalışmaz — sessizce yanlış veri üretmez.</para>
///
/// <para><b>users.last_seen_at — çevrimiçi bilgisi.</b> Kullanıcı sohbeti her yokladığında bu alan
/// tazelenir; "çevrimiçi" = son N saniye içinde görülmüş demektir. Ayrı bir oturum/presence tablosu
/// AÇILMADI: tek sütun aynı işi görür, senkron ve yedek yüzeyini büyütmez.
/// (<c>sync_devices.last_seen_at</c> MAKİNE içindir, kullanıcı için değil — ikisi farklı sorulardır.)</para>
///
/// <para><b>CANLI VERİ GÜVENLİĞİ:</b> bir <c>CREATE TABLE</c> (boş doğar) + bir <c>ADD COLUMN … NULL</c>.
/// Hiç <c>UPDATE</c>/<c>DELETE</c>/backfill yok; mevcut hiçbir kayıt değişmez. Geri alma:
/// <c>DROP TABLE chat_messages</c> + <c>DROP COLUMN users.last_seen_at</c>.</para>
/// </summary>
public sealed class Migration096_Chat : IMigration
{
    public int Version => 96;
    public string Name => "chat";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS chat_messages (
    id            TEXT PRIMARY KEY,
    company_id    TEXT NOT NULL,
    sender_id     TEXT NOT NULL,
    recipient_id  TEXT NOT NULL,
    body          TEXT NOT NULL,
    created_at    BIGINT NOT NULL,
    read_at       BIGINT NULL,
    is_deleted    BIGINT NOT NULL DEFAULT 0
);";
            cmd.ExecuteNonQuery();
        }

        // Sohbet açıkken 3 saniyede bir yoklanır; bu sorgular indekssiz kalırsa mesaj sayısı
        // büyüdükçe her yoklama tam tarama yapar. İki erişim deseni vardır:
        //  1) "iki kişi arasındaki konuşma" → (company_id, sender_id, recipient_id, created_at)
        //  2) "bana gelen okunmamışlar"     → (company_id, recipient_id, read_at)
        foreach (var sql in new[]
        {
            "CREATE INDEX IF NOT EXISTS ix_chat_konusma ON chat_messages(company_id, sender_id, recipient_id, created_at);",
            "CREATE INDEX IF NOT EXISTS ix_chat_okunmamis ON chat_messages(company_id, recipient_id, read_at);",
        })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        if (!DbIntrospect.ColumnExists(conn, tx, "users", "last_seen_at"))
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "ALTER TABLE users ADD COLUMN last_seen_at BIGINT NULL;";
            cmd.ExecuteNonQuery();
        }
    }
}
