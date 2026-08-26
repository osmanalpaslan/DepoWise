using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TENANT SÜPÜRMESİ · <b>YAZMA</b> TARAFI ═══ (denetim 2026-08-26, ikinci tur)
///
/// <b>Neden bu sınıf var:</b> <see cref="ApiTenantSweepTests"/> aynı saldırıyı yalnız <b>okuma</b>
/// uçlarında ölçüyordu ("B firmasının verisi döndü mü?"). Yazma tarafı ise farklı bir soru sorar:
/// <b>"B firmasının veritabanı satırı DEĞİŞTİ mi?"</b> — bir uç 200 dönüp sessizce yazmış olabilir,
/// ya da 403 dönüp yine de yazmış olabilir. Bu yüzden her testte HTTP sonucuna DEĞİL, doğrudan
/// veritabanına bakılır.
///
/// Saldırgan: <b>A firmasının ADMİNİ</b> (süper admin değil — süper adminin çapraz-firma yetkisi meşrudur).
/// Hedef: B firmasının kimliği, şubesi ve makinesi.
///
/// Beklenen: <b>B'de hiçbir değişiklik olmamalı.</b> Uç ister 403 dönsün ister işlemi kendi firmasına
/// uygulasın — kabul edilebilir olan tek şey B'nin dokunulmamış kalmasıdır.
/// </summary>
[Collection("PostgresSchema")]
public class ApiYazmaTenantSweepTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "WSWP-A";
    private const string CoB = "WSWP-B";
    private const string Pass = "Sweep!2026";

    private ServerServices _svc = null!;
    private HttpClient _adminA = null!;
    private string _subeB = "";
    private string _makineB = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
        {
            using var conn = _svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                "VALUES(@c,@n,1,1,1,0,7,20,5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@c", id);
            cmd.AddWithValue("@n", ad);
            cmd.ExecuteNonQuery();
        }

        // B firmasının şubesi + makinesi (hedefler)
        _subeB = Guid.NewGuid().ToString("N");
        using (var conn = _svc.Factory.Create())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO branches(id,company_id,parent_id,name,kind,created_at,updated_at,version,is_deleted) " +
                    "VALUES(@id,@c,NULL,'B-SUBE','branch',1,1,1,0);";
                cmd.AddWithValue("@id", _subeB);
                cmd.AddWithValue("@c", CoB);
                cmd.ExecuteNonQuery();
            }
            _makineB = Guid.NewGuid().ToString("N");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO sync_devices(id,company_id,device_name,status,branch_id,created_at,updated_at,version) " +
                    "VALUES(@id,@c,'B-MAKINE','active',@b,1,1,1);";
                cmd.AddWithValue("@id", _makineB);
                cmd.AddWithValue("@c", CoB);
                cmd.AddWithValue("@b", _subeB);
                cmd.ExecuteNonQuery();
            }
        }

        _svc.Users.EnsureInitialAdmin(CoA, "sweep_admin_a", Pass, RoleKeys.CompanyAdmin);
        _adminA = await _host.LoginAsync("sweep_admin_a", Pass, CoA);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    // ── veritabanı gözlemleri ──────────────────────────────────────────────────────────────────

    private long Sayi(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private string? Metin(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        return cmd.ExecuteScalar() as string;
    }

    // ── 1) Şube oluşturma ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task W1_Baska_Firmaya_Sube_Acilamaz()
    {
        await _adminA.PostAsJsonAsync("/api/branches",
            new { name = "SIZAN-SUBE", companyId = CoB });

        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM branches WHERE company_id=@c AND name='SIZAN-SUBE';",
            ("@c", CoB)));
    }

    // ── 2) Makine kotası (süper admin işlemi) ──────────────────────────────────────────────────

    [Fact]
    public async Task W2_Baska_Firmanin_Makine_Kotasi_Degistirilemez()
    {
        await _adminA.PostAsJsonAsync($"/api/companies/{CoB}/machine-quota", new { quota = 999 });

        Assert.Equal(7, Sayi("SELECT machine_quota FROM companies WHERE id=@c;", ("@c", CoB)));
    }

    /// <summary>Kendi firmasının kotası da firma adminine kapalı olmalı (yalnız süper admin).</summary>
    [Fact]
    public async Task W2b_Kendi_Firmasinin_Kotasini_Da_Degistiremez()
    {
        await _adminA.PostAsJsonAsync($"/api/companies/{CoA}/machine-quota", new { quota = 999 });

        Assert.Equal(7, Sayi("SELECT machine_quota FROM companies WHERE id=@c;", ("@c", CoA)));
    }

    // ── 3) Makine yönetimi ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task W3_Baska_Firmanin_Makinesi_Pasife_Alinamaz()
    {
        await _adminA.PostAsJsonAsync($"/api/machines/{_makineB}/revoke", new { });

        Assert.Equal("active", Metin("SELECT status FROM sync_devices WHERE id=@i;", ("@i", _makineB)));
    }

    [Fact]
    public async Task W4_Baska_Firmanin_Makinesi_Silinemez()
    {
        await _adminA.DeleteAsync($"/api/machines/{_makineB}");

        Assert.Equal(1, Sayi("SELECT COUNT(*) FROM sync_devices WHERE id=@i;", ("@i", _makineB)));
    }

    [Fact]
    public async Task W5_Baska_Firmanin_Makinesinin_Subesi_Degistirilemez()
    {
        await _adminA.PostAsJsonAsync($"/api/machines/{_makineB}/branch", new { branchId = (string?)null });

        Assert.Equal(_subeB, Metin("SELECT branch_id FROM sync_devices WHERE id=@i;", ("@i", _makineB)));
    }

    [Fact]
    public async Task W6_Baska_Firmanin_Makinesinin_Firmasi_Degistirilemez()
    {
        await _adminA.PostAsJsonAsync($"/api/machines/{_makineB}/company", new { companyId = CoA });

        Assert.Equal(CoB, Metin("SELECT company_id FROM sync_devices WHERE id=@i;", ("@i", _makineB)));
    }

    // ── 4) Yerel sıfırlama isteği (ADR-084) ────────────────────────────────────────────────────

    [Fact]
    public async Task W7_Baska_Firma_Icin_Yerel_Sifirlama_Istenemez()
    {
        var r = await _adminA.PostAsJsonAsync("/api/admin/company-local-reset", new { companyId = CoB });

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    // ── 5) İş kayıtları BAŞKA firmanın şubesiyle ───────────────────────────────────────────────

    [Fact]
    public async Task W8_Arac_Baska_Firmanin_Subesine_Yazilamaz()
    {
        await _adminA.PostAsJsonAsync("/api/vehicles", new
        {
            internalCode = "SIZAN-ARAC", plate = "34SIZ01", productionYear = 2020,
            currentMeter = 0, meterUnit = "km", branchId = _subeB,
        });

        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM vehicles WHERE branch_id=@b;", ("@b", _subeB)));
        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM vehicles WHERE company_id=@c;", ("@c", CoB)));
    }

    [Fact]
    public async Task W9_Personel_Baska_Firmanin_Subesine_Yazilamaz()
    {
        await _adminA.PostAsJsonAsync("/api/personnel", new
        {
            fullName = "Sizan Personel", title = "Test", phone = "0555", branchId = _subeB, isActive = true,
        });

        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM personnel WHERE branch_id=@b;", ("@b", _subeB)));
        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM personnel WHERE company_id=@c;", ("@c", CoB)));
    }

    // ── 6) Yetki kontrol matrisleri (yalnız süper admin) ───────────────────────────────────────

    [Fact]
    public async Task W10_Baska_Firmanin_Yetki_Kontrolu_Yazilamaz()
    {
        var r = await _adminA.PostAsJsonAsync($"/api/company-permissions/{CoB}",
            new { levels = new Dictionary<string, string> { ["stock"] = "none" } });

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    [Fact]
    public async Task W11_Baska_Firmanin_Rol_Matrisi_Yazilamaz()
    {
        var r = await _adminA.PostAsJsonAsync("/api/role-permissions", new
        {
            companyId = CoB,
            blocked = new Dictionary<string, List<string>> { ["stock"] = new() { RoleKeys.CompanyAdmin } },
        });

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    // ── 7) Anonim yazma ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task W12_Anonim_Hicbir_Yazma_Yapamaz()
    {
        var anon = _host.Anonymous();
        foreach (var yol in new[] { "/api/branches", "/api/vehicles", "/api/personnel" })
        {
            var r = await anon.PostAsJsonAsync(yol, new { name = "X", fullName = "X", internalCode = "X", companyId = CoB });
            Assert.True(ApiTestHost.IsDenied(r), $"{yol} → {(int)r.StatusCode}");
        }
        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM branches WHERE company_id=@c AND name='X';", ("@c", CoB)));
    }
}
