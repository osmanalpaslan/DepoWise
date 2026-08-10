using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PRT-01 GRUP 3 — GERÇEK HTTP HATTI (2026-08-10).
///
/// Servis testleri (<see cref="MaintenanceDefinitionConcurrencyTests"/>) kuralların veritabanı
/// katmanında çalıştığını kanıtlar. Web servisi DOĞRUDAN çağırmaz; HTTP üzerinden gider. Bu testler
/// zincirin tamamını kapsar — JSON alan bağlanıyor mu, doğru HTTP kodu dönüyor mu:
///
/// • B-1 — bakım tanımı düzenleme kilidi: <c>version</c> listede dönüyor, bayat sürüm <b>409</b>.
/// • B-4 — yakıt iptal gerekçesi: boş gerekçe <b>400</b> (eskiden sessizce "Kullanıcı iptali" yazılıyordu).
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiGroup3Tests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "GRUP3-A";
    private const string User = "grup3_kullanici";
    private const string Pass = "Test!2026";
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();   // sunucuyu ayağa kaldır (migration + tohum)
        var svc = _host.Services.GetRequiredService<ServerServices>();

        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── B-1: bakım tanımı düzenleme kilidi ─────────────────────────────────────────────────

    private async Task<(string Id, long Version, decimal Interval)> ReadDefAsync()
    {
        var r = await _client.GetAsync("/api/maintenance/definitions");
        r.EnsureSuccessStatusCode();
        var arr = await ApiTestHost.JsonAsync(r);
        var d = arr.EnumerateArray().First();
        return (d.GetProperty("id").GetString()!,
                d.GetProperty("version").GetInt64(),
                d.GetProperty("intervalValue").GetDecimal());
    }

    private Task<HttpResponseMessage> PutDefAsync(string id, decimal interval, long? version)
        => _client.PutAsJsonAsync($"/api/maintenance/definitions/{id}", new
        {
            name = "Yağ Değişimi",
            intervalValue = interval,
            intervalUnit = "km",
            parentDefId = (string?)null,
            description = (string?)null,
            vehicleIds = new List<string>(),
            version,
        });

    [Fact]
    public async Task B1_Tanim_Listesi_Surum_Dondurur()
    {
        var c = await _client.PostAsJsonAsync("/api/maintenance/definitions", new
        {
            name = "Yağ Değişimi", intervalValue = 10000m, intervalUnit = "km",
            parentDefId = (string?)null, description = (string?)null, vehicleIds = new List<string>(),
        });
        c.EnsureSuccessStatusCode();

        var def = await ReadDefAsync();
        // Sürüm taşınmazsa istemci kilidi gönderemez → sessiz üzerine yazma geri gelir.
        Assert.True(def.Version > 0, "Liste 'version' alanını döndürmeli.");
    }

    [Fact]
    public async Task B1_Bayat_Surum_409_Doner_Ve_Kayit_Korunur()
    {
        (await _client.PostAsJsonAsync("/api/maintenance/definitions", new
        {
            name = "Yağ Değişimi", intervalValue = 10000m, intervalUnit = "km",
            parentDefId = (string?)null, description = (string?)null, vehicleIds = new List<string>(),
        })).EnsureSuccessStatusCode();

        var def = await ReadDefAsync();

        // 1. kullanıcı kaydeder → sürüm ilerler.
        (await PutDefAsync(def.Id, 15000m, def.Version)).EnsureSuccessStatusCode();

        // 2. kullanıcı formu ESKİ sürümle açmıştı.
        var stale = await PutDefAsync(def.Id, 99999m, def.Version);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // 409 önemlidir: web/masaüstü bu kodu "kayıt değişti" uyarısına çevirir.
        var body = await ApiTestHost.JsonAsync(stale);
        Assert.Contains("başkası tarafından değiştirildi", body.GetProperty("error").GetString());

        // 1. kullanıcının değeri KORUNUR.
        Assert.Equal(15000m, (await ReadDefAsync()).Interval);
    }

    [Fact]
    public async Task B1_Surum_Gonderilmezse_Eski_Istemci_Calismaya_Devam_Eder()
    {
        (await _client.PostAsJsonAsync("/api/maintenance/definitions", new
        {
            name = "Yağ Değişimi", intervalValue = 10000m, intervalUnit = "km",
            parentDefId = (string?)null, description = (string?)null, vehicleIds = new List<string>(),
        })).EnsureSuccessStatusCode();

        var def = await ReadDefAsync();
        (await PutDefAsync(def.Id, 15000m, def.Version)).EnsureSuccessStatusCode();

        // version alanı YOK → kilit kontrolü yapılmaz (geriye uyumluluk).
        var r = await PutDefAsync(def.Id, 16000m, null);
        r.EnsureSuccessStatusCode();
        Assert.Equal(16000m, (await ReadDefAsync()).Interval);
    }

    // ── B-4: yakıt iptal gerekçesi ─────────────────────────────────────────────────────────

    private async Task<string> CreateDepotEntryAsync()
    {
        var r = await _client.PostAsJsonAsync("/api/fuel/depot",
            new { liters = 100m, unitPrice = 42.5m, supplierId = (string?)null, invoiceNo = "G3-001" });
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task B4_Bos_Gerekce_400_Doner_Ve_Kayit_Iptal_Edilmez()
    {
        var id = await CreateDepotEntryAsync();

        var r = await _client.PostAsJsonAsync($"/api/fuel/depot/{id}/cancel", new { reason = "" });

        // Eskiden 200 dönüyor ve denetim kaydına kullanıcının YAZMADIĞI "Kullanıcı iptali" yazılıyordu.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await ApiTestHost.JsonAsync(r);
        Assert.Equal("İptal gerekçesi zorunlu.", body.GetProperty("error").GetString());

        // Kayıt iptal EDİLMEMİŞ olmalı.
        var list = await ApiTestHost.JsonAsync(await _client.GetAsync("/api/fuel/depot"));
        var row = list.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        Assert.False(row.GetProperty("isCancelled").GetBoolean());
    }

    [Fact]
    public async Task B4_Gerekce_Alani_Hic_Gonderilmezse_De_400_Doner()
    {
        var id = await CreateDepotEntryAsync();

        var r = await _client.PostAsJsonAsync($"/api/fuel/depot/{id}/cancel", new { });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task B4_Gercek_Gerekce_Kabul_Edilir_Ve_Kayit_Iptal_Olur()
    {
        var id = await CreateDepotEntryAsync();

        var r = await _client.PostAsJsonAsync($"/api/fuel/depot/{id}/cancel",
            new { reason = "Fatura yanlış firmaya kesilmiş" });
        r.EnsureSuccessStatusCode();

        var list = await ApiTestHost.JsonAsync(await _client.GetAsync("/api/fuel/depot?includeCancelled=true"));
        var row = list.EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        Assert.True(row.GetProperty("isCancelled").GetBoolean());
    }
}
