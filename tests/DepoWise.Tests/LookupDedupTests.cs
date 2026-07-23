using System.Linq;
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

    // ── 50 karakter sınırı (kullanıcı isteği 2026-07-18) ──
    [Fact]
    public void YeniTanim_50KarakterUstunde_Reddedilir()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var ok = new string('A', 50);
        var tooLong = new string('A', 51);
        Assert.NotNull(lk.AddUnit(s, ok));            // 50 tam sınır → geçer
        Assert.Throws<ArgumentException>(() => lk.AddUnit(s, tooLong));
    }

    [Fact]
    public void Rename_50KarakterUstunde_Reddedilir()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        Assert.Throws<ArgumentException>(() => lk.Rename(s, "units", id, new string('A', 51)));
        lk.Rename(s, "units", id, "Kilogram");        // geçerli
        Assert.Contains(lk.List(s, "units"), x => x.Name == "Kilogram");
    }

    // ── Fazladan boşluk normalize (kullanıcı isteği 2026-07-19, ADR-090) ──
    [Fact]
    public void YeniTanim_ArdisikBosluklar_TekBoslugaIner()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddCategory(s, "Yağ   Filtreleri");   // 3 boşluk
        Assert.Equal("Yağ Filtreleri", lk.ListCategories(s, null).First(x => x.Id == id).Name);
    }

    [Fact]
    public void Rename_ArdisikBosluklar_TekBoslugaIner()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        lk.Rename(s, "units", id, "  Kilo   Gram  ");
        Assert.Equal("Kilo Gram", lk.List(s, "units").First(x => x.Id == id).Name);
    }

    /// <summary>Migration050: geçmişte fazladan boşlukla girilmiş tanımları bir kez düzeltir.</summary>
    [Fact]
    public void Migration050_MevcutFazlaBosluklu_Duzeltir()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddCategory(s, "Filtreler");
        using (var conn = _factory.Create())
        using (var raw = conn.CreateCommand())
        {
            raw.CommandText = "UPDATE material_categories SET name='Yağ    Filtreleri' WHERE id=@id;";
            raw.AddWithValue("@id", id);
            raw.ExecuteNonQuery();
        }

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            new DepoWise.Infrastructure.Database.Migrations.Migration050_NormalizeLookupSpaces().Up(conn, tx);
            tx.Commit();
        }

        Assert.Equal("Yağ Filtreleri", lk.ListCategories(s, null).First(x => x.Id == id).Name);
    }

    // ── Sabit tanım / kilit (kullanıcı isteği 2026-07-19) ──
    [Fact]
    public void Kilitli_Rename_Reddedilir()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        lk.SetLocked(s, "units", id, true);
        Assert.Throws<ArgumentException>(() => lk.Rename(s, "units", id, "Kilogram"));
        Assert.True(lk.List(s, "units").First(x => x.Id == id).IsLocked);
    }

    [Fact]
    public void Kilitli_Delete_Reddedilir()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        lk.SetLocked(s, "units", id, true);
        Assert.Throws<ArgumentException>(() => lk.Delete(s, "units", id));
        Assert.Contains(lk.List(s, "units"), x => x.Id == id);
    }

    [Fact]
    public void KilitAcilinca_TekrarDuzenlenebilirSilinebilir()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        lk.SetLocked(s, "units", id, true);
        lk.SetLocked(s, "units", id, false);
        lk.Rename(s, "units", id, "Kilogram");   // kilit açıldı → serbest
        Assert.Contains(lk.List(s, "units"), x => x.Name == "Kilogram");
        lk.Delete(s, "units", id);
        Assert.DoesNotContain(lk.List(s, "units"), x => x.Id == id);
    }

    [Fact]
    public void Kilitliyken_YeniTanimEklemeEtkilenmez()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        lk.SetLocked(s, "units", id, true);
        var id2 = lk.AddUnit(s, "Kutu");   // + ile yeni tanım — kilitten bağımsız her zaman serbest
        Assert.NotNull(id2);
        Assert.Equal(2, lk.List(s, "units").Count);
    }

    [Fact]
    public void SuperAdminOlmayan_KilitDegistiremez()
    {
        var lk = new LookupService(_factory, _clock);
        var s = Su();
        var id = lk.AddUnit(s, "Adet");
        var normal = new SessionContext("u2", s.CompanyId, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => lk.SetLocked(normal, "units", id, true));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
