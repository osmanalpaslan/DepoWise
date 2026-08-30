using DepoWise.Infrastructure.Database;
using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 7b — EKİPMAN BAKIM API SÖZLEŞMESİ (GERÇEK HTTP, PK-F9 / ADR-191) ═══
///
/// Kilitlenenler: uçlar çalışır · firma gövdeden ALINMAZ · başka firmanın ekipmanına bakım açılamaz
/// (IDOR) · araç uçları BOZULMADI · tanım↔ekipman eşlemesi uçtan uca · muayene uçları.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class EkipmanApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "EQAPI-A";
    private const string CoB = "EQAPI-B";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!;
    private ServerServices _svc = null!;
    private string _adminA = "", _ekipmanA = "", _ekipmanB = "", _defA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        _adminA = _svc.Users.EnsureInitialAdmin(CoA, "eq_super", Pass, RoleKeys.SuperAdmin);
        var sa = new SessionContext(_adminA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeA = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _a = await _host.LoginAsync("eq_super", Pass, CoA, subeA);

        var adminB = _svc.Users.EnsureInitialAdmin(CoB, "eq_super_b", Pass, RoleKeys.SuperAdmin);
        var sb = new SessionContext(adminB, CoB, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeB = _svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _b = await _host.LoginAsync("eq_super_b", Pass, CoB, subeB);

        _ekipmanA = Ekipman(CoA, "EKP-A");
        _ekipmanB = Ekipman(CoB, "EKP-B");
        _defA = _svc.MaintenanceDefinitions.Create(sa,
            new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition("API Bakım", 30m, "day", null, null));
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private string Ekipman(string co, string kod)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@k,@k,'active',1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@k", kod);
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>EA01 — Ekipman bakım CRUD uçları uçtan uca çalışır.</summary>
    [Fact]
    public async Task EA01_Ekipman_Bakim_Uclari_Calisir()
    {
        var olustur = await _a.PostAsJsonAsync("/api/equipment-maintenance",
            new { equipmentId = _ekipmanA, definitionId = _defA, description = "API testi" });
        olustur.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(olustur)).GetProperty("id").GetString()!;

        var liste = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/equipment-maintenance"));
        var satir = Assert.Single(liste.EnumerateArray());
        Assert.Equal("EKP-A", satir.GetProperty("equipmentCode").GetString());
        Assert.Equal("API Bakım", satir.GetProperty("definitionName").GetString());
        Assert.Equal("Aktif", satir.GetProperty("statusText").GetString());

        (await _a.PutAsJsonAsync($"/api/equipment-maintenance/{id}/metadata",
            new { description = "guncel", subDefinitionNote = (string?)null, technicianId = (string?)null, version = (long?)null }))
            .EnsureSuccessStatusCode();

        (await _a.PostAsJsonAsync("/api/equipment-maintenance/cancel",
            new { id, reason = "API iptal" })).EnsureSuccessStatusCode();

        liste = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/equipment-maintenance"));
        Assert.Equal("İptal", liste.EnumerateArray().Single().GetProperty("statusText").GetString());

        var mats = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/equipment-maintenance/{id}/materials"));
        Assert.Empty(mats.EnumerateArray());
    }

    /// <summary>EA02 — <b>IDOR:</b> başka firmanın ekipmanına bakım açılamaz; B, A'nın kaydını görmez.</summary>
    [Fact]
    public async Task EA02_Tenant_Ve_IDOR()
    {
        var red = await _a.PostAsJsonAsync("/api/equipment-maintenance",
            new { equipmentId = _ekipmanB, definitionId = _defA });
        Assert.Equal(HttpStatusCode.Forbidden, red.StatusCode);

        var ok = await _a.PostAsJsonAsync("/api/equipment-maintenance",
            new { equipmentId = _ekipmanA, definitionId = _defA });
        ok.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(ok)).GetProperty("id").GetString()!;

        Assert.Empty((await ApiTestHost.JsonAsync(await _b.GetAsync("/api/equipment-maintenance"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _b.PostAsJsonAsync("/api/equipment-maintenance/cancel", new { id, reason = "olmaz" })).StatusCode);
    }

    /// <summary>EA03 — Tanım ↔ ekipman eşlemesi uçları; araç eşlemesi ETKİLENMEZ.</summary>
    [Fact]
    public async Task EA03_Tanim_Ekipman_Eslemesi_Ucu()
    {
        (await _a.PutAsJsonAsync($"/api/maintenance/definitions/{_defA}/equipment",
            new { ids = new[] { _ekipmanA } })).EnsureSuccessStatusCode();

        var esleme = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/maintenance/definitions/{_defA}/equipment"));
        Assert.Equal(_ekipmanA, esleme.EnumerateArray().Single().GetString());

        // Araç eşlemesi boş kaldı (ayrı tablo, ayrı uç).
        var araclar = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/maintenance/definitions/{_defA}/vehicles"));
        Assert.Empty(araclar.EnumerateArray());

        // Yabancı ekipman bağlanamaz.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PutAsJsonAsync($"/api/maintenance/definitions/{_defA}/equipment",
                new { ids = new[] { _ekipmanB } })).StatusCode);
    }

    /// <summary>EA04 — Ekipman muayene uçları: kayıt, listeleme, yumuşak silme, tenant.</summary>
    [Fact]
    public async Task EA04_Ekipman_Muayene_Ucu()
    {
        var olustur = await _a.PostAsJsonAsync("/api/equipment-inspection",
            new { equipmentId = _ekipmanA, docType = "inspection", lastDate = (long?)null, nextDate = (long?)null });
        olustur.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(olustur)).GetProperty("id").GetString()!;

        Assert.Single((await ApiTestHost.JsonAsync(await _a.GetAsync("/api/equipment-inspection"))).EnumerateArray());
        Assert.Empty((await ApiTestHost.JsonAsync(await _b.GetAsync("/api/equipment-inspection"))).EnumerateArray());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PostAsJsonAsync("/api/equipment-inspection",
                new { equipmentId = _ekipmanB, docType = "inspection" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _a.PostAsJsonAsync("/api/equipment-inspection",
                new { equipmentId = _ekipmanA, docType = "gecersiz" })).StatusCode);

        (await _a.DeleteAsync($"/api/equipment-inspection/{id}")).EnsureSuccessStatusCode();
        Assert.Empty((await ApiTestHost.JsonAsync(await _a.GetAsync("/api/equipment-inspection"))).EnumerateArray());
    }

    /// <summary>EA05 — <b>ARAÇ UÇLARI BOZULMADI:</b> mevcut araç bakım ve muayene uçları çalışmaya
    /// devam eder ve ekipman kayıtları o listelere SIZMAZ.</summary>
    [Fact]
    public async Task EA05_Arac_Uclari_Regresyonsuz()
    {
        var arac = Guid.NewGuid().ToString("N");
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO vehicles(id,company_id,internal_code,status,created_at,updated_at,version,is_deleted) " +
                "VALUES(@i,@c,'ARC-API','active',1,1,1,0);";
            cmd.AddWithValue("@i", arac);
            cmd.AddWithValue("@c", CoA);
            cmd.ExecuteNonQuery();
        }

        (await _a.PostAsJsonAsync("/api/maintenance", new { vehicleId = arac, definitionId = _defA }))
            .EnsureSuccessStatusCode();
        (await _a.PostAsJsonAsync("/api/equipment-maintenance", new { equipmentId = _ekipmanA, definitionId = _defA }))
            .EnsureSuccessStatusCode();

        // Araç listesi yalnız aracı, ekipman listesi yalnız ekipmanı içerir.
        Assert.Single((await ApiTestHost.JsonAsync(await _a.GetAsync("/api/maintenance"))).EnumerateArray());
        Assert.Single((await ApiTestHost.JsonAsync(await _a.GetAsync("/api/equipment-maintenance"))).EnumerateArray());
    }
}
