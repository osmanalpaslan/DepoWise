using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ FAZ 3a (ADR-222, kullanıcı onayı 2026-09-05) — ROL BAZLI YETKİ ═══
///
/// Bugüne kadar izinler <b>yalnız kullanıcı seviyesinde</b> tutuluyordu (<c>user_permissions</c> +
/// <c>user_button_permissions</c>). 81 personelli bir firmada her kullanıcıya 60 modül × 4 bayrağı
/// tek tek vermek gerekiyordu — ölçeklenmiyordu. Bu migration rol seviyesini ekler.
///
/// <b>⚠️ İKİ TABLOYU KARIŞTIRMAYIN — adları benziyor, işleri ZIT:</b>
/// <list type="bullet">
///   <item><c>role_grant_limits</c> (mevcut) → rol × modül <b>KAPATMA</b> (negatif). Süper adminin
///     bir rolü bir ekrandan men etmesi. "Rol Yetki Kontrol" ekranı BUNU yönetir ve o ekranın
///     modül anahtarı da <c>"role_permissions"</c>dır — <b>bu tabloyla ilgisi yoktur.</b></item>
///   <item><c>role_permissions</c> (BU migration) → rol × modül <b>VERME</b> (pozitif, ALLOW).</item>
/// </list>
///
/// <b>Geri uyumluluk (kullanıcının en önemli şartı):</b>
///  • YALNIZ EKLEME: iki yeni tablo; hiçbir mevcut tabloya, kolona veya satıra dokunulmaz.
///  • <b>Tablolar BOŞ doğar</b> → etkin izin = birleşim(kullanıcı, ∅) = kullanıcı izinleri
///    → yayın günü davranış <b>bit bit bugünküyle aynıdır</b>. Kimse bir şey kazanmaz/kaybetmez.
///  • Geri alma: tabloları bırakmak yeterlidir; kod boş tabloyla bugünkü gibi çalışır.
///
/// <b>Neden <c>user_permissions</c>'ın birebir aynası:</b> aynı okuma kodu, aynı birleştirme
/// mantığı, aynı yetki ağacı. Serbest metin <c>module_key</c> sayesinde <c>rpt_</c> (rapor) ve
/// <c>datype_</c> (kayıt tipi) önekleri rol seviyesinde de <b>kendiliğinden</b> çalışır — onlar için
/// ayrıca migration gerekmez (projenin kanıtlanmış deseni).
///
/// <b>Kapsam dışı (bilinçli):</b> rol bazlı ŞUBE KAPSAMI eklenmedi. Şube kapsamı bugün
/// <c>user_scopes</c> ile kullanıcı seviyesindedir ve <c>BranchAccess</c> tek yorumlayıcıdır;
/// role taşımak ikinci bir kapsam otoritesi yaratırdı. Faz 3a yalnız ALLOW ekler (K1).
///
/// Idempotent — yeniden çalıştırma zararsızdır. İki lehçede (SQLite + PostgreSQL) aynı SQL.
/// </summary>
public sealed class Migration092_RolePermissions : IMigration
{
    public int Version => 92;
    public string Name => "role_permissions";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // Rol × modül izinleri — user_permissions ile AYNI şekil (user_id yerine role_id).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS role_permissions (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    role_id     TEXT NOT NULL,
    module_key  TEXT NOT NULL,
    can_view    BIGINT NOT NULL DEFAULT 0,
    can_create  BIGINT NOT NULL DEFAULT 0,
    can_edit    BIGINT NOT NULL DEFAULT 0,
    can_delete  BIGINT NOT NULL DEFAULT 0,
    created_at  BIGINT NOT NULL,
    updated_at  BIGINT NOT NULL,
    version     BIGINT NOT NULL DEFAULT 1
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_role_permissions ON role_permissions(role_id, module_key);");
        // Okuma daima firma + rol ile yapılır (sistem rolleri tüm firmalarda kullanılır →
        // company_id satırda tutulur ki bir firmanın verdiği izin diğerine SIZMASIN).
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_role_permissions_company ON role_permissions(company_id, role_id);");

        // Rol × özel buton — user_button_permissions ile AYNI şekil.
        // Ayrı tablo, çünkü butonun dört bayrağı yoktur (var/yok).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS role_button_permissions (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    role_id     TEXT NOT NULL,
    button_key  TEXT NOT NULL,
    created_at  BIGINT NOT NULL
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_role_buttons ON role_button_permissions(role_id, button_key);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_role_buttons_company ON role_button_permissions(company_id, role_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
