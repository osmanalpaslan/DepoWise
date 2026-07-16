using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Kullanıcının FİRMASI, oluşturulduktan sonra HİÇBİR işlemle değişmemelidir — süper admin dahil.
/// Bu, tenant izolasyonunun temelidir: eğer bir kullanıcı sonradan başka firmaya taşınabilseydi, o
/// kullanıcının geçmiş kayıtları (audit, işlemler) ile yeni firması arasında karışıklık doğardı.
///
/// Kod incelemesi (2026-07-16) doğruladı: users.company_id'yi güncelleyen HİÇBİR UPDATE yok — tüm
/// UPDATE'lerde company_id yalnız WHERE filtresinde geçiyor. Bu testler bunu davranışsal olarak sabitler:
/// şube atama, rol değiştirme, aktif/pasif, şifre değiştirme, tüm-şubeler yetkisi — hiçbiri firmayı etkilemez.
/// </summary>
public class UserCompanyImmutableTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly BranchService _branches;
    private readonly AuthService _auth;

    public UserCompanyImmutableTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_uci_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    /// <summary>Veritabanındaki gerçek company_id — servis katmanını atlayıp doğrudan okur (en güvenilir kanıt).</summary>
    private string CompanyIdOf(string userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT company_id FROM users WHERE id=$u;";
        cmd.Parameters.AddWithValue("$u", userId);
        return (string)cmd.ExecuteScalar()!;
    }

    private (SessionContext Su, string BId, string BBranch) Seed()
    {
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = _auth.Login("A", "root", "root123").Session!;
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = _branches.Create(su, new NewBranch("B-Merkez"), companyId: "B");
        var bUser = _users.CreateUser(su, new NewUser("bkul", "p12345", "B Kullanıcı",
            new[] { RoleKeys.Staff }, CompanyId: "B", BranchId: bBranch));
        return (su, bUser, bBranch);
    }

    [Fact]
    public void SubeAtama_FirmayiDegistirmez()
    {
        var (su, bUser, bBranch) = Seed();
        var otherBranch = _branches.Create(su, new NewBranch("B-Depo2"), companyId: "B");
        // Ürün akışında süper admin "Aktif Firma" ile B'ye geçer (bkz. select-company); AssignUser aktörün
        // OTURUM firmasını kullanır — bu yüzden B'ye geçmiş bir oturumla çağırıyoruz (gerçek akışı taklit eder).
        var suInB = _auth.CreateSessionForUser("B", su.UserId)!;

        _branches.AssignUser(suInB, bUser, otherBranch);

        Assert.Equal("B", CompanyIdOf(bUser));
    }

    [Fact]
    public void RolDegistirme_FirmayiDegistirmez()
    {
        var (su, bUser, _) = Seed();

        _users.SetRoles(su, bUser, new[] { RoleKeys.CompanyAdmin });

        Assert.Equal("B", CompanyIdOf(bUser));
    }

    [Fact]
    public void AktifPasif_FirmayiDegistirmez()
    {
        var (su, bUser, _) = Seed();

        _users.SetActive(su, bUser, false);
        Assert.Equal("B", CompanyIdOf(bUser));

        _users.SetActive(su, bUser, true);
        Assert.Equal("B", CompanyIdOf(bUser));
    }

    [Fact]
    public void SifreDegistirme_FirmayiDegistirmez()
    {
        var (su, bUser, _) = Seed();

        _users.ChangePassword(su, bUser, "yenisifre123");

        Assert.Equal("B", CompanyIdOf(bUser));
    }

    [Fact]
    public void TumSubelerYetkisi_FirmayiDegistirmez()
    {
        var (su, bUser, _) = Seed();

        _users.SetViewAllBranches(su, bUser, true);

        Assert.Equal("B", CompanyIdOf(bUser));
    }

    /// <summary>Süper admin dahil: firmayı doğrudan değiştirecek hiçbir servis metodu yoktur.
    /// Bu test, "firma değiştir" imzalı bir metod ileride eklenirse fark edilsin diye ismen arar.</summary>
    [Fact]
    public void UserServiceDe_FirmaDegistirenMetodYok()
    {
        var methods = typeof(UserService).GetMethods()
            .Where(m => m.DeclaringType == typeof(UserService))
            .Select(m => m.Name.ToLowerInvariant());

        Assert.DoesNotContain(methods, n =>
            (n.Contains("company") || n.Contains("firma")) &&
            (n.Contains("set") || n.Contains("change") || n.Contains("move") || n.Contains("transfer") || n.Contains("assign")));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
