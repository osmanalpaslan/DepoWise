using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class OrgPersonnelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ScopeResolver _scope;
    private readonly TestClock _clock = new();

    public OrgPersonnelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_org_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _scope = new ScopeResolver(_factory);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private SessionContext Admin(string company)
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(company, "admin_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private SessionContext SuperAdmin()
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        return new SessionContext(id, "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    }

    // ---- Firma ----
    [Fact]
    public void Firma_NormalAdmin_BaskaFirmayiGoremez()
    {
        var su = SuperAdmin();
        var svc = new CompanyService(_factory, _clock);
        svc.Create(su, "Firma A2");
        svc.Create(su, "Firma B2");

        var adminA = Admin("A");
        var seen = svc.List(adminA);
        Assert.All(seen, c => Assert.Equal("A", c.Id)); // yalnız kendi firması
        Assert.True(svc.List(su).Count >= 3);           // süper admin hepsini görür
    }

    [Fact]
    public void Firma_Olusturma_YalnizSuperAdmin()
    {
        var svc = new CompanyService(_factory, _clock);
        Assert.Throws<ForbiddenException>(() => svc.Create(Admin("A"), "Yeni"));
        Assert.False(string.IsNullOrEmpty(svc.Create(SuperAdmin(), "Yeni")));
    }

    [Fact]
    public void Firma_Silme_Kullanicilari_Silmez_PasifeAlir()
    {
        var users = new UserService(_factory, _clock);
        var su = SuperAdmin(); // firma A + süper admin
        var uid = users.EnsureInitialAdmin("DELCO", "delu", "p12345", RoleKeys.CompanyAdmin); // firma DELCO + kullanıcı
        var svc = new DepoWise.Infrastructure.Organization.CompanyService(_factory, _clock); // API'nin kullandığı servis

        svc.Delete(su, "DELCO"); // artık HATA VERMEZ; kullanıcıları pasife alır, silmez

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_active, is_deleted FROM users WHERE id=$u;";
        cmd.Parameters.AddWithValue("$u", uid);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(0L, r.GetInt64(0)); // is_active=0 → PASİF
        Assert.Equal(0L, r.GetInt64(1)); // is_deleted=0 → SİLİNMEDİ (korundu)
    }

    [Fact]
    public void Firma_Silme_SuperAdmini_PasifeAlmaz() // regresyon: süper admin kendi firmasını silince kilitlenmemeli
    {
        var su = SuperAdmin(); // firma A + süper admin (su.UserId)
        var svc = new DepoWise.Infrastructure.Organization.CompanyService(_factory, _clock);

        svc.Delete(su, "A"); // süper admin KENDİ home firmasını siler

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_active, is_deleted FROM users WHERE id=$u;";
        cmd.Parameters.AddWithValue("$u", su.UserId);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(1L, r.GetInt64(0)); // is_active=1 → süper admin AKTİF kalmalı (kilitlenmez)
        Assert.Equal(0L, r.GetInt64(1)); // is_deleted=0

        // Ve tekrar giriş yapabilmeli (kritik senaryo: çıkış → yeniden login)
        var auth = new AuthService(_factory, _clock);
        var login = auth.Login("A", "root", "root123");
        Assert.True(login.Success);
    }

    [Fact]
    public void Firma_Kuyruk_TekrarGonderiminde_HataVermez_IDEMPOTENT()
    {
        // ÇEVRİMDIŞI KUYRUK: masaüstü offline firma oluşturur, internet gelince kuyruk sunucuya işlenir.
        // Ağ kopması/yeniden deneme yüzünden AYNI işlem birden çok kez gelebilir → HATA VERMEMELİ.
        var su = SuperAdmin();
        var svc = new DepoWise.Infrastructure.Organization.CompanyService(_factory, _clock);
        var dto = new DepoWise.Infrastructure.Organization.NewCompany("Offline Firma", MaxUsers: 5);
        const string clientId = "offline-company-id-1";   // masaüstünün çevrimdışı ürettiği id

        // 1) Kuyruk ilk kez işlenir — istemcinin id'si ile oluşturulur (yerel ↔ sunucu id'leri eşleşsin)
        var id1 = svc.Create(su, dto, clientId);
        Assert.Equal(clientId, id1);

        // 2) AYNI işlem tekrar gönderilir (retry) → hata YOK, mükerrer kayıt YOK
        var id2 = svc.Create(su, dto with { Name = "Offline Firma (guncel)" }, clientId);
        Assert.Equal(clientId, id2);
        Assert.Single(svc.List(su), c => c.Id == clientId);
        Assert.Equal("Offline Firma (guncel)", svc.List(su).Single(c => c.Id == clientId).Name);

        // 3) Silme iki kez gelirse de hata vermez (kuyruk tekrarı)
        svc.Delete(su, clientId);
        svc.Delete(su, clientId);                                   // idempotent — fırlatmamalı
        Assert.DoesNotContain(svc.List(su), c => c.Id == clientId);

        // 4) Aktifleştirme iki kez gelirse de hata vermez
        svc.Reactivate(su, clientId);
        svc.Reactivate(su, clientId);                               // idempotent
        Assert.Contains(svc.List(su), c => c.Id == clientId);

        // 5) Gerçekten olmayan firma → yine de hata (fail-closed korunur)
        Assert.Throws<ForbiddenException>(() => svc.Delete(su, "hic-olmayan-id"));
    }

    [Fact]
    public void SuperAdmin_CalistigiFirmayiSilince_Oturum_Dusmez_401_Vermez()
    {
        // Senaryo (kullanıcının yaşadığı hata): süper admin bir firmayı SEÇİP onun bağlamında çalışıyor,
        // sonra o firmayı siliyor. Eskiden token'daki firma geçersiz olduğu için sonraki her istek 401 dönüyor,
        // firma listesi hiç yüklenemiyordu. Artık home firmaya düşer, oturum yaşar.
        var su = SuperAdmin();                                  // home firma = "A"
        var svc = new DepoWise.Infrastructure.Organization.CompanyService(_factory, _clock);
        var target = svc.Create(su, new DepoWise.Infrastructure.Organization.NewCompany("Silinecek Firma"));

        var auth = new AuthService(_factory, _clock);
        var crossSession = auth.CreateSessionForUser(target, su.UserId);   // seçilen firmada çalışıyor
        Assert.NotNull(crossSession);
        Assert.Equal(target, crossSession!.CompanyId);

        svc.Delete(su, target);                                  // içinde çalıştığı firmayı siler

        // Sonraki istek: oturum DÜŞMEMELİ (eskiden null → 401)
        var after = auth.CreateSessionForUser(target, su.UserId);
        Assert.NotNull(after);
        Assert.Equal("A", after!.CompanyId);                     // home firmaya düştü
        Assert.True(after.IsSuperAdmin);

        // Ve firma listesi çalışmalı; silinen firma listede OLMAMALI
        var list = svc.List(after);
        Assert.DoesNotContain(list, c => c.Id == target);
    }

    [Fact]
    public void Sube_Silinince_HicbirListede_Gorunmez() // regresyon: silinen şube tüm şube alanlarından düşmeli
    {
        var su = SuperAdmin();
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_factory, _clock);
        var keep = branches.Create(su, new DepoWise.Infrastructure.Organization.NewBranch("Kalan", "branch", null, null, null));
        var gone = branches.Create(su, new DepoWise.Infrastructure.Organization.NewBranch("Silinen", "branch", null, null, null));

        branches.Delete(su, gone);

        var list = branches.List(su);
        Assert.Contains(list, b => b.Id == keep);
        Assert.DoesNotContain(list, b => b.Id == gone);   // silinen şube listelenmez

        // Şube seçicilerinin beslendiği kapsam çözümleyicisi de silineni vermemeli
        var allowed = _scope.AllowedBranchIds(su);
        Assert.DoesNotContain(gone, allowed);
    }

    // ---- Fikir B: saha personeli kutucuğu + unvan sabit tanım ----
    [Fact]
    public void SahaPersoneli_Kutucugu_Kaydedilir_VeOkunur()
    {
        var su = SuperAdmin();
        var pers = new PersonnelService(_factory, _scope, _clock);

        var sahaId = pers.Create(su, new NewPersonnel("Saha Adam", "İşçi", "0555", null, true, IsFieldStaff: true));
        var normalId = pers.Create(su, new NewPersonnel("Ofis Adam", "Memur", "0666", null, true)); // varsayılan false

        Assert.True(pers.Get(su, sahaId)!.IsFieldStaff);
        Assert.False(pers.Get(su, normalId)!.IsFieldStaff);

        // Düzenlemede de korunur / değiştirilebilir
        pers.Update(su, normalId, new NewPersonnel("Ofis Adam", "Memur", "0666", null, true, IsFieldStaff: true));
        Assert.True(pers.Get(su, normalId)!.IsFieldStaff);
    }

    [Fact]
    public void Unvan_Tanimi_Eklenir_Listelenir_MukerrerOlmaz()
    {
        var su = SuperAdmin();
        var titles = new PersonnelTitleService(_factory, _clock);

        var t1 = titles.Create(su, "Şoför");
        var t2 = titles.Create(su, "  şoför  ");   // kırpılır + büyük/küçük harf duyarsız → AYNI kayıt döner
        Assert.Equal(t1.Id, t2.Id);

        titles.Create(su, "Operatör");
        var list = titles.List(su);
        Assert.Equal(2, list.Count);                       // mükerrer eklenmedi
        Assert.Contains(list, t => t.Name == "Şoför");
        Assert.Contains(list, t => t.Name == "Operatör");

        Assert.Throws<InvalidOperationException>(() => titles.Create(su, "   ")); // boş unvan
    }

    [Fact]
    public void Kullanici_PersoneleBaglanir_ListedeGorunur() // Fikir B: Kullanıcılar ekranındaki "Personel seç"
    {
        var su = SuperAdmin();
        var pers = new PersonnelService(_factory, _scope, _clock);
        var users = new UserService(_factory, _clock);

        var pid = pers.Create(su, new NewPersonnel("Bagli Kisi", "Şoför", "0555", null));
        users.CreateUser(su, new NewUser("bagli", "p12345", "Bagli Kisi",
            new[] { RoleKeys.Staff }, "A", PersonnelId: pid));

        var row = users.ListUsers(su).Single(u => u.Username == "bagli");
        Assert.Equal(pid, row.PersonnelId);
        Assert.Equal("Bagli Kisi", row.PersonnelName);   // liste bağlı personeli gösterir

        // Bir personele İKİNCİ hesap bağlanamaz (tek kullanıcı kuralı korunur)
        Assert.ThrowsAny<Exception>(() => users.CreateUser(su, new NewUser("ikinci", "p12345", "X",
            new[] { RoleKeys.Staff }, "A", PersonnelId: pid)));
    }

    [Fact]
    public void Unvan_Tanimlari_FirmayaIzole()
    {
        var su = SuperAdmin();                       // firma A
        var titles = new PersonnelTitleService(_factory, _clock);
        titles.Create(su, "Şoför");                  // A firmasına unvan

        var adminB = Admin("B");                     // farklı firma
        Assert.Empty(titles.List(adminB));           // B, A'nın unvanını GÖRMEZ (tenant izolasyonu)
    }

    [Fact]
    public void Calisan_MukerrerPersonel_VeTekKullanici()
    {
        var su = SuperAdmin(); // firma A + süper admin
        var pers = new PersonnelService(_factory, _scope, _clock);
        var users = new UserService(_factory, _clock);

        var p1 = pers.Create(su, new NewPersonnel("Ahmet Yılmaz", "Şoför", "0555 111 22 33", null, true));

        // Mükerrer: ad eşleşmesi (aynı yazım)
        Assert.Contains(pers.FindDuplicates(su, "Ahmet Yılmaz", null, null), d => d.Id == p1);
        // Mükerrer: telefon eşleşmesi (farklı ad, farklı biçim)
        Assert.Contains(pers.FindDuplicates(su, "Farklı Kişi", "0555-111-2233", null), d => d.Id == p1);
        // Kendini hariç tut → boş
        Assert.Empty(pers.FindDuplicates(su, "Ahmet Yılmaz", null, p1));

        // Bir personele tek kullanıcı
        var u1 = users.CreateUser(su, new NewUser("ahmet", "p12345", "Ahmet", new[] { RoleKeys.Staff }, CompanyId: su.CompanyId, PersonnelId: p1));
        var u2 = users.CreateUser(su, new NewUser("mehmet", "p12345", "Mehmet", new[] { RoleKeys.Staff }, CompanyId: su.CompanyId));
        Assert.Throws<InvalidOperationException>(() => users.LinkPersonnel(su, u2, p1)); // p1 zaten bağlı
        // u1 çözülünce u2 bağlanabilir
        users.LinkPersonnel(su, u1, null);
        users.LinkPersonnel(su, u2, p1);
        Assert.Single(users.AccountsByPersonnel(su.CompanyId).Where(kv => kv.Key == p1));
    }

    // ---- Personele MEVCUT kullanıcı bağlama: bağlanabilir liste (yeni akış — hesap açma değil) ----
    [Fact]
    public void BaglanabilirKullanicilar_YalnizBagsiz_SuperAdminHaric()
    {
        var su = SuperAdmin(); // firma A + süper admin (root)
        var pers = new PersonnelService(_factory, _scope, _clock);
        var users = new UserService(_factory, _clock);

        var p1 = pers.Create(su, new NewPersonnel("Bağlı Kişi", "Şoför", "0555", null));
        var uLinked = users.CreateUser(su, new NewUser("bagli", "p12345", "Bağlı",
            new[] { RoleKeys.Staff }, CompanyId: su.CompanyId, PersonnelId: p1));
        var uFree = users.CreateUser(su, new NewUser("serbest", "p12345", "Serbest",
            new[] { RoleKeys.Staff }, CompanyId: su.CompanyId));

        var linkable = users.ListLinkableUsers(su);
        Assert.Contains(linkable, u => u.Id == uFree);              // bağsız → listede
        Assert.DoesNotContain(linkable, u => u.Id == uLinked);      // bağlı → listede değil
        Assert.DoesNotContain(linkable, u => u.Username == "root"); // süper admin → listede değil

        // Serbest kullanıcıyı bir personele bağla → artık bağlanabilir değil.
        var p2 = pers.Create(su, new NewPersonnel("İkinci Kişi", "Memur", "0666", null));
        users.LinkPersonnel(su, uFree, p2);
        Assert.DoesNotContain(users.ListLinkableUsers(su), u => u.Id == uFree);
    }

    [Fact]
    public void BaglanabilirKullanicilar_YalnizAdmin()
    {
        var su = SuperAdmin();
        var users = new UserService(_factory, _clock);
        var staff = new SessionContext("staff-x", su.CompanyId, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => users.ListLinkableUsers(staff));
    }

    [Fact]
    public void Firma_YenidenAktiflestirme_KullanicilariGeriAktifEder()
    {
        var users = new UserService(_factory, _clock);
        var su = SuperAdmin();
        var uid = users.EnsureInitialAdmin("REACT", "reactu", "p12345", RoleKeys.CompanyAdmin);
        var svc = new DepoWise.Infrastructure.Organization.CompanyService(_factory, _clock);

        svc.Delete(su, "REACT");                 // firma pasif + kullanıcı pasif
        var n = svc.Reactivate(su, "REACT");     // sözleşme yenileme

        Assert.Equal(1, n);                      // 1 kullanıcı geri aktifleşti
        using var conn = _factory.Create();
        using (var cc = conn.CreateCommand())
        {
            cc.CommandText = "SELECT is_deleted FROM companies WHERE id='REACT';";
            Assert.Equal(0L, Convert.ToInt64(cc.ExecuteScalar())); // firma geri geldi
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_active FROM users WHERE id=$u;";
        cmd.Parameters.AddWithValue("$u", uid);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar())); // kullanıcı tekrar aktif
        // Admin olmayan reactivate yasak
        Assert.Throws<ForbiddenException>(() => svc.Reactivate(Admin("A"), "REACT"));
    }

    [Fact]
    public void Firma_BaskaFirmaErisimi_Reddedilir()
    {
        var svc = new CompanyService(_factory, _clock);
        var adminA = Admin("A");
        Assert.Throws<ForbiddenException>(() => svc.EnsureAccess(adminA, "B"));
        svc.EnsureAccess(adminA, "A"); // kendi firması ok
    }

    // ---- Şube kapsamı ----
    [Fact]
    public void Sube_KapsamliKullanici_KapsamDisinaTasamaz()
    {
        var admin = Admin("A");
        var branches = new BranchService(_factory, _scope, _clock);
        var b1 = branches.Create(admin, "Şube-1");
        var b2 = branches.Create(admin, "Şube-2");
        branches.Create(admin, "Şube-3");

        // Admin tüm şubeleri görür
        Assert.Equal(3, branches.ListInScope(admin).Count);

        // Kapsamlı kullanıcı: yalnız b1
        var scoped = CreateScopedUser(admin, branchScopes: new[] { b1 },
            perms: new[] { new ModulePermission("branches", true, false, false, false) });

        var visible = branches.ListInScope(scoped);
        Assert.Single(visible);
        Assert.Equal(b1, visible[0].Id);
        Assert.DoesNotContain(visible, x => x.Id == b2);
    }

    // ---- Personel ----
    [Fact]
    public void Personel_CRUD_TenantIzolasyonu()
    {
        var adminA = Admin("A");
        var adminB = Admin("B");
        var pers = new PersonnelService(_factory, _scope, _clock);

        var id = pers.Create(adminA, new NewPersonnel("Ali Veli", "Operatör", "555", null));
        Assert.Empty(pers.List(adminB, new PageRequest { Limit = 50 }).Items);   // B göremez
        Assert.Contains(pers.List(adminA, new PageRequest { Limit = 50 }).Items, p => p.Id == id);
    }

    [Fact]
    public void Personel_SoftDelete_Restore()
    {
        var admin = Admin("A");
        var pers = new PersonnelService(_factory, _scope, _clock);
        var id = pers.Create(admin, new NewPersonnel("Silinecek", null, null, null));

        pers.SoftDelete(admin, id);
        Assert.DoesNotContain(pers.List(admin, new PageRequest { Limit = 50 }).Items, p => p.Id == id);
        pers.Restore(admin, id);
        Assert.Contains(pers.List(admin, new PageRequest { Limit = 50 }).Items, p => p.Id == id);
    }

    [Fact]
    public void Personel_KapsamDisiSube_Reddedilir()
    {
        var admin = Admin("A");
        var branches = new BranchService(_factory, _scope, _clock);
        var b1 = branches.Create(admin, "Şube-1");
        var b2 = branches.Create(admin, "Şube-2");

        var scoped = CreateScopedUser(admin, branchScopes: new[] { b1 },
            perms: new[] { new ModulePermission("personnel", true, true, true, true) });
        var pers = new PersonnelService(_factory, _scope, _clock);

        // Kapsamındaki şube ok
        Assert.False(string.IsNullOrEmpty(pers.Create(scoped, new NewPersonnel("Kapsamlı", null, null, b1))));
        // Kapsam dışı şube reddedilir
        Assert.Throws<ForbiddenException>(() => pers.Create(scoped, new NewPersonnel("Dışı", null, null, b2)));
    }

    [Fact]
    public void Personel_Liste_KapsamDisiPersoneliGostermez()
    {
        var admin = Admin("A");
        var branches = new BranchService(_factory, _scope, _clock);
        var b1 = branches.Create(admin, "Şube-1");
        var b2 = branches.Create(admin, "Şube-2");
        var pers = new PersonnelService(_factory, _scope, _clock);
        _clock.Advance(1000); var p1 = pers.Create(admin, new NewPersonnel("P1", null, null, b1));
        _clock.Advance(1000); pers.Create(admin, new NewPersonnel("P2", null, null, b2));

        var scoped = CreateScopedUser(admin, branchScopes: new[] { b1 },
            perms: new[] { new ModulePermission("personnel", true, false, false, false) });

        var list = pers.List(scoped, new PageRequest { Limit = 50 });
        Assert.Single(list.Items);
        Assert.Equal(p1, list.Items[0].Id);
    }

    [Fact]
    public void Personel_DenyByDefault_YetkisizReddedilir()
    {
        var admin = Admin("A");
        var noPerm = CreateScopedUser(admin, branchScopes: Array.Empty<string>(),
            perms: Array.Empty<ModulePermission>());
        var pers = new PersonnelService(_factory, _scope, _clock);
        Assert.Throws<ForbiddenException>(() => pers.List(noPerm, new PageRequest { Limit = 50 }));
        Assert.Throws<ForbiddenException>(() => pers.Create(noPerm, new NewPersonnel("X", null, null, null)));
    }

    /// <summary>Admin altında gerçek bir kapsamlı (admin olmayan) kullanıcı + şube kapsamı oluşturur.</summary>
    private SessionContext CreateScopedUser(SessionContext admin, string[] branchScopes, ModulePermission[] perms)
    {
        var users = new UserService(_factory, _clock);
        var uid = users.CreateUser(admin, new NewUser(
            Username: "scoped_" + Guid.NewGuid().ToString("N")[..6],
            Password: "p12345",
            FullName: "Kapsamlı",
            RoleKeys: new[] { RoleKeys.Staff },
            Permissions: perms));

        var branches = new BranchService(_factory, _scope, _clock);
        foreach (var b in branchScopes)
            branches.AssignScope(admin, uid, b);

        return new SessionContext(uid, admin.CompanyId, new[] { RoleKeys.Staff }, new PermissionSet(perms));
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
