using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — DEPO ÇIKIŞLARI raporu (<c>stock-issues</c>, kullanıcı isteği: "çıkışı yapılan malzemeleri listele;
/// malzeme(ler) ya da personel seçebileyim; hiçbir şey seçmezsem tümü; en önemli sütun kimin teslim aldığı").
/// Kilitlenenler: yalnız ÇIKIŞLAR (giriş/transfer girmez) · iptal edilen çıkış ve iptal kaydı girmez · Teslim Alan
/// sütunu belgedeki personel · çoklu malzeme / çoklu teslim alan filtresi · filtresiz = tüm çıkışlar · tutar ·
/// çıkış kaynağı · başka firmanın verisi sızmaz. Tamamen yerel SQLite (HTTP yok).
/// </summary>
public class StockIssuesReportTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly StockService _stock;
    private readonly ReportService _reports;
    private readonly SessionContext _s;
    private readonly string _depo, _m1, _m2, _m3, _arac;
    private const string Ahmet = "per-ahmet", Mehmet = "per-mehmet";
    private const long Gunes = 1_699_000_000_000, Batis = 1_701_000_000_000;

    public StockIssuesReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_stkissue_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);");
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('B','B',1,1,1,0);");
        var clock = new TestClock();
        _stock = new StockService(_factory, clock);
        _reports = new ReportService(_factory);
        var uid = new UserService(_factory, clock).EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _depo = new BranchService(_factory, clock).Create(_s, new NewBranch("Merkez Depo"));
        var mats = new MaterialService(_factory, clock);
        _m1 = mats.Create(_s, new NewMaterial("YF-1", "Yağ filtresi"));
        _m2 = mats.Create(_s, new NewMaterial("HF-2", "Hava filtresi"));
        _m3 = mats.Create(_s, new NewMaterial("ELD-3", "Eldiven"));
        _arac = new VehicleService(_factory, clock).Create(_s, new NewVehicle("KMY-01", "34KMY01", 2020, 1000m, "km", _depo));
        Exec($"INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) VALUES('{Ahmet}','A','Ahmet Usta',1,1,1,1,0);");
        Exec($"INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) VALUES('{Mehmet}','A','Mehmet Şoför',1,1,1,1,0);");

        // Stok girişi (raporda GÖRÜNMEMELİ)
        _stock.ReceiveIn(_s, new[] { new StockLine(_m1, 100m, 50m), new StockLine(_m2, 100m, 30m), new StockLine(_m3, 100m, 10m) },
            Op(), _depo, docDate: Gunes + 1000);
        // Ahmet: 2 yağ filtresi + 1 hava filtresi (araçla)
        _stock.IssueOut(_s, new[] { new StockLine(_m1, 2m, 50m), new StockLine(_m2, 1m, 30m) }, Op(), _depo,
            personnelId: Ahmet, vehicleId: _arac, note: "Bakım için", docDate: Gunes + 2000, invoiceNo: "IRS-7");
        // Mehmet: 5 eldiven
        _stock.IssueOut(_s, new[] { new StockLine(_m3, 5m, 10m) }, Op(), _depo, personnelId: Mehmet, docDate: Gunes + 3000);
        // Teslim alan seçilmemiş çıkış: 1 eldiven
        _stock.IssueOut(_s, new[] { new StockLine(_m3, 1m, 10m) }, Op(), _depo, docDate: Gunes + 4000);
        // İPTAL edilen çıkış: Mehmet 9 yağ filtresi → listelenmemeli
        var iptal = _stock.IssueOut(_s, new[] { new StockLine(_m1, 9m, 50m) }, Op(), _depo, personnelId: Mehmet, docDate: Gunes + 5000);
        _stock.ReverseDocument(_s, iptal.DocumentId, "yanlış giriş");
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private TableModel Calistir(IReadOnlyList<string>? malzemeler = null, IReadOnlyList<string>? alanlar = null)
        => _reports.Run(_s, "stock-issues", new ReportRequest(true, Gunes, Batis, MaterialIds: malzemeler, RecipientIds: alanlar),
            ReportLimits.DefaultMaxRows);

    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new Xunit.Sdk.XunitException("Sütun yok: " + baslik);
    }

    private static string Metin(object? o) => o switch { NumCell n => n.Display, null => "", _ => o.ToString() ?? "" };

    [Fact]
    public void Katalogda_var_ve_filtreleri_dogru()
    {
        var d = ReportCatalog.ByKey("stock-issues");
        Assert.NotNull(d);
        Assert.Equal("Depo Çıkışları", d!.Name);
        Assert.True(d.UsesMaterial);
        Assert.True(d.UsesRecipient);
        Assert.True(d.UsesDate && d.RequiresDate);
        Assert.Equal("stock", d.DataModule);
    }

    [Fact]
    public void Filtresiz_tum_cikislar_gelir_giris_ve_iptal_gelmez()
    {
        var t = Calistir();
        Assert.Equal(4, t.Rows.Count);   // 2 (Ahmet) + 1 (Mehmet) + 1 (kişisiz) — giriş ve iptal YOK
        Assert.DoesNotContain(t.Rows, r => Metin(r[K(t, "Miktar")]) == "9");
        Assert.DoesNotContain(t.Rows, r => Metin(r[K(t, "Miktar")]) == "100");
    }

    [Fact]
    public void Teslim_Alan_ikinci_sutun_ve_dogru_kisi()
    {
        var t = Calistir();
        Assert.Equal(ReportService.DepoCikisTeslimAlanBaslik, t.Headers[1]);
        var yag = t.Rows.Single(r => (string)r[K(t, "Malzeme Kodu")]! == "YF-1");
        Assert.Equal("Ahmet Usta", (string)yag[1]!);
        Assert.Equal("Yağ filtresi", (string)yag[K(t, "Malzeme Adı")]!);
        Assert.Equal("Merkez Depo", (string)yag[K(t, "Çıkış Deposu")]!);
        Assert.Contains("KMY-01", (string)yag[K(t, "Araç")]!);
        Assert.Equal("IRS-7", (string)yag[K(t, "Fatura / İrsaliye No")]!);
        Assert.Equal("Giriş/Çıkış", (string)yag[K(t, "Çıkış Kaynağı")]!);
        Assert.Equal(100.0, ((NumCell)yag[K(t, "Tutar")]!).Value, 3);   // 2 × 50
        Assert.Contains(t.Rows, r => (string)r[1]! == "");                // kişisiz çıkış boş geçer
    }

    [Fact]
    public void Personel_secilince_yalniz_ona_teslim_edilenler()
    {
        var t = Calistir(alanlar: new[] { Ahmet });
        Assert.Equal(2, t.Rows.Count);
        Assert.All(t.Rows, r => Assert.Equal("Ahmet Usta", (string)r[1]!));
        var iki = Calistir(alanlar: new[] { Ahmet, Mehmet });
        Assert.Equal(3, iki.Rows.Count);   // Mehmet'in İPTAL edilen çıkışı yine yok
    }

    [Fact]
    public void Coklu_malzeme_secimi_ve_personelle_birlikte()
    {
        var t = Calistir(malzemeler: new[] { _m1, _m3 });
        Assert.Equal(3, t.Rows.Count);   // YF-1 (Ahmet) + ELD-3 (Mehmet) + ELD-3 (kişisiz)
        Assert.DoesNotContain(t.Rows, r => (string)r[K(t, "Malzeme Kodu")]! == "HF-2");
        var kesisim = Calistir(malzemeler: new[] { _m3 }, alanlar: new[] { Mehmet });
        var satir = Assert.Single(kesisim.Rows);
        Assert.Equal("5", Metin(satir[K(kesisim, "Miktar")]));
    }

    [Fact]
    public void Toplam_satiri_tutar_toplar()
    {
        var t = Calistir();
        Assert.NotNull(t.TotalRow);
        // 2×50 + 1×30 + 5×10 + 1×10 = 190
        Assert.Equal(190.0, ((NumCell)t.TotalRow![K(t, "Tutar")]!).Value, 3);
        Assert.Equal("4 çıkış", (string)t.TotalRow[1]!);
    }

    [Fact]
    public void Baska_firmanin_personel_kimligi_sizmaz()
    {
        Exec("INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) VALUES('per-b','B','Yabancı',1,1,1,1,0);");
        Assert.Empty(Calistir(alanlar: new[] { "per-b" }).Rows);
    }

    [Theory]
    [InlineData("assign:op1:0", "", "Zimmet")]
    [InlineData("wo:op1:0", "", "İş Emri")]
    [InlineData("op1:stock:0", "", "Fatura")]
    [InlineData("invoice:abc:cancel:stock:0", "", "Fatura")]
    [InlineData("op1:0", "Talep: r1", "Talep")]
    [InlineData("op1:0", "", "Giriş/Çıkış")]
    public void Cikis_kaynagi_islem_kimliginden_turetilir(string op, string not, string beklenen)
        => Assert.Equal(beklenen, ReportService.CikisKaynagi(op, not));

    private void Exec(string sql)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
