using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Settings;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Liste ekranı kolon tercihi — KİŞİSEL (kullanıcı isteği 2026-07-17): "bu ayar işlemleri her
/// kullanıcıya özel olsun. farklı kullanıcıda bu özelleştirme görünmesin."</summary>
public class UserListPreferenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly UserListPreferenceService _prefs;

    public UserListPreferenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_listprefs_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _prefs = new UserListPreferenceService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext User(string company, string username)
    {
        var id = _users.EnsureInitialAdmin(company, username, "pass123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Fact]
    public void HicKaydedilmediyse_NullDoner()
    {
        var u = User("A", "u1");
        Assert.Null(_prefs.GetColumns(u, "materials"));
    }

    [Fact]
    public void Kaydedilen_AynenGeriDoner()
    {
        var u = User("A", "u1");
        _prefs.SaveColumns(u, "materials", new[] { "code", "name", "category" });

        var cols = _prefs.GetColumns(u, "materials");
        Assert.Equal(new[] { "code", "name", "category" }, cols);
    }

    [Fact]
    public void TekrarKayit_EskisininUzerineYazar()
    {
        var u = User("A", "u1");
        _prefs.SaveColumns(u, "materials", new[] { "code", "name" });
        _prefs.SaveColumns(u, "materials", new[] { "code", "brand", "supplier" });

        var cols = _prefs.GetColumns(u, "materials");
        Assert.Equal(new[] { "code", "brand", "supplier" }, cols);
    }

    /// <summary>KRİTİK: farklı kullanıcı birbirinin tercihini GÖRMEZ (kullanıcının açık isteği).</summary>
    [Fact]
    public void FarkliKullanici_BirbirininTercihiniGormez()
    {
        var u1 = User("A", "u1");
        var u2 = User("A", "u2");
        _prefs.SaveColumns(u1, "materials", new[] { "code", "name", "category" });

        Assert.Null(_prefs.GetColumns(u2, "materials"));
    }

    /// <summary>Aynı kullanıcının farklı liste ekranları (materials/vehicles) BİRBİRİNİ ETKİLEMEZ.</summary>
    [Fact]
    public void FarkliListeAnahtari_BirbiriniEtkilemez()
    {
        var u = User("A", "u1");
        _prefs.SaveColumns(u, "materials", new[] { "code", "name" });
        _prefs.SaveColumns(u, "vehicles", new[] { "internalCode", "plate" });

        Assert.Equal(new[] { "code", "name" }, _prefs.GetColumns(u, "materials"));
        Assert.Equal(new[] { "internalCode", "plate" }, _prefs.GetColumns(u, "vehicles"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
