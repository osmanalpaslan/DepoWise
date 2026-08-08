using Npgsql;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 1 (2026-07-23): "Bağlantıyı doğrula" adımı.
///
/// Bu test, uygulamanın kullanacağı GERÇEK sürücüyle (Npgsql) bir PostgreSQL sunucusuna bağlanıp
/// basit bir sorgu çalıştırabildiğimizi kanıtlar. Henüz veri/şema taşımıyoruz — yalnız "ulaşabiliyor
/// muyuz?" sorusunu yanıtlar.
///
/// ⚠️ GÜVENLİK / İZOLASYON:
/// - Bağlantı bilgisi YALNIZ ortam değişkeninden (`DEPOWISE_PG_URL`) okunur; koda/git'e yazılmaz.
/// - Ortam değişkeni yoksa test SESSİZCE ATLANIR (Skip) → normal `dotnet test` akışını bozmaz,
///   CI'da PostgreSQL olmadan da yeşil kalır.
/// - Bu bir GELİŞTİRME/DENEME veritabanına bağlanır (yerel ya da ayrı test bulutu) — babanın canlı
///   verisiyle hiçbir ilgisi yoktur (bkz. docs/GOREV_PANOSU.md altın kural).
///
/// Çalıştırma:
///   $env:DEPOWISE_PG_URL = "Host=localhost;Port=5432;Database=depowise_dev;Username=postgres;Password=..."
///   dotnet test --filter "FullyQualifiedName~PostgresConnectionTests"
/// </summary>
public class PostgresConnectionTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public async Task PostgreSQL_Sunucusuna_Baglanip_Surum_Okunabiliyor()
    {
        PostgresTestGuard.SkipUnlessSafe();

        await using var conn = new NpgsqlConnection(PgUrl);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT version();", conn);
        var version = (string?)await cmd.ExecuteScalarAsync();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains("PostgreSQL", version); // sürüm metni "PostgreSQL 17.x ..." gibi olmalı
    }

    [SkippableFact]
    public async Task PostgreSQL_Basit_Sorgu_Dogru_Sonuc_Doner()
    {
        PostgresTestGuard.SkipUnlessSafe();

        await using var conn = new NpgsqlConnection(PgUrl);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT 1 + 1;", conn);
        var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(2, result);
    }
}
