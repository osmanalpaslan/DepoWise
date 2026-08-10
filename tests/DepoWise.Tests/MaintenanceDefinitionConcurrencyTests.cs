using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// B-1 (PRT-01 Grup 3): bakım tanımı DÜZENLEME KİLİDİ.
/// Servisin <c>expectedVersion</c> desteği zaten vardı ama sürüm listeden dönmediği için iki platform da
/// gönderemiyordu → aynı tanımı iki kişi düzenlediğinde ikincisi birincinin işini sessizce eziyordu.
/// Bu testler sürümün uçtan uca TAŞINDIĞINI ve bayat sürümün reddedildiğini doğrular.
/// </summary>
public class MaintenanceDefinitionConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaintenanceDefinitionService _defs;
    private readonly SessionContext _admin;

    public MaintenanceDefinitionConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mdefver_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private MaintenanceDefinitionRow Get(string id)
        => _defs.List(_admin).Single(d => d.Id == id);

    [Fact]
    public void Liste_Surumu_Dondurur_Ve_Guncellemede_Artar()
    {
        var id = _defs.Create(_admin, new NewMaintenanceDefinition("Yağ Değişimi", 10000));

        var v1 = Get(id).Version;
        Assert.True(v1 > 0, "Liste sürüm döndürmeli — 0 gelirse istemci kilidi gönderemez.");

        _defs.Update(_admin, id, new NewMaintenanceDefinition("Yağ Değişimi", 15000), v1);
        Assert.Equal(v1 + 1, Get(id).Version);
    }

    [Fact]
    public void Bayat_Surumle_Guncelleme_Reddedilir()
    {
        var id = _defs.Create(_admin, new NewMaintenanceDefinition("Filtre", 5000));
        var v1 = Get(id).Version;

        // 1. kullanıcı kaydeder → sürüm ilerler.
        _defs.Update(_admin, id, new NewMaintenanceDefinition("Filtre", 6000), v1);

        // 2. kullanıcı formu ESKİ sürümle açmıştı; kaydetmeye çalışır.
        var ex = Assert.Throws<ConcurrencyException>(() =>
            _defs.Update(_admin, id, new NewMaintenanceDefinition("Filtre", 9999), v1));
        Assert.Equal(v1, ex.ExpectedVersion);
        Assert.Equal(v1 + 1, ex.ActualVersion);

        // Birinci kullanıcının değeri KORUNUR — sessiz üzerine yazma olmadı.
        Assert.Equal(6000m, Get(id).IntervalValue);
    }

    [Fact]
    public void Surum_Verilmezse_Kontrol_Yapilmaz_Geriye_Uyumlu()
    {
        var id = _defs.Create(_admin, new NewMaintenanceDefinition("Balata", 20000));
        _defs.Update(_admin, id, new NewMaintenanceDefinition("Balata", 21000), Get(id).Version);

        // Eski istemci (sürüm göndermez) çalışmaya devam eder.
        _defs.Update(_admin, id, new NewMaintenanceDefinition("Balata", 22000));
        Assert.Equal(22000m, Get(id).IntervalValue);
    }

    [Fact]
    public void Alt_Bakim_Tanimi_Da_Surum_Tasir()
    {
        var parent = _defs.Create(_admin, new NewMaintenanceDefinition("Genel Bakım", 10000));
        var sub = _defs.Create(_admin, new NewMaintenanceDefinition("Alt İş", 0, ParentDefId: parent));

        var row = _defs.List(_admin, parent).Single(d => d.Id == sub);
        Assert.True(row.Version > 0);

        Assert.Throws<ConcurrencyException>(() =>
        {
            _defs.Update(_admin, sub, new NewMaintenanceDefinition("Alt İş", 1, ParentDefId: parent), row.Version);
            _defs.Update(_admin, sub, new NewMaintenanceDefinition("Alt İş", 2, ParentDefId: parent), row.Version);
        });
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
