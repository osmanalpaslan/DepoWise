using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ŞB-01 / ŞB-02 / ŞB-03 (2026-08-18) — ŞUBE HİYERARŞİSİ.
///
/// <b>ŞB-01 (kullanıcının bildirdiği hata):</b> masaüstünde üst şube seçilip kaydediliyor, sunucu doğru
/// kaydediyor, ama hemen ardından çalışan şube AYNASI (<see cref="BranchMirrorApply"/>) yerel kopyayı
/// üst şubesiz ve tür'ü <c>branch</c> olarak tazeliyordu. Ekran yerelden okuduğu için değer
/// "tanımlanmamış gibi" geri dönüyordu. Ayna artık <c>kind</c> + <c>parent_id</c> taşır.
///
/// <b>ŞB-02:</b> yalnız "kendi üst şubesi olamaz" kontrolü vardı; A→B, B→A döngüsü kurulabiliyordu.
///
/// <b>ŞB-03:</b> liste sorgusu silinmiş üst şubeyi filtrelemiyordu (adı görünmeye devam ediyordu) ve
/// üst şube, altında şube varken silinebiliyordu (kopuk referans).
/// </summary>
public class BranchHierarchyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly BranchService _branches;
    private readonly AuthService _auth;
    private const string Co = "DEPOWISE";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public BranchHierarchyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_subeagac_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
    }

    private SessionContext SuperAdmin()
    {
        _users.EnsureInitialAdmin(Co, "root", "root123", RoleKeys.SuperAdmin);
        return _auth.Login(Co, "root", "root123").Session!;
    }

    // ── ŞB-01 ────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Giriş/ayna ucu (ListForLogin) üst şubeyi ve türü TAŞIMALI — ayna bunu okur.</summary>
    [Fact]
    public void SB01_GirisListesi_UstSube_Ve_Turu_Tasir()
    {
        var su = SuperAdmin();
        var ust = _branches.Create(su, new NewBranch("Merkez"));
        var alt = _branches.Create(su, new NewBranch("Şantiye 1", "site", ust));

        var satirlar = _branches.ListForLogin(Co);

        var a = satirlar.Single(x => x.Id == alt);
        Assert.Equal(ust, a.ParentId);
        Assert.Equal("site", a.Kind);
    }

    /// <summary>Ayna, üst şubeyi ve türü yerel kopyaya YAZMALI (eski davranışta ikisi de düşüyordu).</summary>
    [Fact]
    public void SB01_Ayna_UstSube_Ve_Turu_Yerele_Yazar()
    {
        var su = SuperAdmin();

        BranchMirrorApply.Run(_factory, Co, new[]
        {
            new BranchMirrorApply.Row("B-UST", "Merkez", "MRK", "branch", null),
            new BranchMirrorApply.Row("B-ALT", "Şantiye 1", "S1", "site", "B-UST"),
        });

        var liste = _branches.List(su, Co);
        var alt = liste.Single(x => x.Id == "B-ALT");
        Assert.Equal("B-UST", alt.ParentId);
        Assert.Equal("Merkez", alt.ParentName);
        Assert.Equal("site", alt.Kind);
        Assert.Equal("Şantiye", alt.KindDisplay);
    }

    /// <summary>ALT ŞUBE ÖNCE gelse bile yazma başarılı olmalı (iki geçişli yazma — yabancı anahtar sırası).</summary>
    [Fact]
    public void SB01_Ayna_AltSube_Once_Gelse_De_Calisir()
    {
        var su = SuperAdmin();

        BranchMirrorApply.Run(_factory, Co, new[]
        {
            new BranchMirrorApply.Row("B-ALT", "Şantiye 1", null, "site", "B-UST"),   // ebeveynden ÖNCE
            new BranchMirrorApply.Row("B-UST", "Merkez", null, "branch", null),
        });

        var alt = _branches.List(su, Co).Single(x => x.Id == "B-ALT");
        Assert.Equal("B-UST", alt.ParentId);
    }

    /// <summary>Sunucunun listesinde OLMAYAN bir üst şubeye bağ kurulmaz (kopuk referans üretilmez).</summary>
    [Fact]
    public void SB01_Ayna_Bilinmeyen_UstSubeye_Bag_Kurmaz()
    {
        var su = SuperAdmin();

        BranchMirrorApply.Run(_factory, Co, new[]
        {
            new BranchMirrorApply.Row("B-ALT", "Şantiye 1", null, "site", "YOK-BOYLE-BIR-SUBE"),
        });

        var alt = _branches.List(su, Co).Single(x => x.Id == "B-ALT");
        Assert.Null(alt.ParentId);
    }

    /// <summary>Ayna İKİNCİ kez koştuğunda üst şube KAYBOLMAMALI (asıl şikâyet: "kaydediyorum, geri dönüyor").</summary>
    [Fact]
    public void SB01_Ayna_Tekrar_Kostugunda_UstSube_Kaybolmaz()
    {
        var su = SuperAdmin();
        var ust = _branches.Create(su, new NewBranch("Merkez"));
        var alt = _branches.Create(su, new NewBranch("Şantiye 1", "site", ust));

        // Masaüstünün kaydettikten hemen sonra yaptığı şey: sunucudan çek → yerele uygula.
        var sunucudan = _branches.ListForLogin(Co)
            .Select(b => new BranchMirrorApply.Row(b.Id, b.Name, b.Code, b.Kind, b.ParentId)).ToList();
        BranchMirrorApply.Run(_factory, Co, sunucudan);

        var a = _branches.List(su, Co).Single(x => x.Id == alt);
        Assert.Equal(ust, a.ParentId);
        Assert.Equal("site", a.Kind);
    }

    // ── ŞB-02 ────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SB02_Dogrudan_Dongu_Reddedilir()
    {
        var su = SuperAdmin();
        var a = _branches.Create(su, new NewBranch("A"));
        var b = _branches.Create(su, new NewBranch("B", "branch", a));   // B'nin üstü A

        // A'yı B'nin altına almak döngü kurar (A→B→A).
        Assert.Throws<InvalidOperationException>(() =>
            _branches.Update(su, a, new NewBranch("A", "branch", b)));
    }

    [Fact]
    public void SB02_Derin_Dongu_Reddedilir()
    {
        var su = SuperAdmin();
        var a = _branches.Create(su, new NewBranch("A"));
        var b = _branches.Create(su, new NewBranch("B", "branch", a));
        var c = _branches.Create(su, new NewBranch("C", "branch", b));   // A → B → C

        Assert.Throws<InvalidOperationException>(() =>
            _branches.Update(su, a, new NewBranch("A", "branch", c)));
    }

    [Fact]
    public void SB02_Gecerli_Tasima_Kabul_Edilir()
    {
        var su = SuperAdmin();
        var a = _branches.Create(su, new NewBranch("A"));
        var b = _branches.Create(su, new NewBranch("B"));
        var c = _branches.Create(su, new NewBranch("C", "branch", a));

        _branches.Update(su, c, new NewBranch("C", "branch", b));   // C'yi B'nin altına al — döngü yok

        Assert.Equal(b, _branches.List(su, Co).Single(x => x.Id == c).ParentId);
    }

    // ── ŞB-03 ────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SB03_AltSubesi_Olan_UstSube_Silinemez()
    {
        var su = SuperAdmin();
        var ust = _branches.Create(su, new NewBranch("Merkez"));
        _branches.Create(su, new NewBranch("Şantiye 1", "site", ust));

        var ex = Assert.Throws<InvalidOperationException>(() => _branches.Delete(su, ust));
        Assert.Contains("alt şube", ex.Message);
    }

    [Fact]
    public void SB03_Silinmis_UstSube_Listede_Gorunmez()
    {
        var su = SuperAdmin();
        var ust = _branches.Create(su, new NewBranch("Merkez"));
        var alt = _branches.Create(su, new NewBranch("Şantiye 1", "site", ust));

        // Bağ koparıldıktan sonra üst şube silinebilir.
        _branches.Update(su, alt, new NewBranch("Şantiye 1", "site", null));
        _branches.Delete(su, ust);

        // Eski parent_id'yi geri yazmadan, doğrudan veritabanında kopuk referans kur (eski verideki durum).
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE branches SET parent_id=@p WHERE id=@id;";
            cmd.AddWithValue("@p", ust);
            cmd.AddWithValue("@id", alt);
            cmd.ExecuteNonQuery();
        }

        var a = _branches.List(su, Co).Single(x => x.Id == alt);
        Assert.Null(a.ParentName);        // silinmiş üst şubenin adı GÖSTERİLMEZ
        Assert.Equal("—", a.ParentDisplay);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
