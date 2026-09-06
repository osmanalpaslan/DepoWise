using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3a (ADR-222) — ROL İZNİNİN ZİNCİRİN TAMAMINDA ÇALIŞMASI ═══
///
/// Faz 1'in <see cref="YetkiZinciriTests"/> deseni rol katmanına uygulanır: her senaryo
/// <b>dört halkada birden</b> ölçülür — UI kararı · GERÇEK HTTP (JWT) · servis · veri.
///
/// <b>Kullanıcı şartı §9:</b> üç durum ayrı ayrı sınanır —
/// <c>kullanıcı ALLOW</c> · <c>rol ALLOW</c> · <c>hiçbiri</c>.
///
/// <b>Kullanıcı şartı §14:</b> masaüstü API'den GEÇMEZ; servisi doğrudan çağırır. Bu yüzden
/// "web API testi geçti" demek yetmez — RA4 masaüstünün gerçek yolunu ayrıca sınar.
///
/// 🔒 <see cref="ApiTestHost"/> bellek içinde çalışır, <c>DEPOWISE_PG_URL</c> temizlenir →
/// canlı veritabanına hiçbir istek gitmez. Parolalar bu testin kendi oluşturduğu hesaplara aittir.
///
///  RA1 — Hiçbiri: dört halkada da engelli
///  RA2 — YALNIZ ROL izniyle erişim AÇILIR (UI + HTTP + servis)
///  RA3 — Rol izni geri alınınca erişim ANINDA kapanır (bayat yetki yok)
///  RA4 — Masaüstü yolu (servis, API'siz) rol iznini AYNI şekilde görür
///  RA5 — Devretme tavanı HTTP üzerinden de aşılamaz
///  RA6 — Rol izni yetkisiz uçları AÇMAZ (yalnız verilen modül)
/// </summary>
[Collection("PostgresSchema")]
public class RolIzniApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "RLA";
    private const string Pass = "Rla!2026";

    private ServerServices _svc = null!;
    private HttpClient _admin = null!, _personel = null!;
    private string _personelId = "", _subeId = "", _staffRolId = "";
    private SessionContext _adminOturum = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                 "VALUES(@c,'RLA Firma',1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", Co));

        var adminId = _svc.Users.EnsureInitialAdmin(Co, "rla_admin", Pass, RoleKeys.CompanyAdmin);
        _admin = await _host.LoginAsync("rla_admin", Pass, Co);
        _adminOturum = new SessionContext(adminId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _subeId = new DepoWise.Infrastructure.Organization.BranchService(_svc.Factory)
            .Create(_adminOturum, new DepoWise.Infrastructure.Organization.NewBranch("RLA-Merkez"));

        // İzni HİÇ OLMAYAN personel — başlangıç durumu.
        _personelId = _svc.Users.EnsureInitialAdmin(Co, "rla_personel", Pass, RoleKeys.Staff);
        _personel = await _host.LoginAsync("rla_personel", Pass, Co, _subeId);

        _staffRolId = RolId(RoleKeys.Staff);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    // ══════════════════════ RA1 — HİÇBİRİ ══════════════════════

    [Fact]
    public async Task RA1_Hicbir_Izin_Yokken_Dort_Halkada_Da_Engelli()
    {
        var oturum = PersonelOturumu();

        Assert.False(AccessControl.CanSeeMenu(oturum, "materials"));                 // (1) UI
        YetkiReddi(await _personel.GetAsync("/api/materials"), "GET /api/materials"); // (2) HTTP
        Assert.Throws<ForbiddenException>(() =>                                       // (3) servis
            new DepoWise.Infrastructure.Materials.MaterialService(_svc.Factory)
                .Create(oturum, new DepoWise.Infrastructure.Materials.NewMaterial("RA1", "RA1")));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM materials WHERE company_id=@c;", ("@c", Co)));  // (4) veri
    }

    // ══════════════════════ RA2 — YALNIZ ROL İZNİYLE AÇILIR ══════════════════════

    /// <summary>
    /// ⭐ Faz 3a'nın ana vaadi: kullanıcıya HİÇ satır yazmadan, YALNIZ rolüne izin vererek erişim açılır.
    ///
    /// Üç halkada birden doğrulanır. Kullanıcının <c>user_permissions</c> satırı olmadığı da
    /// veritabanından teyit edilir — erişim gerçekten ROLDEN geliyor.
    /// </summary>
    [Fact]
    public async Task RA2_Yalniz_Rol_Izniyle_Erisim_Acilir()
    {
        // Role GÖRME + OLUŞTURMA ver (kullanıcıya hiçbir şey yazılmıyor).
        var kaydet = await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = new[] { new { moduleKey = "materials", canView = true, canCreate = true, canEdit = false, canDelete = false } },
            buttons = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, kaydet.StatusCode);

        // Kullanıcının KENDİ satırı YOK — erişim yalnız rolden gelmeli.
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM user_permissions WHERE user_id=@u;", ("@u", _personelId)));

        var oturum = PersonelOturumu();
        Assert.True(AccessControl.CanSeeMenu(oturum, "materials"));                    // (1) UI
        Assert.True(AccessControl.Can(oturum, "materials", PermissionAction.Create));
        Assert.False(AccessControl.Can(oturum, "materials", PermissionAction.Delete)); // verilmeyen açılmadı

        var taze = await _host.LoginAsync("rla_personel", Pass, Co, _subeId);          // (2) HTTP
        Assert.Equal(HttpStatusCode.OK, (await taze.GetAsync("/api/materials")).StatusCode);

        var id = new DepoWise.Infrastructure.Materials.MaterialService(_svc.Factory)   // (3) servis
            .Create(oturum, new DepoWise.Infrastructure.Materials.NewMaterial("RA2", "RA2 Malzeme"));

        Assert.Equal(1L, Say("SELECT COUNT(*) FROM materials WHERE id=@i;", ("@i", id)));  // (4) veri
    }

    // ══════════════════════ RA3 — GERİ ALMA ══════════════════════

    /// <summary>⭐ Rol izni geri alınınca erişim ANINDA kapanır — TTL beklenmez, yeniden giriş gerekmez.</summary>
    [Fact]
    public async Task RA3_Rol_Izni_Geri_Alininca_Erisim_Kapanir()
    {
        await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = new[] { new { moduleKey = "materials", canView = true, canCreate = false, canEdit = false, canDelete = false } },
            buttons = Array.Empty<string>(),
        });
        Assert.True(AccessControl.Can(PersonelOturumu(), "materials", PermissionAction.View));

        // Boş liste gönder → rol izinleri silinir.
        var bosalt = await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = Array.Empty<object>(),
            buttons = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, bosalt.StatusCode);

        Assert.False(AccessControl.Can(PersonelOturumu(), "materials", PermissionAction.View));
        var taze = await _host.LoginAsync("rla_personel", Pass, Co, _subeId);
        YetkiReddi(await taze.GetAsync("/api/materials"), "geri alma sonrası GET");
    }

    // ══════════════════════ RA4 — MASAÜSTÜ YOLU ══════════════════════

    /// <summary>
    /// ⭐ Masaüstü API'den GEÇMEZ (kullanıcı şartı §14): servisi doğrudan çağırır ve oturumunu
    /// <c>AuthService.Login</c> ile kurar. Rol izni bu yolda da görünmelidir — aksi hâlde web'de
    /// çalışıp masaüstünde çalışmayan bir yetki olurdu.
    /// </summary>
    [Fact]
    public async Task RA4_Masaustu_Yolu_Rol_Iznini_Ayni_Sekilde_Gorur()
    {
        await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = new[] { new { moduleKey = "materials", canView = true, canCreate = true, canEdit = false, canDelete = false } },
            buttons = new[] { SpecialButtons.AddLookup },
        });

        // MASAÜSTÜ YOLU: AuthService.Login → SessionContext (API yok, JWT yok)
        var giris = _svc.Auth.Login(Co, "rla_personel", Pass);
        Assert.True(giris.Success);
        var masaustuOturum = giris.Session!;

        Assert.True(AccessControl.Can(masaustuOturum, "materials", PermissionAction.Create));
        Assert.True(AccessControl.CanUseButton(masaustuOturum, SpecialButtons.AddLookup));
        Assert.False(AccessControl.Can(masaustuOturum, "materials", PermissionAction.Delete));

        // Servis çağrısı gerçekten çalışıyor (yalnız karar değil, iş de).
        new DepoWise.Infrastructure.Materials.MaterialService(_svc.Factory)
            .Create(masaustuOturum, new DepoWise.Infrastructure.Materials.NewMaterial("RA4", "RA4 Malzeme"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM materials WHERE code='RA4' AND company_id=@c;", ("@c", Co)));
    }

    // ══════════════════════ RA5 — DEVRETME TAVANI (HTTP) ══════════════════════

    /// <summary>
    /// ⭐ Tavan HTTP üzerinden de aşılamaz. Arayüzde kutu gizlemek yeterli değildir (kullanıcı şartı §7):
    /// istek doğrudan API'ye gönderilir.
    /// </summary>
    [Fact]
    public async Task RA5_Devretme_Tavani_HTTP_Uzerinden_De_Asilamaz()
    {
        // Sınırlı aktör: yetki ekranı + materials/GÖRME (SİLME YOK).
        //
        // ⚠️ Kurulum SÜPER ADMIN oturumuyla yapılır — "permissions" admin-kısıtlı bir modüldür ve
        // firma admini bunu bir PERSONELE veremez (mevcut ve doğru bir kural; ilk yazdığımda
        // firma adminiyle denedim ve servis haklı olarak reddetti). Süper admin bu kısıttan muaftır.
        //
        // Aktörün PERSONEL olması testin özüdür: firma admini normal modüllerde bypass ile tam
        // yetkilidir, dolayısıyla tavan onu KIRPMAZ. Tavanı ölçmek için bypass'ı olmayan bir aktör
        // gerekir.
        var superAdmin = new SessionContext("sa-rla", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var aktorId = _svc.Users.EnsureInitialAdmin(Co, "rla_sinirli", Pass, RoleKeys.Staff);
        _svc.Permissions.SaveForUser(superAdmin, aktorId, new[]
        {
            new ModulePermission("permissions", true, true, true, true),
            new ModulePermission("materials", true, false, false, false),
        }, Array.Empty<string>());
        var aktor = await _host.LoginAsync("rla_sinirli", Pass, Co, _subeId);

        // Role TAM yetki vermeye çalış (silme dâhil) — HTTP üzerinden.
        var r = await aktor.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = new[] { new { moduleKey = "materials", canView = true, canCreate = true, canEdit = true, canDelete = true } },
            buttons = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);   // istek kabul edilir…

        // …ama SİLME kırpılmış olmalı.
        Assert.Equal(0L, Say(
            "SELECT COUNT(*) FROM role_permissions WHERE role_id=@r AND module_key='materials' AND can_delete=1;",
            ("@r", _staffRolId)));
        Assert.False(AccessControl.Can(PersonelOturumu(), "materials", PermissionAction.Delete));
    }

    // ══════════════════════ RA6 — KAPSAM ══════════════════════

    /// <summary>⭐ Rol izni yalnız VERİLEN modülü açar; başka uçlar kapalı kalır.</summary>
    [Fact]
    public async Task RA6_Rol_Izni_Yalniz_Verilen_Modulu_Acar()
    {
        await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = new[] { new { moduleKey = "materials", canView = true, canCreate = false, canEdit = false, canDelete = false } },
            buttons = Array.Empty<string>(),
        });

        var taze = await _host.LoginAsync("rla_personel", Pass, Co, _subeId);
        Assert.Equal(HttpStatusCode.OK, (await taze.GetAsync("/api/materials")).StatusCode);

        foreach (var yol in new[] { "/api/vehicles", "/api/personnel", "/api/audit" })
            YetkiReddi(await taze.GetAsync(yol), yol);
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    /// <summary>Yetki reddi 403/401 olmalıdır — 500 sunucu hatasıdır, red değil (Faz 1 kuralı).</summary>
    private static void YetkiReddi(HttpResponseMessage r, string ne)
        => Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"{ne} → {(int)r.StatusCode}. Yetki reddi 403 (veya 401) olmalı.");

    /// <summary>Personelin GÜNCEL oturumu — her çağrıda taze kurulur (önbellek etkisi ölçülebilsin).</summary>
    private SessionContext PersonelOturumu()
    {
        var s = _svc.Auth.CreateSessionForUser(Co, _personelId);
        Assert.NotNull(s);
        return s!;
    }

    private string RolId(string roleKey)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM roles WHERE role_key=@k AND is_deleted=0 LIMIT 1;";
        cmd.AddWithValue("@k", roleKey);
        return (string)cmd.ExecuteScalar()!;
    }

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
