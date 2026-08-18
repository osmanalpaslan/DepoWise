using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ MNU — MENÜ DÜZENİ (kullanıcı isteği 2026-08-18) ═══
///
/// Bir ekranın menüdeki <b>görünen adı</b>, <b>bağlı olduğu üst menü</b> ve <b>sırası</b>; bir üst
/// menünün <b>görünen adı</b> ve <b>sırası</b> — hepsi FİRMA BAZINDA burada tutulur.
///
/// <b>⚠️ BU TABLOLAR EKRAN KAYNAĞI DEĞİLDİR.</b> Ekranların tek doğru kaynağı <c>AppScreens</c>
/// kataloğudur (kod). Burada tutulan yalnız o kataloğun üzerine binen <b>görünüm tercihidir</b>:
/// route, ekran anahtarı, yetki anahtarı ve servis adları BURADAN ETKİLENMEZ. Paralel bir menü
/// sistemi kurulmamıştır — desen <c>screen_platform_visibility</c> (Migration065) ile birebir aynıdır.
///
/// <b>SATIR YOKSA KATALOG VARSAYILANI GEÇERLİDİR</b> → bu migration çalıştığında hiçbir menü
/// değişmez, hiçbir ekran kaybolmaz, sıra bozulmaz. Geri uyumluluk bu şekilde sağlanır (backfill YOK).
///
/// <b>GRUP KİMLİĞİ:</b> <c>menu_group_layout.group_key</c> = <c>AppScreenGroup.Title</c> (katalogdaki
/// DEĞİŞMEZ başlık, ör. "Ön Muhasebe"). Kullanıcı yalnız <c>title_override</c> ile GÖRÜNEN adı
/// değiştirir; anahtar sabit kalır → yeniden adlandırma hiçbir referansı kırmaz. Kullanıcının
/// oluşturduğu yeni gruplar <c>custom:</c> önekli anahtar alır ve <c>is_custom=1</c> ile işaretlenir.
///
/// <b>GRUP GÖRÜNÜRLÜĞÜ AYRI BİR ALAN DEĞİLDİR (bilinçli):</b> menüler zaten "tek görünür ekranı bile
/// kalmayan grubu" göstermiyor (<c>NavMenu.razor</c>: <c>if (links.Count == 0) continue;</c>). Bir grubu
/// gizlemek = içindeki ekranları o platformda kapatmak; bu da mevcut <c>screen_platform_visibility</c>
/// mekanizmasıyla yapılır. İkinci bir gizleme yolu açmak, aynı sonucu iki farklı kurala bağlardı.
///
/// Idempotent — yeniden çalıştırma zararsızdır.
/// </summary>
public sealed class Migration070_MenuLayout : IMigration
{
    public int Version => 70;
    public string Name => "menu_layout";

    /// <summary>Kullanıcının oluşturduğu grupların anahtar öneki (katalog gruplarıyla çakışmaz).</summary>
    public const string CustomGroupPrefix = "custom:";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // ── Ekran düzeni ────────────────────────────────────────────────────────────────────────
        // Üç alan da NULL olabilir: NULL = "bu konuda tercih yok" → katalog varsayılanı.
        // Böylece tek bir alanı değiştirmek diğerlerini katalog varsayılanına sabitlemez.
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS screen_menu_layout (
    id                  TEXT PRIMARY KEY,
    company_id          TEXT NOT NULL,
    screen_key          TEXT NOT NULL,
    label_override      TEXT,
    group_key_override  TEXT,
    sort_order          INTEGER,
    created_at          BIGINT NOT NULL,
    updated_at          BIGINT NOT NULL,
    UNIQUE(company_id, screen_key)
);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_screen_menu_layout_company ON screen_menu_layout(company_id);");

        // ── Üst menü (grup) düzeni ──────────────────────────────────────────────────────────────
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS menu_group_layout (
    id              TEXT PRIMARY KEY,
    company_id      TEXT NOT NULL,
    group_key       TEXT NOT NULL,
    title_override  TEXT,
    sort_order      INTEGER,
    is_custom       INTEGER NOT NULL,
    created_at      BIGINT NOT NULL,
    updated_at      BIGINT NOT NULL,
    UNIQUE(company_id, group_key)
);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_menu_group_layout_company ON menu_group_layout(company_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
