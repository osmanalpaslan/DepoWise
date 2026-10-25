using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ŞUBE ŞİFRESİ (kullanıcı bildirimi 2026-10-25, P0) — ŞİFRESİZ / YANLIŞ ŞİFREYLE ŞUBEYE GİRİLEMEZ ═══
///  SS1 — firma kimliği GÖNDERMEYEN giriş (web akışı): varsayılan dışındaki firmanın şifreli şubesine şifresiz/yanlış şifreyle 401;
///        doğru şifreyle 200 (eskiden şifre varsayılan firmada aranıyor, şube bulunamayınca kapı açılıyordu)
///  SS2 — var olmayan şube "şifresiz" sayılmaz (doğrulama false)
///  SS3 — şifresiz şube serbest kalır (mevcut kural korunur)
///  SS4 — masaüstü: sunucu "doğrulayamadım" (null) derse şifre KABUL EDİLMEZ; internetsizken başka şubeye giriş engellenir
/// </summary>
[Collection("PostgresSchema")]
public class SubeSifresiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "SBS-A";
    private const string Pass = "Test!2026";
    private string _sifreli = "", _sifresiz = "";
    private ServerServices _svc = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, 'SBS Firma', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Co);
            cmd.ExecuteNonQuery();
        }
        var adminId = _svc.Users.EnsureInitialAdmin(Co, "sbs_admin", Pass, RoleKeys.CompanyAdmin);
        var sa = new SessionContext(adminId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sifreli = _svc.Branches.Create(sa, new NewBranch("SBS-SANTIYE", "site", Password: "Sube#123"));
        _sifresiz = _svc.Branches.Create(sa, new NewBranch("SBS-SIFRESIZ"));
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private Task<HttpResponseMessage> Giris(string branchId, string? sifre)
        => _host.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { companyId = (string?)null, username = "sbs_admin", password = Pass, branchId, branchPassword = sifre });

    [Fact]
    public async Task SS1_Firmasiz_Giriste_Sube_Sifresi_Zorunlu()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(_sifreli, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(_sifreli, "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(_sifreli, "Sube#123")).StatusCode);
    }

    [Fact]
    public void SS2_Olmayan_Sube_Sifresiz_Sayilmaz()
    {
        Assert.False(_svc.Branches.VerifyBranchPassword(Co, "yok-boyle-sube", null));
        Assert.False(_svc.Branches.VerifyBranchPassword("DEPOWISE", _sifreli, null));   // başka firmanın kimliğiyle
    }

    [Fact]
    public async Task SS3_Sifresiz_Sube_Serbest()
        => Assert.Equal(HttpStatusCode.OK, (await Giris(_sifresiz, null)).StatusCode);

    [Fact]
    public void SS4_Masaustu_Dogrulanamayan_Sifre_Kabul_Edilmez()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        var s = File.ReadAllText(Path.Combine(d!.FullName, "src/DepoWise.Desktop/ViewModels/LoginViewModel.cs"));
        Assert.Contains("if (ok != true) {", s);                                     // null (doğrulanamadı) → red
        Assert.DoesNotContain("if (ok == false) { Error = \"Şube şifresi hatalı.\"; return; }\n            }\n            bool isAllBranches", s.Replace("\r\n", "\n"));
        Assert.Contains("_subeListesiCevrimdisi = online is null;", s);
        var i = s.IndexOf("if (_subeListesiCevrimdisi)", StringComparison.Ordinal);
        Assert.True(i > 0 && s.IndexOf("return;", i, StringComparison.Ordinal) - i < 400, "internetsizken başka şube engellenmeli");
    }
}
