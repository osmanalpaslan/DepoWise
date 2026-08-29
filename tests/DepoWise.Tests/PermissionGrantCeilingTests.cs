using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G1 — YETKİ DEVRETME TAVANI · SIFIRLAMA · ÖZET (kullanıcı isteği 2026-08-12).
///
/// <b>KURAL:</b> "Kimse kendinde OLMAYAN yetkiyi başkasına veremez." Bu kural UI'da değil, SERVİS
/// katmanında zorunludur — doğrudan servis çağrısı da (API'yi atlayarak) aynı kapıdan geçer.
///
/// <b>🔴 KAPATILAN AÇIK:</b> eski <c>GrantableLimit</c> yalnız aktörün <c>user_permissions</c>
/// SATIRLARINA bakıyordu ve satırı olmayan firma adminini <b>sınırsız</b> sayıyordu. Firma admini tipik
/// olarak bypass ile çalışır ve satırı YOKTUR → kırpma pratikte hiç uygulanmıyordu. Somut sonuç:
/// süper adminin aktörün ROLÜNE kapattığı bir modülü (aktör kendisi kullanamadığı hâlde) başkasına
/// VEREBİLİYORDU. Artık tavan <see cref="AccessControl.GrantCeiling"/>'den gelir ve
/// <see cref="AccessControl.Can"/> ile AYNI kuralları uygular.
/// </summary>
public class PermissionGrantCeilingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly PermissionService _perms;
    private readonly RoleGrantService _roleGrants;
    private const string Co = "A";

    public PermissionGrantCeilingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_gc_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _perms = new PermissionService(_factory, _clock);
        _roleGrants = new RoleGrantService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static SessionContext Session(string userId, string role, PermissionSet? perms = null,
        IReadOnlySet<string>? blocked = null)
        => new(userId, Co, new[] { role }, perms ?? PermissionSet.Empty)
        { BlockedModules = blocked ?? new HashSet<string>(StringComparer.Ordinal) };

    private string NewUser(string username, string role)
        => _users.EnsureInitialAdmin(Co, username, "Test!2026", role);

    private static ModulePermission Mod(string key, bool v, bool c, bool e, bool d) => new(key, v, c, e, d);

    /// <summary>Veritabanındaki HAM izin satırları (servisin ne yazdığı, ne döndürdüğü değil).</summary>
    private Dictionary<string, ModulePermission> Rows(string userId)
    {
        var map = new Dictionary<string, ModulePermission>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete FROM user_permissions WHERE user_id=@u;";
        cmd.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[r.GetString(0)] = new ModulePermission(r.GetString(0),
                r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1);
        return map;
    }

    private long Count(string table, string userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE user_id=@u;";
        cmd.AddWithValue("@u", userId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // G1b — DEVRETME TAVANI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — Kullanıcının verdiği örnek: Malzeme GÖRÜNTÜLEME'si olan ama SİLME'si olmayan aktör,
    /// hedefe görüntülemeyi VEREBİLİR, silmeyi VEREMEZ. Kırpma aksiyon seviyesindedir.</summary>
    [Fact]
    public void G1b_Aktor_Kendinde_Olmayan_Aksiyonu_Veremez()
    {
        var aktor = NewUser("personel_a", RoleKeys.Staff);
        var hedef = NewUser("personel_b", RoleKeys.Staff);

        // Aktör: materials görüntüleme + düzenleme VAR; ekleme/silme YOK. permissions (bu ekran) düzenleme VAR.
        var s = Session(aktor, RoleKeys.Staff, new PermissionSet(new[]
        {
            Mod("materials", true, false, true, false),
            Mod("permissions", true, false, true, false),
        }));

        // Aktör hedefe TAM materials yetkisi vermeye çalışıyor (UI'yi atlayıp doğrudan servis çağrısı).
        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, true, true, true) }, Array.Empty<string>());

        var yazilan = Rows(hedef)["materials"];
        Assert.True(yazilan.CanView);     // aktörde VAR → verilebildi
        Assert.True(yazilan.CanEdit);     // aktörde VAR → verilebildi
        Assert.False(yazilan.CanCreate);  // aktörde YOK → KIRPILDI
        Assert.False(yazilan.CanDelete);  // aktörde YOK → KIRPILDI
    }

    /// <summary>2 — Aktörde HİÇ olmayan bir modül tamamen kırpılır (satır bile yazılmaz).</summary>
    [Fact]
    public void G1b_Aktorde_Hic_Olmayan_Modul_Satiri_Bile_Yazilmaz()
    {
        var aktor = NewUser("personel_c", RoleKeys.Staff);
        var hedef = NewUser("personel_d", RoleKeys.Staff);
        var s = Session(aktor, RoleKeys.Staff, new PermissionSet(new[]
        {
            Mod("materials", true, false, false, false),
            Mod("permissions", true, false, true, false),
        }));

        _perms.SaveForUser(s, hedef, new[]
        {
            Mod("materials", true, false, false, false),
            Mod("vehicles", true, true, true, true),   // aktörde YOK
        }, Array.Empty<string>());

        var rows = Rows(hedef);
        Assert.True(rows.ContainsKey("materials"));
        Assert.False(rows.ContainsKey("vehicles"));   // hiç satır yok = deny-by-default
    }

    /// <summary>3 — ⭐ KAPATILAN AÇIK: aktörün ROLÜNE kapatılmış (Rol Yetki Kontrol) modül,
    /// aktör FİRMA ADMİNİ olsa ve hiç açık izin satırı olmasa bile DEVREDİLEMEZ.
    /// Eski modelde "satırı yok + admin → sınırsız" kısayolu bunu mümkün kılıyordu.</summary>
    [Fact]
    public void G1b_Aktorun_Roluine_Kapatilmis_Modul_Devredilemez()
    {
        var admin = NewUser("firma_admini", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_e", RoleKeys.Staff);

        // Aktör firma admini ve HİÇ açık izin satırı YOK (tipik durum — eski backdoor'un tetiklendiği hâl).
        Assert.Empty(Rows(admin));

        // Süper admin, aktörün ROLÜNE "vehicles" ekranını kapatıyor → aktör onu KENDİ kullanamaz.
        var blocked = new HashSet<string>(StringComparer.Ordinal) { "vehicles" };
        var s = Session(admin, RoleKeys.CompanyAdmin, blocked: blocked);
        Assert.False(AccessControl.Can(s, "vehicles", PermissionAction.View));   // kendisi erişemiyor

        _perms.SaveForUser(s, hedef, new[]
        {
            Mod("vehicles", true, true, true, true),   // kendi kullanamadığı ekran
            Mod("materials", true, true, false, false), // normal ekran → verilebilmeli
        }, Array.Empty<string>());

        var rows = Rows(hedef);
        Assert.False(rows.ContainsKey("vehicles"));   // ⭐ ARTIK VERİLEMİYOR
        Assert.True(rows["materials"].CanView);       // normal ekran bozulmadı
        Assert.True(rows["materials"].CanCreate);
    }

    /// <summary>4 — GERİYE UYUM: firma admini normal ekranlarda bypass ile TAM yetkilidir → onları
    /// devredebilmeye DEVAM eder. Düzeltme mevcut admin davranışını kırmaz.</summary>
    [Fact]
    public void G1b_Firma_Admini_Normal_Ekranlari_Hala_Devredebilir()
    {
        var admin = NewUser("firma_admini2", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_f", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);

        _perms.SaveForUser(s, hedef, new[]
        {
            Mod("materials", true, true, true, true),
            Mod("stock", true, true, true, true),
            Mod("reports", true, false, false, false),
        }, Array.Empty<string>());

        var rows = Rows(hedef);
        Assert.True(rows["materials"].CanDelete);   // admin bypass ile sahip → devredebildi
        Assert.True(rows["stock"].CanCreate);
        Assert.True(rows["reports"].CanView);
    }

    /// <summary>5 — Süper admin sınırsız kalır (platform sahibi kendini kilitlemesin).</summary>
    [Fact]
    public void G1b_Super_Admin_Sinirsiz_Kalir()
    {
        var sa = NewUser("super", RoleKeys.SuperAdmin);
        var hedef = NewUser("personel_g", RoleKeys.Staff);
        var s = Session(sa, RoleKeys.SuperAdmin);

        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, true, true, true) }, Array.Empty<string>());
        Assert.True(Rows(hedef)["materials"].CanDelete);
    }

    /// <summary>6 — BUTON yetkisi de aynı tavana tabidir: aktörde olmayan buton devredilemez.</summary>
    [Fact]
    public void G1b_Aktorde_Olmayan_Buton_Devredilemez()
    {
        var aktor = NewUser("personel_h", RoleKeys.Staff);
        var hedef = NewUser("personel_i", RoleKeys.Staff);
        var s = Session(aktor, RoleKeys.Staff,
            new PermissionSet(new[] { Mod("permissions", true, false, true, false) },
                new[] { SpecialButtons.ExportReports }));

        // ADR-179: "aktörde olmayan buton" örneği BackDate oldu (btn-reset-db katalogdan kaldırıldı).
        _perms.SaveForUser(s, hedef, Array.Empty<ModulePermission>(),
            new[] { SpecialButtons.ExportReports, SpecialButtons.BackDate });

        var data = _perms.GetForUser(Session(aktor, RoleKeys.CompanyAdmin), hedef);
        Assert.Contains(SpecialButtons.ExportReports, data.Buttons);      // aktörde VAR
        Assert.DoesNotContain(SpecialButtons.BackDate, data.Buttons);     // aktörde YOK → kırpıldı
    }

    /// <summary>7 — <see cref="AccessControl.GrantCeiling"/> ile <see cref="AccessControl.Can"/> AYNI
    /// sonucu vermeli: "erişebildiğim" = "verebileceğim". Tek kaynak kuralının kilidi.</summary>
    [Theory]
    [InlineData(RoleKeys.Staff)]
    [InlineData(RoleKeys.CompanyAdmin)]
    [InlineData(RoleKeys.SuperAdmin)]
    public void G1b_Tavan_Ile_Etkin_Yetki_Ayni_Sonucu_Verir(string role)
    {
        var blocked = new HashSet<string>(StringComparer.Ordinal) { "vehicles" };
        var s = Session("u", role, new PermissionSet(new[]
        {
            Mod("materials", true, false, true, false),
            Mod("stock", true, true, false, false),
        }), blocked);

        foreach (var (key, _) in AppModules.All)
        {
            if (AppModules.IsPublic(key)) continue;
            var ceiling = AccessControl.GrantCeiling(s, key);
            // DYR-01 (ADR-173, PK-J1): okuması-herkese modülde View HERKESTE vardır ama bu bir "verilmiş
            // yetki" değildir → devretme tavanına GİRMEZ. Yazma bayrakları normal kurala tabidir.
            if (!AppModules.IsPublicRead(key))
                Assert.Equal(AccessControl.Can(s, key, PermissionAction.View), ceiling.CanView);
            Assert.Equal(AccessControl.Can(s, key, PermissionAction.Create), ceiling.CanCreate);
            Assert.Equal(AccessControl.Can(s, key, PermissionAction.Edit), ceiling.CanEdit);
            Assert.Equal(AccessControl.Can(s, key, PermissionAction.Delete), ceiling.CanDelete);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // G1a — SIFIRLAMA
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>8 — Sıfırlama TÜM modül ve buton satırlarını siler; kullanıcı kaydı ve rolü DURUR.</summary>
    [Fact]
    public void G1a_Sifirlama_Tum_Izinleri_Siler_Kullaniciyi_Silmez()
    {
        var admin = NewUser("admin_r", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_r", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);

        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, true, true, true), Mod("stock", true, false, false, false) },
            new[] { SpecialButtons.ExportReports });
        Assert.Equal(2, Count("user_permissions", hedef));
        Assert.Equal(1, Count("user_button_permissions", hedef));

        var (mods, btns) = _perms.ResetForUser(s, hedef);

        Assert.Equal(2, mods);
        Assert.Equal(1, btns);
        Assert.Equal(0, Count("user_permissions", hedef));
        Assert.Equal(0, Count("user_button_permissions", hedef));

        // Kullanıcı DURUYOR (silme değil, yalnız yetki temizleme)
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE id=@u AND is_deleted=0;";
        cmd.AddWithValue("@u", hedef);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>9 — Sıfırlama sonrası hedef HİÇBİR modüle erişemez (deny-by-default'a döndü).</summary>
    [Fact]
    public void G1a_Sifirlama_Sonrasi_Hedef_Hicbir_Ekrana_Erisemez()
    {
        var admin = NewUser("admin_r2", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_r2", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);
        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, true, true, true) }, Array.Empty<string>());

        _perms.ResetForUser(s, hedef);

        var data = _perms.GetForUser(s, hedef);
        var hedefOturum = Session(hedef, RoleKeys.Staff, new PermissionSet(data.Modules, data.Buttons));
        foreach (var (key, _) in AppModules.All)
        {
            if (AppModules.IsPublic(key) || AppModules.IsUserDirectory(key)) continue;
            // DYR-01 (PK-J1): duyuru OKUMA herkese açıktır — sıfırlama bunu kapatmaz (bilinçli);
            // yazma yine kapalı kalır ve burada kilitlenir.
            if (AppModules.IsPublicRead(key))
            {
                Assert.True(AccessControl.Can(hedefOturum, key, PermissionAction.View));
                Assert.False(AccessControl.Can(hedefOturum, key, PermissionAction.Create));
                Assert.False(AccessControl.Can(hedefOturum, key, PermissionAction.Edit));
                Assert.False(AccessControl.Can(hedefOturum, key, PermissionAction.Delete));
                continue;
            }
            Assert.False(AccessControl.Can(hedefOturum, key, PermissionAction.View));
        }
    }

    /// <summary>10 — Sıfırlama AUDIT kaydı bırakır (kim, kimi, ne zaman).</summary>
    [Fact]
    public void G1a_Sifirlama_Audit_Kaydi_Birakir()
    {
        var admin = NewUser("admin_r3", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_r3", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);
        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, false, false, false) }, Array.Empty<string>());

        _perms.ResetForUser(s, hedef);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type='user_permissions' AND entity_id=@u AND user_id=@a;";
        cmd.AddWithValue("@u", hedef);
        cmd.AddWithValue("@a", admin);
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 1);
    }

    /// <summary>11 — Kendi yetkisini sıfırlamak ENGELLENİR (kullanıcı kendini kilitlemesin).</summary>
    [Fact]
    public void G1a_Kendi_Yetkisini_Sifirlayamaz()
    {
        var admin = NewUser("admin_r4", RoleKeys.CompanyAdmin);
        var s = Session(admin, RoleKeys.CompanyAdmin);
        var ex = Assert.Throws<InvalidOperationException>(() => _perms.ResetForUser(s, admin));
        Assert.Contains("Kendi yetkilerinizi", ex.Message);
    }

    /// <summary>12 — Yetkisiz kullanıcı sıfırlayamaz (deny-by-default; UI tek savunma değildir).</summary>
    [Fact]
    public void G1a_Yetkisiz_Kullanici_Sifirlayamaz()
    {
        var admin = NewUser("admin_r5", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_r5", RoleKeys.Staff);
        _perms.SaveForUser(Session(admin, RoleKeys.CompanyAdmin), hedef,
            new[] { Mod("materials", true, false, false, false) }, Array.Empty<string>());

        var yetkisiz = Session("bilinmeyen", RoleKeys.Staff);   // permissions yetkisi YOK
        Assert.Throws<ForbiddenException>(() => _perms.ResetForUser(yetkisiz, hedef));
        Assert.Equal(1, Count("user_permissions", hedef));      // hiçbir şey silinmedi
    }

    /// <summary>13 — DÜZENLEME KİLİDİ: arada başkası kaydettiyse sıfırlama reddedilir ve
    /// HİÇBİR satır silinmez (kısmi yazma yok).</summary>
    [Fact]
    public void G1a_Surum_Cakismasinda_Hicbir_Satir_Silinmez()
    {
        var admin = NewUser("admin_r6", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_r6", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);
        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, false, false, false) }, Array.Empty<string>());

        var eskiSurum = _perms.GetForUser(s, hedef).Version;
        _perms.SaveForUser(s, hedef, new[] { Mod("stock", true, false, false, false) }, Array.Empty<string>()); // sürüm artar

        Assert.Throws<ConcurrencyException>(() => _perms.ResetForUser(s, hedef, eskiSurum));
        Assert.Equal(1, Count("user_permissions", hedef));   // geri alındı, satır duruyor
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // G1a — ÖZET
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>14 — Özet HAM satırı değil ETKİN yetkiyi gösterir: admin'de satır olmasa da
    /// erişebildiği ekranlar listelenir.</summary>
    [Fact]
    public void G1a_Ozet_Adminde_Satir_Olmasa_Da_Erisilebilenleri_Gosterir()
    {
        var admin = NewUser("admin_s", RoleKeys.CompanyAdmin);
        var s = Session(admin, RoleKeys.CompanyAdmin);

        var ozet = _perms.SummaryForUser(s, admin);

        Assert.Empty(Rows(admin));                       // hiç açık satır YOK
        Assert.True(ozet.VisibleModuleCount > 10);       // ...ama çok sayıda ekrana erişiyor
        Assert.True(ozet.IsCompanyAdmin);
        Assert.Contains("Admin", ozet.SourceText);
        Assert.Equal(0, ozet.ExplicitModuleRows);
    }

    /// <summary>15 — Personelde özet YALNIZ açıkça verilenleri gösterir ve aksiyonları doğru yazar.</summary>
    [Fact]
    public void G1a_Ozet_Personelde_Yalniz_Verilenleri_Gosterir()
    {
        var admin = NewUser("admin_s2", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_s2", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);
        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, false, true, false) }, Array.Empty<string>());

        var ozet = _perms.SummaryForUser(s, hedef);

        var satir = Assert.Single(ozet.Modules, m => m.ModuleKey == "materials");
        Assert.True(satir.View);
        Assert.True(satir.Edit);
        Assert.False(satir.Create);
        Assert.False(satir.Delete);
        Assert.Equal("Görüntüleme · Düzenleme", satir.ActionsText);
        Assert.DoesNotContain(ozet.Modules, m => m.ModuleKey == "vehicles");   // verilmeyen ekran özet dışı
        Assert.Contains("Personel", ozet.SourceText);
    }

    /// <summary>16 — Sıfırlanmış kullanıcının özeti BOŞtur (hiçbir ekran).</summary>
    [Fact]
    public void G1a_Sifirlanmis_Kullanicinin_Ozeti_Bostur()
    {
        var admin = NewUser("admin_s3", RoleKeys.CompanyAdmin);
        var hedef = NewUser("personel_s3", RoleKeys.Staff);
        var s = Session(admin, RoleKeys.CompanyAdmin);
        _perms.SaveForUser(s, hedef, new[] { Mod("materials", true, true, true, true) }, Array.Empty<string>());
        _perms.ResetForUser(s, hedef);

        var ozet = _perms.SummaryForUser(s, hedef);
        // Geriye YALNIZ herkese açık modüller kalır (Ana Ekran/Hakkında/Tema — yetkiyle yönetilmezler;
        // DYR-01/PK-J1: Duyurular'ın okuması da herkese açıktır ve özetten düşmez — bilinçli).
        Assert.All(ozet.Modules, m => Assert.True(AppModules.IsPublic(m.ModuleKey) || AppModules.IsPublicRead(m.ModuleKey)));
        Assert.DoesNotContain(ozet.Modules, m => m.ModuleKey == "materials");
        Assert.Equal(0, ozet.ExplicitModuleRows);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
