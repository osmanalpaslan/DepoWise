using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Adım 3 — Firma kotası: admin (max_admins) ve NORMAL personel (max_users) AYRI kotalanır;
/// %20 kuralı kalktı; makine kotası firma alanı olarak saklanır.</summary>
public class CompanyQuotaTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly AuthService _auth;

    public CompanyQuotaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_cq_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext Su() => _auth.Login("A", "root", "root123").Session!;

    [Fact]
    public void MakineKotasi_Saklanir_ve_Listelenir()
    {
        var su = Su();
        var id = _companies.Create(su, new NewCompany("Firma X", MaxUsers: 5, MaxAdmins: 2, MachineQuota: 7));
        var row = _companies.List(su).Single(c => c.Id == id);
        Assert.Equal(5, row.MaxUsers);
        Assert.Equal(2, row.MaxAdmins);
        Assert.Equal(7, row.MachineQuota);
    }

    [Fact]
    public void AdminKotasi_max_admins_ile_Sinirli()
    {
        var su = Su();
        var id = _companies.Create(su, new NewCompany("Firma A", MaxAdmins: 1));
        _users.CreateUser(su, new NewUser("adm1", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: id));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.CreateUser(su, new NewUser("adm2", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: id)));
        Assert.Contains("Admin kotası", ex.Message);
    }

    [Fact]
    public void NormalKotasi_max_users_ile_Sinirli_AdminSayilmaz()
    {
        var su = Su();
        var id = _companies.Create(su, new NewCompany("Firma B", MaxUsers: 1));
        // Admin normal kotaya SAYILMAZ → admin + 1 personel eklenebilir
        _users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: id));
        _users.CreateUser(su, new NewUser("per1", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: id));
        // 2. personel kotayı aşar
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.CreateUser(su, new NewUser("per2", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: id)));
        Assert.Contains("kullanıcı kotası", ex.Message);
    }

    [Fact]
    public void Yuzde20Kurali_Yok_max_admins_0_Sinirsiz()
    {
        var su = Su();
        // max_users=3 ama max_admins=0 (sınırsız) → eski %20 kuralı olsaydı 1 admin sınırı olurdu; artık yok.
        var id = _companies.Create(su, new NewCompany("Firma C", MaxUsers: 3, MaxAdmins: 0));
        _users.CreateUser(su, new NewUser("a1", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: id));
        _users.CreateUser(su, new NewUser("a2", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: id));
        _users.CreateUser(su, new NewUser("a3", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: id)); // sınırsız → sorun yok
        var quota = _users.GetQuotaMonitor(su).Single(q => q.CompanyId == id);
        Assert.Equal(3, quota.AdminCount);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
