using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// B-5 (PRT-01 Grup 5, 2026-08-11) — MUAYENE/SİGORTA BELGESİ İPTALİ, GERÇEK HTTP HATTI.
///
/// Ürün kararı SEÇENEK B: fiziksel silme veya geçmişi kaybettiren UPDATE YOK. Kayıt <c>is_deleted=1</c>
/// olur, satır veritabanında KALIR, gerekçe ZORUNLUDUR ve denetim kaydına yazılır (yakıt iptali deseni —
/// <c>vehicle_inspections</c>'ta gerekçe kolonu yok, yalnız bunun için migration açılmadı).
///
/// Kritik davranış: iptal edilen kayıt aktif listeden çıkar VE <c>GetAlerts</c> hesabında en güncel kayıt
/// sayılmaz — daha eski AKTİF kayıt varsa uyarı ona göre hesaplanır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiInspectionCancelTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "MUAYENE-A";
    private const string UserA = "muayene_a";
    private const string CompanyB = "MUAYENE-B";
    private const string UserB = "muayene_b";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!;
    private string _vehicleA = "";
    private ServerServices _svc = null!;
    private SessionContext _sa = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        void EnsureCompany(string id)
        {
            using var conn = _svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        EnsureCompany(CompanyA);
        EnsureCompany(CompanyB);
        var uidA = _svc.Users.EnsureInitialAdmin(CompanyA, UserA, Pass, RoleKeys.CompanyAdmin);
        var uidB = _svc.Users.EnsureInitialAdmin(CompanyB, UserB, Pass, RoleKeys.CompanyAdmin);

        _sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(uidB, CompanyB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var brA = _svc.Branches.Create(_sa, new DepoWise.Infrastructure.Organization.NewBranch("A Şantiye"));
        _svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("B Şantiye"));
        _vehicleA = _svc.Vehicles.Create(_sa, new NewVehicle("A-01", "34AAA01", 2020, 100, "km", brA));

        _a = await _host.LoginAsync(UserA, Pass, CompanyA);
        _b = await _host.LoginAsync(UserB, Pass, CompanyB);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private async Task<string> CreateDocAsync(long? nextDate = null)
    {
        var r = await _a.PostAsJsonAsync("/api/inspection", new
        {
            vehicleId = _vehicleA, docType = "inspection", lastDate = (long?)null,
            nextDate, result = "Geçti", place = "QA", note = (string?)null,
        });
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    private async Task<(string Id, long Version)> FirstRowAsync(HttpClient c)
    {
        var list = await ApiTestHost.JsonAsync(await c.GetAsync("/api/inspection"));
        var row = list.EnumerateArray().First();
        return (row.GetProperty("id").GetString()!, row.GetProperty("version").GetInt64());
    }

    private Task<HttpResponseMessage> CancelAsync(HttpClient c, string id, string? reason, long? version = null)
        => c.PostAsJsonAsync($"/api/inspection/{id}/cancel", new { reason, version });

    private long FlagOf(string id)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_deleted FROM vehicle_inspections WHERE id=@id;";
        cmd.AddWithValue("@id", id);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private bool RowExists(string id)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicle_inspections WHERE id=@id;";
        cmd.AddWithValue("@id", id);
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    private string? AuditReasonOf(string id)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT after_json FROM audit_logs WHERE entity_id=@id AND action='reverse' ORDER BY rowid DESC;";
        cmd.AddWithValue("@id", id);
        return cmd.ExecuteScalar() as string;
    }

    // ── temel akış ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Liste_Id_Ve_Surum_Dondurur()
    {
        await CreateDocAsync();
        var (id, version) = await FirstRowAsync(_a);

        // Bunlar dönmezse hiçbir arayüz belirli bir belgeyi iptal edemez.
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(version > 0);
    }

    [Fact]
    public async Task Gercek_Gerekceyle_Iptal_Kaydi_SILMEZ_Ve_Gerekce_Denetime_Yazilir()
    {
        var id = await CreateDocAsync();

        (await CancelAsync(_a, id, "Yanlış tarih girilmiş")).EnsureSuccessStatusCode();

        Assert.True(RowExists(id));            // FİZİKSEL SİLME YOK
        Assert.Equal(1, FlagOf(id));           // is_deleted=1
        Assert.Contains("Yanlış tarih girilmiş", AuditReasonOf(id));
    }

    [Fact]
    public async Task Iptal_Edilen_Belge_Aktif_Listede_GORUNMEZ()
    {
        var id = await CreateDocAsync();
        (await CancelAsync(_a, id, "listeden çıkmalı")).EnsureSuccessStatusCode();

        var list = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/inspection"));
        Assert.Empty(list.EnumerateArray());
    }

    // ── gerekçe zorunluluğu ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bos_Gerekce_400_Doner_Ve_Belge_Iptal_EDILMEZ()
    {
        var id = await CreateDocAsync();

        var r = await CancelAsync(_a, id, "");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("İptal gerekçesi zorunlu.", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
        Assert.Equal(0, FlagOf(id));
    }

    [Fact]
    public async Task Yalnizca_Bosluk_Gerekce_400_Doner()
    {
        var id = await CreateDocAsync();
        var r = await CancelAsync(_a, id, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, FlagOf(id));
    }

    [Fact]
    public async Task Gerekce_Alani_Hic_Gonderilmezse_De_400_Doner()
    {
        var id = await CreateDocAsync();
        var r = await _a.PostAsJsonAsync($"/api/inspection/{id}/cancel", new { });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, FlagOf(id));
    }

    // ── hata durumları ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Zaten_Iptal_Edilmis_Belge_Tekrar_Iptal_EDILEMEZ()
    {
        var id = await CreateDocAsync();
        (await CancelAsync(_a, id, "birinci iptal")).EnsureSuccessStatusCode();

        var second = await CancelAsync(_a, id, "ikinci iptal");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("Bu belge zaten iptal edilmiş.",
            (await ApiTestHost.JsonAsync(second)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Olmayan_Id_403_Doner()
    {
        var r = await CancelAsync(_a, Guid.NewGuid().ToString("N"), "olmayan kayıt");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Baska_Firmanin_Belgesi_Iptal_EDILEMEZ()
    {
        var id = await CreateDocAsync();

        var r = await CancelAsync(_b, id, "yabancı firma denemesi");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0, FlagOf(id));   // A'nın belgesi etkilenmedi
    }

    [Fact]
    public async Task Bayat_Surumle_Iptal_409_Doner()
    {
        await CreateDocAsync();
        var (id, version) = await FirstRowAsync(_a);

        // Başka bir işlem kaydı değiştirdi (sürüm ilerledi).
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE vehicle_inspections SET version=version+1 WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        var r = await CancelAsync(_a, id, "bayat sürümle iptal", version);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, FlagOf(id));   // iptal UYGULANMADI
    }

    [Fact]
    public async Task Surum_Gonderilmezse_Kontrol_Yapilmaz_Geriye_Uyumlu()
    {
        var id = await CreateDocAsync();
        (await CancelAsync(_a, id, "sürümsüz iptal", version: null)).EnsureSuccessStatusCode();
        Assert.Equal(1, FlagOf(id));
    }

    // ── uyarı hesabı (kritik) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Iptal_Edilen_Kayit_Uyari_Hesabinda_EN_GUNCEL_Sayilmaz()
    {
        var now = DateTimeOffset.UtcNow;

        // ESKİ ve AKTİF belge: 200 gün sonra dolacak → uyarı vermez (Normal).
        var eski = await CreateDocAsync(now.AddDays(200).ToUnixTimeMilliseconds());
        // YENİ belge: süresi GEÇMİŞ → normalde uyarı üretirdi.
        var yeni = await CreateDocAsync(now.AddDays(-10).ToUnixTimeMilliseconds());

        var alertsBefore = _svc.Inspection.GetAlerts(_sa);
        Assert.Equal(DateAlertLevel.Expired, Assert.Single(alertsBefore).Level);

        // Yeni (hatalı) kayıt iptal edilir → uyarı ESKİ AKTİF kayda göre hesaplanmalı.
        (await CancelAsync(_a, yeni, "yanlış tarih")).EnsureSuccessStatusCode();

        var alertsAfter = _svc.Inspection.GetAlerts(_sa);
        var alert = Assert.Single(alertsAfter);
        Assert.Equal(DateAlertLevel.Normal, alert.Level);
        Assert.True(RowExists(eski));
    }

    [Fact]
    public async Task Tek_Kayit_Iptal_Edilince_Uyari_Tamamen_Kalkar()
    {
        var id = await CreateDocAsync(DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds());
        Assert.Single(_svc.Inspection.GetAlerts(_sa));

        (await CancelAsync(_a, id, "tek kayıt iptal")).EnsureSuccessStatusCode();

        Assert.Empty(_svc.Inspection.GetAlerts(_sa));
    }
}
