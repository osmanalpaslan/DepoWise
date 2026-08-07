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

    // ── Sayfa boyutu + kolon genişlikleri (kullanıcı isteği 2026-07-18, ADR-089) ──
    [Fact]
    public void SayfaBoyutu_Kaydedilmediyse_NullDoner_KaydedilinceHatirlanir()
    {
        var u = User("A", "u1");
        Assert.Null(_prefs.GetPageSize(u, "materials"));   // → ekran 25 kullanır
        _prefs.SavePageSize(u, "materials", 100);
        Assert.Equal(100, _prefs.GetPageSize(u, "materials"));
    }

    /// <summary>Sayfa boyutu kaydı KOLON seçimini EZMEZ (columns_json '[]' = "ayar yok" → null).</summary>
    [Fact]
    public void SayfaBoyutu_KolonSecimiEzmez()
    {
        var u = User("A", "u1");
        _prefs.SavePageSize(u, "materials", 50);
        Assert.Null(_prefs.GetColumns(u, "materials"));   // hâlâ varsayılan (boş → null)

        _prefs.SaveColumns(u, "materials", new[] { "code", "stock" });
        _prefs.SavePageSize(u, "materials", 200);          // kolonları bozmamalı
        Assert.Equal(new[] { "code", "stock" }, _prefs.GetColumns(u, "materials"));
        Assert.Equal(200, _prefs.GetPageSize(u, "materials"));
    }

    [Fact]
    public void KolonGenislikleri_KaydedilipGeriDoner_KisiyeOzel()
    {
        var u1 = User("A", "u1");
        var u2 = User("A", "u2");
        _prefs.SaveWidths(u1, "materials", new Dictionary<string, int> { ["code"] = 120, ["name"] = 260 });

        var w = _prefs.GetWidths(u1, "materials");
        Assert.NotNull(w);
        Assert.Equal(120, w!["code"]);
        Assert.Equal(260, w["name"]);
        Assert.Null(_prefs.GetWidths(u2, "materials"));   // başka kullanıcı etkilenmez
    }

    // ── Birim 4 (ortak tablo bileşeni) — pinned + sıralama altyapısı + tek-sorguda hepsi ──
    [Fact]
    public void Pinned_KaydedilipGeriDoner_KisiyeOzel()
    {
        var u1 = User("A", "u1");
        var u2 = User("A", "u2");
        Assert.Null(_prefs.GetPinned(u1, "reports:general"));
        _prefs.SavePinned(u1, "reports:general", new[] { "code", "name" });
        Assert.Equal(new[] { "code", "name" }, _prefs.GetPinned(u1, "reports:general"));
        Assert.Null(_prefs.GetPinned(u2, "reports:general"));   // başka kullanıcı etkilenmez
    }

    [Fact]
    public void Sort_KaydedilipGeriDoner()
    {
        var u = User("A", "u1");
        Assert.Null(_prefs.GetSort(u, "reports:fuel"));
        _prefs.SaveSort(u, "reports:fuel", "amount", desc: true);
        var s = _prefs.GetSort(u, "reports:fuel");
        Assert.NotNull(s);
        Assert.Equal("amount", s!.Key);
        Assert.True(s.Desc);
    }

    /// <summary>Pinned/sort kaydı KOLON seçimini EZMEZ (UpsertField columns_json'ı '[]' bırakır → null).</summary>
    [Fact]
    public void PinnedVeSort_KolonSeciminiEzmez()
    {
        var u = User("A", "u1");
        _prefs.SavePinned(u, "reports:general", new[] { "code" });
        _prefs.SaveSort(u, "reports:general", "name", desc: false);
        Assert.Null(_prefs.GetColumns(u, "reports:general"));   // hâlâ varsayılan
    }

    /// <summary>KRİTİK (performans kuralı): tüm tercih TEK sorguda okunur; parça parça yazılanlar birleşir.</summary>
    [Fact]
    public void GetAll_TumTercihiTekSorgudaGetirir()
    {
        var u = User("A", "u1");
        _prefs.SaveColumns(u, "reports:general", new[] { "code", "name", "amount" });
        _prefs.SaveWidths(u, "reports:general", new Dictionary<string, int> { ["code"] = 100 });
        _prefs.SavePageSize(u, "reports:general", 50);
        _prefs.SavePinned(u, "reports:general", new[] { "code" });
        _prefs.SaveSort(u, "reports:general", "amount", desc: true);

        var all = _prefs.GetAll(u, "reports:general");
        Assert.Equal(new[] { "code", "name", "amount" }, all.Columns);
        Assert.Equal(50, all.PageSize);
        Assert.Equal(100, all.Widths!["code"]);
        Assert.Equal(new[] { "code" }, all.Pinned);
        Assert.Equal("amount", all.Sort!.Key);
        Assert.True(all.Sort.Desc);
    }

    [Fact]
    public void GetAll_HicKayitYoksa_TumAlanlarNull()
    {
        var u = User("A", "u1");
        var all = _prefs.GetAll(u, "reports:general");
        Assert.Null(all.Columns);
        Assert.Null(all.PageSize);
        Assert.Null(all.Widths);
        Assert.Null(all.Pinned);
        Assert.Null(all.Sort);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
