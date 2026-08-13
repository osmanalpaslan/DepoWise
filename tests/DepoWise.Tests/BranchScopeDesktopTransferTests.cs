using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// GUI-01 (2026-08-13) — <b>GERÇEK MASAÜSTÜ GUI TESTİNDE BULUNAN AÇIK.</b>
///
/// Windows UI Automation ile masaüstüne gerçek giriş yapıldığında görüldü ki:
/// kapsamı "Şube A + Şube B" olan kullanıcı, giriş ekranında <b>yetkisi olmayan "Şube C"yi</b>
/// görüyor ve o şubeye <b>giriş yapabiliyordu</b>.
///
/// <b>Kök neden:</b> <see cref="RemoteUserBundle"/> (sunucudan masaüstüne inen kullanıcı paketi)
/// firma, kullanıcı, roller, modül izinleri ve buton izinlerini taşıyordu ama
/// <c>user_scopes</c> (ŞUBE KAPSAMI) satırlarını <b>hiç taşımıyordu</b>. Bu yüzden masaüstünde
/// <c>SessionContext.ScopeBranchIds</c> daima boş kalıyor, <see cref="BranchAccess.Allowed"/>
/// bir sonraki basamağa düşüp admin'i <b>kısıtsız</b> sayıyordu.
/// Yani kapsam web'de uygulanırken masaüstünde <b>fiilen yoktu</b>.
///
/// Bu testler kapsamın uçtan uca (sunucu → paket → yerel DB → oturum) taşındığını doğrular.
/// </summary>
public class BranchScopeDesktopTransferTests : IDisposable
{
    private readonly string _sunucuDb, _yerelDb;
    private readonly SqliteConnectionFactory _sunucu, _yerel;
    private readonly TestClock _clock = new();
    private readonly AuthService _sunucuAuth, _yerelAuth;
    private readonly UserService _sunucuUsers;
    private readonly PermissionService _sunucuPerms;
    private readonly string _adminId, _subeA, _subeB, _subeC;
    private readonly SessionContext _super;
    private const string Co = "DEPOWISE";
    private const string Parola = "Gui!2026";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public BranchScopeDesktopTransferTests()
    {
        _sunucuDb = Path.Combine(Path.GetTempPath(), "dw_gui01_srv_" + Guid.NewGuid().ToString("N") + ".db");
        _yerelDb = Path.Combine(Path.GetTempPath(), "dw_gui01_loc_" + Guid.NewGuid().ToString("N") + ".db");
        _sunucu = new SqliteConnectionFactory(_sunucuDb);
        _yerel = new SqliteConnectionFactory(_yerelDb);
        new MigrationRunner(_sunucu).Run();
        new MigrationRunner(_yerel).Run();

        var snap = new PermissionSnapshotCache();
        _sunucuAuth = new AuthService(_sunucu, _clock, snap);
        _yerelAuth = new AuthService(_yerel, _clock, new PermissionSnapshotCache());
        _sunucuUsers = new UserService(_sunucu, _clock, snap);
        _sunucuPerms = new PermissionService(_sunucu, _clock, snap);

        _adminId = _sunucuUsers.EnsureInitialAdmin(Co, "admin", Parola, RoleKeys.CompanyAdmin);
        var yonetici = new SessionContext(_adminId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // Kapsamı BAŞKA bir yetkili atar — kullanıcı kendi kapsamını değiştiremez (ürün kuralı).
        var superId = _sunucuUsers.EnsureInitialAdmin(Co, "superadmin", Parola, RoleKeys.SuperAdmin);
        _super = new SessionContext(superId, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

        var subeler = new BranchService(_sunucu, _clock);
        _subeA = subeler.Create(yonetici, new NewBranch("Sube A"));
        _subeB = subeler.Create(yonetici, new NewBranch("Sube B"));
        _subeC = subeler.Create(yonetici, new NewBranch("Sube C"));
    }

    /// <summary>Şubeleri yerel DB'ye aynalar — masaüstünde BranchMirror'ın yaptığı iş
    /// (user_scopes.branch_id FK'si buna bağlı olduğu için import öncesi çalışmalıdır).</summary>
    private void SubeleriYereleAynala()
    {
        using var s = _sunucu.Create();
        using var oku = s.CreateCommand();
        oku.CommandText = "SELECT id, company_id, name FROM branches WHERE is_deleted=0;";
        var satirlar = new List<(string Id, string Co, string Ad)>();
        using (var r = oku.ExecuteReader())
            while (r.Read()) satirlar.Add((r.GetString(0), r.GetString(1), r.GetString(2)));

        using var y = _yerel.Create();
        foreach (var (id, co, ad) in satirlar)
        {
            using var ins = y.CreateCommand();
            ins.CommandText =
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,0,0,1,0) ON CONFLICT DO NOTHING;" +
                "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                "VALUES(@id,@c,@n,'branch',0,0,1,0) ON CONFLICT(id) DO UPDATE SET name=@n;";
            ins.AddWithValue("@id", id); ins.AddWithValue("@c", co); ins.AddWithValue("@n", ad);
            ins.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<string> YerelKapsam(SqliteConnectionFactory f, string userId)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id FROM user_scopes WHERE user_id=@u ORDER BY branch_id;";
        cmd.AddWithValue("@u", userId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // ═══════════════════════════════════════════════════════════════════
    // 1 — PAKET KAPSAMI TAŞIR
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>D1 — Sunucudaki user_scopes satırları kullanıcı paketine girer.
    /// (Eski davranış: paket kapsam taşımıyordu → bu test kırmızıydı.)</summary>
    [Fact]
    public void D1_Paket_Sube_Kapsamini_Tasir()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA, _subeB });

        var paket = _sunucuAuth.ExportForSync(Co, "admin", Parola);

        Assert.NotNull(paket);
        Assert.NotNull(paket!.ScopeBranchIds);
        Assert.Equal(new[] { _subeA, _subeB }.OrderBy(x => x),
                     paket.ScopeBranchIds!.OrderBy(x => x));
    }

    /// <summary>D2 — Kapsam YOKSA paket null taşır (kısıtsız kullanıcı yanlışlıkla kısıtlanmasın).</summary>
    [Fact]
    public void D2_Kapsamsiz_Kullanicida_Null()
    {
        var paket = _sunucuAuth.ExportForSync(Co, "admin", Parola);
        Assert.NotNull(paket);
        Assert.Null(paket!.ScopeBranchIds);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2 — YEREL DB'YE YAZILIR VE OTURUMA GİRER
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>D3 — İçe aktarım kapsamı YEREL user_scopes tablosuna yazar.</summary>
    [Fact]
    public void D3_Import_Kapsami_Yerele_Yazar()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA, _subeB });
        SubeleriYereleAynala();

        _yerelAuth.ImportRemoteUser(_sunucuAuth.ExportForSync(Co, "admin", Parola)!);

        Assert.Equal(new[] { _subeA, _subeB }.OrderBy(x => x), YerelKapsam(_yerel, _adminId).OrderBy(x => x));
    }

    /// <summary>D4 — 🔴 ASIL HATA: kapsam taşındıktan sonra YEREL oturum artık kısıtsız DEĞİL.
    /// Admin olmasına rağmen yalnız A+B izinli; "Şube C" reddedilir.</summary>
    [Fact]
    public void D4_Yerel_Oturum_Kapsamla_Sinirlanir()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA, _subeB });
        SubeleriYereleAynala();
        _yerelAuth.ImportRemoteUser(_sunucuAuth.ExportForSync(Co, "admin", Parola)!);

        var sonuc = _yerelAuth.Login(Co, "admin", Parola);
        var oturum = sonuc.Session;

        Assert.True(oturum is not null, "yerel giriş başarısız: " + sonuc.Error);
        var izinli = BranchAccess.Allowed(oturum!);
        Assert.NotNull(izinli);   // eski davranış: null (KISITSIZ) → hata buradaydı
        Assert.Equal(new[] { _subeA, _subeB }.OrderBy(x => x), izinli!.OrderBy(x => x));
        Assert.True(BranchAccess.CanAccess(oturum!, _subeA));
        Assert.True(BranchAccess.CanAccess(oturum!, _subeB));
        Assert.False(BranchAccess.CanAccess(oturum!, _subeC));   // 🔴 GİRİŞTE SEÇİLEBİLİYORDU
    }

    /// <summary>D5 — Kapsam sunucuda DARALTILINCA yerel de daralır (bayat geniş kapsam kalmaz).</summary>
    [Fact]
    public void D5_Kapsam_Daralinca_Yerel_De_Daralir()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA, _subeB, _subeC });
        SubeleriYereleAynala();
        _yerelAuth.ImportRemoteUser(_sunucuAuth.ExportForSync(Co, "admin", Parola)!);
        Assert.Equal(3, YerelKapsam(_yerel, _adminId).Count);

        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA });
        _yerelAuth.ImportRemoteUser(_sunucuAuth.ExportForSync(Co, "admin", Parola)!);

        Assert.Equal(new[] { _subeA }, YerelKapsam(_yerel, _adminId));
    }

    /// <summary>D6 — Kapsam TAŞIMAYAN paket (eski sunucu) yereldeki kapsamı SİLMEZ.
    /// Aksi hâlde eski bir sunucuya bağlanmak kullanıcıyı sessizce kısıtsız yapardı.</summary>
    [Fact]
    public void D6_Kapsamsiz_Paket_Yereli_Silmez()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA, _subeB });
        SubeleriYereleAynala();
        var paket = _sunucuAuth.ExportForSync(Co, "admin", Parola)!;
        _yerelAuth.ImportRemoteUser(paket);
        Assert.Equal(2, YerelKapsam(_yerel, _adminId).Count);

        _yerelAuth.ImportRemoteUser(paket with { ScopeBranchIds = null });

        Assert.Equal(2, YerelKapsam(_yerel, _adminId).Count);
    }

    /// <summary>D7 — Şube henüz yerelde yoksa kapsam satırı FK hatası vermeden atlanır;
    /// şube aynalandıktan sonraki içe aktarımda yazılır (masaüstü ilk kurulum senaryosu).</summary>
    [Fact]
    public void D7_Sube_Yerelde_Yoksa_Patlamaz_Sonra_Yazilir()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA });
        var paket = _sunucuAuth.ExportForSync(Co, "admin", Parola)!;

        _yerelAuth.ImportRemoteUser(paket);              // şubeler henüz aynalanmadı
        Assert.Empty(YerelKapsam(_yerel, _adminId));

        SubeleriYereleAynala();
        _yerelAuth.ImportRemoteUser(paket);              // aynalama sonrası

        Assert.Equal(new[] { _subeA }, YerelKapsam(_yerel, _adminId));
    }

    /// <summary>D8 — GİRİŞ SIRASI: kullanıcı → şubeler → kapsam. Şubeler kullanıcı paketinden SONRA
    /// indiği için (firma satırı olmadan aynalanamaz) kapsam ayrı bir adımda yazılabilmelidir.
    /// Gerçek GUI testinde bulundu: tek adımda yapıldığında ilk kurulumda kapsam SESSİZCE düşüyordu.</summary>
    [Fact]
    public void D8_Giris_Sirasi_Kullanici_Sube_Kapsam()
    {
        _sunucuPerms.SaveBranchScope(_super, _adminId, new[] { _subeA, _subeB });
        var paket = _sunucuAuth.ExportForSync(Co, "admin", Parola)!;

        _yerelAuth.ImportRemoteUser(paket);      // 1) kullanıcı + firma (şube yok → kapsam düşer)
        Assert.Empty(YerelKapsam(_yerel, _adminId));
        SubeleriYereleAynala();                  // 2) şubeler
        _yerelAuth.ImportUserScopes(paket);      // 3) kapsam

        Assert.Equal(new[] { _subeA, _subeB }.OrderBy(x => x), YerelKapsam(_yerel, _adminId).OrderBy(x => x));
        var oturum = _yerelAuth.Login(Co, "admin", Parola).Session!;
        Assert.False(BranchAccess.CanAccess(oturum, _subeC));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _sunucuDb, _yerelDb })
            try { File.Delete(p); } catch { }
    }
}
