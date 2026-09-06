using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.16 — PERSONELE KULLANICI BAĞLAMA: YETKİ + YANILTICI MESAJ (2026-09-06) ═══
///
/// <b>Bildirilen hata.</b> Personel ekranından kullanıcı bağlanmak istendiğinde, firmada kullanıcı
/// OLMASINA rağmen liste boş geliyor ve ekran <i>"bağlanabilir kullanıcı yok, önce hesap açın"</i>
/// diyordu. Kök neden: işlem sabit <c>IsAdmin</c> kapısındaydı; admin olmayan kullanıcıda servis
/// <c>ForbiddenException</c> atıyor, arayüz bunu <c>catch { }</c> ile YUTUYOR ve her durumda aynı
/// "veri yok" mesajını gösteriyordu. Kullanıcı yetki sorununu veri sorunu sanıyordu.
///
/// <b>İstenen.</b> Bağlama ayrı bir yetki olsun; ekrana erişen herkes değil YALNIZ bağlama yetkisi
/// olan bağlayabilsin. Yeni yetki motoru yok — mevcut özel buton deseni (migration gerekmez).
///
///  PB1 — Admin bağlayabilir (mevcut davranış korunur)
///  PB2 — 🔴 Yetkisiz kullanıcıya AÇIK yetki hatası verilir ("kullanıcı yok" DEĞİL)
///  PB3 — 🔴 Bağlama yetkisi verilen ADMIN OLMAYAN kullanıcı listeyi görür ve bağlayabilir
///  PB4 — Yetki geri alınınca kapı yeniden kapanır (çift yönlü)
///  PB5 — Yetki kalemi yetki ağacında görünür (yönetici verebilsin)
/// </summary>
public class PersonelKullaniciBaglamaTests : IDisposable
{
    private const string Co = "PBG";
    private const string Pass = "Pbg!2026";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly UserService _users;
    private readonly PersonnelService _personel;
    private readonly PermissionService _yetkiler;
    private readonly AuthService _auth;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly string _personelId, _bagsizKullaniciId, _sinirliId;

    public PersonelKullaniciBaglamaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_pbag_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        _users = new UserService(_f);
        _personel = new PersonnelService(_f, new ScopeResolver(_f));
        _yetkiler = new PermissionService(_f, null, _cache);
        _auth = new AuthService(_f, null, _cache);

        _users.EnsureInitialAdmin(Co, "pbg_admin", Pass, RoleKeys.CompanyAdmin);
        _sinirliId = _users.EnsureInitialAdmin(Co, "pbg_personel", Pass, RoleKeys.Staff);
        _bagsizKullaniciId = _users.EnsureInitialAdmin(Co, "pbg_bagsiz", Pass, RoleKeys.Staff);

        _personelId = _personel.Create(Admin(), new NewPersonnel("Bağlanacak Kişi", null, null, null));

        // Kısıtlı kullanıcı personel ekranını görebilsin — sınanan şey BAĞLAMA yetkisi, ekran yetkisi değil.
        _yetkiler.SaveForUser(SuperAdmin(), _sinirliId,
            new[] { new ModulePermission("personnel", true, true, true, false), new ModulePermission("users", true, false, true, false) },
            Array.Empty<string>());
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SessionContext SuperAdmin() => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    private SessionContext Admin() => Oturum("pbg_admin");
    private SessionContext Sinirli() => Oturum("pbg_personel");

    private SessionContext Oturum(string ad)
    {
        var r = _auth.Login(Co, ad, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + ad);
        return r.Session!;
    }

    /// <summary>Kısıtlı kullanıcıya modül yetkileri + istenen özel butonları verir.</summary>
    private void ButonVer(params string[] butonlar)
        => _yetkiler.SaveForUser(SuperAdmin(), _sinirliId,
            new[] { new ModulePermission("personnel", true, true, true, false), new ModulePermission("users", true, false, true, false) },
            butonlar);

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ PB1 ══════════════════

    [Fact]
    public void PB1_Admin_Baglayabilir()
    {
        var liste = _users.ListLinkableUsers(Admin());
        Assert.Contains(liste, u => u.Id == _bagsizKullaniciId);

        _users.LinkPersonnel(Admin(), _bagsizKullaniciId, _personelId);

        Assert.DoesNotContain(_users.ListLinkableUsers(Admin()), u => u.Id == _bagsizKullaniciId);
    }

    // ══════════════════ PB2 — YANILTICI MESAJ ══════════════════

    /// <summary>
    /// 🔴 Bildirilen hatanın kendisi: yetkisiz kullanıcı BOŞ LİSTE değil, AÇIK bir yetki hatası
    /// almalı. Mesaj "kullanıcı yok" izlenimi vermemeli ve ne yapılacağını söylemeli.
    /// </summary>
    [Fact]
    public void PB2_Yetkisiz_Kullaniciya_Acik_Yetki_Hatasi()
    {
        var ex = Assert.Throws<ForbiddenException>(() => _users.ListLinkableUsers(Sinirli()));

        Assert.Contains("Personele Kullanıcı Bağlama", ex.Message);
        Assert.DoesNotContain("hesap açın", ex.Message);      // "veri yok" izlenimi vermemeli
        Assert.Contains("Yetkiler", ex.Message);              // nereden verileceğini söylemeli
    }

    // ══════════════════ PB3 — YETKİ VERİLİNCE ══════════════════

    [Fact]
    public void PB3_Baglama_Yetkisi_Verilen_Admin_Olmayan_Kullanici_Baglayabilir()
    {
        ButonVer(SpecialButtons.LinkUser);
        var s = Sinirli();

        var liste = _users.ListLinkableUsers(s);
        Assert.Contains(liste, u => u.Id == _bagsizKullaniciId);

        _users.LinkPersonnel(s, _bagsizKullaniciId, _personelId);

        Assert.DoesNotContain(_users.ListLinkableUsers(Admin()), u => u.Id == _bagsizKullaniciId);
    }

    // ══════════════════ PB4 — ÇİFT YÖNLÜ ══════════════════

    [Fact]
    public void PB4_Yetki_Geri_Alininca_Kapi_Kapanir()
    {
        ButonVer(SpecialButtons.LinkUser);
        Assert.NotEmpty(_users.ListLinkableUsers(Sinirli()));

        ButonVer();   // yetki geri alındı

        Assert.Throws<ForbiddenException>(() => _users.ListLinkableUsers(Sinirli()));
    }

    // ══════════════════ PB5 — YETKİ AĞACINDA GÖRÜNÜR ══════════════════

    /// <summary>Yetki kalemi katalogda yoksa yönetici onu KİMSEYE veremez (YET-02 içtihadı).</summary>
    [Fact]
    public void PB5_Yetki_Kalemi_Agacta_Gorunur()
    {
        Assert.Contains(SpecialButtons.All, b => b.Key == SpecialButtons.LinkUser);
        Assert.Equal("Personele Kullanıcı Bağlama",
            SpecialButtons.All.First(b => b.Key == SpecialButtons.LinkUser).Label);
    }
}
