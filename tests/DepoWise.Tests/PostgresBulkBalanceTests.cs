using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// TOPLU BAKİYE OKUMA — PostgreSQL karşılığı (Faz S / İş #11-A, 2026-08-09).
///
/// Bu düzeltmenin asıl hedefi PostgreSQL'di (sunucu orada çalışır; her sorgu ağ üzerinden bir
/// gidiş-dönüş). Bu yüzden yeni <c>GetBalances</c> sorgusu PG'de de doğrulanır: parametreli
/// <c>IN</c> listesi ve <c>quantity</c> okuması iki lehçede de aynı sonucu vermelidir.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresBulkBalanceTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [SkippableFact]
    public void PostgreSQLde_toplu_bakiye_TEK_TEK_okumayla_AYNI()
    {
        PostgresTestGuard.SkipUnlessSafe();

        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var materials = new MaterialService(factory, clock);
        var opening = new OpeningStockService(factory, clock);
        var stock = new StockService(factory, clock);

        var ids = new List<string>();
        for (int i = 1; i <= 12; i++)
        {
            var id = materials.Create(s, new NewMaterial($"PG-{i:00}", $"Malzeme {i}"));
            ids.Add(id);
            if (i % 3 != 0) opening.RecordOpening(s, id, i * 2m, "op-" + i);   // bazılarında hiç hareket yok
        }

        var toplu = stock.GetBalances(s, ids);

        foreach (var id in ids)
        {
            var tekTek = stock.GetBalance(s, id);
            var topludan = toplu.TryGetValue(id, out var q) ? q : 0m;
            Assert.Equal(tekTek, topludan);
        }
        Assert.NotEmpty(toplu);   // testin gerçekten veri gördüğünü kanıtlar (yanlış yeşil olmasın)
    }
}
