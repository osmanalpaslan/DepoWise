using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Firma "yerel sıfırlama" isteği (ADR-084) — SUNUCU tarafı. company_purges'ten (ADR-083, kalıcı silme)
/// FARKI: bu YIKICI/erişim-engelleyici DEĞİLDİR — firma sunucuda durur, yalnız "bir kerelik sıfırlama
/// isteği" kaydı bırakılır ve GetStatus ile okunabilir. Masaüstünün bunu nasıl uyguladığı (LocalPurgeService
/// + LocalResetService) Desktop projesindedir, bu testler yalnız SUNUCU tarafı davranışı doğrular.
/// </summary>
public class CompanyLocalResetTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly AuthService _auth;
    private readonly CompanyLocalResetService _reset;

    public CompanyLocalResetTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_lreset_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _reset = new CompanyLocalResetService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext SuperAdmin()
    {
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        return _auth.Login("A", "root", "root123").Session!;
    }

    [Fact]
    public void IstekYoksa_DurumNull()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        Assert.Null(_reset.GetStatus("B"));
    }

    [Fact]
    public void Istek_DurumaYansir()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        var res = _reset.RequestReset(su, "B");

        Assert.Equal("B", res.CompanyId);
        Assert.Equal(su.UserId, res.RequestedBy);
        var st = _reset.GetStatus("B");
        Assert.NotNull(st);
        Assert.Equal(res.RequestedAt, st!.RequestedAt);
    }

    [Fact]
    public void TekrarIstek_ZamaniGunceller()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        var first = _reset.RequestReset(su, "B");
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        var second = _reset.RequestReset(su, "B");

        Assert.True(second.RequestedAt > first.RequestedAt);
        Assert.Equal(second.RequestedAt, _reset.GetStatus("B")!.RequestedAt);
    }

    /// <summary>Masaüstünün karşılaştırma mantığı bu davranışa dayanır: yeni istek eskisinden büyük zaman
    /// damgasıyla gelir, böylece "zaten uyguladım mı?" kıyası doğru çalışır.</summary>
    [Fact]
    public void SuperAdminOlmayan_IstekBirakamaz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = new DepoWise.Infrastructure.Organization.BranchService(_factory, _clock)
            .Create(su, new NewBranch("B-Merkez"), companyId: "B");
        var badmId = _users.CreateUser(su, new NewUser("badm", "p12345", null, new[] { RoleKeys.CompanyAdmin },
            CompanyId: "B", BranchId: bBranch));
        var admin = _auth.Login("B", "badm", "p12345").Session!;

        Assert.Throws<ForbiddenException>(() => _reset.RequestReset(admin, "B"));
        Assert.Null(_reset.GetStatus("B"));
    }

    [Fact]
    public void OlmayanFirma_IstekReddedilir()
    {
        var su = SuperAdmin();
        Assert.Throws<InvalidOperationException>(() => _reset.RequestReset(su, "yok-boyle-firma"));
    }

    /// <summary>Kendi firmanı sıfırlaman YASAK DEĞİL (bu, ADR-083 kalıcı silmeden farklı) — çünkü sunucu
    /// verisi etkilenmez; süper admin kendi firmasının makinelerini de bu şekilde sıfırlatabilir.</summary>
    [Fact]
    public void KendiFirmasiIcinDeIstekBirakabilir()
    {
        var su = SuperAdmin();
        var res = _reset.RequestReset(su, su.CompanyId);
        Assert.Equal(su.CompanyId, res.CompanyId);
    }

    /// <summary>Tenant izolasyonu: B için istek, A'nın (veya başka firmanın) durumunu etkilemez.</summary>
    [Fact]
    public void BaskaFirmayaSizmaz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        _companies.Create(su, new NewCompany("C Firma"), explicitId: "C");

        _reset.RequestReset(su, "B");

        Assert.NotNull(_reset.GetStatus("B"));
        Assert.Null(_reset.GetStatus("C"));
        Assert.Null(_reset.GetStatus(su.CompanyId));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
