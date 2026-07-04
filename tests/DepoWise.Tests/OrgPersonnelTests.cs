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
