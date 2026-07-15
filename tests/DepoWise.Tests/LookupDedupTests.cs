using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Tanım tekilleştirme (madde 9+11): aynı ad (harf duyarsız) tek Tanım ID'ye eşlenir;
/// aynı isimli birden çok tanım oluşmaz. Ayırt edici (parent_id/brand_type) farklıysa ayrı tanım olur.</summary>
public class LookupDedupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public LookupDedupTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_lk_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext Su()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        return new AuthService(_factory, _clock).Login("A", "root", "root123").Session!;
    }

    [Fact]
    public void AyniAd_TekTanimID_HarfDuyarsiz()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var a = lk.AddUnit(s, "Adet");
        var b = lk.AddUnit(s, "adet");   // harf duyarsız aynı → aynı id
        var c = lk.AddUnit(s, "  Adet "); // trim → aynı id
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Single(lk.List(s, "units"));
    }

    [Fact]
    public void AyiritEdici_Farkli_AyriTanim()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var mat = lk.AddBrand(s, "Bosch", "material");
        var veh = lk.AddBrand(s, "Bosch", "vehicle"); // farklı brand_type → ayrı tanım
        Assert.NotEqual(mat, veh);

        // Alt kategori: aynı ad farklı üst kategori → ayrı; aynı üst → tek.
        var ustA = lk.AddCategory(s, "Üst A");
        var ustB = lk.AddCategory(s, "Üst B");
        var altA = lk.AddCategory(s, "Ortak", ustA);
        var altA2 = lk.AddCategory(s, "Ortak", ustA); // aynı üst → tek
        var altB = lk.AddCategory(s, "Ortak", ustB);  // farklı üst → ayrı
        Assert.Equal(altA, altA2);
        Assert.NotEqual(altA, altB);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
