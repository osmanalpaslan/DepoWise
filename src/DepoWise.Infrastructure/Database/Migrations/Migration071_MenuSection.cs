using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ SEC — ÜST GRUP (menü üçüncü seviyesi, kullanıcı isteği 2026-08-19) ═══
///
/// Menü bugüne kadar iki seviyeliydi: <b>ÜST MENÜ → EKRAN</b>. Kalabalık menüyü toparlamak için
/// üçüncü bir seviye eklenir: <b>ÜST GRUP → ÜST MENÜ → EKRAN</b>.
///
/// <b>YENİ TABLO AÇILMADI (bilinçli):</b> üst grup da bir menü düğümüdür ve mevcut
/// <c>menu_group_layout</c> tablosunda saklanır. Ayırt edici işaret, anahtarın
/// <see cref="SectionPrefix"/> önekiyle başlamasıdır (kullanıcı grupları <c>custom:</c>, katalog
/// grupları ise Türkçe başlıktır — üçü de çakışmaz). Böylece sıralama, ad değiştirme, audit ve
/// senkron yolları TEK kod üzerinden yürür; ikinci bir yapı bakım yükü doğurmaz.
///
/// Tek eklenen alan: <c>parent_group_key</c> — bir üst menünün bağlı olduğu ÜST GRUP.
/// <c>NULL</c> = üst gruba bağlı değil → bugünkü gibi en üst seviyede durur.
///
/// <b>GERİ UYUMLULUK:</b> kolon nullable ve varsayılanı <c>NULL</c>'dır; hiçbir satır
/// güncellenmez. Bu migration çalıştığında <b>hiçbir firmanın menüsü değişmez</b> — üçüncü seviye
/// yalnız yönetici bir üst grup oluşturup ona üst menü bağladığında ortaya çıkar.
///
/// Idempotent: kolon zaten varsa sessizce geçilir (iki lehçede de kolon varlığı kontrol edilir).
/// </summary>
public sealed class Migration071_MenuSection : IMigration
{
    public int Version => 71;
    public string Name => "menu_section";

    /// <summary>Üst grup anahtarlarının öneki. Katalog başlıkları ve <c>custom:</c> ile çakışmaz.</summary>
    public const string SectionPrefix = "section:";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // Tablo henüz yoksa (teoride olmaz — Migration070 açar) sessizce çık: 070 zaten oluşturacak.
        if (!DbIntrospect.TableExists(conn, tx, "menu_group_layout")) return;
        if (DbIntrospect.ColumnExists(conn, tx, "menu_group_layout", "parent_group_key")) return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE menu_group_layout ADD COLUMN parent_group_key TEXT;";
        cmd.ExecuteNonQuery();
    }
}
