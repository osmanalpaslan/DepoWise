using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Firma "yerel sıfırlama" isteği (ADR-084) — company_local_resets.
///
/// Aynı tablo iki farklı yerde iki farklı anlamla kullanılır (server ile masaüstü ayrı SQLite dosyalarıdır):
/// - SUNUCUDA: tek satır = "bu firma için EN SON istenen sıfırlama zamanı" (süper admin yazar).
/// - HER MASAÜSTÜNDE (kendi yerel kopyasında): tek satır = "BU makinenin bu firma için EN SON UYGULADIĞI
///   sıfırlama zamanı". Makine, sunucudakinden daha eski/hiç yoksa bir kerelik yerel temizliği uygular ve
///   kendi satırını sunucudakiyle eşitler. Böylece istek tek sefer uygulanır; makine o an kapalıysa bile
///   bir sonraki aktif olduğu girişte (çevrimiçi) algılanır.
///
/// ADR-083'teki company_purges'ten FARKI: bu KALICI silme/erişim engeli DEĞİLDİR — firma sunucuda durmaya
/// devam eder, kullanıcılar yine giriş yapabilir; yalnız o makinenin YEREL kopyası bir kez temizlenip
/// normal senkron akışıyla sıfırdan yeniden doldurulur (yeni makinenin ilk girişiyle aynı yol).
///
/// Idempotent.
/// </summary>
public sealed class Migration045_CompanyLocalReset : IMigration
{
    public int Version => 45;
    public string Name => "company_local_reset";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS company_local_resets(
    company_id   TEXT PRIMARY KEY,
    requested_at INTEGER NOT NULL,
    requested_by TEXT NOT NULL
);");
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
