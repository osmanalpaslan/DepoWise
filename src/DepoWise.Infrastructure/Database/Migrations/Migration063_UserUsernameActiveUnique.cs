using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// G6-03 (PRT-01 Grup 6, 2026-08-11) — SİLİNEN KULLANICI ADI YENİDEN KULLANILABİLİR OLSUN.
///
/// NEDEN: Migration001'deki <c>ux_users_username(company_id, username)</c> indeksi TÜM satırları kapsıyordu.
/// Kullanıcı silme SOFT delete olduğundan (<c>is_deleted=1</c>) silinen kullanıcının adı firmada KALICI
/// olarak bloke kalıyordu: aynı adla yeni kullanıcı açmak UNIQUE ihlaline düşüyor, hata da tanınmadığı için
/// kullanıcıya jenerik "Sunucuda beklenmeyen bir hata oluştu" (500) olarak dönüyordu. Silinen kullanıcıyı
/// geri getirmenin bir yolu da yoktu (Çöp Kutusu users tablosunu kapsamıyordu — bu migration ile birlikte
/// <see cref="Files.TrashService"/> tarafında da açıldı).
///
/// NE YAPILIR: indeks KOŞULLU (partial) hâle getirilir → benzersizlik yalnız AKTİF (silinmemiş) kullanıcılar
/// için geçerlidir. Migration033'teki <c>ux_users_personnel ... WHERE personnel_id IS NOT NULL AND is_deleted=0</c>
/// deseninin aynısıdır; iki lehçe de koşullu indeksi destekler (SQLite 3.8+ ve PostgreSQL).
///
/// VERİ GÜVENLİĞİ — HİÇBİR SATIR SİLİNMEZ/DEĞİŞTİRİLMEZ ve BU MIGRATION VERİ ÇAKIŞMASIYLA PATLAYAMAZ:
/// eski indeks TÜM satırlarda benzersizliği zorluyordu; dolayısıyla aktif satırlar da zaten benzersizdir.
/// Yeni indeks bu kümenin ALT KÜMESİNİ kısıtladığı için kurulumu her zaman başarılıdır. Kısıt GEVŞETİLİR,
/// sıkılaştırılmaz → mevcut üretim verisi için temizlik gerekmez, temizlik yapılmaz.
///
/// GÜVENLİK: aynı anda İKİ AKTİF kullanıcının aynı adı taşıması hâlâ İMKÂNSIZdır (giriş belirsizleşmez).
/// Silinmiş kayıtlar arasında tekrar serbesttir — onlar oturum açamaz (<c>is_deleted=0</c> her giriş
/// sorgusunda aranır).
///
/// LEHÇE: SON DURUM İKİSİNDE DE AYNI. Tek fark indeksin bırakılma sözdizimidir (IF EXISTS iki lehçede de
/// destekleniyor); indeks adı korunur.
///
/// GERİ ALMA (gerekirse, iki veritabanında da):
///     DROP INDEX IF EXISTS ux_users_username;
///     CREATE UNIQUE INDEX ux_users_username ON users(company_id, username);
///     DELETE FROM schema_migrations WHERE version = 63;
/// NOT: geri alırken, aynı adı taşıyan silinmiş + aktif kayıt oluşmuşsa eski indeks KURULAMAZ.
/// </summary>
public sealed class Migration063_UserUsernameActiveUnique : IMigration
{
    public int Version => 63;
    public string Name => "user_username_active_unique";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, "DROP INDEX IF EXISTS ux_users_username;");
        Exec(conn, tx,
            "CREATE UNIQUE INDEX ux_users_username ON users(company_id, username) WHERE is_deleted = 0;");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
