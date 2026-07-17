using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Malzeme Listesi — kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17):
/// "filtre alanlarında içinde arama ve metnin başlangıcına göre arama yapmalı" + "gösterilecek kayıt
/// sayısı seçim alanı ve 1,2,3... şeklinde sayfa yapısı, her sayfada seçtiğim kadar kayıt gösterilmeli."
/// </summary>
public class MaterialGridTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly LookupService _lookups;
    private readonly OpeningStockService _opening;

    public MaterialGridTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mgrid_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext Admin(string company)
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(company, "admin_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    // ── İçerir + başlangıca göre öncelik ──

    [Fact]
    public void Filtre_Icerir_HerhangiBirYerdeEslesir()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("FLT-001", "Yağ Filtresi"));
        _materials.Create(a, new NewMaterial("FLT-002", "Hava Filtresi"));
        _materials.Create(a, new NewMaterial("BLT-010", "Fren Balatası"));

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Code: "flt"), 1, 50);

        Assert.Equal(2, res.TotalCount);
        Assert.All(res.Items, m => Assert.Contains("FLT", m.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Filtre_BaslangicaGoreOncelik_OneceBaslayanlarUstteCikar()
    {
        var a = Admin("A");
        // "yag" hem "Motor Yağı"nda (ortada) hem "Yağ Filtresi"nde (başta) geçer.
        _materials.Create(a, new NewMaterial("M-1", "Motor Yağı"));
        _materials.Create(a, new NewMaterial("M-2", "Yağ Filtresi"));

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Name: "yağ"), 1, 50);

        Assert.Equal(2, res.TotalCount);
        Assert.Equal("Yağ Filtresi", res.Items[0].Name);   // başta geçen önce
        Assert.Equal("Motor Yağı", res.Items[1].Name);
    }

    [Fact]
    public void Filtre_BuyukKucukHarfDuyarsiz()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("FLT-001", "Yağ Filtresi"));

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Code: "FLT"), 1, 50);
        Assert.Equal(1, res.TotalCount);
    }

    [Fact]
    public void Filtre_BirdenFazlaAlan_VeBirlestirilir()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("FLT-001", "Yağ Filtresi", Type: "Yedek Parça"));
        _materials.Create(a, new NewMaterial("FLT-002", "Hava Filtresi", Type: "Sarf Malzeme"));

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Code: "flt", Type: "yedek"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("FLT-001", res.Items[0].Code);
    }

    [Fact]
    public void Filtre_KategoriAdinaGore_JoinliKolonFiltrelenir()
    {
        var a = Admin("A");
        var catId = _lookups.AddCategory(a, "Filtreler");
        _materials.Create(a, new NewMaterial("FLT-001", "Yağ Filtresi", CategoryId: catId));
        _materials.Create(a, new NewMaterial("BLT-010", "Fren Balatası"));   // kategorisiz

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Category: "filtre"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("FLT-001", res.Items[0].Code);
        Assert.Equal("Filtreler", res.Items[0].Category);
    }

    [Fact]
    public void Filtre_DurumaGore_HesaplananKolonFiltrelenir()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Az Stoklu", MinStock: 10m));
        _opening.RecordOpening(a, m1, 2m, "op-1");   // 2 <= 10 → "Düşük Stok"
        _materials.Create(a, new NewMaterial("M-2", "Stoksuz"));   // hiç hareket yok → "Stok Yok"

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Status: "düşük"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("M-1", res.Items[0].Code);
        Assert.Equal("Düşük Stok", res.Items[0].Status);
    }

    [Fact]
    public void Filtre_UyumluArac_KorelasyonluAltSorgu()
    {
        var a = Admin("A");
        var v = _vehicles.Create(a, new NewVehicle("AR-1", "34 AAA 11"));
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Diğer"));
        _materials.SetCompatibleVehicles(a, m1, new[] { v });

        var res = _materials.SearchGrid(a, new MaterialGridFilter(CompatibleVehicles: "AR-1"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("M-1", res.Items[0].Code);
        Assert.Equal("AR-1", res.Items[0].CompatibleVehicles);
    }

    // ── Sayfalama ──

    [Fact]
    public void Sayfalama_ToplamKayitVeSayfaSayisiDogru()
    {
        var a = Admin("A");
        for (int i = 0; i < 55; i++)
            _materials.Create(a, new NewMaterial($"M-{i:D3}", $"Malzeme {i:D3}"));

        var page1 = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 25);
        Assert.Equal(55, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(25, page1.Items.Count);

        var page3 = _materials.SearchGrid(a, new MaterialGridFilter(), 3, 25);
        Assert.Equal(5, page3.Items.Count);   // 55 - 50 = 5 kalan
    }

    [Fact]
    public void Sayfalama_SayfalarArasindaTekrarVeAtlamaOlmaz()
    {
        var a = Admin("A");
        for (int i = 0; i < 30; i++)
            _materials.Create(a, new NewMaterial($"M-{i:D3}", $"Malzeme {i:D3}"));

        var seen = new HashSet<string>();
        for (int page = 1; page <= 3; page++)
        {
            var res = _materials.SearchGrid(a, new MaterialGridFilter(), page, 10);
            foreach (var item in res.Items)
                Assert.True(seen.Add(item.Code), $"{item.Code} birden fazla sayfada göründü.");
        }
        Assert.Equal(30, seen.Count);
    }

    [Fact]
    public void Sayfalama_HerSayfadaSecilenKadarKayitGosterilir()
    {
        var a = Admin("A");
        for (int i = 0; i < 12; i++)
            _materials.Create(a, new NewMaterial($"M-{i:D3}", $"Malzeme {i:D3}"));

        Assert.Equal(5, _materials.SearchGrid(a, new MaterialGridFilter(), 1, 5).Items.Count);
        Assert.Equal(10, _materials.SearchGrid(a, new MaterialGridFilter(), 1, 10).Items.Count);
    }

    [Fact]
    public void Sayfalama_GecersizSayfaVeBoyutKirpilir()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("M-1", "Malzeme"));

        var res = _materials.SearchGrid(a, new MaterialGridFilter(), page: 0, pageSize: 0);
        Assert.Equal(1, res.Page);
        Assert.Equal(1, res.PageSize);

        var big = _materials.SearchGrid(a, new MaterialGridFilter(), page: 1, pageSize: 10_000);
        Assert.Equal(500, big.PageSize);   // üst sınır
    }

    // ── Tenant izolasyonu ──

    [Fact]
    public void TenantIzolasyonu_BaskaFirmayaSizmaz()
    {
        var a = Admin("A");
        var b = Admin("B");
        _materials.Create(a, new NewMaterial("A-1", "A Firması Malzemesi"));
        _materials.Create(b, new NewMaterial("B-1", "B Firması Malzemesi"));

        var resA = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50);
        var resB = _materials.SearchGrid(b, new MaterialGridFilter(), 1, 50);

        Assert.Equal(1, resA.TotalCount);
        Assert.Equal("A-1", resA.Items[0].Code);
        Assert.Equal(1, resB.TotalCount);
        Assert.Equal("B-1", resB.Items[0].Code);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
