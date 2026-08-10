using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PRT-01 GRUP 4 (TALEPLER) — GERÇEK HTTP HATTI (2026-08-10).
///
/// Servis testleri (<see cref="RequestTests"/>) iş kurallarının veri katmanında çalıştığını kanıtlar.
/// Web servisi DOĞRUDAN çağırmaz; HTTP üzerinden gider. Bu testler zincirin tamamını kapsar:
///
/// • B-1 — durum/arama/limit SUNUCUYA ulaşıyor mu, parametresiz eski çağrı bozuldu mu, tenant korunuyor mu.
/// • B-2 — öncelik UI'nin gönderdiği gibi kaydediliyor ve düzenlemede KORUNUYOR mu.
/// • B-3 — boş ret gerekçesi 400 mü (eskiden sessizce "Reddedildi" yazılıyordu).
/// • B-4 — boş iptal gerekçesi 400 mü (eskiden sabit "Kullanıcı iptali" gidiyordu).
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiGroup4Tests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "GRUP4-A";
    private const string User = "grup4_kullanici";
    private const string Other = "GRUP4-B";          // tenant izolasyonu için ikinci firma
    private const string OtherUser = "grup4_yabanci";
    private const string Pass = "Test!2026";
    private HttpClient _client = null!;
    private HttpClient _otherClient = null!;
    private string _materialId = "";
    /// <summary>İkinci firmanın KENDİ malzemesi. A firmasının malzemesiyle talep açılamaz —
    /// <c>EnsureMaterialOwned</c> 403 verir (tenant izolasyonu doğru çalışıyor).</summary>
    private string _otherMaterialId = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();

        void EnsureCompany(string id)
        {
            using var conn = svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        EnsureCompany(Company);
        EnsureCompany(Other);
        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var oid = svc.Users.EnsureInitialAdmin(Other, OtherUser, Pass, RoleKeys.CompanyAdmin);

        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _materialId = svc.Materials.Create(s, new NewMaterial("MAT-G4", "Grup4 malzemesi"));

        var os = new SessionContext(oid, Other, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _otherMaterialId = svc.Materials.Create(os, new NewMaterial("MAT-G4B", "Grup4-B malzemesi"));

        _client = await _host.LoginAsync(User, Pass, Company);
        _otherClient = await _host.LoginAsync(OtherUser, Pass, Other);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private async Task<string> CreateAsync(string? description = null, string? priority = null,
        HttpClient? client = null)
    {
        var mat = client is null || ReferenceEquals(client, _client) ? _materialId : _otherMaterialId;
        var r = await (client ?? _client).PostAsJsonAsync("/api/requests", new
        {
            items = new[] { new { materialId = mat, quantity = 1m, vehicleId = (string?)null, note = (string?)null } },
            branchId = (string?)null, requesterId = (string?)null, warehouseId = (string?)null,
            approverId = (string?)null, description, requestDate = (long?)null, submitImmediately = true,
            priority,
        });
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    private async Task<JsonElement[]> ListAsync(string query = "", HttpClient? client = null)
    {
        var r = await (client ?? _client).GetAsync("/api/requests" + query);
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).EnumerateArray().ToArray();
    }

    // ── B-1: sunucu tarafı filtre / arama / limit ──────────────────────────────────────────

    [Fact]
    public async Task B1_Parametresiz_Eski_Cagri_Calismaya_Devam_Eder()
    {
        await CreateAsync("eski istemci");
        var rows = await ListAsync();
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task B1_Durum_Filtresi_Sunucuda_Uygulanir()
    {
        var id = await CreateAsync("durum testi");
        (await _client.PostAsJsonAsync($"/api/requests/{id}/approve", new { })).EnsureSuccessStatusCode();
        await CreateAsync("bekleyen kalsin");

        var approved = await ListAsync("?status=approved");
        Assert.Single(approved);
        Assert.Equal(id, approved[0].GetProperty("id").GetString());

        var pending = await ListAsync("?status=pending");
        Assert.Single(pending);
        Assert.NotEqual(id, pending[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task B1_Gecersiz_Durum_400_Doner()
    {
        var r = await _client.GetAsync("/api/requests?status=uydurma");
        // Sessizce "draft"a düşmemeli (RequestStatusMachine.FromDb öyle yapardı) → açık hata.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task B1_Arama_Sunucuda_Uygulanir()
    {
        await CreateAsync("vinç halatı değişimi");
        await CreateAsync("jeneratör yağı");

        var rows = await ListAsync("?search=" + Uri.EscapeDataString("jeneratör"));
        Assert.Single(rows);
        Assert.Contains("jeneratör", rows[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task B1_Arama_Belge_Numarasinda_Da_Calisir()
    {
        var id = await CreateAsync("belge no aramasi");
        var all = await ListAsync();
        var docNo = all.Single(x => x.GetProperty("id").GetString() == id).GetProperty("docNo").GetString()!;

        var rows = await ListAsync("?search=" + Uri.EscapeDataString(docNo));
        Assert.Single(rows);
        Assert.Equal(id, rows[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task B1_Limit_Uygulanir_Ve_Ust_Sinirla_Kisitlanir()
    {
        for (var i = 0; i < 5; i++) await CreateAsync($"limit-{i}");

        Assert.Equal(2, (await ListAsync("?limit=2")).Length);

        // İstemci sınırsız veri isteyemez: aşırı limit üst sınıra çekilir, hata verilmez.
        var huge = await ListAsync("?limit=999999");
        Assert.True(huge.Length >= 5);
    }

    [Fact]
    public async Task B1_Filtre_Ve_Arama_Birlikte_Calisir()
    {
        var id = await CreateAsync("kaynak teli");
        (await _client.PostAsJsonAsync($"/api/requests/{id}/approve", new { })).EnsureSuccessStatusCode();
        await CreateAsync("kaynak maskesi");   // bekleyen kalır

        var rows = await ListAsync("?status=approved&search=" + Uri.EscapeDataString("kaynak"));
        Assert.Single(rows);
        Assert.Equal(id, rows[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task B1_Tenant_Izolasyonu_Filtreyle_De_Korunur()
    {
        await CreateAsync("A firmasinin talebi");
        await CreateAsync("B firmasinin talebi", client: _otherClient);

        // Arama terimi iki firmada da eşleşse bile herkes YALNIZ kendi firmasını görür.
        var mine = await ListAsync("?search=" + Uri.EscapeDataString("firmasinin"));
        Assert.Single(mine);
        Assert.Contains("A firmasinin", mine[0].GetProperty("description").GetString());

        var theirs = await ListAsync("?search=" + Uri.EscapeDataString("firmasinin"), _otherClient);
        Assert.Single(theirs);
        Assert.Contains("B firmasinin", theirs[0].GetProperty("description").GetString());
    }

    // ── B-2: öncelik (UI → API → servis → DB → geri) ───────────────────────────────────────

    [Fact]
    public async Task B2_Oncelik_Gonderildigi_Gibi_Kaydedilir()
    {
        var id = await CreateAsync("acil talep", priority: "urgent");

        var row = (await ListAsync()).Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal("Acil", row.GetProperty("priorityText").GetString());
    }

    [Fact]
    public async Task B2_Oncelik_Gonderilmezse_Normal_Kalir()
    {
        var id = await CreateAsync("onceliksiz talep");

        var row = (await ListAsync()).Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal("Normal", row.GetProperty("priorityText").GetString());
    }

    [Fact]
    public async Task B2_Duzenleme_Ucu_Onceligi_Dondurur_Ve_Guncelleme_KORUR()
    {
        var id = await CreateAsync("kritik talep", priority: "critical");

        // Form önceliği okuyabilmeli; okuyamazsa kaydederken varsayılanı geri yazıp SIFIRLARDI.
        var edit = await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/requests/{id}/edit"));
        Assert.Equal("critical", edit.GetProperty("priorityDb").GetString());

        // Formun okuduğu değerle güncelleme → öncelik korunur.
        var upd = await _client.PutAsJsonAsync($"/api/requests/{id}", new
        {
            items = new[] { new { materialId = _materialId, quantity = 2m, vehicleId = (string?)null, note = (string?)null } },
            branchId = (string?)null, requesterId = (string?)null, warehouseId = (string?)null,
            approverId = (string?)null, description = "kritik talep (güncel)", requestDate = (long?)null,
            submitImmediately = false, priority = "critical", version = edit.GetProperty("version").GetInt64(),
        });
        upd.EnsureSuccessStatusCode();

        var row = (await ListAsync()).Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal("Kritik", row.GetProperty("priorityText").GetString());
    }

    // ── B-3: ret gerekçesi ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task B3_Bos_Ret_Gerekcesi_400_Doner()
    {
        var id = await CreateAsync("ret testi");

        var r = await _client.PostAsJsonAsync($"/api/requests/{id}/reject", new { id, reason = "" });

        // Eskiden 200 dönüyor ve denetime kullanıcının YAZMADIĞI "Reddedildi" yazılıyordu.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Ret gerekçesi zorunlu.", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());

        // Talep reddedilmemiş olmalı (hâlâ Beklemede).
        var row = (await ListAsync()).Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal(1, row.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task B3_Bosluk_Ret_Gerekcesi_400_Doner()
    {
        var id = await CreateAsync("bosluk ret testi");
        var r = await _client.PostAsJsonAsync($"/api/requests/{id}/reject", new { id, reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task B3_Gercek_Ret_Gerekcesi_Kaydedilir_Ve_Gecmiste_Gorunur()
    {
        var id = await CreateAsync("gerçek ret");
        (await _client.PostAsJsonAsync($"/api/requests/{id}/reject",
            new { id, reason = "Bütçe onayı çıkmadı" })).EnsureSuccessStatusCode();

        var history = (await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/requests/{id}/history")))
            .EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        Assert.Contains(history, h => h.Contains("Bütçe onayı çıkmadı"));
        Assert.DoesNotContain(history, h => h.Contains("(Reddedildi)"));   // sahte gerekçe yazılmamalı
    }

    // ── B-4: iptal gerekçesi ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task B4_Bos_Iptal_Gerekcesi_400_Doner_Ve_Talep_Iptal_Edilmez()
    {
        var id = await CreateAsync("iptal testi");

        var r = await _client.PostAsJsonAsync($"/api/requests/{id}/cancel", new { id, reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("İptal gerekçesi zorunlu.", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());

        var row = (await ListAsync()).Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal(1, row.GetProperty("status").GetInt32());   // hâlâ Beklemede
    }

    [Fact]
    public async Task B4_Gerekce_Alani_Hic_Gonderilmezse_De_400_Doner()
    {
        var id = await CreateAsync("iptal alansiz");
        var r = await _client.PostAsJsonAsync($"/api/requests/{id}/cancel", new { id });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task B4_Gercek_Iptal_Gerekcesi_Kaydedilir_Ve_Gecmiste_Gorunur()
    {
        var id = await CreateAsync("gerçek iptal");
        (await _client.PostAsJsonAsync($"/api/requests/{id}/cancel",
            new { id, reason = "Malzeme başka şantiyeden karşılandı" })).EnsureSuccessStatusCode();

        var history = (await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/requests/{id}/history")))
            .EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        Assert.Contains(history, h => h.Contains("Malzeme başka şantiyeden karşılandı"));
        Assert.DoesNotContain(history, h => h.Contains("Kullanıcı iptali"));   // sabit metin artık yok
    }

    [Fact]
    public async Task B4_Baska_Firmanin_Talebi_Iptal_Edilemez()
    {
        var id = await CreateAsync("tenant iptal testi");

        var r = await _otherClient.PostAsJsonAsync($"/api/requests/{id}/cancel",
            new { id, reason = "yabancı firma denemesi" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
