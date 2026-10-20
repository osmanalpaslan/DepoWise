using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-38 (AR-GE 2026-10-19, 05 F-080) — DURUŞ SÜRESİ RAPORU ═══
///  DR1 — Araç başına arıza sayısı ve toplam duruş saati (tamamlanan: bildirim → tamamlanma; açık: dönem sonuna kadar)
///  DR2 — Döneme taşan kısım kesilir; iptal edilen ve arızasız iş emri sayılmaz; firma ayrımı; ters tarih reddedilir
///  DR3 — İki platform + API (kaynak kilidi)
/// </summary>
public class DurusRaporuTests : IDisposable
{
    private const string Co = "DRS";
    private const long S = 3_600_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_drs_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly WorkOrderService _wo;
    private readonly string _a, _b;
    private readonly long _t0;

    public DurusRaporuTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "drs", "Durus!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var v = new VehicleService(_f);
        _a = v.Create(_s, new NewVehicle("EKS-A", null, CurrentMeter: 1m));
        _b = v.Create(_s, new NewVehicle("EKS-B", null, CurrentMeter: 1m));
        _wo = new WorkOrderService(_f);
        var n = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _t0 = n - n % 86_400_000L - 30 * 86_400_000L;   // 30 gün önce, gün başı
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

    /// <summary>Arıza açar, bildirim/tamamlanma zamanını sabitler (deterministik süre).</summary>
    private string Ariza(string arac, long bas, long? bitis, string durum = "completed")
    {
        Exec($"UPDATE vehicles SET status='active' WHERE id='{arac}';");
        Exec($"UPDATE work_orders SET status='completed' WHERE fault_vehicle_id='{arac}';");   // önceki açık arıza engel olmasın
        var (id, _) = _wo.AractanIsEmri(_s, arac, "arıza", ariza: true);
        Exec($"UPDATE work_orders SET created_at={bas}, actual_end={(bitis is null ? "NULL" : bitis.ToString())}, status='{durum}' WHERE id='{id}';");
        return id;
    }

    [Fact]
    public void DR1_Arac_Basina_Durus()
    {
        Ariza(_a, _t0, _t0 + 10 * S);
        Ariza(_a, _t0 + 24 * S, _t0 + 30 * S);
        Ariza(_b, _t0 + 2 * S, null, durum: "in_progress");     // açık: dönem sonuna kadar
        var r = _wo.DurusRaporu(_s, _t0, _t0 + 50 * S);
        Assert.Equal(2, r.Count);
        var b = r[0];                                              // en uzun duruş üstte
        Assert.Equal("EKS-B", b.AracKodu);
        Assert.Equal(48m, b.ToplamSaat);
        Assert.Equal(1, b.AcikAriza);
        var a = r[1];
        Assert.Equal(2, a.ArizaSayisi);
        Assert.Equal(16m, a.ToplamSaat);
        Assert.Equal(0, a.AcikAriza);
        Assert.Contains("16 saat duruş", a.Satir);
    }

    [Fact]
    public void DR2_Kesme_Iptal_Firma()
    {
        Ariza(_a, _t0 - 5 * S, _t0 + 5 * S);                       // dönemden önce başladı → 5 saat
        Ariza(_a, _t0 + 1 * S, _t0 + 3 * S, durum: "cancelled");   // iptal → sayılmaz
        Ariza(_b, _t0 + 60 * S, _t0 + 70 * S);                     // dönemden sonra → yok
        _wo.AractanIsEmri(_s, _b, "bakım", ariza: false);           // arızasız iş emri → yok
        var r = Assert.Single(_wo.DurusRaporu(_s, _t0, _t0 + 50 * S));
        Assert.Equal("EKS-A", r.AracKodu);
        Assert.Equal(1, r.ArizaSayisi);
        Assert.Equal(5m, r.ToplamSaat);
        Assert.Throws<ArgumentException>(() => _wo.DurusRaporu(_s, _t0 + S, _t0));

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('DRB','DRB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("DRB", "drb", "Durus!2026", RoleKeys.CompanyAdmin);
        Assert.Empty(_wo.DurusRaporu(new SessionContext(uid, "DRB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty), _t0, _t0 + 50 * S));
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void DR3_Iki_Platform_Ve_Api()
    {
        Assert.Contains("app.MapGet(\"/api/vehicles/downtime\"", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("DesktopServices.WorkOrders.DurusRaporu(", Oku("src/DepoWise.Desktop/ViewModels/VehiclesViewModel.cs"));
        Assert.Contains("DurusYukleCommand", Oku("src/DepoWise.Desktop/Views/VehiclesView.axaml"));
        Assert.Contains("/api/vehicles/downtime", Oku("src/DepoWise.Web/Components/Pages/Vehicles.razor"));
    }
}
