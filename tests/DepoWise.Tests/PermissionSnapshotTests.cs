using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// F0 (YET-01, 2026-08-10) — PERMISSION SNAPSHOT + CACHE.
///
/// F0'ın amacı YENİ yetki davranışı üretmek DEĞİL; mevcut davranışı kalıcı snapshot/önbellek mimarisine
/// taşımaktır. Bu yüzden buradaki EN ÖNEMLİ test <see cref="Snapshotsiz_ve_Snapshotli_sonuclar_BIREBIR_AYNI"/>:
/// önbellekli ve önbelleksiz hesap AYNI sonucu vermelidir. Fark çıkarsa bu "yeni davranış" değil REGRESYONDUR.
///
/// Ayrıca kritik güvenlik özelliği: <b>yetki KAYBI gecikmemelidir</b> → yazan her nokta önbelleği düşürür.
/// </summary>
public class PermissionSnapshotTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    public PermissionSnapshotTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_snap_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
    }

    private SessionContext Admin(string co)
    {
        var u = new UserService(_f, _clock);
        var id = u.EnsureInitialAdmin(co, "adm_" + co, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    /// <summary>Süper admin oturumu — Rol Yetki Kontrol (SetMatrix) yalnız süper admine açıktır.</summary>
    private SessionContext SuperAdmin(string co)
    {
        var u = new UserService(_f, _clock);
        var id = u.EnsureInitialAdmin(co, "sa_" + co, "admin123", RoleKeys.SuperAdmin);
        return new SessionContext(id, co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    }

    /// <summary>Personel (bypass'sız) kullanıcı — gerçek izin hesabını görebilmek için.</summary>
    private string CreateStaff(SessionContext actor, string username, PermissionSnapshotCache? cache = null)
    {
        var users = new UserService(_f, _clock, cache);
        return users.CreateUser(actor, new NewUser(username, "Test!2026", username, new[] { RoleKeys.Staff }, actor.CompanyId));
    }

    private static void Grant(PermissionService perms, SessionContext actor, string userId,
        bool view, bool create, bool edit, bool delete)
        => perms.SaveForUser(actor, userId,
            new[] { new ModulePermission("materials", view, create, edit, delete) },
            Array.Empty<string>());

    // ── Test 1 — Snapshot'sız hesap == Snapshot'lı hesap ────────────────────────────────────

    [Fact]
    public void Snapshotsiz_ve_Snapshotli_sonuclar_BIREBIR_AYNI()
    {
        var a = Admin("F0A");
        var cache = new PermissionSnapshotCache();
        var uid = CreateStaff(a, "kullanici1");
        Grant(new PermissionService(_f, _clock), a, uid, view: true, create: false, edit: true, delete: false);

        var authNoCache = new AuthService(_f, _clock);                 // önbellek YOK → F0 öncesi davranış
        var authCached = new AuthService(_f, _clock, cache);           // önbellek VAR

        var s1 = authNoCache.CreateSessionForUser("F0A", uid)!;
        var s2 = authCached.CreateSessionForUser("F0A", uid)!;
        var s3 = authCached.CreateSessionForUser("F0A", uid)!;         // ikinci çağrı: önbellekten

        foreach (var action in new[] { PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete })
        {
            var expected = AccessControl.Can(s1, "materials", action);
            Assert.Equal(expected, AccessControl.Can(s2, "materials", action));
            Assert.Equal(expected, AccessControl.Can(s3, "materials", action));
        }
        // Roller, tüm-şube bayrağı ve blocked kümesi de aynı taşınmalı
        Assert.Equal(s1.RoleKeys, s2.RoleKeys);
        Assert.Equal(s1.CanViewAllBranches, s2.CanViewAllBranches);
        Assert.Equal(s1.BlockedModules, s2.BlockedModules);
    }

    [Fact]
    public void Snapshot_ISTEGE_OZEL_durumu_PAYLASMAZ()
    {
        // OperatingBranchId isteğe özeldir; iki oturum aynı fotoğraftan kurulsa da birbirini ETKİLEMEMELİ.
        var a = Admin("F0OB");
        var cache = new PermissionSnapshotCache();
        var uid = CreateStaff(a, "kullanici_ob");
        var auth = new AuthService(_f, _clock, cache);

        var s1 = auth.CreateSessionForUser("F0OB", uid)!;
        s1.OperatingBranchId = "sube-1";
        var s2 = auth.CreateSessionForUser("F0OB", uid)!;

        Assert.Null(s2.OperatingBranchId);       // ikinci oturum temiz gelmeli
        Assert.NotSame(s1, s2);
    }

    // ── Test 2 — Yetki kaydedilince ANINDA yeni sonuç ───────────────────────────────────────

    [Fact]
    public void Yetki_kaydedilince_onbellek_ANINDA_dusurulur()
    {
        var a = Admin("F0B");
        var cache = new PermissionSnapshotCache();
        var uid = CreateStaff(a, "kullanici2");
        var perms = new PermissionService(_f, _clock, cache);
        var auth = new AuthService(_f, _clock, cache);

        Grant(perms, a, uid, view: true, create: false, edit: true, delete: false);
        var before = auth.CreateSessionForUser("F0B", uid)!;
        Assert.True(AccessControl.Can(before, "materials", PermissionAction.Edit));   // önbelleğe girdi

        // Düzenleme yetkisi GERİ ALINIYOR → yetki KAYBI gecikmemeli (TTL beklenmemeli)
        Grant(perms, a, uid, view: true, create: false, edit: false, delete: false);

        var after = auth.CreateSessionForUser("F0B", uid)!;
        Assert.False(AccessControl.Can(after, "materials", PermissionAction.Edit));
        Assert.True(AccessControl.Can(after, "materials", PermissionAction.View));
    }

    // ── Test 3 — Rol ataması değişince ANINDA yeni sonuç ────────────────────────────────────

    [Fact]
    public void Rol_atamasi_degisince_onbellek_dusurulur()
    {
        var a = Admin("F0C");
        var cache = new PermissionSnapshotCache();
        var users = new UserService(_f, _clock, cache);
        var uid = CreateStaff(a, "kullanici3", cache);
        var auth = new AuthService(_f, _clock, cache);

        var before = auth.CreateSessionForUser("F0C", uid)!;
        Assert.False(AccessControl.Can(before, "materials", PermissionAction.Delete));   // Personel: izin yok

        users.SetRoles(a, uid, new[] { RoleKeys.CompanyAdmin });   // admin oldu → bypass

        var after = auth.CreateSessionForUser("F0C", uid)!;
        Assert.True(AccessControl.Can(after, "materials", PermissionAction.Delete));
        Assert.Contains(RoleKeys.CompanyAdmin, after.RoleKeys);
    }

    [Fact]
    public void Kullanici_pasife_alininca_oturum_ANINDA_gecersiz()
    {
        var a = Admin("F0D");
        var cache = new PermissionSnapshotCache();
        var users = new UserService(_f, _clock, cache);
        var uid = CreateStaff(a, "kullanici4", cache);
        var auth = new AuthService(_f, _clock, cache);

        Assert.NotNull(auth.CreateSessionForUser("F0D", uid));   // önbelleğe girdi
        users.SetActive(a, uid, false);
        Assert.Null(auth.CreateSessionForUser("F0D", uid));      // pasif kullanıcı oturum açamaz — gecikme YOK
    }

    // ── Test 4 — Rol KISITI değişince o role sahip HERKES etkilenir ─────────────────────────

    [Fact]
    public void Rol_kisiti_degisince_TUM_kullanicilarin_fotografi_dusurulur()
    {
        var a = Admin("F0E");
        var cache = new PermissionSnapshotCache();
        var uid1 = CreateStaff(a, "kullanici5a");
        var uid2 = CreateStaff(a, "kullanici5b");
        var perms = new PermissionService(_f, _clock, cache);
        var auth = new AuthService(_f, _clock, cache);
        var roleGrants = new RoleGrantService(_f, _clock, cache);

        Grant(perms, a, uid1, view: true, create: false, edit: false, delete: false);
        Grant(perms, a, uid2, view: true, create: false, edit: false, delete: false);

        Assert.True(AccessControl.Can(auth.CreateSessionForUser("F0E", uid1)!, "materials", PermissionAction.View));
        Assert.True(AccessControl.Can(auth.CreateSessionForUser("F0E", uid2)!, "materials", PermissionAction.View));
        Assert.True(cache.Count >= 2);   // ikisi de önbellekte

        // Süper admin "materials" modülünü Personel ROLÜNE kapatıyor → iki kullanıcı da etkilenmeli
        // (Rol Yetki Kontrol yalnız süper admine açıktır — mevcut kural korunuyor.)
        roleGrants.SetMatrix(SuperAdmin("F0E"), new Dictionary<string, IReadOnlyList<string>>
        {
            [RoleKeys.Staff] = new[] { "materials" },
        });

        Assert.Equal(0, cache.Count);   // tüm fotoğraflar düşürüldü
        Assert.False(AccessControl.Can(auth.CreateSessionForUser("F0E", uid1)!, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(auth.CreateSessionForUser("F0E", uid2)!, "materials", PermissionAction.View));
    }

    // ── Test 5 — Tenant izolasyonu ──────────────────────────────────────────────────────────

    [Fact]
    public void Onbellek_anahtari_FIRMA_bazlidir_tenant_sizintisi_olmaz()
    {
        var a = Admin("F0F1");
        var b = Admin("F0F2");
        var cache = new PermissionSnapshotCache();
        var perms = new PermissionService(_f, _clock, cache);
        var auth = new AuthService(_f, _clock, cache);

        var u1 = CreateStaff(a, "firma1_kullanici");
        var u2 = CreateStaff(b, "firma2_kullanici");
        Grant(perms, a, u1, view: true, create: true, edit: true, delete: true);
        Grant(perms, b, u2, view: true, create: false, edit: false, delete: false);

        var s1 = auth.CreateSessionForUser("F0F1", u1)!;
        var s2 = auth.CreateSessionForUser("F0F2", u2)!;

        Assert.Equal("F0F1", s1.CompanyId);
        Assert.Equal("F0F2", s2.CompanyId);
        Assert.True(AccessControl.Can(s1, "materials", PermissionAction.Delete));    // firma 1 tam yetkili
        Assert.False(AccessControl.Can(s2, "materials", PermissionAction.Delete));   // firma 2 yalnız görüntüleme
        // Çapraz firma oturumu (süper admin olmayan) reddedilir ve önbelleğe ALINMAZ
        Assert.Null(auth.CreateSessionForUser("F0F2", u1));
    }

    // ── Test 6 — G2-01 davranışları korunuyor ───────────────────────────────────────────────

    [Fact]
    public void G2_01_davranislari_snapshot_altinda_KORUNUR()
    {
        var a = Admin("F0G");
        var cache = new PermissionSnapshotCache();
        var perms = new PermissionService(_f, _clock, cache);
        var auth = new AuthService(_f, _clock, cache);

        // (1) Create YOK + Edit VAR → düzenleyebilmeli
        var editOnly = CreateStaff(a, "sadece_duzenleyen");
        Grant(perms, a, editOnly, view: true, create: false, edit: true, delete: false);
        var sEdit = auth.CreateSessionForUser("F0G", editOnly)!;
        Assert.True(AccessControl.Can(sEdit, "materials", PermissionAction.Edit));
        Assert.False(AccessControl.Can(sEdit, "materials", PermissionAction.Create));
        Assert.False(AccessControl.Can(sEdit, "materials", PermissionAction.Delete));   // Sil görünmemeli

        // (2) View-only → düzenleyememeli
        var viewOnly = CreateStaff(a, "sadece_goruntuleyen");
        Grant(perms, a, viewOnly, view: true, create: false, edit: false, delete: false);
        var sView = auth.CreateSessionForUser("F0G", viewOnly)!;
        Assert.True(AccessControl.Can(sView, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(sView, "materials", PermissionAction.Edit));

        // (3) Servis katmanı GERÇEK kapı: yetkisiz çağrı istisna atar (UI görünürlüğü değil)
        var materials = new DepoWise.Infrastructure.Materials.MaterialService(_f, _clock);
        Assert.Throws<ForbiddenException>(() =>
            materials.Create(sView, new DepoWise.Infrastructure.Materials.NewMaterial("X-1", "Deneme")));
    }

    // ── Önbellek mekaniği ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TTL_dolunca_fotograf_yeniden_yuklenir()
    {
        var a = Admin("F0H");
        var cache = new PermissionSnapshotCache(ttlSeconds: 1);
        var uid = CreateStaff(a, "kullanici_ttl");
        var auth = new AuthService(_f, _clock, cache);

        Assert.NotNull(auth.CreateSessionForUser("F0H", uid));
        Assert.Equal(1, cache.Count);

        Thread.Sleep(1100);   // TTL doldu

        // Süresi dolmuş girdi kullanılmaz; yeniden yüklenir (sonuç yine doğru)
        var again = auth.CreateSessionForUser("F0H", uid);
        Assert.NotNull(again);
        Assert.Equal(uid, again!.UserId);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_db); } catch { } }
}
