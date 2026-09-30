using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Kullanıcı istekleri 2026-09-30 — davranışı kanıtlayan hedefli testler:
/// Giriş-Çıkış DÜZELTME (stok defteri: iptal + yeni kayıt, tek transaction), yakıt sayaç farkı/tüketim,
/// yakıt aylık özeti, uyumlu malzemelerin kategori grupları, kullanılan malzemelerin ayrıntılı metni.
/// </summary>
public class KullaniciIstekleri20260930Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _a;
    private readonly string _m1, _m2, _sube;

    private sealed class TestClock : IClock
    {
        // 2026-09-15 12:00 UTC
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    }

    public KullaniciIstekleri20260930Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_0930_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f, _clock).EnsureInitialAdmin("A", "admin_0930", "Test!2026", RoleKeys.CompanyAdmin);
        _a = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _stock = new StockService(_f, _clock);
        _opening = new OpeningStockService(_f, _clock);
        var mats = new MaterialService(_f, _clock);
        _sube = new BranchService(_f, _clock).Create(_a, new NewBranch("Merkez"));
        _m1 = mats.Create(_a, new NewMaterial("F-12", "Yağ Filtresi"));
        _m2 = mats.Create(_a, new NewMaterial("Y-5", "Motor Yağı"));
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string Op() => Guid.NewGuid().ToString("N");
    private decimal Bakiye(string m) => _stock.GetBalanceAt(_a, m, _sube);
    private void Acilis(string m, decimal q) => _opening.RecordOpening(_a, m, q, Op(), branchId: _sube);

    private string DocStatus(string docId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM stock_documents WHERE id=@id;";
        cmd.AddWithValue("@id", docId);
        return (string)cmd.ExecuteScalar()!;
    }

    // ═══════════ GİRİŞ-ÇIKIŞ DÜZELTME ═══════════

    [Fact]
    public void Giris_duzeltme_eskiyi_iptal_eder_yenisini_yazar_bakiye_dogru()
    {
        var doc = _stock.ReceiveIn(_a, new[] { new StockLine(_m1, 10m) }, Op(), branchId: _sube);
        Assert.Equal(10m, Bakiye(_m1));

        var yeni = _stock.CorrectDocument(_a, doc.DocumentId,
            new StockCorrection(new[] { new StockLine(_m1, 12m) }, _sube), Op(), "miktar yanlış");

        Assert.Equal(12m, Bakiye(_m1));
        Assert.Equal("cancelled", DocStatus(doc.DocumentId));
        Assert.Equal("active", DocStatus(yeni.DocumentId));
        Assert.NotEqual(doc.DocumentId, yeni.DocumentId);
    }

    [Fact]
    public void Cikis_artirma_duzeltmesi_ara_adimda_negatif_kalkanina_takilmaz()
    {
        // 15 var → 10 çıkış → 5 kaldı. 10'u 12'ye düzeltmek NET olarak geçerlidir (5+10−12=3).
        // Yeni çıkış eskinin iadesinden ÖNCE yazılsaydı (5−12) negatif kalkanı işlemi reddederdi.
        Acilis(_m1, 15m);
        var doc = _stock.IssueOut(_a, new[] { new StockLine(_m1, 10m) }, Op(), branchId: _sube);
        Assert.Equal(5m, Bakiye(_m1));

        _stock.CorrectDocument(_a, doc.DocumentId, new StockCorrection(new[] { new StockLine(_m1, 12m) }, _sube), Op(), "eksik yazılmış");

        Assert.Equal(3m, Bakiye(_m1));
        Assert.Equal("cancelled", DocStatus(doc.DocumentId));
    }

    [Fact]
    public void Stogu_eksiye_dusuren_duzeltme_reddedilir_ve_hicbir_sey_degismez()
    {
        Acilis(_m1, 15m);
        var doc = _stock.IssueOut(_a, new[] { new StockLine(_m1, 10m) }, Op(), branchId: _sube);

        Assert.ThrowsAny<Exception>(() => _stock.CorrectDocument(_a, doc.DocumentId,
            new StockCorrection(new[] { new StockLine(_m1, 20m) }, _sube), Op(), "fazla"));

        Assert.Equal(5m, Bakiye(_m1));                       // rollback: bakiye aynen
        Assert.Equal("active", DocStatus(doc.DocumentId));   // eski belge iptal EDİLMEDİ
    }

    [Fact]
    public void Cok_malzemeli_cikis_duzeltmesi_malzeme_degistirebilir()
    {
        Acilis(_m1, 10m); Acilis(_m2, 10m);
        var doc = _stock.IssueOut(_a, new[] { new StockLine(_m1, 2m), new StockLine(_m2, 3m) }, Op(), branchId: _sube);

        _stock.CorrectDocument(_a, doc.DocumentId, new StockCorrection(new[] { new StockLine(_m2, 4m) }, _sube), Op(), "yanlış malzeme");

        Assert.Equal(10m, Bakiye(_m1));   // M1 çıkışı tamamen geri alındı
        Assert.Equal(6m, Bakiye(_m2));    // M2: 10 − 4
    }

    [Fact]
    public void Ayni_islem_kimligiyle_tekrar_cagri_ikinci_kayit_uretmez()
    {
        var doc = _stock.ReceiveIn(_a, new[] { new StockLine(_m1, 10m) }, Op(), branchId: _sube);
        var op = Op();
        var d = new StockCorrection(new[] { new StockLine(_m1, 7m) }, _sube);
        var y1 = _stock.CorrectDocument(_a, doc.DocumentId, d, op, "düzeltme");
        var y2 = _stock.CorrectDocument(_a, doc.DocumentId, d, op, "düzeltme");

        Assert.Equal(y1.DocumentId, y2.DocumentId);
        Assert.Equal(7m, Bakiye(_m1));
    }

    [Fact]
    public void Baska_modulden_dogan_belge_duzeltilemez_ve_gerekcesi_doner()
    {
        Acilis(_m1, 10m);
        // Satın alma mal kabulü "po:" işlem kimliğiyle yazar (PurchaseOrderService) — burada aynı iz üretilir.
        var doc = _stock.ReceiveIn(_a, new[] { new StockLine(_m1, 5m) }, "po:" + Op(), branchId: _sube);

        var ayr = _stock.GetDocumentForEdit(_a, doc.DocumentId);
        Assert.False(ayr.CanEdit);
        Assert.Contains("Satın Alma", ayr.BlockedReason);
        Assert.ThrowsAny<Exception>(() => _stock.CorrectDocument(_a, doc.DocumentId,
            new StockCorrection(new[] { new StockLine(_m1, 6m) }, _sube), Op(), "x"));
        Assert.Equal(15m, Bakiye(_m1));
    }

    [Fact]
    public void Duzeltme_formu_kaydin_degerleriyle_dolar()
    {
        var doc = _stock.ReceiveIn(_a, new[] { new StockLine(_m1, 10m, 25m) }, Op(), branchId: _sube,
            note: "not", invoiceNo: "IRS-1");
        var ayr = _stock.GetDocumentForEdit(_a, doc.DocumentId);

        Assert.True(ayr.CanEdit);
        Assert.Equal("in", ayr.DocType);
        Assert.Equal(_sube, ayr.BranchId);
        Assert.Equal("IRS-1", ayr.InvoiceNo);
        var l = Assert.Single(ayr.Lines);
        Assert.Equal(("F-12", 10m, 25m), (l.Code, l.Quantity, l.UnitPrice));
    }

    // ═══════════ YAKIT ═══════════

    [Fact]
    public void Yakit_sayac_farki_ve_tuketim_raporla_ayni_formul()
    {
        Assert.Equal(125m, FuelMath.MeterDiff(1000m, 1125m));
        Assert.Null(FuelMath.MeterDiff(1000m, null));
        Assert.Equal(0.4m, FuelMath.Consumption(50m, 125m));
        Assert.Null(FuelMath.Consumption(50m, 0m));           // fark 0 → tanımsız
        Assert.Null(FuelMath.Consumption(50m, -10m));         // geri sayaç → tanımsız
        Assert.Equal("0,40 L/km", FuelMath.ConsumptionText(50m, 125m, "km"));
        Assert.Equal("6,25 L/Saat", FuelMath.ConsumptionText(50m, 8m, "hour"));
        Assert.Equal("—", FuelMath.DiffText(null, "km"));
    }

    [Fact]
    public void Yakit_aylik_ozet_ay_hafta_gun_gruplar_iptali_saymaz()
    {
        var araclar = new VehicleService(_f, _clock);
        var yakit = new FuelService(_f, _clock);
        var arac = araclar.Create(_a, new NewVehicle("KAM-01", "06 KAM 01", CurrentMeter: 1000m));
        yakit.AddDepotEntry(_a, new NewDepotEntry(1000m, 40m, "TRY", EntryDate: Gun(2026, 8, 1)), Op());
        yakit.Distribute(_a, new NewDistribution(arac, 50m, 1100m, DistributionDate: Gun(2026, 9, 1)), Op());
        yakit.Distribute(_a, new NewDistribution(arac, 30m, 1200m, DistributionDate: Gun(2026, 9, 1)), Op());
        yakit.Distribute(_a, new NewDistribution(arac, 20m, 1300m, DistributionDate: Gun(2026, 9, 10)), Op());
        var iptal = yakit.Distribute(_a, new NewDistribution(arac, 99m, 1400m, DistributionDate: Gun(2026, 9, 11)), Op());
        yakit.Distribute(_a, new NewDistribution(arac, 40m, 1500m, DistributionDate: Gun(2026, 8, 20)), Op());
        yakit.CancelDistribution(_a, iptal, "test");

        var ozet = yakit.MonthlySummary(_a, months: 2);
        Assert.Equal(2, ozet.Months.Count);
        var eylul = ozet.Months[0];                          // en yeni üstte
        Assert.Equal((2026, 9), (eylul.Year, eylul.Month));
        Assert.Equal(100m, eylul.Liters);                    // iptal edilen 99 L sayılmaz
        Assert.Equal(3, eylul.Count);
        Assert.Equal(2, eylul.Days.Count);                   // 01.09 (80 L) ve 10.09 (20 L)
        Assert.Equal(80m, eylul.Days[0].Liters);
        Assert.Equal(Math.Round(100m / 15, 2), eylul.DailyAverage);   // bugün 15 Eylül → 15 gün
        Assert.Equal(100m, eylul.Weeks.Sum(w => w.Liters));
        Assert.Equal(40m, ozet.Months[1].Liters);            // Ağustos
    }

    private static long Gun(int y, int m, int d) => new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    // ═══════════ UYUMLU MALZEMELER / KULLANILAN MALZEMELER ═══════════

    [Fact]
    public void Uyumlu_malzemeler_kategoriye_gore_gruplanir_kategorisiz_sonda()
    {
        var g = MaterialStockGroups.Group(new[]
        {
            new MaterialStock("1", "B-2", "Balata", 1, "Fren › Balata"),
            new MaterialStock("2", "Z-1", "Vida", 1, null),
            new MaterialStock("3", "A-1", "Hava Filtresi", 1, "Filtre"),
            new MaterialStock("4", "A-0", "Yağ Filtresi", 1, "Filtre"),
        });
        Assert.Equal(new[] { "Filtre", "Fren › Balata", "Kategorisiz" }, g.Select(x => x.Category));
        Assert.Equal(new[] { "A-0", "A-1" }, g[0].Items.Select(x => x.Code));   // grup içi koda göre
        Assert.Equal("Filtre (2)", g[0].Header);
    }

    [Fact]
    public void Bagimli_kategori_filtresi_ana_secilince_alt_listesi_daralir()
    {
        var m = new[]
        {
            new MaterialStock("1", "S-10", "10 Amper", 1, "Elektrik › Sigorta"),
            new MaterialStock("2", "R-1", "Röle", 1, "Elektrik › Röle"),
            new MaterialStock("3", "F-1", "Filtre", 1, "Filtre"),
            new MaterialStock("4", "V-1", "Vida", 1, null),
        };
        Assert.Equal(new[] { "Tümü", "Elektrik", "Filtre", "Kategorisiz" }, MaterialStockGroups.TopOptions(m));
        Assert.Equal(new[] { "Tümü", "Röle", "Sigorta" }, MaterialStockGroups.SubOptions(m, "Elektrik"));
        Assert.Equal(new[] { "Tümü" }, MaterialStockGroups.SubOptions(m, "Tümü"));
        Assert.Equal(new[] { "1", "2" }, MaterialStockGroups.Filter(m, "Elektrik", "Tümü").Select(x => x.MaterialId));
        Assert.Equal(new[] { "1" }, MaterialStockGroups.Filter(m, "Elektrik", "Sigorta").Select(x => x.MaterialId));
        Assert.Equal(new[] { "4" }, MaterialStockGroups.Filter(m, "Kategorisiz", null).Select(x => x.MaterialId));
        Assert.Equal(4, MaterialStockGroups.Filter(m, null, null).Count());
    }

    [Fact]
    public void Kullanilan_malzemeler_ayri_ayri_ve_ayni_malzeme_toplanir()
    {
        var metin = KullanilanMalzemeler.Metin(new[]
        {
            new KullanilanMalzemeler.Kalem("Y-5", "Motor Yağı", "Litre", 2m),
            new KullanilanMalzemeler.Kalem("F-12", "Yağ Filtresi", "Adet", 1m),
            new KullanilanMalzemeler.Kalem("Y-5", "Motor Yağı", "Litre", 3m),
        });
        Assert.Equal("Motor Yağı (Y-5) 5 Litre · Yağ Filtresi (F-12) 1 Adet", metin);
        Assert.Null(KullanilanMalzemeler.Metin(Array.Empty<KullanilanMalzemeler.Kalem>()));
    }
}
