using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-37 (AR-GE 2026-10-19, 05 F-079) — BAKIM PLANI: KM/SAAT + TARİH BİRLİKTE ═══
///  BP1 — Tanım ek tarih periyodunu saklar/okur; görünüm "10000 km ya da 365 gün"; gün biriminde reddedilir; negatif reddedilir
///  BP2 — Km ölçütü dolmamış ama tarih dolmuşsa uyarı TARİHE göre (ilki tetikler); km daha çok dolmuşsa km'ye göre
///  BP3 — Bakımda sonraki tarih de hesaplanır (next_due_date)
///  BP4 — Ek periyot yoksa davranış aynen (yalnız km); eski şema (kolon yok) okunur, ek periyot yazımı anlaşılır hata
///  BP5 — İki platform + API (kaynak kilidi)
/// </summary>
public class BakimPlaniTests : IDisposable
{
    private const string Co = "BPL";
    private const long G = 86_400_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_bpl_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly TestClock _saat = new();
    private readonly MaintenanceDefinitionService _tanim;
    private readonly MaintenanceService _bakim;
    private readonly string _arac;

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_000); }

    public BakimPlaniTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "bpl", "Bakim!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _arac = new VehicleService(_f, _saat).Create(_s, new NewVehicle("BP-1", "06 BP 1", CurrentMeter: 50_000m));
        _tanim = new MaintenanceDefinitionService(_f, _saat);
        _bakim = new MaintenanceService(_f, _saat);
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
    private long Simdi => _saat.UtcNow.ToUnixTimeMilliseconds();
    private MaintenanceAlert Uyari(string tanim) => _bakim.GetAlerts(_s).Single(a => a.DefinitionId == tanim);

    [Fact]
    public void BP1_Tanim_Ek_Tarih_Periyodu()
    {
        var id = _tanim.Create(_s, new NewMaintenanceDefinition("Yağ", 10_000m, "km", IntervalDays: 365m));
        var row = _tanim.List(_s).Single(x => x.Id == id);
        Assert.Equal(365m, row.IntervalDays);
        Assert.Equal("10000 km ya da 365 gün", row.IntervalDisplay);
        _tanim.Update(_s, id, new NewMaintenanceDefinition("Yağ", 10_000m, "km", IntervalDays: null));
        Assert.Null(_tanim.List(_s).Single(x => x.Id == id).IntervalDays);
        Assert.Throws<ArgumentException>(() => _tanim.Create(_s, new NewMaintenanceDefinition("Gün", 30m, "day", IntervalDays: 10m)));
        Assert.Throws<ArgumentException>(() => _tanim.Create(_s, new NewMaintenanceDefinition("Eksi", 10m, "km", IntervalDays: -1m)));
    }

    [Fact]
    public void BP2_Ilki_Tetikler()
    {
        var id = _tanim.Create(_s, new NewMaintenanceDefinition("Filtre", 10_000m, "km", IntervalDays: 100m));
        _bakim.Save(_s, new NewMaintenance(_arac, id, PerformedKm: 50_000m, PerformedDate: Simdi - 95 * G), Op());
        var u = Uyari(id);                               // km: 0/10000 · tarih: 95/100 gün → tarih
        Assert.Equal(100m, u.Interval);
        Assert.True(u.Progress >= 0.9);
        Assert.NotEqual(AlertLevel.Normal, u.Level);

        Exec($"UPDATE vehicles SET current_meter='59900' WHERE id='{_arac}';");   // km 9900/10000 > tarih 95/100
        var k = Uyari(id);
        Assert.Equal(10_000m, k.Interval);
        Assert.Equal(9_900m, k.Consumed);
    }

    [Fact]
    public void BP3_Sonraki_Tarih_Hesaplanir()
    {
        var id = _tanim.Create(_s, new NewMaintenanceDefinition("Antifriz", 20_000m, "km", IntervalDays: 365m));
        var bakim = _bakim.Save(_s, new NewMaintenance(_arac, id, PerformedKm: 50_000m, PerformedDate: Simdi), Op());
        Assert.Equal(Simdi + 365 * G, Convert.ToInt64(Tek($"SELECT next_due_date FROM vehicle_maintenances WHERE id='{bakim}';")));
        Assert.Equal("70000", Convert.ToString(Tek($"SELECT next_due_km FROM vehicle_maintenances WHERE id='{bakim}';"))!.Split('.')[0]);
    }

    [Fact]
    public void BP4_Ek_Periyot_Yoksa_Aynen_Ve_Eski_Sema()
    {
        var id = _tanim.Create(_s, new NewMaintenanceDefinition("Yalnız km", 10_000m, "km"));
        _bakim.Save(_s, new NewMaintenance(_arac, id, PerformedKm: 50_000m, PerformedDate: Simdi - 900 * G), Op());
        var u = Uyari(id);
        Assert.Equal(10_000m, u.Interval);              // tarih ne kadar eski olsa da km ölçütü
        Assert.Equal(AlertLevel.Normal, u.Level);

        Exec("ALTER TABLE maintenance_definitions DROP COLUMN interval_days;");
        Assert.Null(_tanim.List(_s).Single(x => x.Id == id).IntervalDays);
        Assert.Equal(10_000m, Uyari(id).Interval);
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _tanim.Create(_s, new NewMaintenanceDefinition("Yeni", 5m, "km", IntervalDays: 30m))).Message);
        _tanim.Create(_s, new NewMaintenanceDefinition("Yeni", 5m, "km"));   // ek periyotsuz eskisi gibi
        using (var c = _f.Create()) new Migration113_MaintenanceIntervalDays().Up(c, null!);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void BP5_Iki_Platform_Ve_Api()
    {
        Assert.Contains("d.Description, d.IntervalDays)", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("IntervalDays: DefIntervalDays", Oku("src/DepoWise.Desktop/ViewModels/MaintenanceViewModel.cs"));
        Assert.Contains("{Binding DefIntervalDays}", Oku("src/DepoWise.Desktop/Views/MaintenanceView.axaml"));
        Assert.Contains("intervalDays = _defDays", Oku("src/DepoWise.Web/Components/Pages/Maintenance.razor"));
    }
}
