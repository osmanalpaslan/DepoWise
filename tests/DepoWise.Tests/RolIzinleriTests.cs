using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3a (ADR-222, kullanıcı onayı 2026-09-05) — ROL BAZLI YETKİ ═══
///
/// <b>K1 — yalnız ALLOW (birleşim).</b> Rol izni kullanıcı iznine <b>EKLER</b>; hiçbir şeyi
/// kaldırmaz, hiçbir precedence basamağını aşmaz.
///
/// <b>En önemli kabul kriteri (kullanıcının şartı):</b>
/// <i>"Role permission tablosu boş olduğunda sistemin davranışı bugünkü sistemle birebir aynı olmalı."</i>
/// RL1 bunu doğrudan ölçer.
///
///  RL1  — Tablolar BOŞKEN davranış bugünküyle BİREBİR aynı
///  RL2  — Rol izni kullanıcıya EKLENİR (union)
///  RL3  — İki rol birleşir; aynı izin iki rolden gelirse tek sonuç
///  RL4  — Rol kaldırılınca o rolden gelen izin DÜŞER, kullanıcı izni KALIR
///  RL5  — Rol ALLOW'ı ROL KİLİDİNİ aşamaz (precedence 1)
///  RL6  — Rol ALLOW'ı YAPISAL SINIFI aşamaz (precedence 2)
///  RL7  — Rol ALLOW'ı TENANT sınırını aşamaz
///  RL8  — Rol izni ŞUBE KAPSAMINI genişletmez
///  RL9  — DEVRETME TAVANI: aktör kendinde olmayanı role VEREMEZ (backend)
///  RL10 — Rol butonları da birleşir
///  RL11 — Rapor (rpt_) ve kayıt tipi (datype_) anahtarları rol seviyesinde de çalışır
///  RL12 — Rol izni değişince önbellek düşer (bayat yetki yok)
///  RL13 — Başka firmanın rolüne izin yazılamaz
/// </summary>
public class RolIzinleriTests : IDisposable
{
    private const string Co = "ROL";
    private const string CoB = "ROL-B";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AuthService _auth;
    private readonly PermissionService _perms;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly string _adminId, _personelId;
    private const string Pass = "Rol!2026";

    public RolIzinleriTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rol_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        foreach (var c in new[] { Co, CoB })
            Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{c}','{c}',1,1,1,0);");

        var users = new UserService(_f);
        _adminId = users.EnsureInitialAdmin(Co, "rol_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "rol_personel", Pass, RoleKeys.Staff);

        _auth = new AuthService(_f, null, _cache);
        _perms = new PermissionService(_f, null, _cache);
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private string RolId(string roleKey)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM roles WHERE role_key=@k AND is_deleted=0 LIMIT 1;";
        cmd.AddWithValue("@k", roleKey);
        return (string)cmd.ExecuteScalar()!;
    }

    /// <summary>Oturumu GERÇEK yoldan kurar (AuthService) — böylece birleştirme kodu da sınanır.</summary>
    private SessionContext Oturum(string kullaniciAdi)
    {
        var r = _auth.Login(Co, kullaniciAdi, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + kullaniciAdi);
        return r.Session!;
    }

    private SessionContext SuperAdminOturumu()
        => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    private static ModulePermission Tam(string modul) => new(modul, true, true, true, true);

    // ══════════════════════ RL1 — EN ÖNEMLİ: BOŞ TABLO = BUGÜNKÜ DAVRANIŞ ══════════════════════

    /// <summary>
    /// ⭐ Geriye dönük uyumluluğun tek cümlelik kanıtı.
    ///
    /// Yeni tablolar oluştu ama BOŞ. Bu durumda etkin izin kümesi, kullanıcının kendi satırlarının
    /// BİREBİR aynısı olmalı — ne fazla ne eksik. Yayın günü hiçbir kullanıcı bir şey kazanmaz
    /// veya kaybetmez.
    /// </summary>
    [Fact]
    public void RL1_Rol_Tablolari_Bosken_Davranis_Aynidir()
    {
        // Tablolar gerçekten VAR ve BOŞ.
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM role_permissions;"));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM role_button_permissions;"));

        // Personele YALNIZ okuma izni ver (klasik senaryo).
        _perms.SaveForUser(SuperAdminOturumu(), _personelId,
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());

        var s = Oturum("rol_personel");

        Assert.True(AccessControl.Can(s, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Create));
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Edit));
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Delete));
        Assert.False(AccessControl.Can(s, "vehicles", PermissionAction.View));   // deny-by-default

        // İzin kümesi TAM OLARAK bir modül taşır — rol katmanı hiçbir şey eklememiş.
        Assert.Single(s.Permissions.Modules);
        Assert.Empty(s.Permissions.Buttons);
    }

    // ══════════════════════ RL2–RL4 — BİRLEŞİM ══════════════════════

    /// <summary>⭐ Rol izni kullanıcının iznine EKLENİR; kullanıcı izni kaybolmaz.</summary>
    [Fact]
    public void RL2_Rol_Izni_Kullaniciya_Eklenir()
    {
        // Kullanıcı: yalnız GÖRME
        _perms.SaveForUser(SuperAdminOturumu(), _personelId,
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());
        // Rol (Personel): DÜZENLEME
        _perms.SaveForRole(SuperAdminOturumu(), RolId(RoleKeys.Staff),
            new[] { new ModulePermission("materials", false, false, true, false) }, Array.Empty<string>());

        var s = Oturum("rol_personel");

        Assert.True(AccessControl.Can(s, "materials", PermissionAction.View));   // kullanıcıdan
        Assert.True(AccessControl.Can(s, "materials", PermissionAction.Edit));   // ROLDEN
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Delete));// hiçbirinden değil
    }

    /// <summary>⭐ İki rol birleşir; aynı izin iki rolden gelirse sonuç TEK satırdır.</summary>
    [Fact]
    public void RL3_Iki_Rol_Birlesir_Duplicate_Tek_Sonuc()
    {
        var sa = SuperAdminOturumu();
        // Personel rolü: görme · Admin rolü: silme (kullanıcıya İKİ rol atanır)
        _perms.SaveForRole(sa, RolId(RoleKeys.Staff),
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());
        _perms.SaveForRole(sa, RolId(RoleKeys.CompanyAdmin),
            new[] { new ModulePermission("materials", true, false, false, true) }, Array.Empty<string>());

        // İkinci rolü ata (kullanıcı zaten Staff)
        Calistir($"INSERT INTO user_roles(user_id, role_id) VALUES('{_personelId}','{RolId(RoleKeys.CompanyAdmin)}');");

        var s = Oturum("rol_personel");

        // Aynı modül iki rolden geldi → TEK ModulePermission satırı, bayraklar birleşmiş.
        var materyal = s.Permissions.Modules.Where(m => m.ModuleKey == "materials").ToList();
        Assert.Single(materyal);
        Assert.True(materyal[0].CanView);      // iki rolde de var
        Assert.True(materyal[0].CanDelete);    // yalnız Admin rolünde var
        Assert.False(materyal[0].CanCreate);   // hiçbirinde yok
    }

    /// <summary>⭐ Rol kaldırılınca o rolden gelen izin DÜŞER; kullanıcının kendi izni KALIR.</summary>
    [Fact]
    public void RL4_Rol_Kaldirilinca_Rol_Izni_Duser_Kullanici_Izni_Kalir()
    {
        var sa = SuperAdminOturumu();
        _perms.SaveForUser(sa, _personelId,
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());
        _perms.SaveForRole(sa, RolId(RoleKeys.Staff),
            new[] { new ModulePermission("materials", false, false, true, false) }, Array.Empty<string>());

        Assert.True(AccessControl.Can(Oturum("rol_personel"), "materials", PermissionAction.Edit));

        // Rolü kullanıcıdan al
        Calistir($"DELETE FROM user_roles WHERE user_id='{_personelId}' AND role_id='{RolId(RoleKeys.Staff)}';");
        _cache.InvalidateAll();

        var s = Oturum("rol_personel");
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Edit));  // rol izni GİTTİ
        Assert.True(AccessControl.Can(s, "materials", PermissionAction.View));   // kullanıcı izni DURUYOR
    }

    // ══════════════════════ RL5–RL8 — PRECEDENCE AŞILAMAZ ══════════════════════

    /// <summary>
    /// ⭐ Rol ALLOW'ı ROL KİLİDİNİ (precedence 1) aşamaz.
    ///
    /// Bu, Faz 3a'nın en kritik güvenlik testidir: rol katmanı yanlış yere konsaydı, süper adminin
    /// bir role kapattığı ekran o role izin verilerek geri açılabilirdi.
    /// </summary>
    [Fact]
    public void RL5_Rol_Allow_Rol_Kilidini_Asamaz()
    {
        var sa = SuperAdminOturumu();

        // Rol kilidi: Personel rolüne "materials" KAPATILDI.
        Calistir($"INSERT INTO role_grant_limits(id,company_id,role_key,module_key,created_at) " +
                 $"VALUES('{Guid.NewGuid():N}','{Co}','{RoleKeys.Staff}','materials',1);");

        // Kapalı modülü role vermeye çalışmak REDDEDİLİR (yazma kapısı).
        Assert.ThrowsAny<Exception>(() =>
            _perms.SaveForRole(sa, RolId(RoleKeys.Staff), new[] { Tam("materials") }, Array.Empty<string>()));

        // Satır DOĞRUDAN yazılsa bile erişim AÇILMAZ (okuma kapısı — asıl güvence).
        Calistir($"INSERT INTO role_permissions(id,company_id,role_id,module_key,can_view,can_create,can_edit,can_delete,created_at,updated_at,version) " +
                 $"VALUES('{Guid.NewGuid():N}','{Co}','{RolId(RoleKeys.Staff)}','materials',1,1,1,1,1,1,1);");
        _cache.InvalidateAll();

        var s = Oturum("rol_personel");
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Delete));
    }

    /// <summary>⭐ Rol ALLOW'ı YAPISAL SINIFI (precedence 2) aşamaz — herkese açık modülde yazma açılmaz.</summary>
    [Fact]
    public void RL6_Rol_Allow_Yapisal_Sinifi_Asamaz()
    {
        // Doğrudan satır yazarak en kötü durumu kur: role Dashboard'da TAM yetki verilmiş.
        Calistir($"INSERT INTO role_permissions(id,company_id,role_id,module_key,can_view,can_create,can_edit,can_delete,created_at,updated_at,version) " +
                 $"VALUES('{Guid.NewGuid():N}','{Co}','{RolId(RoleKeys.Staff)}','{AppModules.Dashboard}',1,1,1,1,1,1,1);");
        _cache.InvalidateAll();

        var s = Oturum("rol_personel");
        Assert.True(AccessControl.Can(s, AppModules.Dashboard, PermissionAction.View));    // zaten herkese açık
        Assert.False(AccessControl.Can(s, AppModules.Dashboard, PermissionAction.Create)); // YAZMA yine kapalı
        Assert.False(AccessControl.Can(s, AppModules.Dashboard, PermissionAction.Delete));
    }

    /// <summary>⭐ Rol izni TENANT sınırını aşamaz: başka firmanın satırı bu firmada geçerli değildir.</summary>
    [Fact]
    public void RL7_Rol_Izni_Tenant_Sinirini_Asamaz()
    {
        // AYNI (sistem) role, BAŞKA firma adına izin satırı yazılmış.
        Calistir($"INSERT INTO role_permissions(id,company_id,role_id,module_key,can_view,can_create,can_edit,can_delete,created_at,updated_at,version) " +
                 $"VALUES('{Guid.NewGuid():N}','{CoB}','{RolId(RoleKeys.Staff)}','materials',1,1,1,1,1,1,1);");
        _cache.InvalidateAll();

        // Bizim firmamızdaki kullanıcı bundan ETKİLENMEZ.
        var s = Oturum("rol_personel");
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.View));
    }

    /// <summary>⭐ Rol izni ŞUBE KAPSAMINI genişletmez — kapsam ayrı bir otoritedir (BranchAccess).</summary>
    [Fact]
    public void RL8_Rol_Izni_Sube_Kapsamini_Genisletmez()
    {
        var sa = SuperAdminOturumu();
        _perms.SaveForRole(sa, RolId(RoleKeys.Staff), new[] { Tam("materials") }, Array.Empty<string>());

        var s = Oturum("rol_personel");
        s.ScopeBranchIds = new[] { "sube-A" };   // açık kapsam

        var izinli = BranchAccess.Allowed(s);
        Assert.NotNull(izinli);
        Assert.Equal(new[] { "sube-A" }, izinli!);   // rol izni kapsamı BÜYÜTMEDİ
    }

    // ══════════════════════ RL9 — DEVRETME TAVANI ══════════════════════

    /// <summary>
    /// ⭐ Aktör kendinde OLMAYAN yetkiyi bir role vererek dolaylı olarak kazandıramaz.
    ///
    /// Kullanıcı şartı §7: "Sadece UI'da checkbox disable etmek yeterli değildir." Bu test
    /// doğrudan SERVİSİ çağırır — arayüzü hiç kullanmaz.
    /// </summary>
    [Fact]
    public void RL9_Devretme_Tavani_Rol_Yolunda_Da_Uygulanir()
    {
        var sa = SuperAdminOturumu();

        // Sınırlı bir aktör: yalnız "permissions" (yetki ekranı) + materials/GÖRME.
        var aktorId = new UserService(_f).EnsureInitialAdmin(Co, "rol_sinirli", Pass, RoleKeys.Staff);
        _perms.SaveForUser(sa, aktorId, new[]
        {
            new ModulePermission("permissions", true, true, true, true),
            new ModulePermission("materials", true, false, false, false),   // SİLME YOK
        }, Array.Empty<string>());

        var aktor = Oturum("rol_sinirli");
        Assert.False(AccessControl.Can(aktor, "materials", PermissionAction.Delete));   // kendinde yok

        // Role SİLME vermeye çalışır — kırpılmalı.
        _perms.SaveForRole(aktor, RolId(RoleKeys.Staff),
            new[] { Tam("materials") }, Array.Empty<string>());

        // Yazılan satır: görme geçti, SİLME KIRPILDI.
        var yazilan = _perms.LoadForRole(sa, RolId(RoleKeys.Staff)).Modules
            .First(m => m.ModuleKey == "materials");
        Assert.True(yazilan.CanView);
        Assert.False(yazilan.CanDelete);   // ⭐ aktör kendinde olmayanı VEREMEDİ

        // Ve rolü taşıyan kullanıcı da silme kazanmadı.
        _cache.InvalidateAll();
        Assert.False(AccessControl.Can(Oturum("rol_personel"), "materials", PermissionAction.Delete));
    }

    // ══════════════════════ RL10–RL11 — BUTON · RAPOR · KAYIT TİPİ ══════════════════════

    /// <summary>⭐ Özel buton izinleri de birleşir (8 butonun deseni bozulmadan).</summary>
    [Fact]
    public void RL10_Rol_Butonlari_Birlesir()
    {
        var sa = SuperAdminOturumu();
        _perms.SaveForUser(sa, _personelId, Array.Empty<ModulePermission>(),
            new[] { SpecialButtons.AddLookup });
        _perms.SaveForRole(sa, RolId(RoleKeys.Staff), Array.Empty<ModulePermission>(),
            new[] { SpecialButtons.Reverse });

        var s = Oturum("rol_personel");
        Assert.True(AccessControl.CanUseButton(s, SpecialButtons.AddLookup));   // kullanıcıdan
        Assert.True(AccessControl.CanUseButton(s, SpecialButtons.Reverse));     // ROLDEN
        Assert.False(AccessControl.CanUseButton(s, SpecialButtons.BackDate));   // hiçbirinden
    }

    /// <summary>
    /// ⭐ Rapor (<c>rpt_</c>) ve kayıt tipi (<c>datype_</c>) anahtarları rol seviyesinde de çalışır.
    ///
    /// Bu, <c>module_key</c>'in serbest metin olmasının kanıtlanmış faydasıdır: iki katman için de
    /// AYRI migration gerekmedi. Mevcut rapor OR mantığı (K4) bozulmaz.
    /// </summary>
    [Fact]
    public void RL11_Rapor_Ve_Kayit_Tipi_Anahtarlari_Rolde_De_Calisir()
    {
        var rapor = DepoWise.Application.Reports.ReportCatalog.All[0];
        var kalem = AppModules.ReportItemKey(rapor.Key);

        var sa = SuperAdminOturumu();
        _perms.SaveForRole(sa, RolId(RoleKeys.Staff),
            new[] { new ModulePermission(kalem, true, false, false, false) }, Array.Empty<string>());

        var s = Oturum("rol_personel");
        Assert.True(AccessControl.Can(s, kalem, PermissionAction.View));
        Assert.True(DepoWise.Application.Reports.ReportCatalog.CanSee(s, rapor));   // K4: OR korunuyor
    }

    // ══════════════════════ RL12 — ÖNBELLEK ══════════════════════

    /// <summary>
    /// ⭐ Rol izni değişince BAYAT yetki kullanılmaz.
    ///
    /// Rol izni o role sahip HERKESİ etkiler; bu yüzden tüm fotoğraflar düşürülür. Yeniden giriş
    /// gerekmez — etki bir sonraki istekte görünür.
    /// </summary>
    [Fact]
    public void RL12_Rol_Izni_Degisince_Onbellek_Duser()
    {
        var sa = SuperAdminOturumu();

        // Fotoğrafı önbelleğe al
        var ilk = _auth.CreateSessionForUser(Co, _personelId);
        Assert.NotNull(ilk);
        Assert.False(AccessControl.Can(ilk!, "materials", PermissionAction.View));
        Assert.True(_cache.Count > 0, "Fotoğraf önbelleğe alınmadı — test ölçemez.");

        // Role izin ver
        _perms.SaveForRole(sa, RolId(RoleKeys.Staff),
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());

        // Önbellek DÜŞMÜŞ olmalı ve yeni yetki ANINDA geçerli olmalı (TTL beklenmeden).
        Assert.Equal(0, _cache.Count);
        var yeni = _auth.CreateSessionForUser(Co, _personelId);
        Assert.True(AccessControl.Can(yeni!, "materials", PermissionAction.View));
    }

    // ══════════════════════ RL13 — TENANT (YAZMA) ══════════════════════

    /// <summary>⭐ Başka firmaya ait bir role izin yazılamaz.</summary>
    [Fact]
    public void RL13_Baska_Firmanin_Rolune_Yazilamaz()
    {
        // B firmasına ait ÖZEL bir rol
        var yabanciRol = Guid.NewGuid().ToString("N");
        Calistir($"INSERT INTO roles(id,company_id,role_key,name,is_system,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{yabanciRol}','{CoB}','ozel-rol','Özel','0',1,1,1,0);");

        Assert.Throws<ForbiddenException>(() =>
            _perms.SaveForRole(SuperAdminOturumu(), yabanciRol, new[] { Tam("materials") }, Array.Empty<string>()));

        Assert.Equal(0L, Say("SELECT COUNT(*) FROM role_permissions;"));
    }

    // ══════════════════════ RL14 — ARAYÜZ SÖZLEŞMESİ ══════════════════════

    /// <summary>
    /// ⭐ ARAYÜZ, MODELİN DESTEKLEMEDİĞİ BİR DAVRANIŞ SUNMAZ (kullanıcı şartı S8/S9-a).
    ///
    /// Faz 3a'da rol izni <b>yalnız ALLOW</b> üretir. Bu yüzden rol bölümünde "engelle/yasakla/deny"
    /// gibi bir seçenek OLMAMALIDIR — olsaydı arayüz tutamayacağı bir söz vermiş olurdu
    /// ("globalde ver, ekranda engelle" bu modelde imkânsızdır).
    ///
    /// Ayrıca bölümün kullanıcı akışından AYRI durum değişkenleri kullandığı doğrulanır: mevcut
    /// çalışan ekranı bozmama şartının kaynak düzeyindeki kanıtı.
    /// </summary>
    [Fact]
    public void RL14_Arayuz_Desteklenmeyen_Deny_Sunmaz()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);

        var sayfa = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Web", "Components", "Pages", "Permissions.razor"));

        var bas = sayfa.IndexOf("FAZ 3a (ADR-222, 2026-09-05) — ROL YETKİLERİ", StringComparison.Ordinal);
        Assert.True(bas > 0, "Rol yetkileri bölümü bulunamadı.");
        var bolum = sayfa[bas..];

        // Rol bölümü VAR ve rol anahtarıyla çalışıyor.
        Assert.Contains("/api/permissions/role/", bolum);
        Assert.Contains("_rolMatrix", bolum);

        // ⭐ DENY sunulmuyor: bu kelimelerin hiçbiri rol bölümünde geçmemeli.
        foreach (var yasak in new[] { "Engelle", "Yasakla", "deny", "Deny", "Reddet" })
            Assert.DoesNotContain(yasak, bolum);

        // Kullanıcı akışının durumu PAYLAŞILMIYOR (ayrı değişkenler) → mevcut ekran bozulmadı.
        Assert.Contains("_rolEdit", bolum);
        Assert.Contains("_rolBusy", bolum);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
