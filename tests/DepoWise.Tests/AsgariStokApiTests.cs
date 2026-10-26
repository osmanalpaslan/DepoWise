using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-31 — ASGARİ STOK API SÖZLEŞMESİ (web'in yolu) ═══
///  AA1 — malzeme kartından depo asgarisi yaz/oku; değer kaldırma
///  AA2 — öneri listesi çalışma şubesi başlığıyla daralır (web = masaüstü kapsamı); başlıksız tüm yetkili depolar
///  AA3 — seçilen önerilerden talep oluşur (belge no döner); Excel aktarımı dosya döner
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class AsgariStokApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "AS-API";
    private HttpClient _client = null!;
    private string _a = "", _b = "", _m = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = svc.Users.EnsureInitialAdmin(Company, "as_api", "Test!2026", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _a = svc.Branches.Create(s, new NewBranch("Merkez"));
        _b = svc.Branches.Create(s, new NewBranch("Şantiye"));
        _m = svc.Materials.Create(s, new NewMaterial("ASA-1", "Conta"));
        foreach (var depo in new[] { _a, _b })
            svc.Stock.ReceiveIn(s, new[] { new StockLine(_m, 1m) }, Guid.NewGuid().ToString("N"), branchId: depo);
        _client = await _host.LoginAsync("as_api", "Test!2026", Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<int> OneriSayisi(string? sube)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, "/api/stock/min-suggestions");
        if (sube is not null) r.Headers.TryAddWithoutValidation("X-Calisma-Subesi", sube);
        return (await ApiTestHost.JsonAsync(await _client.SendAsync(r))).GetArrayLength();
    }

    [Fact]
    public async Task AA1_Depo_Asgarisi_Yaz_Oku_Kaldir()
    {
        (await _client.PutAsJsonAsync($"/api/materials/{_m}/location-mins", new { locationId = _a, minQty = 4m })).EnsureSuccessStatusCode();
        var liste = await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/materials/{_m}/location-mins"));
        Assert.Equal(4m, liste[0].GetProperty("asgari").GetDecimal());
        (await _client.PutAsJsonAsync($"/api/materials/{_m}/location-mins", new { locationId = _a, minQty = (decimal?)null })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/materials/{_m}/location-mins"))).GetArrayLength());
        var kotu = await _client.PutAsJsonAsync($"/api/materials/{_m}/location-mins", new { locationId = _a, minQty = -1m });
        Assert.Equal(HttpStatusCode.BadRequest, kotu.StatusCode);
    }

    [Fact]
    public async Task AA2_Oneri_Calisma_Subesine_Gore()
    {
        foreach (var depo in new[] { _a, _b })
            (await _client.PutAsJsonAsync($"/api/materials/{_m}/location-mins", new { locationId = depo, minQty = 5m })).EnsureSuccessStatusCode();
        Assert.Equal(2, await OneriSayisi(null));
        Assert.Equal(1, await OneriSayisi(_a));
        var o = (await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/stock/min-suggestions?locationId={_b}")))[0];
        Assert.Equal((_b, 4m, "depo"), (o.GetProperty("locationId").GetString(), o.GetProperty("onerilen").GetDecimal(), o.GetProperty("kaynak").GetString()));
    }

    [Fact]
    public async Task AA3_Talep_Olusur_Ve_Excel_Aktarilir()
    {
        var r = await _client.PostAsJsonAsync("/api/stock/min-suggestions/request",
            new { locationId = _a, items = new[] { new { materialId = _m, quantity = 4m } }, note = (string?)null });
        r.EnsureSuccessStatusCode();
        Assert.False(string.IsNullOrEmpty((await ApiTestHost.JsonAsync(r)).GetProperty("docNo").GetString()));
        var bos = await _client.PostAsJsonAsync("/api/stock/min-suggestions/request", new { locationId = _a, items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, bos.StatusCode);
        var x = await _client.GetAsync("/api/stock/min-suggestions/export");
        Assert.Equal(HttpStatusCode.OK, x.StatusCode);
        Assert.True((await x.Content.ReadAsByteArrayAsync()).Length > 100);
    }
}
