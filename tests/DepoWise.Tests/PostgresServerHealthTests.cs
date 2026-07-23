using DepoWise.Infrastructure.Database;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 3 (2026-07-23): sunucu açılış sağlık kontrolü (<see cref="DatabaseHealth"/>)
/// PostgreSQL'de doğru çalışır mı? Eskiden yalnız SQLite PRAGMA'larıyla (journal_mode/foreign_keys)
/// çalışıyordu → PG'de patlardı. Artık lehçe-duyarlı: PG'de PRAGMA çalıştırmaz, FK'yi true + journal'ı
/// "postgres" raporlar ve gerçek write/read testini yapar.
///
/// Bu, üretim <see cref="DepoWise.Api.PostgresConnectionFactory"/>'nin de kanıtıdır (aynı Npgsql bağlantısı;
/// factory yalnızca bağlantıyı açıp döndürür). ⚠️ Yalnız DEPOWISE_PG_URL varsa; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresServerHealthTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public void Saglik_Kontrolu_PostgreSQLde_Calisir()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL yok → PostgreSQL health testi atlandı.");
        // GERÇEK üretim factory'si (DepoWise.Api) — bağlantı açma + DatabasePath etiketi de kanıtlanır.
        var factory = new DepoWise.Api.PostgresConnectionFactory(PgUrl!);

        var health = new DatabaseHealth(factory);
        var result = health.CheckAsync().GetAwaiter().GetResult();

        Assert.True(result.Ok, result.Error ?? "health başarısız");
        Assert.True(result.WriteReadOk);          // gerçek INSERT + MAX(ts) geri okundu
        Assert.True(result.ForeignKeysOn);         // PG'de FK'ler her zaman zorunlu → true
        Assert.Equal("postgres", result.JournalMode);
        Assert.StartsWith("postgres://", result.DatabasePath);
    }
}
