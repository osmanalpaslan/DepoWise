using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 3 (2026-07-24): eşitleme uygulamasının (ApplyCore) SATIR-HATASI DAYANIKLILIĞI.
///
/// SQLite'ta hatalı bir satır atlanıp devam edilir. PostgreSQL'de bir satır hatası TÜM transaction'ı abort
/// eder (25P02) → sonraki her komut da patlar; naif kod TÜM push'u kaybettirirdi. ApplyCore artık PG'de
/// tablo-savepoint (hızlı yol) + hata olursa satır-başı savepoint (kurtarma) kullanır. Bu test onu kanıtlar:
/// bir malzeme satırı OLMAYAN bir kategoriye (FK ihlali) işaret etse bile, GEÇERLİ satır yazılır ve push
/// bir bütün olarak BAŞARISIZ OLMAZ (yalnız hatalı satır atlanır).
///
/// ⚠️ Yalnız DEPOWISE_PG_URL üzerinde; şemayı sıfırlar. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresSyncRecoveryTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [SkippableFact]
    public void ApplyCore_PostgreSQLde_HataliSatiri_Atlar_GecerliyiYazar()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL yok → PG sync kurtarma testi atlandı.");
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
            cmd.ExecuteNonQuery();
        }
        new MigrationRunner(factory).Run();

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var companies = new CompanyService(factory, clock);
        var rootId = users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new SessionContext(rootId, "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        // İki malzeme: biri GEÇERLİ, biri OLMAYAN kategoriye FK ile bağlı (PG'de INSERT'te patlar).
        // ValidateRow FK bakmaz (tenant + negatif bakar) → ikisi de doğrulamayı geçer; hata DB katmanında.
        var payload = JsonDocument.Parse(@"{
  ""tables"": {
    ""materials"": [
      { ""id"":""m_good"", ""company_id"":""B"", ""code"":""G1"", ""name"":""Gecerli"",
        ""created_at"":1700000000000, ""updated_at"":1700000000000 },
      { ""id"":""m_bad"", ""company_id"":""B"", ""code"":""B1"", ""name"":""Hatali"",
        ""category_id"":""OLMAYAN_KATEGORI"",
        ""created_at"":1700000000000, ""updated_at"":1700000000000 }
    ]
  }
}").RootElement;

        var sync = new BusinessSyncService(factory, clock);
        var res = sync.Apply("B", payload);        // FIRLATMAMALI (push bir bütün olarak batmaz)

        Assert.True(res.Upserted >= 1, "geçerli satır yazılmalıydı");
        Assert.NotEmpty(res.Errors);               // hatalı satır rapor edildi (atlandı)

        // Geçerli yazıldı, hatalı yazılmadı → kurtarma yolu izole etti.
        Assert.Equal(1, CountById(factory, "materials", "m_good"));
        Assert.Equal(0, CountById(factory, "materials", "m_bad"));
    }

    private static long CountById(PostgresMigrationTests.NpgsqlTestFactory f, string table, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE id=@i;";
        var p = cmd.CreateParameter(); p.ParameterName = "@i"; p.Value = id; cmd.Parameters.Add(p);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
