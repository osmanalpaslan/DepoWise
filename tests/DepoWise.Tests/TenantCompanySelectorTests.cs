using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Tenant izolasyonu: firma seçici + şube yönetimi. Süper admin tüm firmaları; süper-admin-altı
/// roller YALNIZ kendi firmasını görür/yönetir; başka firmanın kaydına asla erişemez.</summary>
public class TenantCompanySelectorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public TenantCompanySelectorTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_tn_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void FirmaSecici_SuperAdminTumunu_DigerleriKendiFirmasini()
    {
        var users = new UserService(_factory, _clock);
        var companies = new CompanyService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        // İkinci firma + o firmada bir admin.
        companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var adminId = users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "B"));
        var admin = auth.Login("B", "adm", "p12345").Session!;

        // Süper admin: iki firmayı da görür.
        var suList = companies.Selectable(su);
        Assert.Contains(suList, x => x.Id == "A");
        Assert.Contains(suList, x => x.Id == "B");

        // B admini: YALNIZ kendi firmasını (B) görür; A asla görünmez.
        var adminList = companies.Selectable(admin);
        Assert.Single(adminList);
        Assert.Equal("B", adminList[0].Id);
    }

    [Fact]
    public void Sube_SuperAdmin_BaskaFirmayaEkler_DigerleriIzole()
    {
        var users = new UserService(_factory, _clock);
        var companies = new CompanyService(_factory, _clock);
        var branches = new BranchService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;
        companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var adminId = users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "B"));
        var admin = auth.Login("B", "adm", "p12345").Session!;

        // Süper admin B firmasına şube ekler (companyId=B).
        var bBranch = branches.Create(su, new NewBranch("B-Depo", "branch", null, null, null), companyId: "B");
        // Süper admin A firmasına şube ekler (varsayılan = kendi firması A).
        branches.Create(su, new NewBranch("A-Depo", "branch", null, null, null));

        // B admini yalnız B'nin şubesini görür.
        var bList = branches.List(admin);
        Assert.Single(bList);
        Assert.Equal("B-Depo", bList[0].Name);

        // Süper admin companyId=B verince yalnız B'nin şubesini görür.
        var suB = branches.List(su, "B");
        Assert.Single(suB);
        Assert.Equal("B-Depo", suB[0].Name);

        // B admini payload'da A firmasını zorlasa bile TenantAccessGuard reddeder (tenant ihlali).
        Assert.Throws<ForbiddenException>(() => branches.List(admin, "A"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
