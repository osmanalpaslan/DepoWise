using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// BKM-04 / KARAR-9 — WEB'İN GERÇEK HTTP HATTI.
///
/// Web ekranı `branchId`'yi POST gövdesinde gönderir (API oturumu şube taşımadığı için TEK yol budur —
/// analiz bulgusu: <c>OperatingBranchId</c> API'de her zaman null). Bu testler o sözleşmeyi kilitler:
/// gönderilen depo UYGULANIR, gönderilmezse eski davranış (ATANMAMIŞ) korunur, yabancı depo REDDEDİLİR.
/// </summary>
[Collection("PostgresSchema")]
public class ApiMaintenanceLocationTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "BKM-A";
    private const string Other = "BKM-B";
    private const string User = "bkm_kullanici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private ServerServices _svc = null!;
    private string _mat = "", _depoA = "", _depoB = "", _yabanciDepo = "", _vehicle = "", _def = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        Company_(_svc, Company); Company_(_svc, Other);

        var uid = _svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depoA = _svc.Branches.Create(s, new NewBranch("Depo A"));
        _depoB = _svc.Branches.Create(s, new NewBranch("Depo B"));
        _mat = _svc.Materials.Create(s, new NewMaterial("BKM-API-1", "Yağ filtresi"));
        _vehicle = _svc.Vehicles.Create(s, new NewVehicle("API-IS-1", "34API01", 2020, 1000m, "km", _depoA));
        _def = _svc.MaintenanceDefinitions.Create(s, new NewMaintenanceDefinition("Periyodik", 10000m, "km"));
        _svc.OpeningStock.RecordOpening(s, _mat, 10m, "op-a", branchId: _depoA);
        _svc.OpeningStock.RecordOpening(s, _mat, 10m, "op-b", branchId: _depoB);

        var otherUid = _svc.Users.EnsureInitialAdmin(Other, "bkm_b", Pass, RoleKeys.CompanyAdmin);
        var so = new SessionContext(otherUid, Other, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _yabanciDepo = _svc.Branches.Create(so, new NewBranch("Yabancı Depo"));

        _client = await _host.LoginAsync(User, Pass, Company);
    }

    private static void Company_(ServerServices svc, string id)
    {
        using var conn = svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<decimal> AtAsync(string locationId)
        => (await ApiTestHost.JsonAsync(await _client.GetAsync(
                $"/api/stock/balance/{_mat}/location?locationId={Uri.EscapeDataString(locationId)}")))
            .GetProperty("balance").GetDecimal();

    /// <summary>Gövdeye `branchId` KOYMADAN gönderim — eski istemcinin birebir taklidi.</summary>
    private Task<HttpResponseMessage> BakimEskiIstemci(decimal qty) =>
        _client.PostAsJsonAsync("/api/maintenance", new
        {
            vehicleId = _vehicle, definitionId = _def, performedKm = 5000m, performedDate = 1_700_000_000_000L,
            materials = new[] { new { materialId = _mat, quantity = qty, fromTeamStock = false } },
        });

    private Task<HttpResponseMessage> BakimAsync(string? branchId, decimal qty) =>
        _client.PostAsJsonAsync("/api/maintenance", new
        {
            vehicleId = _vehicle, definitionId = _def, performedKm = 5000m, performedDate = 1_700_000_000_000L,
            materials = new[] { new { materialId = _mat, quantity = qty, fromTeamStock = false } },
            branchId,
        });

    // ── 5. Web hattında seçilen depo uygulanır ────────────────────────────────────────────

    /// <summary>5 — Gövdede gönderilen depo GERÇEKTEN uygulanır (Web'in tek yolu budur).</summary>
    [Fact]
    public async Task Web_Hattinda_Secilen_Depo_Uygulanir()
    {
        var r = await BakimAsync(_depoB, 4m);
        r.EnsureSuccessStatusCode();

        Assert.Equal(10m, await AtAsync(_depoA));   // dokunulmadı
        Assert.Equal(6m, await AtAsync(_depoB));    // seçilen depo düştü
        Assert.Equal(0m, await AtAsync(""));        // ATANMAMIŞ'a düşmedi
    }

    /// <summary>8 — `branchId` GÖNDERMEYEN eski istemci kırılmaz; davranış ATANMAMIŞ olarak kalır.</summary>
    [Fact]
    public async Task BranchId_Gondermeyen_Eski_Istemci_ATANMAMIS_Davranisini_Korur()
    {
        var r = await BakimEskiIstemci(3m);
        r.EnsureSuccessStatusCode();

        Assert.Equal(-3m, await AtAsync(""));       // ATANMAMIŞ (eski davranış birebir)
        Assert.Equal(10m, await AtAsync(_depoA));   // gerçek depolara dokunulmadı
        Assert.Equal(10m, await AtAsync(_depoB));
    }

    /// <summary>9 — Başka firmanın deposu 403 ile reddedilir; hiçbir stok değişmez.</summary>
    [Fact]
    public async Task Yabanci_Firmanin_Deposu_403_ile_Reddedilir()
    {
        var r = await BakimAsync(_yabanciDepo, 1m);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        Assert.Equal(10m, await AtAsync(_depoA));
        Assert.Equal(10m, await AtAsync(_depoB));
        Assert.Equal(0m, await AtAsync(""));
    }

    /// <summary>10 — Bilinmeyen/uydurma lokasyon da 403.</summary>
    [Fact]
    public async Task Bilinmeyen_Lokasyon_403()
    {
        var r = await BakimAsync("uydurma-depo", 1m);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    /// <summary>7 — Günlük Faaliyet uçları da aynı lokasyon semantiğini uygular.</summary>
    [Fact]
    public async Task Gunluk_Faaliyet_Bakim_Ucu_Lokasyonu_Uygular()
    {
        var r = await _client.PostAsJsonAsync("/api/daily/maintenance", new
        {
            vehicleId = _vehicle, definitionId = _def, performedKm = 6000m, performedDate = 1_700_000_000_000L,
            materials = new[] { new { materialId = _mat, quantity = 2m, fromTeamStock = false } },
            branchId = _depoB,
        });
        r.EnsureSuccessStatusCode();

        Assert.Equal(8m, await AtAsync(_depoB));
        Assert.Equal(10m, await AtAsync(_depoA));
    }

    /// <summary>7b — "İlave Yağ" ucu da aynı semantiği uygular.</summary>
    [Fact]
    public async Task Gunluk_Faaliyet_Ilave_Islem_Ucu_Lokasyonu_Uygular()
    {
        var r = await _client.PostAsJsonAsync("/api/daily/extra", new
        {
            type = "extra_oil", vehicleId = _vehicle, performedKm = 7000m, performedDate = 1_700_000_000_000L,
            materials = new[] { new { materialId = _mat, quantity = 5m, fromTeamStock = false } },
            branchId = _depoB,
        });
        r.EnsureSuccessStatusCode();

        Assert.Equal(5m, await AtAsync(_depoB));
        Assert.Equal(10m, await AtAsync(_depoA));
    }

    /// <summary>Günlük Faaliyet ucunda da yabancı depo 403.</summary>
    [Fact]
    public async Task Gunluk_Faaliyet_Yabanci_Depo_403()
    {
        var r = await _client.PostAsJsonAsync("/api/daily/maintenance", new
        {
            vehicleId = _vehicle, definitionId = _def, performedKm = 6000m, performedDate = 1_700_000_000_000L,
            materials = new[] { new { materialId = _mat, quantity = 1m, fromTeamStock = false } },
            branchId = _yabanciDepo,
        });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(10m, await AtAsync(_depoA));
    }

    /// <summary>13 — HTTP hattında da iptal ORİJİNAL depoya geri yazar.</summary>
    [Fact]
    public async Task Iptal_HTTP_Hattinda_da_Orijinal_Depoya_Doner()
    {
        var r = await BakimAsync(_depoB, 4m);
        r.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
        Assert.Equal(6m, await AtAsync(_depoB));

        var c = await _client.PostAsJsonAsync("/api/maintenance/cancel", new { id, reason = "yanlış kayıt" });
        c.EnsureSuccessStatusCode();

        Assert.Equal(10m, await AtAsync(_depoB));   // orijinal depoya döndü
        Assert.Equal(10m, await AtAsync(_depoA));   // başka depo şişmedi
    }

    /// <summary>15 — Ekip stoğu işaretli satır HTTP hattında da hiçbir depodan düşmez.</summary>
    [Fact]
    public async Task Ekip_Stogu_HTTP_Hattinda_da_Dusmez()
    {
        var r = await _client.PostAsJsonAsync("/api/maintenance", new
        {
            vehicleId = _vehicle, definitionId = _def, performedKm = 5000m, performedDate = 1_700_000_000_000L,
            materials = new[] { new { materialId = _mat, quantity = 4m, fromTeamStock = true } },
            branchId = _depoB,
        });
        r.EnsureSuccessStatusCode();

        Assert.Equal(10m, await AtAsync(_depoB));
        Assert.Equal(10m, await AtAsync(_depoA));
    }
}
