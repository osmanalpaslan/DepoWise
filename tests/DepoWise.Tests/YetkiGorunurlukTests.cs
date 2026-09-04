using System.Text.Json;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YETKİ GÖRÜNÜRLÜĞÜ + DEĞİŞİKLİK İZLENEBİLİRLİĞİ (kullanıcı isteği 2026-09-04) ═══
///
/// Kullanıcı: "yetkisi yok ise webte ve masaüstünde ilgili ekranlar ve kayıt tipleri menü ağacında ve
/// ekranlarda görünmemeli." Bu davranışı kanıtlayan test YOKTU — eklendi.
///
/// Ayrıca gerçek bir olay: kullanıcı web'den bazı yetkileri kaldırdığını söyledi, veritabanında ise
/// 60 modülün 60'ı da TAM yetkiliydi. Denetim kaydı vardı (kim/ne zaman) ama <b>before/after boştu</b>,
/// yani NE gönderildiği veriden kanıtlanamadı. Artık kayda geçiyor — YG5/YG6 bunu kilitler.
///
///  YG1 — Yetkisiz kullanıcının menüsünde o ekran YOK
///  YG2 — Yetki verilince ekran menüde GÖRÜNÜR
///  YG3 — Personel rolü kendiliğinden yetki KAZANMAZ (deny-by-default)
///  YG4 — Firma admini bypass'ı: admin her ekranı görür (mevcut tasarım — bilinçli kilitlenir)
///  YG5 — Yetki kaydı denetime ÖNCEKİ ve SONRAKİ durumla yazılır
///  YG6 — Kaldırılan yetki denetim kaydında GÖRÜNÜR (fark kanıtlanabilir)
/// </summary>
public class YetkiGorunurlukTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly PermissionService _perms;
    private readonly UserService _users;
    private readonly SessionContext _superAdmin;
    private const string Co = "YGORUN";

    public YetkiGorunurlukTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ygorun_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        _users = new UserService(_f);
        _perms = new PermissionService(_f);
        var sid = _users.EnsureInitialAdmin(Co, "sa", "Test!2026", RoleKeys.SuperAdmin);
        _superAdmin = new SessionContext(sid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    }

    /// <summary>Personel oturumu: yalnız verilen modüller (deny-by-default).</summary>
    private static SessionContext Personel(params string[] moduller)
        => new("u-p", Co, new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, false, false, false)).ToArray()));

    // ── Menü görünürlüğü ───────────────────────────────────────────────────────────────────

    [Fact]
    public void YG1_Yetkisiz_Kullanicinin_Menusunde_Ekran_YOK()
    {
        var s = Personel("daily_activity");   // yalnız Günlük Faaliyet

        Assert.True(AccessControl.Can(s, "daily_activity", PermissionAction.View));
        // Verilmeyen ekranlar KAPALI — menü bu karardan üretilir.
        foreach (var kapali in new[] { "materials", "vehicles", "fuel", "requests", "users", "permissions" })
            Assert.False(AccessControl.Can(s, kapali, PermissionAction.View));
    }

    [Fact]
    public void YG2_Yetki_Verilince_Ekran_Gorunur()
    {
        var s = Personel("daily_activity", "materials");
        Assert.True(AccessControl.Can(s, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(s, "vehicles", PermissionAction.View));
    }

    [Fact]
    public void YG3_Personel_Rolu_Kendiliginden_Yetki_KAZANMAZ()
    {
        var bos = new SessionContext("u-b", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        foreach (var m in new[] { "materials", "vehicles", "daily_activity", "fuel", "stock" })
            Assert.False(AccessControl.Can(bos, m, PermissionAction.View));
    }

    [Fact]
    public void YG4_Firma_Admini_Bypass_ile_Tum_Ekranlari_Gorur()
    {
        // Bu MEVCUT tasarımdır ve bilinçlidir; test onu kilitler ki farkında olmadan değişmesin.
        // (Kullanıcının babası role-staff'tır — bu yüzden bypass onun durumunu AÇIKLAMAZ.)
        var admin = new SessionContext("u-a", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.True(AccessControl.Can(admin, "materials", PermissionAction.View));
        Assert.True(AccessControl.Can(admin, "vehicles", PermissionAction.Delete));
    }

    // ── Yetki değişikliğinin izlenebilirliği ───────────────────────────────────────────────

    [Fact]
    public void YG5_Yetki_Kaydi_Denetime_Oncesi_ve_Sonrasi_ile_Yazilir()
    {
        var uid = _users.CreateUser(_superAdmin, new NewUser("personel1", "Test!2026", null, new[] { RoleKeys.Staff }));

        _perms.SaveForUser(_superAdmin, uid,
            new[] { new ModulePermission("materials", true, true, false, false) }, Array.Empty<string>());

        var (before, after) = SonDenetim(uid);
        Assert.NotNull(after);
        Assert.Contains("materials:1100", after);          // görüntüle+oluştur açık, düzenle/sil kapalı
        Assert.NotNull(before);
        Assert.DoesNotContain("materials", before!);        // ilk kayıttan ÖNCE hiç yetkisi yoktu
    }

    [Fact]
    public void YG6_Kaldirilan_Yetki_Denetim_Kaydinda_Gorunur()
    {
        var uid = _users.CreateUser(_superAdmin, new NewUser("personel2", "Test!2026", null, new[] { RoleKeys.Staff }));

        // Önce iki ekran ver
        _perms.SaveForUser(_superAdmin, uid, new[]
        {
            new ModulePermission("materials", true, false, false, false),
            new ModulePermission("vehicles", true, false, false, false),
        }, Array.Empty<string>());

        // Sonra birini KALDIR (gönderilmeyen modül silinir)
        _perms.SaveForUser(_superAdmin, uid,
            new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());

        var (before, after) = SonDenetim(uid);
        Assert.Contains("vehicles", before!);      // önce vardı
        Assert.DoesNotContain("vehicles", after!); // sonra YOK → kaldırma kanıtlanabilir
        Assert.Contains("materials", after!);      // kalan yetki duruyor
    }

    /// <summary>Kullanıcının SON denetim kaydındaki önceki/sonraki durum.</summary>
    private (string? Before, string? After) SonDenetim(string userId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT before_json, after_json FROM audit_logs " +
                          "WHERE entity_type='user' AND entity_id=@u ORDER BY created_at DESC, rowid DESC LIMIT 1;";
        cmd.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null);
        return (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
