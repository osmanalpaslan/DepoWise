using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ FAZ 3b (ADR-223, kullanıcı onayı D2 · 2026-09-05) — KORUMALI ALANLAR ═══
///
/// Bir alanın "korumalı" olup olmadığını FİRMA belirler. Yetki katmanı yalnız ALLOW üretir (K1);
/// gizleme kararı bu yüzden yetkide değil, <b>firma yapılandırmasında</b> durur.
///
/// <b>Anlam — tek yerde tanımlı:</b>
/// <list type="bullet">
///   <item><b>Korumasız alan</b> (bu tabloda satırı YOK) → bugünkü davranış: herkes görür ve
///     düzenler. Yetki sorgusu bile yapılmaz.</item>
///   <item><b>Korumalı alan</b> (satırı VAR) → deny-by-default: yalnız kullanıcı ya da rolü
///     <c>fld_&lt;ekran&gt;_&lt;alan&gt;</c> iznine sahipse görünür/düzenlenir.</item>
/// </list>
///
/// <b>Geri uyumluluk (kullanıcının en önemli şartı):</b>
///  • YALNIZ EKLEME: tek yeni tablo; hiçbir mevcut tabloya, kolona veya satıra dokunulmaz.
///  • <b>Tablo BOŞ doğar</b> → hiçbir alan korumalı değildir → <b>yayın günü hiçbir kullanıcının
///    gördüğü/düzenlediği alan değişmez.</b> (<c>AlanKorumasiBoskenDavranisAynidir</c> testi kilitler.)
///  • Geri alma: tabloyu bırakmak yeterlidir; kod boş tabloyla bugünkü gibi çalışır.
///
/// <b>Neden <c>field_requirements</c> (M087) genişletilmedi:</b> şekli benziyor ama semantiği
/// FARKLI — o tablo "bu alan doldurulmak ZORUNDA mı" sorusunu yanıtlar (doğrulama), bu tablo
/// "bu alan korunuyor mu" sorusunu (görünürlük). İkisini tek tabloda birleştirmek, bir alanın
/// zorunluluğunu değiştirenin farkında olmadan görünürlüğünü de değiştirmesine yol açardı.
/// Mevcut çalışan tabloya dokunmama kuralı da bunu destekliyor.
///
/// Idempotent — yeniden çalıştırma zararsızdır. İki lehçede (SQLite + PostgreSQL) aynı SQL.
/// </summary>
public sealed class Migration093_FieldProtections : IMigration
{
    public int Version => 93;
    public string Name => "field_protections";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS field_protections (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    screen_key  TEXT NOT NULL,
    field_key   TEXT NOT NULL,
    created_at  BIGINT NOT NULL,
    UNIQUE(company_id, screen_key, field_key)
);");
        // Okuma daima firma bazlıdır (oturumun firması) → tek kolonlu indeks yeterli (065/087 deseni).
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_field_protections_company ON field_protections(company_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
