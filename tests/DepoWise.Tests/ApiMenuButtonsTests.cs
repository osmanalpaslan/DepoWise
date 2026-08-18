using System.Net;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G7 (2026-08-18) — <b>WEB'DE ÖZEL BUTON YETKİSİ HİÇ YOKTU.</b>
///
/// <c>/api/me/menu</c> yalnız <c>modules</c> döndürüyordu; web'deki <c>AuthState</c> içinde buton
/// desteği bulunmuyordu. Masaüstü 6 yerde <c>AccessControl.CanUseButton</c> kontrolü yaparken web
/// kullanıcının yetkisi olmayan butonu <b>gösteriyor</b>, kullanıcı tıklayıp hata alıyordu.
/// CLAUDE.md §5: *"menü, işlem, alan ve özel buton yetkisi UI ile API'da aynı uygulanır."*
///
/// ⚠️ Bu bir <b>güvenlik açığı değildi</b> — sunucu tarafı fail-closed'du (<c>RequireButton</c>:
/// ters kayıt, çöp kutusu geri yükleme, Excel dışa aktarma, şube seçimi). Düzeltme arayüzü sunucuyla
/// hizalar. Bu testler menü ucunun buton listesini <b>doğru ve fail-closed</b> döndürdüğünü kilitler.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiMenuButtonsTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "BTN-CO";
    private const string Super = "btn_super";
    private const string Staff = "btn_personel";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private string _staffId = "";
    private HttpClient _super = null!, _staff = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        var uid = _svc.Users.EnsureInitialAdmin(Co, Super, Pass, RoleKeys.SuperAdmin);
        _staffId = _svc.Users.EnsureInitialAdmin(Co, Staff, Pass, RoleKeys.Staff);
        var sa = new SessionContext(uid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var sube = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _super = await _host.LoginAsync(Super, Pass, Co, sube);
        _staff = await _host.LoginAsync(Staff, Pass, Co, sube);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static async Task<List<string>> ButtonsAsync(HttpClient c)
    {
        var resp = await c.GetAsync("/api/me/menu");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await ApiTestHost.JsonAsync(resp);
        var list = new List<string>();
        if (json.TryGetProperty("buttons", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var b in arr.EnumerateArray()) list.Add(b.GetString() ?? "");
        return list;
    }

    /// <summary>Menü ucu artık `buttons` alanını DÖNDÜRMELİ (eskiden hiç yoktu).</summary>
    [Fact]
    public async Task Menu_Ucu_Buttons_Alanini_Dondurur()
    {
        var resp = await _super.GetAsync("/api/me/menu");
        var json = await ApiTestHost.JsonAsync(resp);
        Assert.True(json.TryGetProperty("buttons", out _), "/api/me/menu 'buttons' alanı döndürmüyor.");
    }

    /// <summary>Süper admin tüm özel butonları kullanabilir (bypass).</summary>
    [Fact]
    public async Task SuperAdmin_Tum_Butonlari_Alir()
    {
        var btns = await ButtonsAsync(_super);
        foreach (var (key, _) in SpecialButtons.All)
            Assert.Contains(key, btns);
    }

    /// <summary>⭐ Yetkisiz personel HİÇBİR özel buton almamalı (deny-by-default).</summary>
    [Fact]
    public async Task Yetkisiz_Personel_Hicbir_Buton_Almaz()
    {
        var btns = await ButtonsAsync(_staff);
        Assert.Empty(btns);
    }

    /// <summary>Açıkça verilen buton listede görünür; verilmeyen görünmez.</summary>
    [Fact]
    public async Task Acikca_Verilen_Buton_Listede_Gorunur()
    {
        var su = _svc.Auth.Login(Co, Super, Pass).Session!;
        _svc.Permissions.SaveForUser(su, _staffId, Array.Empty<ModulePermission>(),
            new[] { SpecialButtons.RestoreTrash });

        // Yetki fotoğrafı düşürüldüğü için yeni istek güncel listeyi görür.
        var btns = await ButtonsAsync(_staff);
        Assert.Contains(SpecialButtons.RestoreTrash, btns);
        Assert.DoesNotContain(SpecialButtons.Reverse, btns);   // verilmeyen SIZMAZ
    }
}
