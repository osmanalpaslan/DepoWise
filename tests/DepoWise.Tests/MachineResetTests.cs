using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Makine "tanım sıfırlama" isteği (ADR-085) — SUNUCU tarafı. company_local_resets'ten (ADR-084) FARKI:
/// bu firma bağımsızdır — makine adı firmalar arası bir anahtardır (sync_devices (firma, makine adı)
/// çiftiyle tutulur) — "bu makinenin tanımını sıfırla" TÜM firmalardaki satırları siler. Masaüstünün bunu
/// nasıl uyguladığı (LoginViewModel.HandleMachineResetAsync + MachineResetLocalService) Desktop
/// projesindedir, bu testler yalnız SUNUCU tarafı davranışı doğrular.
/// </summary>
public class MachineResetTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly AuthService _auth;
    private readonly EnrollmentService _enroll;
    private readonly MachineResetService _reset;

    public MachineResetTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_mreset_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _enroll = new EnrollmentService(_factory, _clock);
        _reset = new MachineResetService(_factory, _clock);
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
        Assert.Null(_reset.GetStatus("DESKTOP-1"));
    }

    [Fact]
    public void Istek_DurumaYansir()
    {
        var su = SuperAdmin();
        var res = _reset.RequestReset(su, "DESKTOP-1");

        Assert.Equal("DESKTOP-1", res.MachineName);
        Assert.Equal(su.UserId, res.RequestedBy);
        var st = _reset.GetStatus("DESKTOP-1");
        Assert.NotNull(st);
        Assert.Equal(res.RequestedAt, st!.RequestedAt);
    }

    [Fact]
    public void TekrarIstek_ZamaniGunceller()
    {
        var su = SuperAdmin();
        var first = _reset.RequestReset(su, "DESKTOP-1");
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        var second = _reset.RequestReset(su, "DESKTOP-1");

        Assert.True(second.RequestedAt > first.RequestedAt);
        Assert.Equal(second.RequestedAt, _reset.GetStatus("DESKTOP-1")!.RequestedAt);
    }

    [Fact]
    public void SuperAdminOlmayan_IstekBirakamaz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = new BranchService(_factory, _clock).Create(su, new NewBranch("B-Merkez"), companyId: "B");
        _users.CreateUser(su, new NewUser("badm", "p12345", null, new[] { RoleKeys.CompanyAdmin },
            CompanyId: "B", BranchId: bBranch));
        var admin = _auth.Login("B", "badm", "p12345").Session!;

        Assert.Throws<ForbiddenException>(() => _reset.RequestReset(admin, "DESKTOP-1"));
        Assert.Null(_reset.GetStatus("DESKTOP-1"));
    }

    [Fact]
    public void BosMakineAdi_Reddedilir()
    {
        var su = SuperAdmin();
        Assert.Throws<ArgumentException>(() => _reset.RequestReset(su, "   "));
    }

    /// <summary>Aynı fiziksel makine iki firmada satıra sahip olabilir (test firması + asıl firma) — sıfırlama
    /// TÜM firmalardaki satırları siler, çünkü künye makine adına aittir, tek bir firmaya değil.</summary>
    [Fact]
    public void TumFirmalardakiKayitlarSilinir()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        _enroll.RegisterSelf(su.CompanyId, "DESKTOP-1");
        _enroll.RegisterSelf("B", "DESKTOP-1");

        _reset.RequestReset(su, "DESKTOP-1");

        var inA = _enroll.ListDevices(su, su.CompanyId);
        var inB = _enroll.ListDevices(su, "B");
        Assert.DoesNotContain(inA, d => d.Name == "DESKTOP-1");
        Assert.DoesNotContain(inB, d => d.Name == "DESKTOP-1");
    }

    /// <summary>Başka makinelerin kaydı etkilenmez — silme yalnız hedef makine adıyla eşleşen satırları alır.</summary>
    [Fact]
    public void BaskaMakineEtkilenmez()
    {
        var su = SuperAdmin();
        _enroll.RegisterSelf(su.CompanyId, "DESKTOP-1");
        _enroll.RegisterSelf(su.CompanyId, "DESKTOP-2");

        _reset.RequestReset(su, "DESKTOP-1");

        var devices = _enroll.ListDevices(su, su.CompanyId);
        Assert.DoesNotContain(devices, d => d.Name == "DESKTOP-1");
        Assert.Contains(devices, d => d.Name == "DESKTOP-2");
    }

    /// <summary>Sıfırlama sonrası aynı makine adıyla yeniden kayıt (yeni firmayla) sorunsuz — künye satırı
    /// silindiği için eski firmaya ait hiçbir iz kalmaz; makine sıfırdan tanımlanır.</summary>
    [Fact]
    public void SifirlamaSonrasi_YenidenKayitCalisir()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        _enroll.RegisterSelf(su.CompanyId, "DESKTOP-1");
        _reset.RequestReset(su, "DESKTOP-1");

        var result = _enroll.RegisterSelf("B", "DESKTOP-1");

        Assert.NotNull(result.DeviceId);
        var inB = _enroll.ListDevices(su, "B");
        Assert.Contains(inB, d => d.Name == "DESKTOP-1");
        var inA = _enroll.ListDevices(su, su.CompanyId);
        Assert.DoesNotContain(inA, d => d.Name == "DESKTOP-1");
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
