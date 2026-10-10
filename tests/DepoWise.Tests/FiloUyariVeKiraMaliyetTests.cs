using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-10 (kullanıcının seçtiği öneriler 1 ve 2).
/// FUA1: son dolum aracın ortalamasının %30+ üstündeyse sapma uyarısı; %60+ kritik; ana ekranda "Yakıt tüketimi yüksek".
/// FUA2: kalkanlar — normal tüketim, 5'ten az dolum, çok kısa sayaç aralığı, İPTAL edilen dolum → uyarı YOK.
/// FUA3: başka firmanın dolumları sayılmaz (tenant).
/// KRU1: kira bitişi 7 gün içinde → "Kira bitişi yaklaşıyor"; geçmiş + aktif → "Kira süresi doldu" (kritik);
///       pasif (iade edilmiş) ve bitişi uzak olan → uyarı yok.
/// KRM1: Kiralık Araç Maliyeti raporu — günlük/aylık bedel dönemle kesişen günden, yakıt aynı dönemden, toplam satırı.
/// </summary>
public class FiloUyariVeKiraMaliyetTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "depowise_fua_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _a, _b;
    private readonly VehicleService _v;
    private readonly FuelService _fuel;
    private const long GunMs = 86_400_000L;

    public FiloUyariVeKiraMaliyetTests()
    {
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
        var users = new UserService(_f, new SystemClock());
        _a = new SessionContext(users.EnsureInitialAdmin("A", "a", "a123456", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _b = new SessionContext(users.EnsureInitialAdmin("B", "b", "b123456", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _v = new VehicleService(_f);
        _fuel = new FuelService(_f);
        _fuel.AddDepotEntry(_a, new NewDepotEntry(100_000m, 40m, EntryDate: Bugun() - 100 * GunMs), Op());
        _fuel.AddDepotEntry(_b, new NewDepotEntry(100_000m, 40m, EntryDate: Bugun() - 100 * GunMs), Op());
    }

    public void Dispose() { try { File.Delete(_db); } catch { } }

    private static string Op() => Guid.NewGuid().ToString("N");
    private static long Bugun() { var n = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); return n - n % GunMs; }

    /// <summary>Her dolum: sayaç +<paramref name="km"/>, <paramref name="litre"/> L; tarihler geçmişten bugüne sıralı.</summary>
    private string Dolumlar(SessionContext s, string kod, params (decimal Km, decimal Litre)[] dolumlar)
    {
        var id = _v.Create(s, new NewVehicle(kod, CurrentMeter: 1000m));
        decimal sayac = 1000m;
        for (int i = 0; i < dolumlar.Length; i++)
        {
            sayac += dolumlar[i].Km;
            _fuel.Distribute(s, new NewDistribution(id, dolumlar[i].Litre, sayac,
                DistributionDate: Bugun() - (dolumlar.Length - i) * GunMs), Op());
        }
        return id;
    }

    private List<FuelAnomaly> Sapmalar(SessionContext s)
    {
        using var conn = _f.Create();
        return FleetAlerts.FuelAnomalies(conn, s, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void FUA1_Son_Dolum_Ortalamanin_Ustunde_Uyari_ve_Kritik()
    {
        var arac = Dolumlar(_a, "KAM-01", (100, 30), (100, 30), (100, 30), (100, 30), (100, 30), (100, 50));
        var y = Assert.Single(Sapmalar(_a));
        Assert.Equal(arac, y.VehicleId);
        Assert.Equal(0.5m, y.Last);
        Assert.Equal(0.3m, y.Baseline);
        Assert.Equal(6, y.Samples);
        Assert.True(y.Last >= y.Baseline * FleetAlerts.KritikOran);   // +%67 → kritik

        var ozet = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f)).GetSummary(_a);
        var u = Assert.Single(ozet.Alerts, a => a.Title == "Yakıt tüketimi yüksek");
        Assert.Equal(AlertKind.Fuel, u.Kind);
        Assert.True(u.IsCritical);
        Assert.Contains("KAM-01", u.Detail);
        Assert.Contains("+%67", u.Detail);
        Assert.Equal("fuel:dist", u.NavigateKey);
    }

    [Fact]
    public void FUA2_Kalkanlar_Yanlis_Alarm_Uretmez()
    {
        Dolumlar(_a, "NORMAL", (100, 30), (100, 30), (100, 31), (100, 29), (100, 30), (100, 35));   // +%17 → eşik altı
        Dolumlar(_a, "AZ", (100, 30), (100, 30), (100, 30), (100, 60));                             // 4 dolum → ölçülmez
        Dolumlar(_a, "KISA", (100, 30), (100, 30), (100, 30), (100, 30), (100, 30), (5, 10));       // son aralık 5 km
        Assert.Empty(Sapmalar(_a));

        // İptal edilen sapmalı dolum sayılmaz.
        var id = _v.Create(_a, new NewVehicle("IPTAL", CurrentMeter: 1000m));
        string? son = null;
        for (int i = 0; i < 6; i++)
            son = _fuel.Distribute(_a, new NewDistribution(id, i == 5 ? 60m : 30m, 1100m + i * 100m,
                DistributionDate: Bugun() - (6 - i) * GunMs), Op());
        Assert.Contains(Sapmalar(_a), x => x.VehicleId == id);
        _fuel.CancelDistribution(_a, son!, "test");
        Assert.DoesNotContain(Sapmalar(_a), x => x.VehicleId == id);
    }

    [Fact]
    public void FUA3_Baska_Firmanin_Dolumlari_Sayilmaz()
    {
        Dolumlar(_b, "B-KAM", (100, 30), (100, 30), (100, 30), (100, 30), (100, 30), (100, 90));
        Assert.Empty(Sapmalar(_a));
        Assert.Single(Sapmalar(_b));
    }

    [Fact]
    public void KRU1_Kira_Bitisi_Uyarilari()
    {
        var bugun = Bugun();
        var yakin = _v.Create(_a, new NewVehicle("KRL-YAKIN", Rental: new RentalInfo("Kiracı", bugun - 30 * GunMs, End: bugun + 3 * GunMs)));
        var gecmis = _v.Create(_a, new NewVehicle("KRL-GECMIS", Rental: new RentalInfo("Kiracı", bugun - 30 * GunMs, End: bugun - 2 * GunMs)));
        _v.Create(_a, new NewVehicle("KRL-UZAK", Rental: new RentalInfo("Kiracı", bugun - 30 * GunMs, End: bugun + 60 * GunMs)));
        var iade = _v.Create(_a, new NewVehicle("KRL-IADE", Rental: new RentalInfo("Kiracı", bugun - 30 * GunMs, End: bugun + 2 * GunMs)));
        _v.EndRental(_a, iade, bugun);

        var ozet = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f)).GetSummary(_a);
        var kira = ozet.Alerts.Where(a => a.Kind == AlertKind.Rental).ToList();
        Assert.Equal(2, kira.Count);
        var y = Assert.Single(kira, a => a.EntityId == yakin);
        Assert.Equal("Kira bitişi yaklaşıyor", y.Title);
        Assert.Contains("3 gün kaldı", y.Detail);
        Assert.False(y.IsCritical);
        var g = Assert.Single(kira, a => a.EntityId == gecmis);
        Assert.Equal("Kira süresi doldu", g.Title);
        Assert.True(g.IsCritical);
        Assert.Equal("vehicles:rental", g.NavigateKey);
    }

    [Fact]
    public void KRM1_Kiralik_Arac_Maliyeti_Raporu()
    {
        var bugun = Bugun();
        var bas = bugun - 40 * GunMs;
        var gunluk = _v.Create(_a, new NewVehicle("KRL-GUN", CurrentMeter: 1000m,
            Rental: new RentalInfo("Kiracı", bas, Price: 1000m, PriceUnit: "day")));
        _v.Create(_a, new NewVehicle("KRL-AY", Rental: new RentalInfo("Kiracı", bas, Price: 30_000m, PriceUnit: "month")));
        _v.Create(_a, new NewVehicle("SRK-01"));                                                 // şirket aracı → raporda yok
        _fuel.Distribute(_a, new NewDistribution(gunluk, 40m, 1100m, DistributionDate: bugun - 15 * GunMs), Op());   // 40 L × 40 = 1600

        var donemBas = bugun - 19 * GunMs;                    // 20 günlük dönem: [bugün-19, bugün]
        var donemSon = bugun + GunMs - 1;
        var t = new ReportService(_f).Run(_a, "rental-cost", new ReportRequest(true, donemBas, donemSon));

        Assert.Equal("Kiralık Araç Maliyeti", t.Title);
        Assert.Equal(2, t.Rows.Count);
        var gun = t.Rows.Single(r => ((string)r[1]!).StartsWith("KRL-GUN"));
        Assert.Equal("20 gün", gun[6]);
        Assert.Equal(20_000d, Assert.IsType<NumCell>(gun[8]).Value);
        Assert.Equal(1_600d, Assert.IsType<NumCell>(gun[9]).Value);
        Assert.Equal(21_600d, Assert.IsType<NumCell>(gun[10]).Value);
        var ay = t.Rows.Single(r => ((string)r[1]!).StartsWith("KRL-AY"));
        Assert.Equal(20_000d, Assert.IsType<NumCell>(ay[8]).Value);   // 20 ÷ 30 × 30.000
        Assert.Equal(41_600d, Assert.IsType<NumCell>(t.TotalRow![10]).Value);
    }
}
