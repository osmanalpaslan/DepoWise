using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ ALAN ZORUNLULUĞU (kullanıcı isteği 2026-09-03, migration onayı aynı gün) ═══
///
/// Firma yöneticisinin, form alanlarının ZORUNLU olup olmadığını FİRMA BAZINDA ayarlayabildiği yapı.
///
/// <b>Güvenlik/geri uyumluluk (kullanıcı şartı: "en sorunsuz şekilde"):</b>
///  • YALNIZ EKLEMELİDİR: tek yeni tablo; hiçbir mevcut tabloya/veriye dokunmaz.
///  • <b>Satır YOKSA katalog varsayılanı geçerlidir</b> → migration çalıştığında hiçbir formun
///    davranışı değişmez (yayın günü kimse fark etmez).
///  • Yapı yalnız SIKILAŞTIRIR: opsiyonel bir alan firma isteğiyle zorunlu yapılabilir; SİSTEM
///    zorunluları (iç kod, litre gibi iş kuralı alanları) buradan GEVŞETİLEMEZ — o kural
///    katalogda (FieldCatalog.SystemRequired) ve serviste uygulanır, tabloda değil.
///  • Firma bazlıdır: bir firmanın ayarı diğerini ETKİLEMEZ (company_id zorunlu).
///
/// Desen <c>screen_platform_visibility</c> (Migration065) ile BİREBİR aynıdır: sunucu otoriteli
/// yapılandırma; masaüstüne tanım senkronu aynasıyla iner (masaüstü asla yazmaz → LWW sorusu yok).
/// Idempotent — yeniden çalıştırma zararsızdır. İki lehçede (SQLite + PostgreSQL) aynı SQL.
/// </summary>
public sealed class Migration087_FieldRequirements : IMigration
{
    public int Version => 87;
    public string Name => "field_requirements";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS field_requirements (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    screen_key  TEXT NOT NULL,
    field_key   TEXT NOT NULL,
    required    INTEGER NOT NULL,
    created_at  BIGINT NOT NULL,
    updated_at  BIGINT NOT NULL,
    UNIQUE(company_id, screen_key, field_key)
);");
        // Okuma her zaman firma bazlıdır (oturumun firması) → tek kolonlu indeks yeterli (065 deseni).
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_field_requirements_company ON field_requirements(company_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
