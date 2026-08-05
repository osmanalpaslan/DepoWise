using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
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

    // ── A1 (Aurora): "Yalnız kritik" (criticalOnly) — stok <= min stok, min stok > 0 ──
    [Fact]
    public void YalnizKritik_StokMinAltinda_Olanlari_Getirir()
    {
        var a = Admin("A");
        var kritik = _materials.Create(a, new NewMaterial("K-1", "Kritik", MinStock: 10m));
        _opening.RecordOpening(a, kritik, 3m, "op-k");            // 3 <= 10 → kritik
        var stoksuzMinli = _materials.Create(a, new NewMaterial("K-2", "StoksuzMinli", MinStock: 5m)); // stok 0 <= 5 → kritik
        var bol = _materials.Create(a, new NewMaterial("B-1", "Bol", MinStock: 10m));
        _opening.RecordOpening(a, bol, 50m, "op-b");             // 50 > 10 → kritik DEĞİL
        _materials.Create(a, new NewMaterial("M-0", "Minsiz"));   // min 0 → kritik DEĞİL (min tanımsız)

        // criticalOnly=false → hepsi (4)
        Assert.Equal(4, _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, criticalOnly: false).TotalCount);

        // criticalOnly=true → yalnız K-1 ve K-2
        var res = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, criticalOnly: true);
        Assert.Equal(2, res.TotalCount);
        Assert.Contains(res.Items, m => m.Code == "K-1");
        Assert.Contains(res.Items, m => m.Code == "K-2");
        Assert.DoesNotContain(res.Items, m => m.Code == "B-1" || m.Code == "M-0");

        // Export yolu (SearchGridAll) da aynı filtreyi uygular
        var all = _materials.SearchGridAll(a, new MaterialGridFilter(), criticalOnly: true);
        Assert.Equal(2, all.Count);
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

    // ── Sayısal filtre (kullanıcı isteği 2026-07-18): "stokta sadece 5 olanları listelemek istiyorum
    // ama bütün içinde 5 olan malzemeler listeleniyor" — Stok/Min Stok/Birim Fiyat artık SAYISAL. ──

    [Fact]
    public void SayisalFiltre_TamSayi_IcerenlerDegilSadeceEsitOlanlarGelir()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Beş Stoklu"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Onbeş Stoklu"));
        var m3 = _materials.Create(a, new NewMaterial("M-3", "Elli Stoklu"));
        _opening.RecordOpening(a, m1, 5m, "op-1");
        _opening.RecordOpening(a, m2, 15m, "op-2");
        _opening.RecordOpening(a, m3, 50m, "op-3");

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Stock: "5"), 1, 50);

        Assert.Equal(1, res.TotalCount);   // eskiden "içerir" mantığıyla 15 ve 50 de gelirdi
        Assert.Equal("M-1", res.Items[0].Code);
    }

    [Fact]
    public void SayisalFiltre_NegatifTamSayi_AcilisNegatifStokEslesir()
    {
        // ADR-086: açılış stoğu negatif olabilir (devralınan eksik stok).
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Eksi Stoklu"));
        _materials.Create(a, new NewMaterial("M-2", "Sıfır Stoklu"));
        _opening.RecordOpening(a, m1, -9m, "op-neg");

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Stock: "-9"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("M-1", res.Items[0].Code);
    }

    [Theory]
    [InlineData(">10", new[] { "M-2", "M-3" })]
    [InlineData("<10", new[] { "M-1" })]
    [InlineData(">=15", new[] { "M-2", "M-3" })]
    [InlineData("<=15", new[] { "M-1", "M-2" })]
    public void SayisalFiltre_Karsilastirma(string term, string[] expectedCodes)
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Az"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Orta"));
        var m3 = _materials.Create(a, new NewMaterial("M-3", "Çok"));
        _opening.RecordOpening(a, m1, 5m, "op-1");
        _opening.RecordOpening(a, m2, 15m, "op-2");
        _opening.RecordOpening(a, m3, 50m, "op-3");

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Stock: term), 1, 50);

        Assert.Equal(expectedCodes.OrderBy(x => x), res.Items.Select(i => i.Code).OrderBy(x => x));
    }

    [Fact]
    public void SayisalFiltre_Aralik_IkiUcaDahil()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Az"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Orta"));
        var m3 = _materials.Create(a, new NewMaterial("M-3", "Çok"));
        _opening.RecordOpening(a, m1, 5m, "op-1");
        _opening.RecordOpening(a, m2, 15m, "op-2");
        _opening.RecordOpening(a, m3, 50m, "op-3");

        var res = _materials.SearchGrid(a, new MaterialGridFilter(Stock: "5-15"), 1, 50);

        Assert.Equal(2, res.TotalCount);
        Assert.Equal(new[] { "M-1", "M-2" }, res.Items.Select(i => i.Code).OrderBy(x => x));
    }

    [Fact]
    public void SayisalFiltre_OndalikVirgul_NoktaGibiCalisir()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Ondalık", UnitPrice: 12.5m));
        _materials.Create(a, new NewMaterial("M-2", "Diğer", UnitPrice: 20m));

        var res = _materials.SearchGrid(a, new MaterialGridFilter(UnitPrice: "12,5"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("M-1", res.Items[0].Code);
    }

    /// <summary>Sayısal söz dizimiyle eşleşmeyen bir şey yazılırsa (örn. yanlışlıkla harf) filtre kutusu
    /// SESSİZCE hiçbir şey yapmaz DEĞİL — eski "içerir" davranışına düşer (biçimlendirilmiş metne göre).</summary>
    [Fact]
    public void SayisalFiltre_TaninmayanSozDizimi_IcerirAramasinaDuser()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Test"));
        _opening.RecordOpening(a, m1, 5m, "op-1");   // stok_text = "5.00"

        // "5." tanınan üç sayısal söz dizimine de (tam/karşılaştırma/aralık) uymaz → "5.00" metninde "içerir" araması.
        var res = _materials.SearchGrid(a, new MaterialGridFilter(Stock: "5."), 1, 50);

        Assert.Equal(1, res.TotalCount);
    }

    // ── Sıralama (kullanıcı isteği 2026-07-18: başlığa tıklayınca A→Z/Z→A, sayısalda küçük→büyük) ──

    [Fact]
    public void Siralama_Metin_AZ_VeZA()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("M-1", "Çınar"));
        _materials.Create(a, new NewMaterial("M-2", "Armut"));
        _materials.Create(a, new NewMaterial("M-3", "Zeytin"));

        var asc = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, sortColumn: MaterialListColumns.Name, sortDesc: false);
        Assert.Equal(new[] { "Armut", "Çınar", "Zeytin" }, asc.Items.Select(i => i.Name));

        var desc = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, sortColumn: MaterialListColumns.Name, sortDesc: true);
        Assert.Equal(new[] { "Zeytin", "Çınar", "Armut" }, desc.Items.Select(i => i.Name));
    }

    [Fact]
    public void Siralama_Sayisal_KucuktenBuyuge_MetinDegilSayiOlarak()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Az", MinStock: 9m));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Orta", MinStock: 10m));
        var m3 = _materials.Create(a, new NewMaterial("M-3", "Çok", MinStock: 100m));

        var asc = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, sortColumn: MaterialListColumns.MinStock, sortDesc: false);
        // Sayısal sıralama: 9 < 10 < 100 (metin sıralaması olsaydı "10","100","9" olurdu).
        Assert.Equal(new[] { "M-1", "M-2", "M-3" }, asc.Items.Select(i => i.Code));

        var desc = _materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, sortColumn: MaterialListColumns.MinStock, sortDesc: true);
        Assert.Equal(new[] { "M-3", "M-2", "M-1" }, desc.Items.Select(i => i.Code));
    }

    // ── "Excel'e Aktar" (kullanıcı isteği 2026-07-19): filtrelenmiş TÜM sonuçlar, sayfalama sınırı YOK ──

    [Fact]
    public void SearchGridAll_SayfalamaSinirinAsar_TumFiltrelenmisSonuclariDoner()
    {
        var a = Admin("A");
        for (int i = 0; i < 650; i++)   // 500'lük SearchGrid sayfa sınırını aşan hacim
            _materials.Create(a, new NewMaterial($"FLT-{i:D3}", $"Filtre {i:D3}"));
        _materials.Create(a, new NewMaterial("OTH-1", "Diğer"));   // filtreye uymayan

        var all = _materials.SearchGridAll(a, new MaterialGridFilter(Code: "FLT"));

        Assert.Equal(650, all.Count);
        Assert.All(all, m => Assert.StartsWith("FLT", m.Code));
    }

    [Fact]
    public void ToTableModel_KolonSirasiKatalogaGoreDogru()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("M-1", "Filtre", Type: "Yedek Parça", UnitPrice: 10m));
        var rows = _materials.SearchGridAll(a, new MaterialGridFilter());

        var table = MaterialService.ToTableModel(rows);

        Assert.Equal(MaterialListColumns.All.Count, table.Headers.Count);
        Assert.Equal("Kod", table.Headers[0]);
        Assert.Equal("Ad", table.Headers[1]);
        Assert.Single(table.Rows);
        Assert.Equal("M-1", table.Rows[0][0]);
        Assert.Equal("Filtre", table.Rows[0][1]);
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
