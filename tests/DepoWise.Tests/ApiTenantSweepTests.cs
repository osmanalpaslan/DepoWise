using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TENANT SÜPÜRMESİ · "İSTEĞE BAŞKA FİRMANIN KİMLİĞİNİ YAZ" ═══ (denetim 2026-08-26)
///
/// <b>Neden bu test var:</b> SEC-04'te (<c>/api/backups</c>) bulunan hata tek bir ucun hatası değil, bir
/// KALIPTI: "istek gövdesinden/adresinden gelen <c>companyId</c>'yi doğrulamadan kullanmak". Aynı kalıbın
/// başka uçlarda tekrar edip etmediğini tek tek okuyarak değil, <b>gerçek HTTP istekleriyle</b> ölçmek gerekir.
///
/// Bu sınıf, firma kimliği kabul eden TÜM uçlara A firmasının admini olarak B firmasının kimliğini yazar
/// ve iki sonuçtan birini bekler: <b>reddedilme</b> ya da <b>kendi firmasının verisi</b>. Üçüncü bir sonuç
/// (B firmasının verisi) kabul edilemez.
/// </summary>
[Collection("PostgresSchema")]
public class ApiTenantSweepTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "SWP-A";
    private const string CoB = "SWP-B";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _adminA = null!, _depoA = null!;
    private string _subeA = "", _subeB = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        void Firma(string id, string ad)
        {
            using var conn = _svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @ad, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@ad", ad);
            cmd.ExecuteNonQuery();
        }

        Firma(CoA, "A Firması");
        Firma(CoB, "B Firması");

        var aId = _svc.Users.EnsureInitialAdmin(CoA, "swp_admin_a", Pass, RoleKeys.CompanyAdmin);
        var bId = _svc.Users.EnsureInitialAdmin(CoB, "swp_admin_b", Pass, RoleKeys.CompanyAdmin);
        var sa = new SessionContext(aId, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(bId, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _subeA = _svc.Branches.Create(sa, new NewBranch("A-MERKEZ"));
        _subeB = _svc.Branches.Create(sb, new NewBranch("B-GIZLI-SUBE"));

        // B firmasında bir makine kaydı (makine listesi sızıntısı için).
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO sync_devices(id, company_id, device_name, status, created_at, updated_at, branch_id) " +
                "VALUES('SWP-DEV-B', @c, 'B-GIZLI-MAKINE', 'active', 1, 1, @b) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@c", CoB);
            cmd.AddWithValue("@b", _subeB);
            cmd.ExecuteNonQuery();
        }

        var depoId = _svc.Users.CreateUser(sa, new NewUser("swp_depo_a", Pass, "Depo A",
            new[] { RoleKeys.Staff }, CoA, BranchId: _subeA));
        _svc.Permissions.SaveForUser(sa, depoId,
            new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>());

        _adminA = await _host.LoginAsync("swp_admin_a", Pass, CoA);
        _depoA = await _host.LoginAsync("swp_depo_a", Pass, CoA, _subeA);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private static async Task<string> GovdeAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    /// <summary>Reddedilmediyse gövde B firmasının hiçbir izini TAŞIMAMALI.</summary>
    private static async Task RedVeyaKendiVerisi(HttpResponseMessage r, params string[] sizmamasiGerekenler)
    {
        if (ApiTestHost.IsDenied(r)) return;                    // reddedildi → kabul
        Assert.True(r.IsSuccessStatusCode, $"beklenmeyen durum: {(int)r.StatusCode}");
        var govde = await GovdeAsync(r);
        foreach (var iz in sizmamasiGerekenler)
            Assert.DoesNotContain(iz, govde, StringComparison.Ordinal);
    }

    // ── GET uçları: adreste companyId ─────────────────────────────────────────────────────────

    [Fact]
    public async Task T1_Subeler_Baska_Firma_Kimligiyle_Sizdirmaz()
        => await RedVeyaKendiVerisi(await _adminA.GetAsync($"/api/branches?companyId={CoB}"), "B-GIZLI-SUBE", CoB);

    [Fact]
    public async Task T2_Makineler_Baska_Firma_Kimligiyle_Sizdirmaz()
        => await RedVeyaKendiVerisi(await _adminA.GetAsync($"/api/machines?companyId={CoB}"), "B-GIZLI-MAKINE");

    [Fact]
    public async Task T3_Makineler_Baska_Firma_Subesiyle_Sizdirmaz()
        => await RedVeyaKendiVerisi(await _adminA.GetAsync($"/api/machines?branchId={_subeB}"), "B-GIZLI-MAKINE");

    /// <summary>SEC-04 regresyonu: yedek/makine listesi firma sınırını uygulamalı.</summary>
    [Fact]
    public async Task T4_Yedekler_Baska_Firma_Kimligiyle_Sizdirmaz()
        => await RedVeyaKendiVerisi(await _adminA.GetAsync($"/api/backups?companyId={CoB}"), "B-GIZLI-MAKINE");

    [Fact]
    public async Task T5_Rapor_Kapsami_Baska_Firma_Kimligiyle_Sizdirmaz()
        => await RedVeyaKendiVerisi(await _adminA.GetAsync($"/api/reports/scope?companyId={CoB}"), "B-GIZLI-SUBE");

    [Fact]
    public async Task T6_Rol_Yetki_Kontrolu_Firma_Adminine_Kapali()
    {
        var r = await _adminA.GetAsync($"/api/role-permissions?companyId={CoB}");
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    [Fact]
    public async Task T7_Firma_Yetki_Kontrolu_Firma_Adminine_Kapali()
    {
        var r = await _adminA.GetAsync($"/api/company-permissions/{CoB}");
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    // ── POST gövdesinde companyId ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task T8_Rapor_Govdesinde_Baska_Firma_Kimligi_Reddedilir()
    {
        var r = await _adminA.PostAsJsonAsync("/api/reports/stock-movements",
            new { fromDate = (long?)null, toDate = (long?)null, companyId = CoB });
        await RedVeyaKendiVerisi(r, "B-GIZLI-SUBE");
    }

    [Fact]
    public async Task T9_Rapor_Exportunda_Baska_Firma_Kimligi_Reddedilir()
    {
        var r = await _adminA.PostAsJsonAsync("/api/reports/stock-movements/export",
            new { fromDate = (long?)null, toDate = (long?)null, companyId = CoB });
        // Excel ikili olduğu için içerik değil, YALNIZ reddedilme/kendi firması ayrımı ölçülür.
        if (!ApiTestHost.IsDenied(r)) Assert.True(r.IsSuccessStatusCode, $"beklenmeyen: {(int)r.StatusCode}");
    }

    [Fact]
    public async Task T10_Kullanici_Olusturmada_Baska_Firma_Reddedilir()
    {
        var r = await _adminA.PostAsJsonAsync("/api/users", new
        {
            username = "swp_kacak",
            password = Pass,
            fullName = "Kaçak",
            roleKeys = new[] { RoleKeys.Staff },
            companyId = CoB,
            branchId = _subeB,
        });
        Assert.True(ApiTestHost.IsDenied(r) || r.StatusCode == HttpStatusCode.BadRequest,
            $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");

        // Gerçekten yazılmadığını doğrula (yalnız HTTP koduna güvenme).
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username='swp_kacak';";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ── Yetkisiz / anonim ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task T11_Depo_Personeli_Makine_Listesini_Goremez()
    {
        var r = await _depoA.GetAsync("/api/machines");
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    [Fact]
    public async Task T12_Anonim_Istek_Hicbir_Firma_Verisi_Alamaz()
    {
        var anon = _host.Anonymous();
        foreach (var yol in new[]
                 {
                     $"/api/branches?companyId={CoB}", $"/api/machines?companyId={CoB}",
                     $"/api/backups?companyId={CoB}", $"/api/reports/scope?companyId={CoB}",
                     "/api/personnel", "/api/vehicles", "/api/materials",
                 })
        {
            var r = await anon.GetAsync(yol);
            Assert.True(ApiTestHost.IsDenied(r), $"{yol} → beklenen: reddedilme, gelen: {(int)r.StatusCode}");
        }
    }

    /// <summary>
    /// KİLİT: süper admin firma seçebilmeye DEVAM etmeli — sıkılaştırma meşru işlevi bozmamalı.
    /// </summary>
    [Fact]
    public async Task T13_Super_Admin_Firma_Secebilir()
    {
        var super = await _host.LoginSeedAsync();
        var r = await super.GetAsync($"/api/branches?companyId={CoB}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("B-GIZLI-SUBE", await GovdeAsync(r), StringComparison.Ordinal);
    }
}
