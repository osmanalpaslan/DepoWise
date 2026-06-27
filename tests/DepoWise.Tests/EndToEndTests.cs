using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Faz 17 — Uçtan uca: temiz DB üzerinde çapraz-modül tam akış + tenant izolasyonu kanıtı.
/// Tek senaryoda: kurulum → malzeme/stok → araç → bakım (stok+sayaç+uyarı) → talep (onay stok değiştirmez) →
/// kontrollü çıkış → offline sync (idempotent) → yedek/geri yükleme.
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _backupFolder;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public EndToEndTests()
    {
        var stamp = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_e2e_" + stamp + ".db");
        _backupFolder = Path.Combine(Path.GetTempPath(), "depowise_e2e_bak_" + stamp);
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run(); // temiz DB → tüm migration'lar
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private SessionContext Admin(string company, string user)
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(company, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Fact]
    public void UctanUca_TamAkis_TenantIzolasyonu()
    {
        var a = Admin("A", "admin_a");
        var b = Admin("B", "admin_b");

        var materials = new MaterialService(_factory, _clock);
        var opening = new OpeningStockService(_factory, _clock);
        var stock = new StockService(_factory, _clock);
        var vehicles = new VehicleService(_factory, _clock);
        var defs = new MaintenanceDefinitionService(_factory, _clock);
        var maint = new MaintenanceService(_factory, _clock);
        var requests = new RequestService(_factory, stock, _clock);

        // 1) Malzeme + açılış stoğu (ledger)
        var m = materials.Create(a, new NewMaterial("M-1", "Filtre", UnitPrice: 50m, MinStock: 5m));
        opening.RecordOpening(a, m, 100m, "e2e-open");
        Assert.Equal(100m, stock.GetBalance(m));

        // 2) Araç + sayaç
        var v = vehicles.Create(a, new NewVehicle("V-1", CurrentMeter: 1000m));

        // 3) Bakım: stok düşer (tek), sayaç ileri, sonraki hedef + uyarı döngüsü
        var def = defs.Create(a, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        maint.Save(a, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), "e2e-mnt");
        Assert.Equal(90m, stock.GetBalance(m));            // 100 - 10
        Assert.Equal(1000m, vehicles.GetMeter(a, v));
        vehicles.SetMeter(a, v, 1098m);                    // tüketilen 98 → %98 kritik
        Assert.Equal(AlertLevel.Critical, maint.GetAlerts(a).Single().Level);
        _clock.Advance(60_000);
        maint.Save(a, new NewMaintenance(v, def, PerformedKm: 1098m), "e2e-mnt2"); // yeni bakım → uyarı temizlenir
        Assert.Equal(AlertLevel.Normal, maint.GetAlerts(a).Single().Level);

        // 4) Talep: onay STOK DEĞİŞTİRMEZ; kontrollü çıkış stok düşürür
        var req = requests.Create(a, new NewRequest(new[] { new RequestItemInput(m, 20m) }, SubmitImmediately: true));
        requests.Approve(a, req.Id);
        Assert.Equal(90m, stock.GetBalance(m));            // onayda değişmedi
        requests.CreateIssueFromRequest(a, req.Id, "e2e-issue");
        Assert.Equal(70m, stock.GetBalance(m));            // kontrollü çıkış: 90 - 20

        // 5) Tenant izolasyonu: B firması A'nın malzemesini görmez
        Assert.Empty(materials.List(b, new PageRequest { Limit = 50 }).Items);

        // 6) Offline sync: idempotent push (retry çift uygulamaz)
        var enroll = new EnrollmentService(_factory, _clock);
        var server = new SyncServer(_factory, _clock);
        var key = enroll.CreateEnrollmentKey(a);
        var dev = enroll.Enroll("A", key, "Saha-1");
        var token = enroll.ApproveDevice(a, dev.DeviceId).Token;
        var ops = new[] { new SyncOperation("e2e-op", "material", m, "{\"name\":\"x\"}") };
        Assert.Equal(SyncOpResult.Accepted, server.Push(token, ops)[0].Result);
        Assert.Equal(SyncOpResult.AlreadyApplied, server.Push(token, ops)[0].Result); // retry

        // 7) Yedek + geri yükleme: yedek sonrası değişiklik → restore yedek anına döner
        var backup = new BackupService(_factory, _clock, _backupFolder);
        var bkPath = backup.Backup();
        Assert.True(backup.IntegrityCheck(bkPath));
        materials.Create(a, new NewMaterial("M-2", "Sonradan"));
        Assert.Equal(2, materials.List(a, new PageRequest { Limit = 50 }).Items.Count);
        backup.Restore(a, bkPath, reauthenticated: true);
        Assert.Single(materials.List(a, new PageRequest { Limit = 50 }).Items); // M-2 yok (geri yüklendi)
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
        try { if (Directory.Exists(_backupFolder)) Directory.Delete(_backupFolder, true); } catch { }
    }
}
