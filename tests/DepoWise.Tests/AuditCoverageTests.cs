using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G6 (2026-08-18) — YETKİ/CİHAZ DEĞİŞİKLİKLERİNİN İZ KAYDI.
///
/// <b>DEN-B1</b> — <c>UserService.SetViewAllBranches</c> ("Tüm Şubeler" yetkisi) süper admin kapılıydı ✔
/// ama <c>AuditWriter.Write</c> YOKTU (kim ne zaman verdi/aldı kayıtsız) ve
/// <c>_snapshots.InvalidateUser</c> YOKTU → değişiklik 90 saniyeye kadar etkisiz kalıyordu.
/// Asıl risk GERİ ALMA yönünde: yetki kaldırıldıktan sonra kullanıcı 90 sn daha TÜM ŞUBELERİN
/// verisini görmeye devam ediyordu. Kardeş metotlar (DeleteUser/SetActive/SetRoles) düşürüyordu.
///
/// <b>DEN-B2</b> — <c>EnrollmentService</c> dosyasında <c>AuditWriter</c> geçiş sayısı 0'dı: cihaz
/// onayı/iptali/silinmesi, token yenileme, makine kotası ve cihaza firma/şube atama İZSİZDİ.
///
/// <b>DEN-B3 — BULGU GERİ ÇEKİLDİ (denetim hatası):</b> <c>Infrastructure/Org/BranchService.cs</c> ve
/// <c>Org/CompanyService.cs</c> "ölü kod" olarak işaretlenmişti. Silinince derleme KIRILDI:
/// <c>tests/DepoWise.Tests/OrgPersonnelTests.cs</c> ikisini de kullanıyor. Tarama yalnız <c>src/</c>
/// altına bakmıştı — <c>tests/</c> dahil edilmemişti. Dosyalar geri alındı.
/// Kalan (daha zayıf) gözlem: bu iki servisi ÜRETİM kodu kullanmıyor, yalnız bir test kullanıyor →
/// silme değil, gerekirse ayrı bir değerlendirme konusudur. Kod DEĞİŞTİRİLMEDİ.
/// </summary>
public class AuditCoverageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PermissionSnapshotCache _snapshots = new();
    private readonly UserService _users;
    private readonly AuthService _auth;
    private const string Co = "AUD-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public AuditCoverageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_audit_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock, _snapshots);
        _auth = new AuthService(_factory, _clock, _snapshots);
    }

    private long AuditCount(string entity)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type=@e;";
        cmd.AddWithValue("@e", entity);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private SessionContext SuperAdmin()
    {
        _users.EnsureInitialAdmin(Co, "root", "root123", RoleKeys.SuperAdmin);
        return _auth.Login(Co, "root", "root123").Session!;
    }

    // ── DEN-B1 ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TumSubeler_Yetkisi_Iz_Birakir()
    {
        var su = SuperAdmin();
        var uid = _users.CreateUser(su, new NewUser("depocu", "Depo!2026", "Depo",
            new List<string> { RoleKeys.Staff }, Co, null, null, false, null));

        Assert.Equal(0, AuditCount("user_view_all_branches"));

        _users.SetViewAllBranches(su, uid, true);
        Assert.Equal(1, AuditCount("user_view_all_branches"));

        _users.SetViewAllBranches(su, uid, false);
        Assert.Equal(2, AuditCount("user_view_all_branches"));   // GERİ ALMA da iz bırakmalı
    }

    /// <summary>⭐ Yetki GERİ ALINDIĞINDA fotoğraf ANINDA düşmeli — 90 sn beklenmemeli.</summary>
    [Fact]
    public void TumSubeler_Yetkisi_Onbellegi_ANINDA_Duser()
    {
        var su = SuperAdmin();
        var uid = _users.CreateUser(su, new NewUser("sef", "Sef!2026", "Şef",
            new List<string> { RoleKeys.Staff }, Co, null, null, false, null));

        _users.SetViewAllBranches(su, uid, true);
        var acikken = _auth.CreateSessionForUser(Co, uid);       // fotoğraf önbelleğe girer
        Assert.True(acikken!.CanViewAllBranches);

        _users.SetViewAllBranches(su, uid, false);               // yetki GERİ ALINIR
        var kapaliyken = _auth.CreateSessionForUser(Co, uid);    // önbellek düşmüş olmalı

        Assert.False(kapaliyken!.CanViewAllBranches);
    }

    // ── DEN-B2 ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cihaz_Islemleri_Iz_Birakir()
    {
        var su = SuperAdmin();
        var enroll = new EnrollmentService(_factory, _clock);

        // Makine kendini kaydeder (kota içinde → active).
        var kayit = enroll.RegisterSelf(Co, "TEST-PC");
        Assert.NotNull(kayit.DeviceId);

        enroll.RevokeDevice(su, kayit.DeviceId);
        Assert.Equal(1, AuditCount("sync_device_revoke"));

        enroll.Reactivate(su, kayit.DeviceId);
        Assert.Equal(1, AuditCount("sync_device_reactivate"));

        enroll.SetQuota(su, Co, 5);
        Assert.Equal(1, AuditCount("machine_quota"));

        enroll.DeleteDevice(su, kayit.DeviceId);
        Assert.Equal(1, AuditCount("sync_device_delete"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
