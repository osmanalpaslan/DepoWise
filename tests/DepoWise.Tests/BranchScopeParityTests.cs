using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-3e — WEB ↔ MASAÜSTÜ ŞUBE KAPSAMI PARİTESİ + KAPSAM YÖNETİMİ SÖZLEŞMESİ.
///
/// Masaüstü <c>BranchScopeSelector</c> ve web <c>BranchPicker.razor</c> AYNI kuralları uygular;
/// ikisi de <see cref="BranchAccess"/>'ten türer. Burada UI sınıfları değil, <b>ikisinin dayandığı
/// kurallar</b> test edilir (masaüstü VM'i Avalonia bağımlılığı taşıdığı için doğrudan kurulamaz).
///
/// <b>Gerçek tıklama testi AYRIDIR ve YAPILMAMIŞTIR.</b>
/// </summary>
public class BranchScopeParityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PermissionService _perms;
    private readonly UserService _users;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce, _karaman;
    private const string CoA = "A";

    public BranchScopeParityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g43e_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _perms = new PermissionService(_factory, _clock);
        _users = new UserService(_factory, _clock);

        var id = _users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
        _karaman = branches.Create(_admin, new NewBranch("KARAMAN"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static SessionContext Kullanici(string id, string[]? scope = null, string? home = null, string? operating = null)
        => new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission("permissions", true, true, true, true),
        }))
        { ScopeBranchIds = scope, HomeBranchId = home, OperatingBranchId = operating };

    private string YeniKullanici(string username)
        => _users.CreateUser(_admin, new NewUser(username, "Test!2026", username, new[] { RoleKeys.Staff }, CoA));

    // ═════════════════════════════════════════════════════════════════════════
    // A — MASAÜSTÜ ÇOKLU SEÇİM SÖZLEŞMESİ (web ile AYNI)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// M1 — ⭐ ÇOKLU SEÇİM: A+B seçildiğinde kapsam A+B'dir. Yetkisiz C seçilse bile servis
    /// kesişimle düşürür (fail-closed).
    /// </summary>
    [Fact]
    public void M1_Coklu_Secim_Kesisim()
    {
        var yonetici = Kullanici("m1", scope: new[] { _ankara, _duzce });

        Assert.Equal(2, BranchAccess.Effective(yonetici, new[] { _ankara, _duzce })!.Count);
        Assert.Equal(new[] { _duzce }, BranchAccess.Effective(yonetici, new[] { _duzce, _karaman }));
        Assert.Empty(BranchAccess.Effective(yonetici, new[] { _karaman })!);
    }

    /// <summary>
    /// M2 — ⭐ "Hiçbiri seçili değil" = TÜM YETKİLİ ŞUBELER (firmanın tümü DEĞİL).
    /// Masaüstünde ve web'de aynı anlam.
    /// </summary>
    [Fact]
    public void M2_Bos_Secim_Tum_Yetkili_Demektir()
    {
        var yonetici = Kullanici("m2", scope: new[] { _ankara, _duzce });
        var eff = BranchAccess.Effective(yonetici, null);
        Assert.Equal(2, eff!.Count);
        Assert.DoesNotContain(_karaman, eff);   // erişemediği şube TOPLAMA GİRMEZ
    }

    /// <summary>M3 — Tek yetkili şubeli kullanıcıda "boş seçim" yine kendi şubesidir.</summary>
    [Fact]
    public void M3_Tek_Yetkili_Bos_Secim()
        => Assert.Equal(new[] { _duzce }, BranchAccess.Effective(Kullanici("m3", scope: new[] { _duzce })));

    /// <summary>
    /// M4 — ⭐ ÇOKLU SEÇİM YAZMAYA GEÇMEZ: iki şube seçiliyken yazma hedefi oturumun çalışma
    /// şubesidir. Kapsam dışı hedef REDDEDİLİR. Bu kural masaüstü ve web'de AYNIDIR.
    /// </summary>
    [Fact]
    public void M4_Coklu_Secim_Yazmaya_Gecmez()
    {
        var u = Kullanici("m4", scope: new[] { _ankara, _duzce }, operating: _ankara);
        Assert.Equal(_ankara, BranchAccess.Resolve(u, null, "yazma"));
        Assert.Throws<ForbiddenException>(() => BranchAccess.Resolve(u, _karaman, "yazma"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B — KAPSAM YÖNETİMİ (masaüstü ekranı da AYNI servisi çağırır)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>P1 — Kapsam ekranı hedefin mevcut kapsamını ve atanabilir şubeleri verir.</summary>
    [Fact]
    public void P1_Kapsam_Ekrani_Verisi()
    {
        var u = YeniKullanici("pp1");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce });

        var v = _perms.GetBranchScope(_admin, u);
        Assert.Equal("explicit", v.Mode);
        Assert.Equal("Seçili şubeler", v.ModeText);
        Assert.Equal(new[] { _duzce }, v.ScopeBranchIds);
        Assert.Equal(3, v.AssignableBranches.Count);   // admin hepsini verebilir
    }

    /// <summary>
    /// P2 — ⭐ ATANABİLİR LİSTE AKTÖRÜN KAPSAMIYLA KIRPILIR: yönetici veremeyeceği şubeyi
    /// ekranda GÖREMEZ (UI yanlışlıkla sunamaz).
    /// </summary>
    [Fact]
    public void P2_Atanabilir_Liste_Kirpilir()
    {
        var u = YeniKullanici("pp2");
        var v = _perms.GetBranchScope(Kullanici("mgr", scope: new[] { _ankara, _duzce }), u);
        Assert.Equal(2, v.AssignableBranches.Count);
        Assert.DoesNotContain(v.AssignableBranches, b => b.Id == _karaman);
    }

    /// <summary>P3 — ⭐ Aktör kendisinde OLMAYAN şubeyi devredemez — sessiz kırpma YOK, HATA.</summary>
    [Fact]
    public void P3_Devir_Tavani()
    {
        var u = YeniKullanici("pp3");
        var yonetici = Kullanici("mgr2", scope: new[] { _duzce });

        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(yonetici, u, new[] { _karaman }));
        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(yonetici, u, new[] { _duzce, _karaman }));
        Assert.Empty(_perms.GetBranchScope(_admin, u).ScopeBranchIds);   // kısmi yazım YOK

        _perms.SaveBranchScope(yonetici, u, new[] { _duzce });
        Assert.Equal(new[] { _duzce }, _perms.GetBranchScope(_admin, u).ScopeBranchIds);
    }

    /// <summary>P4 — Kullanıcı KENDİ kapsamını değiştiremez (masaüstünde de aynı kural).</summary>
    [Fact]
    public void P4_Kendi_Kapsamini_Degistiremez()
        => Assert.Throws<InvalidOperationException>(() =>
            _perms.SaveBranchScope(_admin, _admin.UserId, new[] { _duzce }));

    /// <summary>P5 — Çoklu şube devredilebilir (yönetici birden fazla şube verebilir).</summary>
    [Fact]
    public void P5_Coklu_Sube_Devredilebilir()
    {
        var u = YeniKullanici("pp5");
        _perms.SaveBranchScope(_admin, u, new[] { _ankara, _duzce, _karaman });
        Assert.Equal(3, _perms.GetBranchScope(_admin, u).ScopeBranchIds.Count);
    }

    /// <summary>
    /// P6 — ⭐ FİRMA İZOLASYONU: başka firmanın şubesi kapsam olarak verilemez; kısmi yazım olmaz.
    /// </summary>
    [Fact]
    public void P6_Firma_Izolasyonu()
    {
        var u = YeniKullanici("pp6");
        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(_admin, u, new[] { "baska-firma-subesi" }));
        Assert.Empty(_perms.GetBranchScope(_admin, u).ScopeBranchIds);
    }

    /// <summary>
    /// P7 — ⭐ UÇTAN UCA: masaüstü ekranından kaydedilen kapsam, kullanıcının OTURUMUNU gerçekten
    /// kısıtlar (yetki fotoğrafı tazelenir) — kapsam yalnız kayıtta kalmaz.
    /// </summary>
    [Fact]
    public void P7_Kapsam_Oturumu_Kisitlar()
    {
        var u = YeniKullanici("pp7");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce });

        var oturum = new AuthService(_factory, _clock).CreateSessionForUser(CoA, u);
        Assert.NotNull(oturum);
        Assert.Equal(new[] { _duzce }, BranchAccess.Allowed(oturum!));
        Assert.False(BranchAccess.CanAccess(oturum!, _karaman));
    }

    /// <summary>P8 — Kapsam kaldırılabilir (boş liste) → kullanıcı varsayılan davranışına döner.</summary>
    [Fact]
    public void P8_Kapsam_Kaldirilabilir()
    {
        var u = YeniKullanici("pp8");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce });
        _perms.SaveBranchScope(_admin, u, Array.Empty<string>());

        var v = _perms.GetBranchScope(_admin, u);
        Assert.Empty(v.ScopeBranchIds);
        Assert.NotEqual("explicit", v.Mode);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
