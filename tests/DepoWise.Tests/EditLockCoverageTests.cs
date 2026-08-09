using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DÜZENLEME KİLİDİ KAPSAM TAMAMLAMA — Talepler + Şube/Şantiye (İş #6, 2026-08-09).
///
/// Envanter sonucu: <c>version</c> sütunu HER İKİ tabloda da vardı ve her UPDATE'te ilerletiliyordu,
/// ama HİÇ KONTROL EDİLMİYORDU → iki kullanıcı aynı talebi/şubeyi düzenlediğinde ikincisi birincisinin
/// değişikliğini SESSİZCE eziyordu. Diğer servisler (Malzeme/Araç/Personel/Bakım Tanımı) bu kilidi
/// zaten kullanıyordu; burada YENİ bir mekanizma icat edilmedi, mevcut <c>EditLockGuard</c> deseni
/// bu iki servise de uygulandı.
///
/// Senaryo (her iki kayıt türü için aynı): A kaydı açar · B aynı kaydı açar · A kaydeder ·
/// B eski sürümle kaydetmeye çalışır · B REDDEDİLİR · A'nın değişikliği KORUNUR.
/// </summary>
public class EditLockCoverageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly RequestService _requests;
    private readonly BranchService _branches;
    private readonly MaterialService _materials;
    private readonly UserService _users;
    private readonly SessionContext _a, _b;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public EditLockCoverageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_editlock_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _users = new UserService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _requests = new RequestService(_factory, new StockService(_factory, _clock), _clock);
        _branches = new BranchService(_factory, _clock);

        Company("A"); Company("B");
        _a = Admin("A", "kul_a");
        _b = Admin("B", "kul_b");
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

    private string NewRequestFor(SessionContext s, string desc)
    {
        var mat = _materials.Create(s, new NewMaterial("M-" + Guid.NewGuid().ToString("N")[..6], "Filtre"));
        var h = _requests.Create(s, new NewRequest(new[] { new RequestItemInput(mat, 1m) }, Description: desc));
        return h.Id;
    }

    private NewRequest DtoFor(SessionContext s, string requestId, string desc)
    {
        var cur = _requests.GetForEdit(s, requestId);
        var items = cur.Items.Select(i => new RequestItemInput(i.MaterialId, i.Quantity, i.VehicleId)).ToList();
        return new NewRequest(items, Description: desc);
    }

    // ── TALEPLER ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Talep_duzenleme_verisi_SURUM_tasir()
    {
        var id = NewRequestFor(_a, "ilk");
        Assert.True(_requests.GetForEdit(_a, id).Version > 0);   // form açılışında sürüm okunabilmeli
    }

    [Fact]
    public void Talep_DUZENLEME_KILIDI_eski_surumle_kaydetmeyi_engeller()
    {
        var id = NewRequestFor(_a, "ilk");
        var bninSurumu = _requests.GetForEdit(_a, id).Version;    // B formu açtı

        _clock.UtcNow = _clock.UtcNow.AddMilliseconds(1000);
        var aninSurumu = _requests.GetForEdit(_a, id).Version;    // A formu açtı
        _requests.Update(_a, id, DtoFor(_a, id, "A kaydetti"), aninSurumu);   // A kaydetti

        // B eski sürümle kaydetmeye çalışıyor → SESSİZCE ÜZERİNE YAZMAMALI
        Assert.Throws<ConcurrencyException>(() =>
            _requests.Update(_a, id, DtoFor(_a, id, "B'nin eski verisi"), bninSurumu));

        Assert.Equal("A kaydetti", _requests.GetForEdit(_a, id).Description);   // A'nın değişikliği KORUNDU
    }

    [Fact]
    public void Talep_dogru_surumle_kaydedilir_ve_surum_ilerler()
    {
        var id = NewRequestFor(_a, "ilk");
        var v = _requests.GetForEdit(_a, id).Version;
        _requests.Update(_a, id, DtoFor(_a, id, "guncel"), v);

        var sonra = _requests.GetForEdit(_a, id);
        Assert.Equal("guncel", sonra.Description);
        Assert.True(sonra.Version > v);
    }

    [Fact]
    public void Talep_surum_verilmezse_eski_davranis_korunur()
    {
        var id = NewRequestFor(_a, "ilk");
        _requests.Update(_a, id, DtoFor(_a, id, "surumsuz"));   // geriye uyumlu: kontrol yok
        Assert.Equal("surumsuz", _requests.GetForEdit(_a, id).Description);
    }

    [Fact]
    public void Talep_BASKA_firmanin_kaydi_surum_dogru_olsa_bile_reddedilir()
    {
        var idB = NewRequestFor(_b, "B'nin talebi");
        var vB = _requests.GetForEdit(_b, idB).Version;

        Assert.ThrowsAny<Exception>(() => _requests.Update(_a, idB, DtoFor(_b, idB, "ELE GECIRILDI"), vB));
        Assert.Equal("B'nin talebi", _requests.GetForEdit(_b, idB).Description);
    }

    [Fact]
    public void Talep_kalemleri_reddedilen_kayitta_DEGISMEZ()
    {
        // Kilit reddi transaction'ı geri almalı: başlık gibi KALEMLER de eski hâlinde kalmalı.
        var id = NewRequestFor(_a, "ilk");
        var eskiSurum = _requests.GetForEdit(_a, id).Version;
        var eskiKalemSayisi = _requests.GetForEdit(_a, id).Items.Count;

        _requests.Update(_a, id, DtoFor(_a, id, "A kaydetti"));   // araya giren kayıt

        var yeniMat = _materials.Create(_a, new NewMaterial("M-EK", "Ek malzeme"));
        var kalemler = _requests.GetForEdit(_a, id).Items
            .Select(i => new RequestItemInput(i.MaterialId, i.Quantity)).ToList();
        kalemler.Add(new RequestItemInput(yeniMat, 5m));

        Assert.Throws<ConcurrencyException>(() =>
            _requests.Update(_a, id, new NewRequest(kalemler, Description: "eski veri"), eskiSurum));

        var sonra = _requests.GetForEdit(_a, id);
        Assert.Equal(eskiKalemSayisi, sonra.Items.Count);        // kalem EKLENMEDİ (rollback çalıştı)
        Assert.Equal("A kaydetti", sonra.Description);
    }

    // ── ŞUBE / ŞANTİYE ────────────────────────────────────────────────────────────────────

    private BranchRow Branch(SessionContext s, string id) => _branches.List(s).Single(b => b.Id == id);

    [Fact]
    public void Sube_listesi_SURUM_tasir()
    {
        var id = _branches.Create(_a, new NewBranch("Merkez"));
        Assert.True(Branch(_a, id).Version > 0);
    }

    [Fact]
    public void Sube_DUZENLEME_KILIDI_eski_surumle_kaydetmeyi_engeller()
    {
        var id = _branches.Create(_a, new NewBranch("Merkez"));
        var bninSurumu = Branch(_a, id).Version;                 // B formu açtı

        _clock.UtcNow = _clock.UtcNow.AddMilliseconds(1000);
        var aninSurumu = Branch(_a, id).Version;                 // A formu açtı
        _branches.Update(_a, id, new NewBranch("A kaydetti"), expectedVersion: aninSurumu);

        Assert.Throws<ConcurrencyException>(() =>
            _branches.Update(_a, id, new NewBranch("B'nin eski verisi"), expectedVersion: bninSurumu));

        Assert.Equal("A kaydetti", Branch(_a, id).Name);         // A'nın değişikliği KORUNDU
    }

    [Fact]
    public void Sube_dogru_surumle_kaydedilir_ve_surum_ilerler()
    {
        var id = _branches.Create(_a, new NewBranch("Merkez"));
        var v = Branch(_a, id).Version;
        _branches.Update(_a, id, new NewBranch("Merkez Yeni"), expectedVersion: v);

        var sonra = Branch(_a, id);
        Assert.Equal("Merkez Yeni", sonra.Name);
        Assert.True(sonra.Version > v);
    }

    [Fact]
    public void Sube_surum_verilmezse_eski_davranis_korunur()
    {
        var id = _branches.Create(_a, new NewBranch("Merkez"));
        _branches.Update(_a, id, new NewBranch("Surumsuz"));
        Assert.Equal("Surumsuz", Branch(_a, id).Name);
    }

    [Fact]
    public void Sube_BASKA_firmanin_kaydi_surum_dogru_olsa_bile_reddedilir()
    {
        var idB = _branches.Create(_b, new NewBranch("B Şubesi"));
        var vB = Branch(_b, idB).Version;

        Assert.ThrowsAny<Exception>(() =>
            _branches.Update(_a, idB, new NewBranch("ELE GECIRILDI"), expectedVersion: vB));
        Assert.Equal("B Şubesi", Branch(_b, idB).Name);
    }

    [Fact]
    public void Sube_SIFRESI_kilit_reddinde_DEGISMEZ()
    {
        // Şifre COALESCE ile yazılıyor; kilit reddi bu yazımı da geri almalı.
        var id = _branches.Create(_a, new NewBranch("Merkez", "branch", null, "K1", "ilkSifre"));
        var eskiSurum = Branch(_a, id).Version;

        _branches.Update(_a, id, new NewBranch("A kaydetti", "branch", null, "K1", "yeniSifre"));
        var araSurum = Branch(_a, id).Version;

        Assert.Throws<ConcurrencyException>(() =>
            _branches.Update(_a, id, new NewBranch("B eski", "branch", null, "K1", "bSifresi"), expectedVersion: eskiSurum));

        var sonra = Branch(_a, id);
        Assert.Equal("A kaydetti", sonra.Name);
        Assert.Equal(araSurum, sonra.Version);   // sürüm İLERLEMEDİ → UPDATE hiç uygulanmadı
        Assert.True(sonra.HasPassword);
    }
}
