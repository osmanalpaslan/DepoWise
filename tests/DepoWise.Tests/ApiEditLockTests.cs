using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DÜZENLEME KİLİDİ — GERÇEK HTTP HATTI (İş #6, 2026-08-09).
///
/// Servis testleri (<see cref="EditLockCoverageTests"/>) kilidin veritabanı katmanında çalıştığını
/// kanıtlar. Web ve masaüstü ise servisi DOĞRUDAN çağırmaz; HTTP üzerinden gider. Bu testler
/// zincirin tamamını kapsar: JSON gövdedeki <c>version</c> alanı bağlanıyor mu, servis kilidi
/// tetikliyor mu, hata <b>409</b> olarak dönüyor mu, kayıt korunuyor mu.
///
/// 409 önemlidir: masaüstü <c>OrgServerClient</c> ve web sayfaları bu kodu "kayıt değişti" uyarısına
/// çevirir; 400/500 dönerse kullanıcı yanlış mesaj görür.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiEditLockTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "KILIT-A";
    private const string User = "kilit_kullanici";
    private const string Pass = "Test!2026";
    private string _requestId = "", _branchId = "";
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
        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var mat = svc.Materials.Create(s, new NewMaterial("MAT-KILIT", "Kilit malzemesi"));
        _requestId = svc.Requests.Create(s, new NewRequest(
            Items: new[] { new RequestItemInput(mat, 1m) }, Description: "ilk")).Id;
        _branchId = svc.Branches.Create(s, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));

        _client = await _host.LoginAsync(User, Pass, Company);
        _materialId = mat;
    }

    private string _materialId = "";

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private async Task<(long Version, string? Description)> ReadRequestAsync()
    {
        var r = await _client.GetAsync($"/api/requests/{_requestId}/edit");
        r.EnsureSuccessStatusCode();
        var j = await ApiTestHost.JsonAsync(r);
        return (j.GetProperty("version").GetInt64(),
                j.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null);
    }

    private Task<HttpResponseMessage> PutRequestAsync(string desc, long? version) =>
        _client.PutAsJsonAsync($"/api/requests/{_requestId}", new
        {
            items = new[] { new { materialId = _materialId, quantity = 1m, vehicleId = (string?)null, note = (string?)null } },
            branchId = (string?)null, requesterId = (string?)null, warehouseId = (string?)null, approverId = (string?)null,
            description = desc, requestDate = (long?)null, submitImmediately = false, version
        });

    private async Task<(long Version, string Name)> ReadBranchAsync()
    {
        var r = await _client.GetAsync("/api/branches");
        r.EnsureSuccessStatusCode();
        var j = await ApiTestHost.JsonAsync(r);
        var row = j.EnumerateArray().Single(e => e.GetProperty("id").GetString() == _branchId);
        return (row.GetProperty("version").GetInt64(), row.GetProperty("name").GetString() ?? "");
    }

    private Task<HttpResponseMessage> PutBranchAsync(string name, long? version) =>
        _client.PutAsJsonAsync($"/api/branches/{_branchId}", new
        {
            name, kind = "branch", parentId = (string?)null, code = (string?)null,
            password = (string?)null, companyId = (string?)null, version
        });

    // ── TALEPLER ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Talep_duzenleme_ucu_SURUM_dondurur()
    {
        var (version, _) = await ReadRequestAsync();
        Assert.True(version > 0);   // web/masaüstü sürümü buradan okur
    }

    [Fact]
    public async Task Talep_ESKI_surumle_PUT_409_doner_ve_kayit_korunur()
    {
        var (eskiSurum, _) = await ReadRequestAsync();          // B formu açtı
        (await PutRequestAsync("A kaydetti", null)).EnsureSuccessStatusCode();   // A araya girdi

        var r = await PutRequestAsync("B'nin eski verisi", eskiSurum);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);    // 409 — üzerine YAZILMADI

        var (_, desc) = await ReadRequestAsync();
        Assert.Equal("A kaydetti", desc);
    }

    [Fact]
    public async Task Talep_DOGRU_surumle_PUT_basarili()
    {
        var (surum, _) = await ReadRequestAsync();
        (await PutRequestAsync("guncel", surum)).EnsureSuccessStatusCode();

        var (yeniSurum, desc) = await ReadRequestAsync();
        Assert.Equal("guncel", desc);
        Assert.True(yeniSurum > surum);
    }

    [Fact]
    public async Task Talep_surum_GONDERILMEZSE_eski_davranis_korunur()
    {
        (await PutRequestAsync("surumsuz", null)).EnsureSuccessStatusCode();
        var (_, desc) = await ReadRequestAsync();
        Assert.Equal("surumsuz", desc);
    }

    // ── ŞUBE / ŞANTİYE ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sube_listesi_SURUM_dondurur()
    {
        var (version, _) = await ReadBranchAsync();
        Assert.True(version > 0);
    }

    [Fact]
    public async Task Sube_ESKI_surumle_PUT_409_doner_ve_kayit_korunur()
    {
        var (eskiSurum, _) = await ReadBranchAsync();
        (await PutBranchAsync("A kaydetti", null)).EnsureSuccessStatusCode();

        var r = await PutBranchAsync("B'nin eski verisi", eskiSurum);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

        var (_, name) = await ReadBranchAsync();
        Assert.Equal("A kaydetti", name);
    }

    [Fact]
    public async Task Sube_DOGRU_surumle_PUT_basarili()
    {
        var (surum, _) = await ReadBranchAsync();
        (await PutBranchAsync("Merkez Yeni", surum)).EnsureSuccessStatusCode();

        var (yeniSurum, name) = await ReadBranchAsync();
        Assert.Equal("Merkez Yeni", name);
        Assert.True(yeniSurum > surum);
    }

    [Fact]
    public async Task Sube_surum_GONDERILMEZSE_eski_davranis_korunur()
    {
        (await PutBranchAsync("Surumsuz", null)).EnsureSuccessStatusCode();
        var (_, name) = await ReadBranchAsync();
        Assert.Equal("Surumsuz", name);
    }
}
