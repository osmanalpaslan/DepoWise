using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Kullanıcı oluştururken FİRMA SEÇİMİ (yalnız süper admin) + Firma Tanım'da firmaya bağlı ilk şube.
/// Kritik güvenlik: süper admin seçtiği firmaya kullanıcı açar; süper-admin-altı roller firma seçemez
/// (payload'da gönderseler bile kendi firmasına kilitlenir/reddedilir) ve başka firmanın şubesi atanamaz.
/// </summary>
public class UserCompanySelectorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly BranchService _branches;
    private readonly AuthService _auth;

    public UserCompanySelectorTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_ucs_" + Guid.NewGuid().ToString("N") + ".db");
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

    /// <summary>A firması + süper admin oturumu; ayrıca B firması ve B'nin şubesi.</summary>
    private (SessionContext Su, string BBranchId) Seed()
    {
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = _auth.Login("A", "root", "root123").Session!;
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = _branches.Create(su, new NewBranch("B-Merkez"), companyId: "B");
        return (su, bBranch);
    }

    [Fact]
    public void SuperAdmin_SectigiFirmayaKullaniciAcar()
    {
        var (su, bBranch) = Seed();

        var uid = _users.CreateUser(su, new NewUser("bpersonel", "p12345", "B Personeli",
            new[] { RoleKeys.Staff }, CompanyId: "B", BranchId: bBranch));

        // Kullanıcı B firmasında oluşmalı → B firmasından giriş yapabilmeli.
        var login = _auth.Login("B", "bpersonel", "p12345");
        Assert.NotNull(login.Session);
        Assert.Equal("B", login.Session!.CompanyId);
        Assert.Equal(uid, login.Session.UserId);
    }

    [Fact]
    public void SuperAdmin_BaskaFirmaSecip_KendiFirmasininSubesiniVerirse_Reddedilir()
    {
        var (su, _) = Seed();
        var aBranch = _branches.Create(su, new NewBranch("A-Merkez"));   // A firmasının şubesi

        // Ekranda firma B seçili ama şube listesi A'dan kalmış olsaydı bu çağrı yapılırdı → engellenmeli.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.ValidateBranchForNewUser(su, "B", new[] { RoleKeys.Staff }, aBranch));
        Assert.Contains("bu firmaya ait değil", ex.Message);
    }

    [Fact]
    public void FirmaAdmini_BaskaFirmaSecemez_PayloadReddedilir()
    {
        var (su, bBranch) = Seed();
        _users.CreateUser(su, new NewUser("badm", "p12345", null, new[] { RoleKeys.CompanyAdmin },
            CompanyId: "B", BranchId: bBranch));
        var admin = _auth.Login("B", "badm", "p12345").Session!;

        // B admini A firmasına kullanıcı açmayı denerse tenant guard reddeder (yetki/tenant ihlali).
        Assert.Throws<ForbiddenException>(() =>
            _users.CreateUser(admin, new NewUser("kacak", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A")));
        Assert.Throws<ForbiddenException>(() =>
            _users.ValidateBranchForNewUser(admin, "A", new[] { RoleKeys.Staff }, null));
    }

    [Fact]
    public void SubesizFirmaya_KullaniciAcilamaz_OnceSubeIstenir()
    {
        var (su, _) = Seed();
        _companies.Create(su, new NewCompany("C Firma"), explicitId: "C");   // şubesiz firma

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.ValidateBranchForNewUser(su, "C", new[] { RoleKeys.Staff }, null));
        Assert.Contains("henüz şube/şantiye yok", ex.Message);
    }

    /// <summary>Firma Tanım ekranı: firma + ilk şube birlikte oluşur → o firmaya hemen kullanıcı açılabilir.</summary>
    [Fact]
    public void YeniFirma_IlkSubesiyleOlusunca_KullaniciAcilabilir()
    {
        var (su, _) = Seed();
        var newCompany = _companies.Create(su, new NewCompany("D Firma"));
        var firstBranch = _branches.Create(su, new NewBranch("D-Şantiye", "site"), companyId: newCompany);

        // Şube firmaya bağlı olmalı ve şube zorunluluğu artık sağlanmalı (hata fırlatmamalı).
        var list = _branches.List(su, newCompany);
        Assert.Single(list);
        Assert.Equal("D-Şantiye", list[0].Name);
        _users.ValidateBranchForNewUser(su, newCompany, new[] { RoleKeys.Staff }, firstBranch);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
