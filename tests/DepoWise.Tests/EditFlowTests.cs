using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DÜZENLEME HATTI — Personel + Talepler (İş #4, 2026-08-09).
///
/// Masaüstünde çift tık artık mevcut <c>BeginEdit</c> / <c>BeginEditRequest</c> komutlarını tetikliyor.
/// UI için ayrı test altyapısı YOKTUR; bu yüzden UI'nin kullandığı **servis hattı** test edilir:
/// yetki, firma izolasyonu, düzenleme kilidi (expectedVersion) ve iş kuralları.
///
/// Bu testler çift tık eklenmeden ÖNCE de geçer — amaçları mevcut davranışı KANITLAMAK ve
/// çift tık kısayolunun bu korumaları atlatmadığını güvence altına almaktır (regresyon kalkanı).
/// </summary>
public class EditFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PersonnelService _personnel;
    private readonly RequestService _requests;
    private readonly MaterialService _materials;
    private readonly UserService _users;
    private readonly SessionContext _a, _b, _readOnlyA;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public EditFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_editflow_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _users = new UserService(_factory, _clock);
        _personnel = new PersonnelService(_factory, new ScopeResolver(_factory), _clock);
        _materials = new MaterialService(_factory, _clock);
        _requests = new RequestService(_factory, new StockService(_factory, _clock), _clock);

        Company("A"); Company("B");
        _a = Admin("A", "kul_a");
        _b = Admin("B", "kul_b");
        // Yalnız görüntüleme yetkisi olan kullanıcı (düzenleme yetkisi YOK)
        var roUid = _users.EnsureInitialAdmin("A", "salt_okur", "Test!2026", RoleKeys.Staff);
        _readOnlyA = new SessionContext(roUid, "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("personnel", true, false, false, false) }));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    private void Company(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private SessionContext Admin(string company, string user)
    {
        var uid = _users.EnsureInitialAdmin(company, user, "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    // ── PERSONEL düzenleme hattı (çift tık → BeginEdit → Update) ───────────────────────────

    [Fact]
    public void Personel_kendi_firmasinin_kaydini_duzenleyebilir()
    {
        var id = _personnel.Create(_a, new NewPersonnel("Ali Veli", "Şoför", "555", null));
        _personnel.Update(_a, id, new NewPersonnel("Ali Veli Yeni", "Operatör", "666", null));

        var rec = _personnel.Get(_a, id)!;
        Assert.Equal("Ali Veli Yeni", rec.FullName);
        Assert.Equal("Operatör", rec.Title);
    }

    [Fact]
    public void Personel_BASKA_firmanin_kaydini_duzenleyemez()
    {
        var idB = _personnel.Create(_b, new NewPersonnel("B Personeli", "Şoför", "555", null));

        Assert.ThrowsAny<Exception>(() =>
            _personnel.Update(_a, idB, new NewPersonnel("ELE GECIRILDI", null, null, null)));

        Assert.Equal("B Personeli", _personnel.Get(_b, idB)!.FullName);   // B'nin kaydı DEĞİŞMEDİ
    }

    [Fact]
    public void Personel_BASKA_firmanin_kaydini_OKUYAMAZ()
    {
        var idB = _personnel.Create(_b, new NewPersonnel("B Personeli", null, null, null));
        Assert.Null(_personnel.Get(_a, idB));
    }

    [Fact]
    public void Personel_duzenleme_YETKISI_yoksa_reddedilir()
    {
        var id = _personnel.Create(_a, new NewPersonnel("Ali", null, null, null));
        Assert.ThrowsAny<Exception>(() =>
            _personnel.Update(_readOnlyA, id, new NewPersonnel("Yeni Ad", null, null, null)));
        Assert.Equal("Ali", _personnel.Get(_a, id)!.FullName);
    }

    [Fact]
    public void Personel_DUZENLEME_KILIDI_eski_surumle_kaydetmeyi_engeller()
    {
        var id = _personnel.Create(_a, new NewPersonnel("Ali", null, null, null));
        var acilistakiSurum = _personnel.Get(_a, id)!.Version;   // kullanıcı A formu açtı

        // Kullanıcı B (aynı firmada, başka oturum) araya girip kaydetti → sürüm ilerledi
        _clock.UtcNow = _clock.UtcNow.AddMilliseconds(1000);
        _personnel.Update(_a, id, new NewPersonnel("B'nin kaydettiği", null, null, null));

        // Kullanıcı A eski sürümle kaydetmeye çalışıyor → SESSİZCE ÜZERİNE YAZMAMALI
        Assert.ThrowsAny<Exception>(() =>
            _personnel.Update(_a, id, new NewPersonnel("A'nin eski verisi", null, null, null), acilistakiSurum));

        Assert.Equal("B'nin kaydettiği", _personnel.Get(_a, id)!.FullName);
    }

    // ── TALEP düzenleme hattı (çift tık → BeginEditRequest → GetForEdit/Update) ────────────

    private string SeedRequest(SessionContext s)
    {
        var mat = _materials.Create(s, new NewMaterial("MAT-" + s.CompanyId, "Malzeme"));
        return _requests.Create(s, new NewRequest(new[] { new RequestItemInput(mat, 2m) })).Id;
    }

    [Fact]
    public void Talep_kendi_firmasinin_kaydini_duzenlemek_icin_ACABILIR()
    {
        var id = SeedRequest(_a);
        var d = _requests.GetForEdit(_a, id);
        Assert.Single(d.Items);
        Assert.Equal(2m, d.Items[0].Quantity);
    }

    [Fact]
    public void Talep_BASKA_firmanin_kaydini_duzenlemek_icin_ACAMAZ()
    {
        var idB = SeedRequest(_b);
        Assert.ThrowsAny<Exception>(() => _requests.GetForEdit(_a, idB));
    }

    [Fact]
    public void Talep_BASKA_firmanin_kaydini_GUNCELLEYEMEZ()
    {
        var idB = SeedRequest(_b);
        var matA = _materials.Create(_a, new NewMaterial("MAT-A2", "A malzemesi"));

        Assert.ThrowsAny<Exception>(() =>
            _requests.Update(_a, idB, new NewRequest(new[] { new RequestItemInput(matA, 99m) })));

        // B'nin talebi DEĞİŞMEDİ (kalem sayısı ve miktar aynı)
        var d = _requests.GetForEdit(_b, idB);
        Assert.Single(d.Items);
        Assert.Equal(2m, d.Items[0].Quantity);
    }

    [Fact]
    public void Talep_ONAYLI_ise_duzenlenemez()
    {
        var id = SeedRequest(_a);
        _requests.Submit(_a, id);
        _requests.Approve(_a, id);

        var mat = _materials.Create(_a, new NewMaterial("MAT-A3", "Baska"));
        Assert.ThrowsAny<Exception>(() =>
            _requests.Update(_a, id, new NewRequest(new[] { new RequestItemInput(mat, 5m) })));
    }
}
