using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FIN-B1 (ADR-179) — Migration082 POSTGRESQL KANITI ═══
///
/// SQLite kanıtı <see cref="FinalStabilizasyonTests"/>'ta; burada AYNI migration'ın GERÇEK PostgreSQL'de
/// çalıştığı ve yeni sözleşmenin PG'de de geçerli olduğu kanıtlanır:
///  • 6 indeks (company_id, operation_id) üzerinde UNIQUE olarak kurulur (information_schema'dan doğrulanır).
///  • Farklı firma + aynı operation_id artık birbirini engellemez (FINAL simülasyonunun PG bulgusunun kapanışı).
///  • Aynı firma retry idempotent kalır.
/// ⚠️ Yalnız izole test PG'sinde (PostgresTestGuard çift kilidi: onay değişkeni + DB adında "test" +
/// boş şema + boyut tavanı). Migration082 PRODUCTION'DA ÇALIŞTIRILMADI — canlı şema 81.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresMigration082Tests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public void Migration082_PostgreSQLde_FirmaKapsamli_Indeks_Kurar()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();   // 082 dahil tüm katalog

        // 1) 6 indeksin kolonları TAM (company_id, operation_id) ve UNIQUE.
        using (var conn = factory.Create())
        {
            foreach (var (index, table) in Migration082_OperationIdCompanyScope.Targets)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT a.attname
FROM pg_index i
JOIN pg_class ic ON ic.oid = i.indexrelid
JOIN pg_class tc ON tc.oid = i.indrelid
JOIN unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
JOIN pg_attribute a ON a.attrelid = tc.oid AND a.attnum = k.attnum
WHERE ic.relname = @ix AND tc.relname = @tb AND i.indisunique
ORDER BY k.ord;";
                var pIx = cmd.CreateParameter(); pIx.ParameterName = "@ix"; pIx.Value = index; cmd.Parameters.Add(pIx);
                var pTb = cmd.CreateParameter(); pTb.ParameterName = "@tb"; pTb.Value = table; cmd.Parameters.Add(pTb);
                var cols = new List<string>();
                using var r = cmd.ExecuteReader();
                while (r.Read()) cols.Add(r.GetString(0));
                Assert.Equal(new[] { "company_id", "operation_id" }, cols);
            }
        }

        // 2) Yeni sözleşme PG'de: farklı firma + aynı op-id ENGELLENMEZ; aynı firma retry idempotent.
        var users = new UserService(factory);
        var aId = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var bId = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var a = new SessionContext(aId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var b = new SessionContext(bId, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var fuel = new FuelService(factory);
        const string Op = "PG-FIN-OP";
        Assert.NotEqual("", fuel.AddDepotEntry(a, new NewDepotEntry(100m, 40m), Op));
        Assert.NotEqual("", fuel.AddDepotEntry(b, new NewDepotEntry(70m, 40m), Op));    // eskiden sessiz no-op'tu
        Assert.Equal("", fuel.AddDepotEntry(a, new NewDepotEntry(100m, 40m), Op));      // retry → idempotent
    }
}
