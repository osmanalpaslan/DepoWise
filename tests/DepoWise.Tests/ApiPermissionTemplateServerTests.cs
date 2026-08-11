using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-01 (PRT-01 Grup 6, 2026-08-11) — YETKİ ŞABLONLARI SUNUCU-OTORİTELİ OLDU.
///
/// Bulunan durum: masaüstü şablonları YEREL SQLite'a yazıyordu ve <c>permission_templates</c> iş senkron
/// kataloğunda yok. Sonuç: masaüstünde oluşturulan şablon web'de/başka makinede hiç görünmüyordu ve
/// şablonla oluşturulan kullanıcının yetkileri sunucuya HİÇ ulaşmıyordu (ekran yine "uygulandı" diyordu).
///
/// Masaüstü artık kullanıcı/yetkideki kanıtlanmış deseni izleyip aynı API uçlarına gidiyor. Bu testler
/// masaüstünün DAYANDIĞI sözleşmeyi kilitler: uçların varlığı, süper admin sınırı ve tenant izolasyonu.
/// Masaüstü ViewModel'leri test projesinden erişilebilir değildir (test projesi DepoWise.Desktop'a
/// referans vermez) → masaüstü tarafı yalnız KOD DÜZEYİNDE doğrulanmıştır, GUI ile değil.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiPermissionTemplateServerTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "SBL-A";
    private const string AdminA = "sbl_a";
    private const string Pass = "Test!2026";

    private HttpClient _super = null!, _adminA = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        svc.Users.EnsureInitialAdmin(CompanyA, AdminA, Pass, RoleKeys.CompanyAdmin);
        _super = await _host.LoginSeedAsync();
        _adminA = await _host.LoginAsync(AdminA, Pass, CompanyA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static object Body(string name, string? roleKey = null, string? companyId = null, bool scopeAll = true) => new
    {
        name, roleKey,
        modules = new[] { new { moduleKey = "fuel", canView = true, canCreate = false, canEdit = false, canDelete = false } },
        buttons = Array.Empty<string>(),
        companyId, scopeAll,
    };

    [Fact]
    public async Task Sablon_Olusturulur_Listelenir_Ve_Silinir()
    {
        // Masaüstünün kullandığı üç uç: POST → GET (yönetim listesi) → DELETE.
        var create = await _super.PostAsJsonAsync("/api/permission-templates", Body("Sunucu Şablonu"));
        create.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(create)).GetProperty("id").GetString()!;

        var listed = (await ApiTestHost.JsonAsync(await _super.GetAsync("/api/permission-templates"))).ToString();
        Assert.Contains("Sunucu Şablonu", listed);

        (await _super.DeleteAsync($"/api/permission-templates/{id}")).EnsureSuccessStatusCode();

        var after = (await ApiTestHost.JsonAsync(await _super.GetAsync("/api/permission-templates"))).ToString();
        Assert.DoesNotContain("Sunucu Şablonu", after);
    }

    [Fact]
    public async Task Sablon_Icerigi_Ucta_Tam_Doner()
    {
        // Masaüstü şablonu uygularken modülleri BU uçtan okur; alanlar eksik dönerse yetki yanlış yazılır.
        var create = await _super.PostAsJsonAsync("/api/permission-templates", Body("İçerik", RoleKeys.Staff));
        var id = (await ApiTestHost.JsonAsync(create)).GetProperty("id").GetString()!;

        var data = await ApiTestHost.JsonAsync(await _super.GetAsync($"/api/permission-templates/{id}"));

        Assert.Equal(RoleKeys.Staff, data.GetProperty("roleKey").GetString());
        var mod = data.GetProperty("modules").EnumerateArray().Single();
        Assert.Equal("fuel", mod.GetProperty("moduleKey").GetString());
        Assert.True(mod.GetProperty("canView").GetBoolean());
        Assert.False(mod.GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task Sablon_Olusturma_Ve_Yonetim_Listesi_YALNIZ_Super_Admin()
    {
        // Masaüstü artık sunucuya gittiği için bu sınır GERÇEKTEN uygulanır (yerelde de uygulanıyordu,
        // ama yerel kopya kimseye görünmediği için etkisi yoktu).
        var create = await _adminA.PostAsJsonAsync("/api/permission-templates", Body("Olmaz"));
        var list = await _adminA.GetAsync("/api/permission-templates");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Sablon_Silme_YALNIZ_Super_Admin()
    {
        var create = await _super.PostAsJsonAsync("/api/permission-templates", Body("Korunan"));
        var id = (await ApiTestHost.JsonAsync(create)).GetProperty("id").GetString()!;

        var del = await _adminA.DeleteAsync($"/api/permission-templates/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
        Assert.Contains("Korunan",
            (await ApiTestHost.JsonAsync(await _super.GetAsync("/api/permission-templates"))).ToString());
    }
}
