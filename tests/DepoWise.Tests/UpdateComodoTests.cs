using System.Security.Cryptography;
using System.Text;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Update;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Update;
using Xunit;

namespace DepoWise.Tests;

public class UpdateComodoTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _installRoot;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public UpdateComodoTests()
    {
        var stamp = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_upd_" + stamp + ".db");
        _installRoot = Path.Combine(Path.GetTempPath(), "depowise_install_" + stamp);
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static byte[] Pkg(string content) => Encoding.UTF8.GetBytes(content);
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    // ---- SemVer ----
    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.2.0", "1.1.9", 1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    public void SemVer_Karsilastirma(string a, string b, int sign)
    {
        Assert.True(SemVer.TryParse(a, out var va));
        Assert.True(SemVer.TryParse(b, out var vb));
        Assert.Equal(sign, Math.Sign(va.CompareTo(vb)));
    }

    [Fact]
    public void SemVer_Gecersiz_Reddedilir()
    {
        Assert.False(SemVer.TryParse("1.0", out _));
        Assert.False(SemVer.TryParse("x.y.z", out _));
    }

    // ---- Release yönetimi ----
    [Fact]
    public void Release_Yayin_YalnizSuperAdmin_LatestDoner()
    {
        var users = new UserService(_factory, _clock);
        var su = new SessionContext(users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin), "A",
            new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var admin = new SessionContext("a", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var rel = new ReleaseService(_factory, _clock);

        var sha = Sha(Pkg("v110"));
        Assert.Throws<ForbiddenException>(() => rel.Publish(admin, new NewRelease("1.1.0", sha, 4)));
        rel.Publish(su, new NewRelease("1.1.0", sha, 4, MinSupportedVersion: "1.0.0", Signed: true));
        rel.Publish(su, new NewRelease("1.0.5", Sha(Pkg("v105")), 4));

        var latest = rel.Latest();
        Assert.Equal("1.1.0", latest!.Version); // en yüksek SemVer
    }

    [Fact]
    public void Release_GecersizChecksum_Reddedilir()
    {
        var users = new UserService(_factory, _clock);
        var su = new SessionContext(users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin), "A",
            new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var rel = new ReleaseService(_factory, _clock);
        Assert.Throws<ArgumentException>(() => rel.Publish(su, new NewRelease("1.0.0", "kisa", 1)));
    }

    // ---- Updater: kontrol ----
    [Fact]
    public void Updater_Check_GuncellemeVarMi_MinSupported_SignedWarning()
    {
        var svc = new UpdateService(_installRoot); // current 0.0.0
        var pkgSigned = new UpdatePackage("1.0.0", new string('A', 64), 10, "0.5.0", null, Signed: true);
        var c1 = svc.Check(pkgSigned);
        Assert.True(c1.UpdateAvailable);
        Assert.False(c1.SignedWarning);

        var pkgUnsigned = pkgSigned with { Signed = false, MinSupportedVersion = "1.0.0" };
        var c2 = svc.Check(pkgUnsigned);
        Assert.True(c2.SignedWarning);          // imzasız → şeffaf uyarı
        Assert.True(c2.BelowMinSupported);      // current 0.0.0 < min 1.0.0
    }

    // ---- Updater: bozuk paket kurulmaz ----
    [Fact]
    public void Updater_BozukPaket_Kurulmaz()
    {
        var svc = new UpdateService(_installRoot);
        var content = Pkg("yeni surum");
        var pkg = new UpdatePackage("1.0.0", "DEADBEEF" + new string('0', 56), content.Length, "0.0.0", null, true);
        Assert.Throws<UpdateFailedException>(() => svc.ApplyUpdate(pkg, content));
        Assert.Equal("0.0.0", svc.CurrentVersion()); // sürüm değişmedi
    }

    // ---- Updater: başarılı + yüzde ----
    [Fact]
    public void Updater_Basarili_Yuzde0_100_VeSurumGuncellenir()
    {
        var svc = new UpdateService(_installRoot);
        var content = Pkg("yeni surum 1.0.0");
        var pkg = new UpdatePackage("1.0.0", Sha(content), content.Length, "0.0.0", "ilk", true);

        var progress = new List<int>();
        svc.ApplyUpdate(pkg, content, progress.Add);
        Assert.Equal("1.0.0", svc.CurrentVersion());
        Assert.Equal(0, progress.First());
        Assert.Equal(100, progress.Last());
    }

    // ---- Updater: başarısız → rollback ----
    [Fact]
    public void Updater_KurulumHatasi_EskiSurumeDoner()
    {
        var svc = new UpdateService(_installRoot);
        // önce 1.0.0'a çık
        var v1 = Pkg("v1.0.0");
        svc.ApplyUpdate(new UpdatePackage("1.0.0", Sha(v1), v1.Length, "0.0.0", null, true), v1);
        Assert.Equal("1.0.0", svc.CurrentVersion());

        // 1.1.0 kurulumu installStep ile başarısız → rollback
        var v2 = Pkg("v1.1.0");
        var pkg2 = new UpdatePackage("1.1.0", Sha(v2), v2.Length, "0.0.0", null, true);
        Assert.Throws<UpdateFailedException>(() => svc.ApplyUpdate(pkg2, v2, installStep: () => false));
        Assert.Equal("1.0.0", svc.CurrentVersion()); // eski sürüm korunur
    }

    // ---- COMODO: host + gerçek DB yolu + kalıcılık ----
    [Fact]
    public void Comodo_GercekDBYolu_Mutlak_LocalAppData()
    {
        var path = AppPaths.DatabasePath("Development");
        Assert.True(Path.IsPathRooted(path));
        Assert.Contains("DepoWise", path);
        Assert.EndsWith("depowise.db", path);
    }

    [Fact]
    public void Comodo_KapatAc_VeriAyniDBdeKalir()
    {
        var users = new UserService(_factory, _clock);
        var admin = new SessionContext(users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin), "A",
            new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var materials = new MaterialService(_factory, _clock);
        var id = materials.Create(admin, new NewMaterial("M-1", "Kalıcı"));

        // Uygulamayı kapat (bağlantı havuzunu boşalt) + yeni factory ile aç (gerçek aynı DB dosyası)
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var reopened = new SqliteConnectionFactory(_dbPath);
        var materials2 = new MaterialService(reopened, _clock);
        Assert.Contains(materials2.List(admin, new PageRequest { Limit = 50 }).Items, m => m.Id == id);
        Assert.Equal(_dbPath, reopened.DatabasePath);
    }

    [Fact]
    public void Comodo_Health_WAL_WriteRead_Ok()
    {
        var health = new DatabaseHealth(_factory).CheckAsync().GetAwaiter().GetResult();
        Assert.True(health.Ok);
        Assert.Equal("wal", health.JournalMode, ignoreCase: true);
        Assert.True(health.WriteReadOk);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
        try { if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, true); } catch { }
    }
}
