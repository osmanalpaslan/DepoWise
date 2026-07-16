using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// FİRMA KALICI SİLME (ADR-083) — geri alınamaz. Bu testler korumaların TUTTUĞUNU sabitler:
/// kendi firmasını silememe (kilitlenme koruması), yalnız süper admin, sistem rollerinin korunması,
/// künye (tombstone) yazılması ve başka firmanın verisine DOKUNULMAMASI.
/// </summary>
public class CompanyPurgeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly BranchService _branches;
    private readonly AuthService _auth;
    private readonly CompanyPurgeService _purge;
    private readonly SpecialCodeService _code;

    public CompanyPurgeTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_purge_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _purge = new CompanyPurgeService(_factory, _clock);
        _code = new SpecialCodeService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    /// <summary>A firması (süper admin) + B firması (şube + kullanıcı dolu).</summary>
    private (SessionContext Su, string BBranch, string BUser) Seed()
    {
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = _auth.Login("A", "root", "root123").Session!;
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = _branches.Create(su, new NewBranch("B-Merkez"), companyId: "B");
        var bUser = _users.CreateUser(su, new NewUser("bkul", "p12345", "B Kullanıcı",
            new[] { RoleKeys.Staff }, CompanyId: "B", BranchId: bBranch));
        return (su, bBranch, bUser);
    }

    [Fact]
    public void KendiFirmasini_KaliciSilemez()
    {
        var (su, _, _) = Seed();

        var ex = Assert.Throws<InvalidOperationException>(() => _purge.Purge(su, su.CompanyId));
        Assert.Contains("Kendi bağlı olduğunuz firmayı", ex.Message);

        // Firma DURUYOR ve süper admin hâlâ girebiliyor (kilitlenme yok — ADR-064 dersi).
        Assert.NotNull(_purge.FindName(su.CompanyId));
        Assert.NotNull(_auth.Login("A", "root", "root123").Session);
    }

    [Fact]
    public void SuperAdminOlmayan_KaliciSilemez()
    {
        var (su, bBranch, _) = Seed();
        _users.CreateUser(su, new NewUser("badm", "p12345", null, new[] { RoleKeys.CompanyAdmin },
            CompanyId: "B", BranchId: bBranch));
        var admin = _auth.Login("B", "badm", "p12345").Session!;

        Assert.Throws<ForbiddenException>(() => _purge.Purge(admin, "B"));
        Assert.NotNull(_purge.FindName("B"));   // firma duruyor
    }

    [Fact]
    public void Purge_FirmayiVeVerisiniSiler_KunyeBirakir()
    {
        var (su, _, _) = Seed();

        var res = _purge.Purge(su, "B");

        Assert.Equal("B Firma", res.CompanyName);
        Assert.True(res.RowsDeleted > 0);
        Assert.Null(_purge.FindName("B"));                       // firma gitti
        Assert.Empty(_branches.List(su, "B"));                   // şubeleri gitti
        // Künye kaldı → masaüstü eşitlemede silmeyi öğrenir (yoksa yerel kopya veriyi diriltirdi).
        var k = _purge.GetPurge("B");
        Assert.NotNull(k);
        Assert.Equal("B Firma", k!.CompanyName);
        Assert.Equal(su.UserId, k.PurgedBy);
    }

    [Fact]
    public void Purge_SonrasiFirmaKullanicisi_GirisYapamaz()
    {
        var (su, _, _) = Seed();
        Assert.NotNull(_auth.Login("B", "bkul", "p12345").Session);   // silmeden ÖNCE girebiliyordu

        _purge.Purge(su, "B");

        // "Firma silindikten sonra firma kullanıcıları verilere erişememeli" (kullanıcı şartı).
        Assert.Null(_auth.Login("B", "bkul", "p12345").Session);
    }

    [Fact]
    public void Purge_BaskaFirmaninVerisine_DOKUNMAZ()
    {
        var (su, _, _) = Seed();
        var aBranch = _branches.Create(su, new NewBranch("A-Merkez"));   // A firması (aktörün firması)
        _companies.Create(su, new NewCompany("C Firma"), explicitId: "C");
        var cBranch = _branches.Create(su, new NewBranch("C-Merkez"), companyId: "C");

        _purge.Purge(su, "B");

        // A ve C dokunulmadan durmalı; yalnız B gitmeli.
        Assert.NotNull(_purge.FindName("A"));
        Assert.NotNull(_purge.FindName("C"));
        Assert.Single(_branches.List(su, "A"));
        Assert.Single(_branches.List(su, "C"));
        Assert.Equal("C-Merkez", _branches.List(su, "C")[0].Name);
        Assert.NotNull(_auth.Login("A", "root", "root123").Session);   // süper admin hâlâ girebiliyor
    }

    /// <summary>Sistem rolleri (company_id NULL) TÜM firmalar içindir — purge onları silmemeli,
    /// aksi halde purge'den sonra hiçbir firmada rol atanamaz.</summary>
    [Fact]
    public void Purge_SistemRollerini_Korur()
    {
        var (su, _, _) = Seed();
        _purge.Purge(su, "B");

        // Purge'den SONRA yeni firma + kullanıcı açılabilmeli (roller ayakta).
        _companies.Create(su, new NewCompany("D Firma"), explicitId: "D");
        var dBranch = _branches.Create(su, new NewBranch("D-Merkez"), companyId: "D");
        var uid = _users.CreateUser(su, new NewUser("dkul", "p12345", null, new[] { RoleKeys.Staff },
            CompanyId: "D", BranchId: dBranch));
        Assert.False(string.IsNullOrEmpty(uid));
        Assert.NotNull(_auth.Login("D", "dkul", "p12345").Session);
    }

    // ── Özel kod ──────────────────────────────────────────────────────────────
    [Fact]
    public void OzelKod_YoksaDogrulamaDaimaFalse_KisaKodReddedilir()
    {
        var (su, _, _) = Seed();

        Assert.False(_code.HasCode(su.UserId));
        Assert.False(_code.Verify(su, "herhangi"));                 // kod yok → fail-closed
        Assert.Throws<ArgumentException>(() => _code.SetCode(su, "abc"));   // çok kısa

        _code.SetCode(su, "gizli-kod-1");
        Assert.True(_code.HasCode(su.UserId));
        Assert.True(_code.Verify(su, "gizli-kod-1"));
        Assert.False(_code.Verify(su, "yanlis-kod"));
    }

    /// <summary>Masaüstü eşitleme adımı: künye YALNIZ silinmiş firma için döner. Silinmemiş firmada null
    /// olmalı — aksi halde çalışan makineler kendi verisini boşuna siler (felaket).</summary>
    [Fact]
    public void PurgeStatus_YalnizSilinmisFirmada_KunyeDoner()
    {
        var (su, _, _) = Seed();
        _companies.Create(su, new NewCompany("C Firma"), explicitId: "C");

        Assert.Null(_purge.GetPurge("B"));   // henüz silinmedi
        Assert.Null(_purge.GetPurge("C"));

        _purge.Purge(su, "B");

        Assert.NotNull(_purge.GetPurge("B"));   // masaüstü: "firmam silinmiş" → yerel temizlik
        Assert.Null(_purge.GetPurge("C"));      // C makineleri ETKİLENMEZ
        Assert.Null(_purge.GetPurge(su.CompanyId));
    }

    [Fact]
    public void OzelKod_SuperAdminOlmayanda_Yok()
    {
        var (su, bBranch, _) = Seed();
        _users.CreateUser(su, new NewUser("badm", "p12345", null, new[] { RoleKeys.CompanyAdmin },
            CompanyId: "B", BranchId: bBranch));
        var admin = _auth.Login("B", "badm", "p12345").Session!;

        Assert.Throws<ForbiddenException>(() => _code.SetCode(admin, "gizli-kod-1"));
        Assert.False(_code.Verify(admin, "gizli-kod-1"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
