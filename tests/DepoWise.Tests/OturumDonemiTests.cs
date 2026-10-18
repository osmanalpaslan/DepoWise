using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
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
/// ═══ M-10 (AR-GE 2026-10-18, 05 F-066) — ŞİFRE DEĞİŞİNCE ESKİ JETONLAR DÜŞER ═══
///  OTD1 — Yönetici şifreyi SIFIRLAYINCA kullanıcının eski jetonu 401 (yenileme dahil); yöneticinin kendi jetonu etkilenmez
///  OTD2 — Yönetici şifreyi DEĞİŞTİRİNCE eski jeton 401; yeni şifreyle alınan jeton çalışır
///  OTD3 — İlk giriş şifre belirleme: dönen YENİ jeton çalışır, adım-1 jetonu artık 401
///  OTD4 — Dönem bilgisi taşımayan (eski sürüm) jeton şifre değişene kadar geçerli; değişince 401
///  OTD5 — Başka kullanıcının şifresi değişince diğer kullanıcıların jetonları etkilenmez; jeton dönemi taşır
/// </summary>
[Collection("PostgresSchema")]
public class OturumDonemiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "OTD-A", Admin = "otd_admin", Pass = "Test!2026";
    private const string JwtKey = "api-test-jwt-key-0123456789-0123456789";   // ApiTestHost'un verdiği anahtar
    private HttpClient _admin = null!;
    private SessionContext _sa = null!;
    private string _sube = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        using (var conn = Svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var aid = Svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _sa = new SessionContext(aid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = Svc.Branches.Create(_sa, new NewBranch("OTD ŞUBE"));
        _admin = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private string Personel(string ad) =>
        Svc.Users.CreateUser(_sa, new NewUser(ad, Pass, ad, new[] { RoleKeys.Staff }, Company, BranchId: _sube));

    private Task<HttpClient> Giris(string ad, string sifre) => _host.LoginAsync(ad, sifre, Company, _sube);

    private static async Task<HttpStatusCode> Yenile(HttpClient c) => (await c.PostAsync("/api/auth/refresh", null)).StatusCode;
    private static async Task<HttpStatusCode> Ufuk(HttpClient c) => (await c.GetAsync("/api/sync/business-version-v2")).StatusCode;

    private static long JetonDonemi(HttpClient c)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(c.DefaultRequestHeaders.Authorization!.Parameter);
        return long.Parse(jwt.Claims.Single(x => x.Type == JwtTokens.DonemClaim).Value);
    }

    [Fact]
    public async Task OTD1_Sifirlama_Eski_Jetonu_Dusurur()
    {
        var uid = Personel("otd_p1");
        var p = await Giris("otd_p1", Pass);
        Assert.Equal(HttpStatusCode.OK, await Ufuk(p));
        Assert.Equal(0, JetonDonemi(p));

        var r = await _admin.PostAsync($"/api/users/{uid}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await Ufuk(p));        // çalınmış olabilecek jeton artık işe yaramaz
        Assert.Equal(HttpStatusCode.Unauthorized, await Yenile(p));      // yenilemeyle de canlandırılamaz
        Assert.Equal(HttpStatusCode.OK, await Ufuk(_admin));             // işlemi yapan yönetici etkilenmez
        Assert.Equal(HttpStatusCode.OK, await Yenile(_admin));
    }

    [Fact]
    public async Task OTD2_Yonetici_Degistirince_Eski_Jeton_Duser_Yeni_Calisir()
    {
        var uid = Personel("otd_p2");
        var p = await Giris("otd_p2", Pass);
        Assert.Equal(HttpStatusCode.OK, await Ufuk(p));

        var r = await _admin.PostAsJsonAsync($"/api/users/{uid}/password", new { password = "Yeni!2026" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await Ufuk(p));
        Assert.Equal(HttpStatusCode.Unauthorized, await Yenile(p));

        // Yeni şifreyle giriş (zorunlu değiştirme açık olsa da adım-1 jetonu verilir) → yeni dönemle, çalışır
        var c = _host.CreateClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { companyId = Company, username = "otd_p2", password = "Yeni!2026", branchId = _sube });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var token = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(1, JetonDonemi(c));
        Assert.Equal(HttpStatusCode.OK, await Yenile(c));
    }

    [Fact]
    public async Task OTD3_Ilk_Giris_Sifre_Belirleme_Yeni_Jeton_Verir()
    {
        var uid = Personel("otd_p3");
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"/api/users/{uid}/reset-password", null)).StatusCode);

        // Sıfırlanan kullanıcı geçici şifreyle (= kullanıcı adı) girer → adım-1 jetonu
        var adim1 = await Giris("otd_p3", "otd_p3");
        var r = await adim1.PostAsJsonAsync("/api/auth/change-initial-password", new { newPassword = "Kendi!2026" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var yeni = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();

        Assert.Equal(HttpStatusCode.Unauthorized, await Yenile(adim1));  // adım-1 jetonu geçersiz
        var c = _host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", yeni);
        Assert.Equal(2, JetonDonemi(c));                                   // sıfırlama + kendi belirleme = 2 değişim
        Assert.Equal(HttpStatusCode.OK, await Yenile(c));                  // kullanıcı oturumdan düşmeden devam eder
        Assert.Equal(HttpStatusCode.OK, (await (await Giris("otd_p3", "Kendi!2026")).PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task OTD4_Donemsiz_Eski_Jeton_Degisene_Kadar_Gecerli()
    {
        var uid = Personel("otd_p4");
        // Eski sürüm sunucunun ürettiği jeton (M-10 öncesi JwtTokens.Issue ile aynı): "te" alanı yok → dönem 0 sayılır
        var anahtar = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(JwtKey));
        var imza = new Microsoft.IdentityModel.Tokens.SigningCredentials(anahtar, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var donemsiz = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: new[] { new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, uid), new System.Security.Claims.Claim(JwtTokens.CompanyClaim, Company) },
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: imza));
        Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(donemsiz).Claims, x => x.Type == JwtTokens.DonemClaim);

        var c = _host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", donemsiz);
        Assert.Equal(HttpStatusCode.OK, await Ufuk(c));                    // yayından önce alınmış jeton çalışmaya devam eder

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"/api/users/{uid}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await Ufuk(c));
    }

    [Fact]
    public async Task OTD5_Baska_Kullanicinin_Degisimi_Etkilemez()
    {
        var u1 = Personel("otd_p5a");
        Personel("otd_p5b");
        var a = await Giris("otd_p5a", Pass);
        var b = await Giris("otd_p5b", Pass);

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"/api/users/{u1}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await Ufuk(a));
        Assert.Equal(HttpStatusCode.OK, await Ufuk(b));
        Assert.Equal(HttpStatusCode.OK, await Yenile(b));

        // Veritabanında dönem yalnız sıfırlanan kullanıcıda arttı
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT username, token_epoch FROM users WHERE username IN ('otd_p5a','otd_p5b') ORDER BY username;";
        using var rd = cmd.ExecuteReader();
        var d = new Dictionary<string, long>();
        while (rd.Read()) d[rd.GetString(0)] = Convert.ToInt64(rd.GetValue(1));
        Assert.Equal(1, d["otd_p5a"]);
        Assert.Equal(0, d["otd_p5b"]);
    }
}
