using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PERSONEL ARAMASI — PostgreSQL karşılığı (İş A, 2026-08-09).
///
/// <c>PersonnelService.List</c>'e eklenen arama <c>SqlDialect.LikeTr</c> kullanır; bu yardımcı
/// SQLite ve PostgreSQL'de FARKLI SQL üretir (Türkçe büyük/küçük harf eşleşmesi). Sunucu PG'de
/// çalıştığı için aramanın orada da doğru çalıştığı ayrıca kanıtlanmalıdır.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresPersonnelSearchTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [SkippableFact]
    public void PostgreSQLde_personel_aramasi_dogru_calisir()
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
                "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5), ('B', 'B', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var scope = new ScopeResolver(factory);
        var personnel = new PersonnelService(factory, scope, clock);

        var uidA = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var a = new SessionContext(uidA, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var uidB = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uidB, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        personnel.Create(a, new NewPersonnel("İsmail Şahin", null, null, null));
        personnel.Create(a, new NewPersonnel("Ahmet Yilmaz", null, null, null));
        personnel.Create(b, new NewPersonnel("Gizli Şahin", null, null, null));   // BAŞKA firma

        List<string> Search(SessionContext s, string? q)
            => personnel.List(s, new PageRequest { Limit = 200 }, search: q).Items.Select(p => p.FullName).ToList();

        // Türkçe karakter-doğru eşleşme (küçük "ş" → "Ş")
        var sahin = Search(a, "şahin");
        Assert.Contains("İsmail Şahin", sahin);
        Assert.DoesNotContain("Ahmet Yilmaz", sahin);

        // Firma izolasyonu: B'nin kaydı A'nın aramasında ÇIKMAZ
        Assert.DoesNotContain("Gizli Şahin", sahin);

        // Arama verilmezse eski davranış: tüm (firma içi) kayıtlar
        var hepsi = Search(a, null);
        Assert.Contains("İsmail Şahin", hepsi);
        Assert.Contains("Ahmet Yilmaz", hepsi);
        Assert.DoesNotContain("Gizli Şahin", hepsi);
    }
}
