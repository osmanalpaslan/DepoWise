using System.Net;
using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-23 (AR-GE 2026-10-18, 05 F-179) — UCUZ "SUNUCUDA DEĞİŞİKLİK VAR MI" ═══
///  UFS1 — /api/sync/business-version-v2 oturum ister; ufuk döner; yazımdan sonra ARTAR, yazım yokken DEĞİŞMEZ
///  UFS2 — Ufuk, aynı anda alınan çekme (v2) yanıtının nextAfter'ı ile aynı ölçektedir: çekimden sonra "ileri" değil
///  UFS3 — Eski uç (business-version) eski masaüstleri için aynen çalışır
///  UFS4 — Masaüstü yoklaması yeni yöntemi kullanır; eski yöntemde ipucu saklanır (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class UfukSurumTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "UFS-A", User = "ufs_kullanici", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!;

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
        var uid = Svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private async Task<long> Ufuk()
    {
        var r = await _client.GetAsync("/api/sync/business-version-v2");
        r.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("ufuk").GetInt64();
    }

    [Fact]
    public async Task UFS1_Ufuk_Yazimla_Artar_Yazimsiz_Sabit()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.CreateClient().GetAsync("/api/sync/business-version-v2")).StatusCode);
        var u1 = await Ufuk();
        Assert.Equal(u1, await Ufuk());                                       // yazım yok → aynı
        Svc.Materials.Create(_s, new NewMaterial("UFS-1", "Yeni"));
        Assert.True(await Ufuk() > u1);                                      // yazım → ileri
    }

    [Fact]
    public async Task UFS2_Cekimden_Sonra_Ileri_Degil()
    {
        Svc.Materials.Create(_s, new NewMaterial("UFS-2", "Önce"));
        var r = await _client.GetAsync("/api/sync/business-pull-v2?after=0");
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var imlec = doc.RootElement.GetProperty("nextAfter").GetInt64();
        Assert.False(await Ufuk() > imlec);                                  // yeni yazım yok → çekmeye gerek yok
        Svc.Materials.Create(_s, new NewMaterial("UFS-3", "Sonra"));
        Assert.True(await Ufuk() > imlec);                                   // yeni yazım → çek
    }

    [Fact]
    public async Task UFS3_Eski_Uc_Calisir()
    {
        var r = await _client.GetAsync("/api/sync/business-version");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("version").ValueKind);
    }

    [Fact]
    public void UFS4_Masaustu_Yoklamasi_Yeni_Yontemi_Kullanir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var shell = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"));
        Assert.Contains("BusinessSyncPullService.SunucuDurumuAsync(_lastServerVersionPulled)", shell);
        Assert.Contains("if (sunucuIleri || checkConflicts)", shell);
        Assert.Contains("if (d.EskiYontem && sv > _lastServerVersionPulled)", shell);
        var pull = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "BusinessSyncPullService.cs"));
        Assert.Contains("/api/sync/business-version-v2", pull);
        Assert.Contains("ufuk > OkuImlec(companyId!)", pull);
    }
}
