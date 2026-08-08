using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ŞUBE / ŞANTİYE TANIM KİLİDİ (kullanıcı kararı 2026-08-09).
///
/// Şube/Şantiye tanımları admin-kısıtlı <c>branches</c> modülüne aittir. Daha önce <c>LookupService</c>
/// ("definitions" modülü — normal rollere verilebilir) üzerinden ekleme/yeniden adlandırma/silme
/// yapılabiliyordu; bu, admin kısıtının ATLATILMASI demekti. Kilit artık SERVİS katmanındadır:
/// arayüzdeki buton gizlense, istemci değiştirilse veya servis doğrudan çağrılsa bile yazma olmaz.
///
/// Ayrıca içe aktarma artık tanınmayan Şube/Şantiye adı için kayıt OLUŞTURMAZ; satır hatası verir.
/// </summary>
public class BranchDefinitionLockTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly LookupService _lookups;
    private readonly BranchService _branches;
    private readonly SessionContext _admin;

    public BranchDefinitionLockTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_branchlock_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _lookups = new LookupService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string SeedBranch(string name, string kind = "site")
        => _branches.Create(_admin, new NewBranch(name, kind, null, null, null));

    // ── 1. SERVİS KİLİDİ ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tanim_Servisi_Sube_OLUSTURAMAZ()
    {
        // Insert yolu artık kapalı: "branches" yazılabilir tablo değil.
        var ex = Assert.Throws<ForbiddenException>(() => _lookups.Rename(_admin, "branches", "x", "y"));
        Assert.Contains("Şube / Şantiye", ex.Message);
    }

    [Fact]
    public void Tanim_Servisi_Subeyi_YENIDEN_ADLANDIRAMAZ_ve_SILEMEZ()
    {
        var id = SeedBranch("KARAMAN");

        Assert.Throws<ForbiddenException>(() => _lookups.Rename(_admin, "branches", id, "YENİ AD"));
        Assert.Throws<ForbiddenException>(() => _lookups.Delete(_admin, "branches", id));

        // Kayıt bozulmadı.
        var list = _lookups.List(_admin, "branches");
        Assert.Single(list);
        Assert.Equal("KARAMAN", list[0].Name);
    }

    [Fact]
    public void Sube_LISTELEME_calismaya_devam_eder()
    {
        SeedBranch("KARAMAN");
        SeedBranch("ANKARA GENEL MERKEZ", "branch");

        // Seçim listeleri okumayı sürdürmeli (yalnız YAZMA kapatıldı).
        Assert.Equal(2, _lookups.List(_admin, "branches").Count);
    }

    [Fact]
    public void Mesru_yol_BranchService_ile_olusturma_calisir()
    {
        var id = SeedBranch("DÜZCE");
        Assert.NotNull(id);
        Assert.Single(_lookups.List(_admin, "branches"));
    }

    [Fact]
    public void Diger_tanim_turleri_ETKILENMEDI()
    {
        // Regresyon: birim/marka/tip vb. eskisi gibi eklenip yeniden adlandırılabilir.
        var unitId = _lookups.AddUnit(_admin, "Adet");
        _lookups.Rename(_admin, "units", unitId, "ADET");
        Assert.Equal("ADET", _lookups.List(_admin, "units").Single().Name);

        var typeId = _lookups.AddVehicleType(_admin, "Kamyon");
        _lookups.Delete(_admin, "vehicle_types", typeId);
        Assert.Empty(_lookups.List(_admin, "vehicle_types"));
    }

    // ── 2. EXCEL İÇE AKTARMA ────────────────────────────────────────────────────────────────

    [Fact]
    public void Import_TANINMAYAN_sube_icin_KAYIT_OLUSTURMAZ()
    {
        SeedBranch("KARAMAN");
        var res = new ImportLookupResolver(_lookups, _admin);

        Assert.Null(res.Branch("KARAMN"));                 // yazım hatası → eşleşme yok
        Assert.NotNull(res.Branch("KARAMAN"));             // doğru ad → eşleşir
        Assert.Equal("KARAMAN", _lookups.List(_admin, "branches").Single().Name);   // YENİ KAYIT YOK
        Assert.Empty(res.CreatedNames);                    // "oluşturuldu" listesine de girmez
    }

    [Fact]
    public void Import_ONIZLEME_tanimsiz_subeyi_SATIR_HATASI_olarak_bildirir()
    {
        SeedBranch("KARAMAN");
        var vehicles = new VehicleService(_factory, _clock);
        var import = new VehicleImportService(vehicles, _lookups);

        var rows = new List<ImportRow>
        {
            Row(1, "ARC-1", "KARAMAN"),      // geçerli
            Row(2, "ARC-2", "KARAMN"),       // tanınmayan şube → hata
        };

        var dry = import.DryRun(_admin, rows);

        Assert.Equal(1, dry.Valid);
        var err = Assert.Single(dry.Errors);
        Assert.Equal(2, err.RowNumber);                    // hangi satır olduğu belli
        Assert.Contains("KARAMN", err.Message);            // hangi değer olduğu belli
        Assert.Contains("Şube / Şantiye bulunamadı", err.Message);

        // Önizleme HİÇBİR kayıt oluşturmadı.
        Assert.Single(_lookups.List(_admin, "branches"));
    }

    [Fact]
    public void Import_KISMI_aktarim_korunur_gecerli_satirlar_gecer()
    {
        SeedBranch("KARAMAN");
        var vehicles = new VehicleService(_factory, _clock);
        var import = new VehicleImportService(vehicles, _lookups);

        var rows = new List<ImportRow>
        {
            Row(1, "ARC-1", "KARAMAN"),
            Row(2, "ARC-2", "KARAMN"),
            Row(3, "ARC-3", "KARAMAN"),
        };

        var (result, _) = import.CommitWithLookups(_admin, rows);

        Assert.Equal(2, result.Added);                      // geçerli 2 satır aktarıldı
        Assert.Equal(1, result.Failed);                     // 1 satır hatalı
        Assert.Contains(result.Errors, e => e.RowNumber == 2);
        Assert.Single(_lookups.List(_admin, "branches"));   // yeni şube OLUŞMADI
    }

    private static ImportRow Row(int no, string code, string branch)
        => new(no, new Dictionary<string, string?>
        {
            [VehicleImportService.ColCode] = code,
            [VehicleImportService.ColBranch] = branch,
        });

    // ── 3. SENKRONİZASYON HİPOTEZİ ──────────────────────────────────────────────────────────

    /// <summary>
    /// HİPOTEZ TESTİ: masaüstünde oluşan bir şube sunucuya GÖNDERİLMEZ; ona bağlı araç gönderilirse
    /// sunucuda yabancı anahtar (FK) nedeniyle uygulanamaz. İki ayrı yerel veritabanı ile
    /// (istemci + "sunucu") izole olarak test edilir; canlı veriye DOKUNULMAZ.
    /// </summary>
    [Fact]
    public void Senkron_Sube_PUSH_edilmez_ve_ona_bagli_arac_sunucuda_UYGULANMAZ()
    {
        // 1) Şube PUSH listesinde yok mu?
        Assert.DoesNotContain("branches", BusinessSyncService.Tables);

        // 2) İstemci: şube + o şubeye bağlı araç
        var branchId = SeedBranch("YEREL ŞANTİYE");
        var vehicles = new VehicleService(_factory, _clock);
        vehicles.Create(_admin, new NewVehicle("ARC-LOCAL", BranchId: branchId));

        var sync = new BusinessSyncService(_factory, _clock);
        var snapshotJson = sync.BuildSnapshot("A");
        using var doc = JsonDocument.Parse(snapshotJson);
        var tables = doc.RootElement.GetProperty("tables");

        // Snapshot'ta şube YOK, ama araç VAR → araç sunucuya şubesiz gider.
        Assert.False(tables.TryGetProperty("branches", out _));
        Assert.True(tables.TryGetProperty("vehicles", out var vhs));
        Assert.Equal(1, vhs.GetArrayLength());

        // 3) "Sunucu": aynı firma var ama o şube YOK
        var serverPath = Path.Combine(Path.GetTempPath(), "depowise_srv_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var serverFactory = new SqliteConnectionFactory(serverPath);
            new MigrationRunner(serverFactory).Run();
            new UserService(serverFactory, _clock).EnsureInitialAdmin("A", "srvadmin", "admin123", RoleKeys.CompanyAdmin);

            var serverSync = new BusinessSyncService(serverFactory, _clock);
            var applied = serverSync.Apply("A", doc.RootElement);

            // Araç satırı FK nedeniyle UYGULANAMAZ → atlanır ve hata listesine düşer.
            var serverVehicles = new VehicleService(serverFactory, _clock);
            var sAdmin = new SessionContext("srv", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
            Assert.Empty(serverVehicles.List(sAdmin, null, 100));
            Assert.True(applied.Skipped > 0, "araç satırı atlanmalıydı");
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(serverPath); } catch { }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
