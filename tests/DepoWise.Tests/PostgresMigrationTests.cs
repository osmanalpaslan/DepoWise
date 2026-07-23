using System.Data.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Npgsql;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 2 Adım 4 (2026-07-23): 52 migration'ın (şema) GERÇEK bir PostgreSQL'de
/// (Neon deneme DB'si) temiz kurulduğunu doğrular. Adım 1-3 hep SQLite'ta test edildi; burada
/// PostgreSQL'in katı tip kuralları devreye girer ve gizli farklar (varsa) ortaya çıkar.
///
/// ⚠️ İZOLASYON: yalnız DEPOWISE_PG_URL (Neon deneme DB'si, BOŞ) üzerinde çalışır; her koşuda
/// public şemayı sıfırlar. Babanın canlı verisiyle ilgisi YOKTUR (altın kural).
/// Ortam değişkeni yoksa SESSİZCE ATLANIR.
/// </summary>
public class PostgresMigrationTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    /// <summary>MigrationRunner'ın beklediği fabrika; Npgsql bağlantısı döndürür (taban DbConnection).</summary>
    internal sealed class NpgsqlTestFactory : IDbConnectionFactory
    {
        private readonly string _cs;
        public NpgsqlTestFactory(string cs) => _cs = cs;
        public string DatabasePath => "(postgres)";
        public DbConnection Create()
        {
            var conn = new NpgsqlConnection(_cs);
            conn.Open();
            return conn;
        }
    }

    [SkippableFact]
    public void Migrationlar_PostgreSQLde_Temiz_Kurulur()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL yok → PostgreSQL migration testi atlandı.");
        var factory = new NpgsqlTestFactory(PgUrl!);

        // 1) TEMİZ ŞEMA (boş dev DB) — her koşu sıfırdan.
        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
            cmd.ExecuteNonQuery();
        }

        // 2) Tüm migration'ları çalıştır.
        var runner = new MigrationRunner(factory);
        var applied = runner.Run();

        // 3) Beklenen en yüksek sürüm uygulanmış olmalı.
        var expectedMax = MigrationCatalog.All().Max(m => m.Version);
        Assert.Equal(expectedMax, runner.CurrentVersion());
    }
}
