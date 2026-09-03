using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ GÜNLÜK FAALİYET — KAYIT TİPİ YETKİSİ (kullanıcı isteği 2026-09-03, ADR-199) ═══
///
/// Kullanıcı: "kayıt tipine yetki verilmemiş ise kayıt tipi görünmemeli."
///
/// GEÇİŞ GÜVENLİ kural (rapor kalemleriyle aynı): hiç datype_* anahtarı verilmemişse TÜM tipler
/// görünür (yayında kimse bir şey kaybetmez); en az bir anahtar verildiği anda kullanıcı YALNIZ
/// verilen tipleri görür/seçer.
///
///  TY1 — Hiç tip ataması yok → tüm tipler seçilebilir ve tüm satırlar listelenir (mevcut davranış).
///  TY2 — Yalnız "Hareket" izni → seçenekler ve liste yalnız hareket; bakım/transfer satırı GİZLİ.
///  TY3 — Hareket/Transfer ayrımı: yalnız "Transfer" izni transfer satırını gösterir, düz hareketi GÖSTERMEZ
///        (ikisi de DB'de activity_type='movement'; ayrım movement_kind ile).
///  TY4 — datype_* anahtarları menü kaynağına (AppModules.All) SIZMADI; kalem üretimi katalogdan otomatik.
///  TY5 — SearchGrid (sayfalı liste) de aynı süzgeci uygular (List ile tutarlı — iki yol da kapalı).
/// </summary>
public class GunlukFaaliyetTipYetkisiTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _admin;
    private const string Co = "TIPYETKI";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public GunlukFaaliyetTipYetkisiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_tipyetki_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        var clock = new TestClock();
        var materials = new MaterialService(_f, clock);
        var vehicles = new VehicleService(_f, clock);
        var defs = new MaintenanceDefinitionService(_f, clock);
        var maint = new MaintenanceService(_f, clock);
        _daily = new DailyActivityService(_f, maint, clock, defs);

        var users = new UserService(_f, clock);
        var uid = users.EnsureInitialAdmin(Co, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // Tohum: 1 bakım + 1 hareket + 1 transfer (üçü de firmada; süzgeç bunların üstünde ölçülür).
        var v = vehicles.Create(_admin, new NewVehicle("ARC-1", CurrentMeter: 1000m));
        var d = defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, d, PerformedKm: 1100m), "op-bakim");
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", Description: "sevk"), "op-hareket");
        _daily.SaveMovement(_admin, new NewMovementActivity("transfer", Description: "devir"), "op-transfer");
    }

    /// <summary>Personel oturumu: daily_activity View + verilen datype_* anahtarları (yalnız View).</summary>
    private static SessionContext Personel(params string[] tipAnahtarlari)
        => new("u-t", Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { "daily_activity" }.Concat(tipAnahtarlari)
                .Select(m => new ModulePermission(m, true, false, false, false)).ToArray()));

    [Fact]
    public void TY1_Hic_Atama_Yoksa_Tum_Tipler_Gorunur()
    {
        var s = Personel();                                     // hiç datype_* yok → mevcut davranış
        Assert.Equal(DailyActivityTypeOptions.All.Count, DailyActivityTypeGate.AllowedTypes(s).Count);
        Assert.Equal(3, _daily.List(s).Count);                  // bakım + hareket + transfer, hepsi görünür
    }

    [Fact]
    public void TY2_Yalniz_Hareket_Izni_Digerlerini_Gizler()
    {
        var s = Personel(DailyActivityTypeGate.Key(DailyActivityTypeOptions.Movement));
        Assert.Equal(new[] { DailyActivityTypeOptions.Movement }, DailyActivityTypeGate.AllowedTypes(s));

        var satirlar = _daily.List(s);
        var satir = Assert.Single(satirlar);
        Assert.Equal("movement", satir.ActivityType);
        Assert.NotEqual("transfer", satir.MovementKind);        // transfer DEĞİL, düz hareket
    }

    [Fact]
    public void TY3_Transfer_Izni_Duz_Hareketi_Gostermez()
    {
        var s = Personel(DailyActivityTypeGate.Key(DailyActivityTypeOptions.Transfer));
        var satir = Assert.Single(_daily.List(s));
        Assert.Equal("movement", satir.ActivityType);           // DB tipi movement...
        Assert.Equal("transfer", satir.MovementKind);           // ...ama yalnız transfer türü

        Assert.True(DailyActivityTypeGate.CanSeeRow(s, "movement", "transfer"));
        Assert.False(DailyActivityTypeGate.CanSeeRow(s, "movement", null));
        Assert.False(DailyActivityTypeGate.CanSeeRow(s, "maintenance", null));
    }

    [Fact]
    public void TY4_Tip_Anahtarlari_Menu_Kaynagina_Sizmaz_ve_Katalogdan_Uretilir()
    {
        Assert.Equal(DailyActivityTypeOptions.All.Count, DailyActivityTypeGate.Items.Count);
        foreach (var (key, label) in DailyActivityTypeGate.Items)
        {
            Assert.True(DailyActivityTypeGate.IsTypeKey(key));
            Assert.StartsWith("Günlük Faaliyet", label);
            Assert.DoesNotContain(AppModules.All, m => m.Key == key);   // menü kaynağına SIZMADI
            Assert.Equal(label, AppModules.Label(key));                 // etiket ağaçta çözülür
        }
    }

    [Fact]
    public void TY5_SearchGrid_de_Ayni_Suzgeci_Uygular()
    {
        var kisitli = _daily.SearchGrid(Personel(DailyActivityTypeGate.Key(DailyActivityTypeOptions.Maintenance)),
            new DailyActivityGridFilter(), page: 1, pageSize: 50);
        Assert.Equal(1, kisitli.TotalCount);
        Assert.Equal("Bakım", Assert.Single(kisitli.Items).TypeText);

        var serbest = _daily.SearchGrid(Personel(), new DailyActivityGridFilter(), page: 1, pageSize: 50);
        Assert.Equal(3, serbest.TotalCount);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
