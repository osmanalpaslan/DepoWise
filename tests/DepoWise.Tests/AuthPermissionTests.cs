using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class AuthPermissionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public AuthPermissionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_auth_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(TimeSpan t) => UtcNow = UtcNow.Add(t);
    }

    // ---- Parola hash ----
    [Fact]
    public void Parola_Hash_DogrulanirVeYanlisReddedilir()
    {
        var h = PasswordHasher.Hash("S3cret!");
        Assert.StartsWith("pbkdf2$sha256$", h);
        Assert.True(PasswordHasher.Verify("S3cret!", h));
        Assert.False(PasswordHasher.Verify("yanlis", h));
    }

    [Fact]
    public void Parola_FarkliSalt_FarkliHash()
        => Assert.NotEqual(PasswordHasher.Hash("ayni"), PasswordHasher.Hash("ayni"));

    // ---- Login + kilit ----
    [Fact]
    public void Login_BasariliVeYanlis()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);

        Assert.False(auth.Login("A", "admin", "yanlis").Success);
        var ok = auth.Login("A", "admin", "admin123");
        Assert.True(ok.Success);
        Assert.Equal("A", ok.Session!.CompanyId);
        Assert.True(ok.Session.IsCompanyAdmin);
    }

    [Fact]
    public void Login_5Hata_Sonra_Kilit_VeBasariSifirlar()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);

        for (int i = 0; i < AuthService.MaxFailures; i++)
            Assert.False(auth.Login("A", "admin", "yanlis").Success);

        // Doğru parola bile olsa kilitli
        var locked = auth.Login("A", "admin", "admin123");
        Assert.True(locked.Locked);
        Assert.True(locked.SecondsRemaining > 0);

        // Kilit süresi geçince tekrar denenebilir ve başarı kilidi sıfırlar
        _clock.Advance(AuthService.LockWindow + TimeSpan.FromSeconds(1));
        var ok = auth.Login("A", "admin", "admin123");
        Assert.True(ok.Success);
        for (int i = 0; i < AuthService.MaxFailures - 1; i++)
            auth.Login("A", "admin", "yanlis"); // başarıdan sonra tekrar 4 hata kilitlemez
        Assert.False(auth.Login("A", "admin", "yanlis2").Locked);
    }

    // ---- Deny-by-default ----
    [Fact]
    public void DenyByDefault_YetkiYoksaErisimYok()
    {
        var s = Session("A", "u1");
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.View));
        Assert.False(AccessControl.CanSeeMenu(s, "materials"));
        Assert.False(AccessControl.CanUseButton(s, SpecialButtons.Approve));
        Assert.Throws<ForbiddenException>(() => AccessControl.Require(s, "materials", PermissionAction.Create));
    }

    [Fact]
    public void Dashboard_HerkeseAcik_YalnizOkuma()
    {
        var s = Session("A", "u1");
        Assert.True(AccessControl.CanSeeMenu(s, AppModules.Dashboard));
        Assert.False(AccessControl.Can(s, AppModules.Dashboard, PermissionAction.Create));
    }

    [Fact]
    public void Yetki_Sadece_View_Verilince_MenuGorunur_YazmaReddedilir()
    {
        var s = Session("A", "u1", perms: new[]
        {
            new ModulePermission("materials", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false),
        });
        Assert.True(AccessControl.CanSeeMenu(s, "materials"));
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Create)); // gizli + API reddi
        Assert.Throws<ForbiddenException>(() => AccessControl.Require(s, "materials", PermissionAction.Create));
    }

    [Fact]
    public void Admin_TamYetkili()
    {
        var admin = Session("A", "adm", roles: new[] { RoleKeys.CompanyAdmin });
        Assert.True(AccessControl.Can(admin, "materials", PermissionAction.Delete));
        Assert.True(AccessControl.CanUseButton(admin, SpecialButtons.ResetDatabase));
    }

    // ---- Tenant izolasyonu / firma değiştirme ----
    [Fact]
    public void Payload_FarkliCompany_Reddedilir()
    {
        var s = Session("A", "u1");
        Assert.Throws<ForbiddenException>(() => TenantAccessGuard.ResolveCompanyId(s, "B"));
        Assert.Equal("A", TenantAccessGuard.ResolveCompanyId(s, "A"));
        Assert.Equal("A", TenantAccessGuard.ResolveCompanyId(s, null));
    }

    [Fact]
    public void SuperAdmin_FirmaSecebilir()
    {
        var su = Session("A", "su", roles: new[] { RoleKeys.SuperAdmin });
        Assert.Equal("B", TenantAccessGuard.ResolveCompanyId(su, "B")); // çapraz firma
        Assert.Throws<ForbiddenException>(() => TenantAccessGuard.EnsureOwnership(Session("A", "x"), "B"));
        TenantAccessGuard.EnsureOwnership(su, "B"); // süper admin sahiplik kontrolünden muaf
    }

    // ---- Yetki yükseltme ----
    [Fact]
    public void AdminOlmayan_AdminRolu_Atayamaz()
    {
        var users = new UserService(_factory, _clock);
        // create yetkili ama admin olmayan müdür
        var manager = Session("A", "mgr", roles: new[] { RoleKeys.Staff }, perms: new[]
        {
            new ModulePermission("users", true, true, true, false),
        });
        Assert.Throws<ForbiddenException>(() => users.CreateUser(manager,
            new NewUser("yeni", "p12345", null, new[] { RoleKeys.CompanyAdmin })));
        Assert.Throws<ForbiddenException>(() => users.CreateUser(manager,
            new NewUser("yeni2", "p12345", null, new[] { RoleKeys.SuperAdmin })));
    }

    [Fact]
    public void FirmaAdmini_KullaniciOlusturur_KendiFirmasinda()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);
        var admin = auth.Login("A", "admin", "admin123").Session!;

        // Farklı firma (B) istemek REDDEDİLİR (fail-closed; firma değiştiremez)
        Assert.Throws<ForbiddenException>(() => users.CreateUser(admin,
            new NewUser("depocu", "p12345", "Depo", new[] { RoleKeys.Staff }, CompanyId: "B")));

        // CompanyId verilmeyince kendi firmasında oluşturur
        var newId = users.CreateUser(admin,
            new NewUser("depocu", "p12345", "Depo", new[] { RoleKeys.Staff }));
        Assert.False(string.IsNullOrEmpty(newId));

        var login = new AuthService(_factory, _clock).Login("A", "depocu", "p12345");
        Assert.True(login.Success);
        Assert.Equal("A", login.Session!.CompanyId);
    }

    [Fact]
    public void ListUsers_FirmaKullanicilariniDoner_RolleriIle()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var admin = new AuthService(_factory, _clock).Login("A", "admin", "admin123").Session!;
        users.CreateUser(admin, new NewUser("depocu", "p12345", "Depo Bey", new[] { RoleKeys.Staff }));

        var list = users.ListUsers(admin);
        Assert.Equal(2, list.Count);
        var depocu = list.Single(u => u.Username == "depocu");
        Assert.Equal("Depo Bey", depocu.FullName);
        Assert.True(depocu.IsActive);
        Assert.Contains("Personel", depocu.Roles); // rol adı (Personel — 2-rol modeli)
    }

    [Fact]
    public void Sube_OlusturAta_DetaydaKullaniciListele()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var admin = new AuthService(_factory, _clock).Login("A", "admin", "admin123").Session!;
        var branches = new BranchService(_factory, _clock);

        var bid = branches.Create(admin, new NewBranch("Merkez Şube", "branch"));
        var uid = users.CreateUser(admin, new NewUser("p1", "p12345", "Per Bir", new[] { RoleKeys.Staff }, BranchId: bid));

        // Şube detayında atanmış kullanıcı otomatik listelenir
        var bu = branches.GetUsers(admin, bid);
        Assert.Single(bu);
        Assert.Equal("p1", bu[0].Username);

        // ListUsers şube adını döner
        var row = users.ListUsers(admin).Single(u => u.Username == "p1");
        Assert.Equal("Merkez Şube", row.BranchName);

        // Şubeyi kaldır
        branches.AssignUser(admin, uid, null);
        Assert.Empty(branches.GetUsers(admin, bid));

        // Şube silinince atanmış kullanıcıların şubesi boşalır
        var bid2 = branches.Create(admin, new NewBranch("Geçici", "site"));
        branches.AssignUser(admin, uid, bid2);
        branches.Delete(admin, bid2);
        Assert.Null(users.ListUsers(admin).Single(u => u.Username == "p1").BranchId);
    }

    [Fact]
    public void Yetki_KaydetYukle_ModulVeButon_LoginYansitir()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var admin = new AuthService(_factory, _clock).Login("A", "admin", "admin123").Session!;
        var uid = users.CreateUser(admin, new NewUser("op", "p12345", "Op", new[] { RoleKeys.Staff }));

        var perms = new PermissionService(_factory, _clock);
        perms.SaveForUser(admin, uid,
            new[] { new ModulePermission("materials", true, true, false, false) },
            new[] { SpecialButtons.AddLookup });

        var data = perms.GetForUser(admin, uid);
        Assert.Single(data.Modules);
        Assert.True(data.Modules[0].CanView);
        Assert.Contains(SpecialButtons.AddLookup, data.Buttons);

        // Login oturuma yansır (modül + buton + deny-by-default)
        var op = new AuthService(_factory, _clock).Login("A", "op", "p12345").Session!;
        Assert.True(AccessControl.Can(op, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(op, "materials", PermissionAction.Delete));
        Assert.False(AccessControl.Can(op, "vehicles", PermissionAction.View)); // verilmeyen ekran gizli
        Assert.True(AccessControl.CanUseButton(op, SpecialButtons.AddLookup));
        Assert.False(AccessControl.CanUseButton(op, SpecialButtons.Approve));
    }

    [Fact]
    public void SuperAdmin_Kullanici_DigerRollerGoremez()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var admin = auth.Login("A", "admin", "admin123").Session!;
        var su = auth.Login("A", "root", "root123").Session!;

        // Firma Admini, Süper Admin kullanıcı kaydını GÖREMEZ
        Assert.DoesNotContain(users.ListUsers(admin), u => u.Username == "root");
        Assert.Contains(users.ListUsers(admin), u => u.Username == "admin");
        // Süper Admin tümünü görür
        Assert.Contains(users.ListUsers(su), u => u.Username == "root");
    }

    [Fact]
    public void Kullanici_SifreDegistir_Sil_YalnizAdmin()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);
        var admin = auth.Login("A", "admin", "admin123").Session!;
        var uid = users.CreateUser(admin, new NewUser("u1", "p12345", null, new[] { RoleKeys.Staff }));

        // Şifre değiştir → yeni şifreyle giriş
        users.ChangePassword(admin, uid, "yeni1234");
        Assert.True(auth.Login("A", "u1", "yeni1234").Success);

        // Admin olmayan (manager) sil/şifre yapamaz
        var mgrId = users.CreateUser(admin, new NewUser("mgr", "p12345", null, new[] { RoleKeys.Staff }));
        var mgr = auth.Login("A", "mgr", "p12345").Session!;
        Assert.Throws<ForbiddenException>(() => users.DeleteUser(mgr, uid));
        Assert.Throws<ForbiddenException>(() => users.ChangePassword(mgr, uid, "abcd"));

        // Kendini silemez
        Assert.Throws<InvalidOperationException>(() => users.DeleteUser(admin, admin.UserId));

        // Admin siler → giriş başarısız + listede yok
        users.DeleteUser(admin, uid);
        Assert.False(auth.Login("A", "u1", "yeni1234").Success);
        Assert.DoesNotContain(users.ListUsers(admin), u => u.Username == "u1");
    }

    [Fact]
    public void Admin_BaskaAdmini_Yonetemez_PersoneliYonetir()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        // Süper admin iki admin + bir personel oluşturur
        var admin1Id = users.CreateUser(su, new NewUser("adm1", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var admin2Id = users.CreateUser(su, new NewUser("adm2", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var staffId = users.CreateUser(su, new NewUser("per1", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var admin1 = auth.Login("A", "adm1", "p12345").Session!;

        // #8: admin1 diğer admini düzenleyemez (şifre/sil/rol/yetki)
        Assert.Throws<ForbiddenException>(() => users.ChangePassword(admin1, admin2Id, "abcd"));
        Assert.Throws<ForbiddenException>(() => users.DeleteUser(admin1, admin2Id));
        Assert.Throws<ForbiddenException>(() => users.SetRoles(admin1, admin2Id, new[] { RoleKeys.Staff }));
        Assert.Throws<ForbiddenException>(() => perms.SaveForUser(admin1, admin2Id,
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>()));

        // Ama personeli yönetebilir + kendini
        users.ChangePassword(admin1, staffId, "yeni1234");
        Assert.True(auth.Login("A", "per1", "yeni1234").Success);
        users.ChangePassword(admin1, admin1.UserId, "self1234"); // kendini yönetebilir
    }

    [Fact]
    public void KisitliModul_AltRoleVerilemez_AdminOlunca_Verilir()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        var adminId = users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var staffId = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var admin = auth.Login("A", "adm", "p12345").Session!;

        // Firma admini Personel'e KISITLI modül (branches) veremez → hata
        var ex = Assert.Throws<InvalidOperationException>(() =>
            perms.SaveForUser(admin, staffId, new[] { new ModulePermission("branches", true, false, false, false) }, Array.Empty<string>()));
        Assert.Contains("Admin", ex.Message);

        // Kısıtlı OLMAYAN modül (materials) verilebilir
        perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());

        // Süper admin kuraldan muaf: Personel'e doğrudan kısıtlı verebilir
        var staff2 = users.CreateUser(su, new NewUser("per2", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        perms.SaveForUser(su, staff2, new[] { new ModulePermission("audit", true, false, false, false) }, Array.Empty<string>());

        // Hedef Admin ise kısıtlı modül kaydı reddedilmez (süper admin aktörle; admin zaten hepsine erişir)
        perms.SaveForUser(su, adminId, new[] { new ModulePermission("branches", true, false, false, false) }, Array.Empty<string>());
    }

    [Fact]
    public void SubeKodSifre_YalnizAdmin_GorurVeDegistirir()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var branches = new BranchService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        // Süper admin şube oluşturur (kod + şifre)
        var bid = branches.Create(su, new NewBranch("Merkez", "branch", null, "MRZ", "sifre123"));

        // Personel (branches tam yetkili) — kod/şifre GÖREMEZ
        var perId = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        perms.SaveForUser(su, perId, new[] { new ModulePermission("branches", true, true, true, true) }, Array.Empty<string>());
        var per = auth.Login("A", "per", "p12345").Session!;

        var seenByStaff = branches.List(per).Single(b => b.Id == bid);
        Assert.Null(seenByStaff.Code);
        Assert.False(seenByStaff.HasPassword);

        // Personel güncelleme yapsa da kod/şifre DEĞİŞMEZ (korunur)
        branches.Update(per, bid, new NewBranch("Merkez 2", "branch", null, Code: null, Password: null));
        var seenByAdmin = branches.List(su).Single(b => b.Id == bid);
        Assert.Equal("Merkez 2", seenByAdmin.Name);   // ad güncellendi
        Assert.Equal("MRZ", seenByAdmin.Code);        // kod korundu
        Assert.True(seenByAdmin.HasPassword);          // şifre korundu
        Assert.True(branches.VerifyBranchPassword("A", bid, "sifre123")); // eski şifre hâlâ geçerli
    }

    [Fact]
    public void Firma_YalnizSuperAdmin_AdminErisemez()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var admin = auth.Login("A", "admin", "admin123").Session!;
        var su = auth.Login("A", "root", "root123").Session!;
        var companies = new CompanyService(_factory, _clock);

        // Firma Admini erişemez (admin bypass GEÇERSİZ — atanamaz)
        Assert.False(AccessControl.Can(admin, "companies", PermissionAction.View));
        Assert.Throws<ForbiddenException>(() => companies.List(admin));
        Assert.Throws<ForbiddenException>(() => companies.Create(admin, new NewCompany("X")));

        // Explicit izin verilse bile (manager) erişemez
        var perms = new PermissionService(_factory, _clock);
        var mgrId = users.CreateUser(su, new NewUser("mgr", "p12345", null, new[] { RoleKeys.Staff }));
        perms.SaveForUser(su, mgrId, new[] { new ModulePermission("companies", true, true, true, true) }, Array.Empty<string>());
        var mgr = auth.Login("A", "mgr", "p12345").Session!;
        Assert.False(AccessControl.Can(mgr, "companies", PermissionAction.View));

        // Süper Admin yapar
        Assert.True(AccessControl.Can(su, "companies", PermissionAction.Create));
        var id = companies.Create(su, new NewCompany("Acme A.Ş.", TaxNo: "123", Phone: "555"));
        Assert.Contains(companies.List(su), c => c.Id == id && c.Name == "Acme A.Ş." && c.TaxNo == "123");
    }

    [Fact]
    public void SuperAdmin_SuperAdmin_Olusturabilir()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new AuthService(_factory, _clock).Login("A", "root", "root123").Session!;
        var id = users.CreateUser(su, new NewUser("root2", "root234", null, new[] { RoleKeys.SuperAdmin }));
        Assert.False(string.IsNullOrEmpty(id));
    }

    [Fact]
    public void CreateSessionForUser_GecerliKullanici_OturumKurar_PasifNull()
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);

        var s = auth.CreateSessionForUser("A", id);
        Assert.NotNull(s);
        Assert.Equal("A", s!.CompanyId);
        Assert.True(s.IsCompanyAdmin);

        // Olmayan kullanıcı → null (Beni Hatırla token'ı geçersiz olur)
        Assert.Null(auth.CreateSessionForUser("A", "yok-boyle-id"));
    }

    private SessionContext Session(string company, string user,
        IEnumerable<string>? roles = null, IEnumerable<ModulePermission>? perms = null)
        => new(user, company, roles ?? Array.Empty<string>(), new PermissionSet(perms ?? Array.Empty<ModulePermission>()));

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
