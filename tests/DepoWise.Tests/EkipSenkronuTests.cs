using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ H7 REGRESYONU (kullanıcı bildirimi, 2026-09-06) ═══
///
/// <para><b>Bildirim:</b> "webte ekip tanımı yaptım ama masaüstüne kayıt atmadı;
/// oluşturduğum kaydı görüntüleyemedim."</para>
///
/// <para><b>Ölçüm sonucu:</b> sunucu ucu doğruydu — web'in kullandığı <c>POST /api/teams</c> ile
/// açılan ekip, masaüstünün çektiği <c>GET /api/lookups/sync</c> yanıtında GELİYOR (bu dosya bunu
/// kalıcı olarak korur). Eksik olan zamanlamaydı: masaüstü tanımları YALNIZ girişte ve elle
/// "Eşitle"de çekiyordu. Düzeltme, tanım tazelemesini otomatik senkron turuna ekledi
/// (şubelerdeki SNK-12 deseninin aynısı) ve Ekipler ekranını yenilenebilir yaptı.</para>
/// </summary>
[Collection("PostgresSchema")]
public class EkipSenkronuTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Firma = "EKIP-A";
    private const string Admin = "ekip_admin";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _c = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        _svc.Users.EnsureInitialAdmin(Firma, Admin, Pass, RoleKeys.CompanyAdmin);
        _c = await _host.LoginAsync(Admin, Pass, Firma);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    /// <summary>Web'de açılan ekip, masaüstünün çektiği tanım paketinde YER ALIR.</summary>
    [Fact]
    public async Task WebdeAcilanEkip_MasaustununCektigiPakette_Gelir()
    {
        var olustur = await _c.PostAsJsonAsync("/api/teams", new { name = "Saha Ekibi" });
        olustur.EnsureSuccessStatusCode();
        var id = (await olustur.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var paket = await _c.GetFromJsonAsync<JsonElement>("/api/lookups/sync");

        Assert.True(paket.TryGetProperty("teams", out var teams),
            "Tanım paketinde 'teams' alanı yok — masaüstü ekipleri hiç göremez.");
        Assert.Equal(JsonValueKind.Array, teams.ValueKind);

        var satir = teams.EnumerateArray()
            .FirstOrDefault(t => t.GetProperty("id").GetString() == id);
        Assert.Equal(JsonValueKind.Object, satir.ValueKind);
        Assert.Equal("Saha Ekibi", satir.GetProperty("name").GetString());

        // Masaüstü aynası bu alanları okur; adları değişirse ayna sessizce boş yazar.
        Assert.True(satir.TryGetProperty("lead_user_id", out _));
        Assert.True(satir.TryGetProperty("is_active", out _));
        Assert.True(paket.TryGetProperty("teamMembers", out var uyeler)
                    && uyeler.ValueKind == JsonValueKind.Array);
    }

    /// <summary>Ekip üyesi de aynı pakette taşınır (ekip gelip üyeleri gelmezse ekran yanıltır).</summary>
    [Fact]
    public async Task EkipUyesi_De_TanimPaketinde_Gelir()
    {
        var olustur = await _c.PostAsJsonAsync("/api/teams", new { name = "Bakım Ekibi" });
        var teamId = (await olustur.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var uid = _svc.Users.EnsureInitialAdmin(Firma, "ekip_uye", Pass, RoleKeys.Staff);
        var ekle = await _c.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = uid, isLead = false });
        ekle.EnsureSuccessStatusCode();

        var paket = await _c.GetFromJsonAsync<JsonElement>("/api/lookups/sync");
        var uyeler = paket.GetProperty("teamMembers").EnumerateArray()
            .Where(m => m.GetProperty("team_id").GetString() == teamId).ToList();
        Assert.Single(uyeler);
        Assert.Equal(uid, uyeler[0].GetProperty("user_id").GetString());
    }

    /// <summary>
    /// Masaüstü sözleşmesi: tanımlar OTOMATİK turda da tazelenir ve Ekipler ekranı yenilenebilir.
    /// (Masaüstü projesi test projesinden referanslanmaz → kaynak sözleşmesi denetlenir.)
    /// </summary>
    [Fact]
    public void Masaustu_OtomatikTurda_TanimlariTazeler_VeEkranYenilenir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { d!.FullName }.Concat(p).ToArray()));

        var shell = Oku("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs");
        Assert.Contains("LookupSyncService.RefreshAsync()", shell);

        var lookup = Oku("src", "DepoWise.Desktop", "LookupSyncService.cs");
        Assert.Contains("public static async Task<bool> RefreshAsync", lookup);
        Assert.Contains("MinInterval", lookup);
        // Değişmediyse yerele yazma: gereksiz yazma ve ekran yenilemesi olmamalı.
        Assert.Contains("_sonImza", lookup);

        var teams = Oku("src", "DepoWise.Desktop", "ViewModels", "TeamsViewModel.cs");
        Assert.Contains("IRefreshable", teams);
        Assert.Contains("public void RefreshData()", teams);
    }
}
