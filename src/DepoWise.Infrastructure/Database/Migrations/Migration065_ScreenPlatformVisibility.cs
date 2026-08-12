using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// G5 — EKRAN PLATFORM GÖRÜNÜRLÜĞÜ (kullanıcı isteği 2026-08-12).
///
/// Bir ekranın FİRMA BAZINDA hangi platformda (masaüstü / web) kullanılabileceğini tutar.
/// <b>Satır YOKSA</b> ekranın <c>AppScreens.Platforms</c> derleme-zamanı varsayılanı geçerlidir →
/// bu migration çalıştığında hiçbir ekran kapanmaz, mevcut davranış birebir korunur.
///
/// <b>Firma bazlıdır:</b> bir firmanın ayarı diğerini ETKİLEMEZ (<c>company_id</c> zorunlu).
/// Katalog (<c>AppScreens</c>) sistem düzeyi ve globaldir; burada tutulan yalnız çalışma zamanı
/// KISITLAMASIDIR.
///
/// <b>Yalnız DARALTIR:</b> etkin platform = katalogdaki varsayılan <b>VE</b> buradaki kayıt. Katalogda
/// olmayan bir platform buradan AÇILAMAZ — açılsaydı, karşılığı olmayan bir menü girişi üretilir ve
/// tıklandığında hiçbir yere gitmezdi (ör. yalnız web'de var olan "Kota İzleme" masaüstü menüsüne düşerdi).
///
/// Desen <c>role_grant_limits</c> / <c>company_grant_limits</c> ile aynıdır; yeni bir yapı icat edilmemiştir.
/// Idempotent — yeniden çalıştırma zararsızdır.
/// </summary>
public sealed class Migration065_ScreenPlatformVisibility : IMigration
{
    public int Version => 65;
    public string Name => "screen_platform_visibility";

    /// <summary>Platform değerleri — metin olarak saklanır (iki lehçede de aynı).</summary>
    public const string PlatformDesktop = "desktop";
    public const string PlatformWeb = "web";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS screen_platform_visibility (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    screen_key  TEXT NOT NULL,
    platform    TEXT NOT NULL,
    enabled     INTEGER NOT NULL,
    created_at  BIGINT NOT NULL,
    updated_at  BIGINT NOT NULL,
    UNIQUE(company_id, screen_key, platform)
);");
        // Okuma her zaman firma bazlıdır (oturumun firması) → tek kolonlu indeks yeterli.
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_screen_visibility_company ON screen_platform_visibility(company_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
