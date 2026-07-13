using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Adım 6 — Kullanıcı oluşturma akışında şube/şantiye zorunluluğu (ValidateBranchForNewUser):
/// operasyonel (personel) kullanıcıda zorunlu; Süper/Kısıtlı Süper Admin + Admin muaf; şube yoksa özel mesaj.</summary>
public class UserBranchRequirementTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly SessionContext _su = new("root", "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    public UserBranchRequirementTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ubr_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private void EnsureCompany(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($c,$c,0,0,1,0);";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.ExecuteNonQuery();
    }

    private string AddBranch(string companyId)
    {
        EnsureCompany(companyId);
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) VALUES($i,$c,'Merkez','branch',0,0,1,0);";
        cmd.Parameters.AddWithValue("$i", id); cmd.Parameters.AddWithValue("$c", companyId);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static string[] Staff => new[] { RoleKeys.Staff };

    [Fact]
    public void SubeYok_Personel_OzelMesaj()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.ValidateBranchForNewUser(_su, "A", Staff, branchId: null));
        Assert.Contains("henüz şube", ex.Message);
    }

    [Fact]
    public void SubeVar_AmaSecilmemis_Personel_SubeSecUyarisi()
    {
        AddBranch("A");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.ValidateBranchForNewUser(_su, "A", Staff, branchId: null));
        Assert.Contains("şube seçin", ex.Message);
    }

    [Fact]
    public void GecerliSube_Personel_Gecer()
    {
        var b = AddBranch("A");
        _users.ValidateBranchForNewUser(_su, "A", Staff, b); // istisna yok
    }

    [Fact]
    public void BaskaFirmaSubesi_Reddedilir()
    {
        var bOther = AddBranch("OTHER");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.ValidateBranchForNewUser(_su, "A", Staff, bOther));
        Assert.Contains("geçersiz", ex.Message);
    }

    [Fact]
    public void SuperAdmin_ve_Admin_Muaf()
    {
        // Şube olmasa bile platform rolleri + admin şube gerektirmez
        _users.ValidateBranchForNewUser(_su, "A", new[] { RoleKeys.SuperAdmin }, branchId: null);
        _users.ValidateBranchForNewUser(_su, "A", new[] { RoleKeys.RestrictedSuperAdmin }, branchId: null);
        _users.ValidateBranchForNewUser(_su, "A", new[] { RoleKeys.CompanyAdmin }, branchId: null);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
