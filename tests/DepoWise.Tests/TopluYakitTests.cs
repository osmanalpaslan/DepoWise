using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-34 (AR-GE 2026-10-18, F-074, K-13) — TOPLU YAKIT DAĞITIMI ═══
///  TY1 — Tek işlemde N araç: her kayıt yazılır, depo düşer, araç sayaçları ilerler
///  TY2 — Hepsi ya da hiçbiri: depo toplamda yetmezse / bir satır hatalıysa HİÇBİR satır yazılmaz; hata satır numarası söyler
///  TY3 — Tekrar gönderim (aynı işlem kimliği) ikinci kayıt üretmez; aynı araç iki satırda reddedilir; sınırlar
///  TY4 — Firma ayrımı: başka firmanın aracı reddedilir ve hiçbir şey yazılmaz
///  TY5 — Onay metni (tek onay, uyarı sayısı) + iki platform/API kaynak kilidi
/// </summary>
public class TopluYakitTests : IDisposable
{
    private const string Co = "TYK";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_tyk_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly FuelService _yakit;
    private readonly string _a1, _a2, _a3;

    public TopluYakitTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "tyk", "Yakit!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var araclar = new VehicleService(_f);
        _a1 = araclar.Create(_s, new NewVehicle("TY-1", "06 TY 1", CurrentMeter: 1_000m));
        _a2 = araclar.Create(_s, new NewVehicle("TY-2", "06 TY 2", CurrentMeter: 2_000m));
        _a3 = araclar.Create(_s, new NewVehicle("TY-3", "06 TY 3", CurrentMeter: 3_000m));
        _yakit = new FuelService(_f);
        _yakit.AddDepotEntry(_s, new NewDepotEntry(300m, 40m, "TRY"), Op());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private long Kayit() => Convert.ToInt64(Tek($"SELECT COUNT(*) FROM fuel_distributions WHERE company_id='{Co}' AND is_deleted=0;"));
    private string Sayac(string arac) => (string)Tek($"SELECT current_meter FROM vehicles WHERE id='{arac}';")!;

    [Fact]
    public void TY1_Tek_Islemde_Cok_Arac()
    {
        var ids = _yakit.TopluDagit(_s, new[]
        {
            new NewDistribution(_a1, 50m, 1_100m), new NewDistribution(_a2, 60m, 2_150m), new NewDistribution(_a3, 70m, 3_200m),
        }, Op());
        Assert.Equal(3, ids.Count);
        Assert.Equal(3L, Kayit());
        Assert.Equal(120m, _yakit.GetDepotBalance(_s));
        Assert.Equal(1_100m, decimal.Parse(Sayac(_a1), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(3_200m, decimal.Parse(Sayac(_a3), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TY2_Hepsi_Ya_Da_Hicbiri()
    {
        // Satır satır depo yeter görünür ama toplam (320 L) 300 L'yi aşar → 3. satır reddedilir, ilk ikisi de yazılmaz.
        var ex = Assert.Throws<InvalidOperationException>(() => _yakit.TopluDagit(_s, new[]
        {
            new NewDistribution(_a1, 150m, 1_100m), new NewDistribution(_a2, 100m, 2_100m), new NewDistribution(_a3, 70m, 3_100m),
        }, Op()));
        Assert.Contains("3. satır", ex.Message);
        Assert.Contains("hiçbir satır yazılmadı", ex.Message);
        Assert.Equal(0L, Kayit());
        Assert.Equal(300m, _yakit.GetDepotBalance(_s));
        Assert.Equal(1_000m, decimal.Parse(Sayac(_a1), System.Globalization.CultureInfo.InvariantCulture));   // sayaç da geri döndü

        Assert.Contains("2. satır", Assert.Throws<ArgumentException>(() => _yakit.TopluDagit(_s, new[]
        {
            new NewDistribution(_a1, 10m, 1_100m), new NewDistribution(_a2, 0m, 2_100m),
        }, Op())).Message);
        Assert.Equal(0L, Kayit());
    }

    [Fact]
    public void TY3_Tekrar_Gonderim_Ve_Sinirlar()
    {
        var op = Op();
        var satirlar = new[] { new NewDistribution(_a1, 10m, 1_050m), new NewDistribution(_a2, 20m, 2_050m) };
        var ilk = _yakit.TopluDagit(_s, satirlar, op);
        Assert.Equal(ilk, _yakit.TopluDagit(_s, satirlar, op));
        Assert.Equal(2L, Kayit());
        Assert.Equal(270m, _yakit.GetDepotBalance(_s));

        Assert.Contains("iki kez", Assert.Throws<ArgumentException>(() => _yakit.TopluDagit(_s, new[]
        {
            new NewDistribution(_a3, 1m, 3_010m), new NewDistribution(_a3, 1m, 3_020m),
        }, Op())).Message);
        Assert.Throws<ArgumentException>(() => _yakit.TopluDagit(_s, Array.Empty<NewDistribution>(), Op()));
        Assert.Throws<ArgumentException>(() => _yakit.TopluDagit(_s, Enumerable.Range(0, 201)
            .Select(i => new NewDistribution(_a1, 1m, 2_000m + i)).ToList(), Op()));
        Assert.Throws<ArgumentException>(() => _yakit.TopluDagit(_s, satirlar, " "));
    }

    [Fact]
    public void TY4_Baska_Firmanin_Araci()
    {
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('TYB','TYB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("TYB", "tyb", "Yakit!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "TYB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yabanci = new VehicleService(_f).Create(b, new NewVehicle("TB-1", "34 TB 1", CurrentMeter: 10m));
        Assert.ThrowsAny<Exception>(() => _yakit.TopluDagit(_s, new[]
        {
            new NewDistribution(_a1, 10m, 1_100m), new NewDistribution(yabanci, 10m, 20m),
        }, Op()));
        Assert.Equal(0L, Kayit());
        Assert.Equal(10m, decimal.Parse(Sayac(yabanci), System.Globalization.CultureInfo.InvariantCulture));   // yabancı araca dokunulmadı
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void TY5_Onay_Metni_Ve_Kaynak_Kilidi()
    {
        var m = FuelService.TopluOnayMetni(3, 180m, 7200m, 1);
        Assert.Contains("3 araca toplam 180 L", m);
        Assert.Contains("1 satırda uyarı", m);
        Assert.Contains("hiçbiri yazılmaz", m);
        Assert.DoesNotContain("uyarı var", FuelService.TopluOnayMetni(2, 10m, 400m, 0));

        Assert.Contains("app.MapPost(\"/api/fuel/distribute-bulk\"", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("DesktopServices.Fuel.TopluDagit(", Oku("src/DepoWise.Desktop/ViewModels/FuelViewModel.cs"));
        Assert.Contains("TopluKaydetCommand", Oku("src/DepoWise.Desktop/Views/FuelView.axaml"));
        Assert.Contains("/api/fuel/distribute-bulk", Oku("src/DepoWise.Web/Components/Pages/Fuel.razor"));
    }
}
