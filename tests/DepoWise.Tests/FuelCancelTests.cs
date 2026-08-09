using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// YAKIT KAYDI İPTALİ (kullanıcı kararları Y1–Y5, 2026-08-09).
///
/// Kurallar:
///  Y1 — Depo girişi, bakiyeyi eksiye düşürecekse iptal EDİLEMEZ.
///  Y2 — İptal araç sayacını GERİ ALMAZ; düzeltme kaydına başlangıç sayacı taşınır (zincir korunur).
///  Y3 — İptal edilenler varsayılan GİZLİ; istenirse gösterilir.
///  Y4 — İptal GERİ ALINAMAZ; aynı kayıt ikinci kez iptal edilemez.
///  Y5 — Yetki: fuel/Edit + mevcut "Ters Kayıt" (btn-reverse) özel butonu.
/// </summary>
public class FuelCancelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly FuelService _fuel;
    private readonly VehicleService _vehicles;
    private readonly SessionContext _admin;

    public FuelCancelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fuelcancel_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _fuel = new FuelService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private string Depot(decimal liters, decimal price = 40m)
        => _fuel.AddDepotEntry(_admin, new NewDepotEntry(liters, price), "op-depot-" + Guid.NewGuid().ToString("N"));

    private string Vehicle(decimal meter = 10_000m)
        => _vehicles.Create(_admin, new NewVehicle("ARC-" + Guid.NewGuid().ToString("N")[..6], CurrentMeter: meter));

    private string Distribute(string vehicleId, decimal liters, decimal currentMeter, decimal? prevMeter = null)
        => _fuel.Distribute(_admin, new NewDistribution(vehicleId, liters, currentMeter, PrevMeter: prevMeter),
            "op-dist-" + Guid.NewGuid().ToString("N"));

    private long AuditCount(string action)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE action=@a;";
        cmd.AddWithValue("@a", action);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private long MeterLogCount()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicle_meter_logs;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ── 1. DAĞITIM İPTALİ ───────────────────────────────────────────────────────────────────

    [Fact]  // T1 + T2
    public void Dagitim_iptal_edilebilir_ve_bakiyeye_geri_doner()
    {
        Depot(1000m);
        var v = Vehicle();
        var d = Distribute(v, 120m, 10_500m);
        Assert.Equal(880m, _fuel.GetDepotBalance(_admin));       // 1000 - 120

        _fuel.CancelDistribution(_admin, d, "Yanlış litre girildi");

        Assert.Equal(1000m, _fuel.GetDepotBalance(_admin));      // çıkış geri sayılmaz
    }

    [Fact]  // T7 + T8 — EN KRİTİK KURAL
    public void Dagitim_iptali_ARAC_SAYACINI_GERI_ALMAZ()
    {
        Depot(1000m);
        var v = Vehicle(10_000m);
        var d = Distribute(v, 120m, 10_500m);

        Assert.Equal(10_500m, _vehicles.GetMeter(_admin, v));    // sayaç ilerledi
        var logsBefore = MeterLogCount();

        _fuel.CancelDistribution(_admin, d, "hatalı kayıt");

        Assert.Equal(10_500m, _vehicles.GetMeter(_admin, v));    // ❗ GERİ ALINMADI
        Assert.Equal(logsBefore, MeterLogCount());               // ❗ iz kaydı da silinmedi
    }

    [Fact]  // T4
    public void Iptal_audit_kaydi_olusturur()
    {
        Depot(1000m);
        var d = Distribute(Vehicle(), 50m, 10_100m);
        var before = AuditCount(AuditActions.Reverse);

        _fuel.CancelDistribution(_admin, d, "gerekçe metni");

        Assert.Equal(before + 1, AuditCount(AuditActions.Reverse));
    }

    [Fact]  // T5 + Y4
    public void Iptal_edilen_kayit_TEKRAR_IPTAL_EDILEMEZ()
    {
        Depot(1000m);
        var d = Distribute(Vehicle(), 50m, 10_100m);
        _fuel.CancelDistribution(_admin, d, "ilk iptal");

        var ex = Assert.Throws<InvalidOperationException>(
            () => _fuel.CancelDistribution(_admin, d, "ikinci iptal"));
        Assert.Contains("zaten iptal", ex.Message);
    }

    [Fact]
    public void Gerekce_zorunlu_ve_baska_firmanin_kaydi_iptal_edilemez()
    {
        Depot(1000m);
        var d = Distribute(Vehicle(), 50m, 10_100m);

        Assert.Throws<ArgumentException>(() => _fuel.CancelDistribution(_admin, d, "  "));
        Assert.Throws<ForbiddenException>(() => _fuel.CancelDistribution(_admin, "yok-boyle-id", "gerekçe"));
    }

    // ── 2. DEPO GİRİŞİ İPTALİ (Y1) ──────────────────────────────────────────────────────────

    [Fact]  // T12
    public void Depo_girisi_bagli_dagitim_yokken_iptal_edilebilir()
    {
        var e = Depot(1000m);
        Assert.Equal(1000m, _fuel.GetDepotBalance(_admin));

        _fuel.CancelDepotEntry(_admin, e, "yanlış giriş");

        Assert.Equal(0m, _fuel.GetDepotBalance(_admin));
    }

    [Fact]  // T11 — Y1
    public void Depo_girisi_bakiyeyi_EKSIYE_dusurecekse_IPTAL_EDILEMEZ()
    {
        var e = Depot(1000m);
        Distribute(Vehicle(), 800m, 10_400m);                    // bakiye 200
        Assert.Equal(200m, _fuel.GetDepotBalance(_admin));

        var ex = Assert.Throws<InvalidOperationException>(
            () => _fuel.CancelDepotEntry(_admin, e, "iptal denemesi"));

        Assert.Contains("eksiye düşer", ex.Message);
        Assert.Contains("dağıtımlarını iptal", ex.Message);      // kullanıcıya ne yapacağı söyleniyor
        Assert.Equal(200m, _fuel.GetDepotBalance(_admin));       // bakiye DEĞİŞMEDİ
    }

    [Fact]
    public void Once_dagitim_iptal_edilirse_depo_girisi_iptal_edilebilir()
    {
        var e = Depot(1000m);
        var d = Distribute(Vehicle(), 800m, 10_400m);

        _fuel.CancelDistribution(_admin, d, "önce dağıtım");
        _fuel.CancelDepotEntry(_admin, e, "sonra giriş");        // artık serbest

        Assert.Equal(0m, _fuel.GetDepotBalance(_admin));
    }

    // ── 3. SAYAÇ ZİNCİRİ (Y2) ───────────────────────────────────────────────────────────────

    [Fact]  // T9 + T10 — kullanıcının verdiği örnek senaryo
    public void Iptal_ve_yeniden_giris_SAYAC_ZINCIRINI_BOZMAZ()
    {
        Depot(1000m);
        var v = Vehicle(10_000m);

        Distribute(v, 100m, 10_200m);                            // D1: 10.000 → 10.200
        var d2 = Distribute(v, 120m, 10_500m);                   // D2: 10.200 → 10.500  (YANLIŞ)
        Distribute(v, 100m, 10_800m);                            // D3: 10.500 → 10.800

        // D2'nin başlangıç sayacı korunmalı
        _fuel.CancelDistribution(_admin, d2, "yanlış litre");
        var carried = _fuel.GetCancelledPrevMeter(_admin, d2);
        Assert.Equal(10_200m, carried);                          // ❗ taşınacak değer doğru

        // Düzeltme kaydı: başlangıç sayacı TAŞINIR, bitiş sayacı gerçek değer
        Distribute(v, 100m, 10_500m, prevMeter: carried);

        // Araç sayacı GERİ ALINMADI (D3 zaten 10.800'e taşımıştı)
        Assert.Equal(10_800m, _vehicles.GetMeter(_admin, v));

        // Zincir doğru: toplam km = 200 + 300 + 300 = 800
        var rows = _fuel.ListDistributions(_admin);
        var totalKm = rows.Sum(r => r.CurrentMeter - r.PrevMeter);
        Assert.Equal(800m, totalKm);
        Assert.Equal(3, rows.Count);                             // iptal edilen görünmüyor
    }

    [Fact]
    public void Tasinan_baslangic_sayaci_ARAC_SAYACINI_GERI_ALDIRMAZ()
    {
        Depot(1000m);
        var v = Vehicle(10_000m);
        Distribute(v, 100m, 10_800m);                            // sayaç 10.800

        // Geçmişe dönük düzeltme kaydı (başlangıç 10.200, bitiş 10.500)
        Distribute(v, 100m, 10_500m, prevMeter: 10_200m);

        Assert.Equal(10_800m, _vehicles.GetMeter(_admin, v));    // ❗ sayaç geriye GİTMEDİ
    }

    // ── 4. GÖRÜNÜRLÜK (Y3) ──────────────────────────────────────────────────────────────────

    [Fact]  // T14 + T15
    public void Iptal_edilen_kayit_varsayilan_GIZLI_istenirse_GORUNUR()
    {
        var e = Depot(1000m);
        var v = Vehicle();
        var d = Distribute(v, 100m, 10_200m);
        _fuel.CancelDistribution(_admin, d, "gerekçe");
        _fuel.CancelDepotEntry(_admin, e, "gerekçe");

        Assert.Empty(_fuel.ListDistributions(_admin));                                  // varsayılan gizli
        Assert.Empty(_fuel.ListDepotEntries(_admin));

        var dists = _fuel.ListDistributions(_admin, includeCancelled: true);
        var depots = _fuel.ListDepotEntries(_admin, includeCancelled: true);
        Assert.Single(dists);
        Assert.Single(depots);
        Assert.True(dists[0].IsCancelled);                                              // ekranda ayırt edilebilir
        Assert.Equal("İptal edildi", dists[0].StatusText);
        Assert.True(depots[0].IsCancelled);
    }

    // ── 5. RAPOR (T3) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Iptal_edilen_dagitim_YAKIT_RAPORUNDAN_cikar()
    {
        Depot(1000m);
        var v = Vehicle(10_000m);
        Distribute(v, 100m, 10_200m);
        var d2 = Distribute(v, 120m, 10_500m);

        // Rapor sorgusu da listelerle AYNI "is_deleted=0" filtresini kullanır (ReportService).
        decimal LitersInReport() => _fuel.ListDistributions(_admin).Sum(x => x.Liters);

        Assert.Equal(220m, LitersInReport());
        _fuel.CancelDistribution(_admin, d2, "hatalı");
        Assert.Equal(100m, LitersInReport());                    // iptal edilen litre düştü
    }

    // ── 6. YETKİ (Y5) ───────────────────────────────────────────────────────────────────────

    [Fact]  // T13
    public void Yetkisiz_kullanici_IPTAL_EDEMEZ()
    {
        Depot(1000m);
        var d = Distribute(Vehicle(), 50m, 10_100m);

        // fuel modülünde tam yetki VAR ama "Ters Kayıt" özel butonu YOK → iptal edemez.
        var perms = new PermissionSet(new[] { new ModulePermission("fuel", true, true, true, false) });
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, perms);

        Assert.Throws<ForbiddenException>(() => _fuel.CancelDistribution(staff, d, "gerekçe"));
        Assert.Equal(950m, _fuel.GetDepotBalance(_admin));       // değişmedi
    }

    // ── 7. REGRESYON (T18–T20) ──────────────────────────────────────────────────────────────

    [Fact]
    public void Mevcut_yakit_davranisi_BOZULMADI()
    {
        var e = Depot(500m, 42m);
        Assert.NotEqual("", e);
        Assert.Equal(500m, _fuel.GetDepotBalance(_admin));
        Assert.Equal(42m, _fuel.GetCurrentFuelPrice(_admin));

        var v = Vehicle(5_000m);
        Distribute(v, 60m, 5_300m);
        Assert.Equal(440m, _fuel.GetDepotBalance(_admin));
        Assert.Equal(5_300m, _vehicles.GetMeter(_admin, v));

        // Depo yetersizken dağıtım reddi (mevcut kural)
        Assert.Throws<InvalidOperationException>(() => Distribute(v, 10_000m, 5_400m));

        // prev_meter verilmezse eski davranış: araçtan okunur
        var rows = _fuel.ListDistributions(_admin);
        Assert.Equal(5_000m, rows.Single().PrevMeter);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
