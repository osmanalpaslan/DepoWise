using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PRT-01 GRUP 5 — YABANCI ARAÇ REFERANSI (B-2 · B-3 · B-4), GERÇEK HTTP HATTI (2026-08-10).
///
/// Bulunan durum: <c>InspectionService.Save</c> ve <c>DailyActivityService.SaveMovement</c> istemciden gelen
/// araç id'sini HİÇ doğrulamıyordu. Satır doğru <c>company_id</c> alıyordu (firma verisi karışmıyordu) ama
/// BAŞKA firmanın aracına referans veren kayıt oluşabiliyordu; liste JOIN'leri de aracın firmasını
/// süzmediği için o aracın iç kodu/plakası ekranda görünebiliyordu.
///
/// Projenin kendi emsali kullanıldı: <c>EnsureVehicleOwned</c> (MaintenanceService:85,
/// MaintenanceDefinitionService:71/192) ve <c>JOIN vehicles … AND v.company_id</c> (MaintenanceService:337).
///
/// NOT — bakım ve "ilave yağ/filtre/tamir" akışları bu korumayı <c>MaintenanceService.Save</c> üzerinden
/// ZATEN alıyordu; boşluk yalnız hareket/transfer ve muayene akışlarındaydı.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiGroup5TenantTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "GRUP5-A";
    private const string UserA = "grup5_a";
    private const string CompanyB = "GRUP5-B";
    private const string UserB = "grup5_b";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!;
    private string _vehicleA = "", _vehicleB = "", _branchA = "";

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

        EnsureCompany(CompanyA);
        EnsureCompany(CompanyB);
        var uidA = svc.Users.EnsureInitialAdmin(CompanyA, UserA, Pass, RoleKeys.CompanyAdmin);
        var uidB = svc.Users.EnsureInitialAdmin(CompanyB, UserB, Pass, RoleKeys.CompanyAdmin);

        var sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(uidB, CompanyB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _branchA = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("A Şantiye"));
        var branchB = svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("B Şantiye"));

        _vehicleA = svc.Vehicles.Create(sa, new NewVehicle("A-01", "34AAA01", 2020, 100, "km", _branchA));
        _vehicleB = svc.Vehicles.Create(sb, new NewVehicle("B-01", "34BBB01", 2020, 100, "km", branchB));

        _a = await _host.LoginAsync(UserA, Pass, CompanyA);
        _b = await _host.LoginAsync(UserB, Pass, CompanyB);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private Task<HttpResponseMessage> SaveInspectionAsync(HttpClient c, string vehicleId) =>
        c.PostAsJsonAsync("/api/inspection", new
        {
            vehicleId, docType = "inspection", lastDate = (long?)null, nextDate = (long?)null,
            result = "Geçti", place = "QA", note = (string?)null,
        });

    private Task<HttpResponseMessage> SaveMovementAsync(HttpClient c, string vehicleId) =>
        c.PostAsJsonAsync("/api/daily/movement", new
        {
            movementKind = "movement", vehicleId, fromLocationId = (string?)null, toLocationId = (string?)null,
            operatorId = (string?)null, durationDays = (int?)null, description = "QA hareket",
            activityDate = (long?)null,
        });

    // ── B-2: muayene ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task B2_Kendi_Araciyla_Muayene_Kaydi_Olusur()
    {
        var r = await SaveInspectionAsync(_a, _vehicleA);
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task B2_Yabanci_Aracla_Muayene_Kaydi_REDDEDILIR()
    {
        // A firması, B firmasının araç id'siyle muayene kaydetmeye çalışır.
        var r = await SaveInspectionAsync(_a, _vehicleB);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Araç bulunamadı veya başka firmaya ait.",
            (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task B2_Olmayan_Arac_Idsi_De_REDDEDILIR()
    {
        var r = await SaveInspectionAsync(_a, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task B2_Reddedilen_Kayit_Listeye_SIZMAZ()
    {
        await SaveInspectionAsync(_a, _vehicleB);   // reddedilir

        var list = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/inspection"));
        Assert.Empty(list.EnumerateArray());

        // B firmasının listesi de etkilenmemeli.
        var listB = await ApiTestHost.JsonAsync(await _b.GetAsync("/api/inspection"));
        Assert.Empty(listB.EnumerateArray());
    }

    // ── B-3: günlük faaliyet (hareket/transfer) ─────────────────────────────────────────────

    [Fact]
    public async Task B3_Kendi_Araciyla_Hareket_Kaydi_Olusur()
    {
        var r = await SaveMovementAsync(_a, _vehicleA);
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task B3_Yabanci_Aracla_Hareket_Kaydi_REDDEDILIR()
    {
        var r = await SaveMovementAsync(_a, _vehicleB);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Araç bulunamadı veya başka firmaya ait.",
            (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task B3_Aracsiz_Hareket_Kaydi_Calismaya_Devam_Eder()
    {
        // vehicleId opsiyoneldir (yalnız konum/operatör girilebilir) → kontrol bunu ENGELLEMEMELİ.
        var r = await _a.PostAsJsonAsync("/api/daily/movement", new
        {
            movementKind = "movement", vehicleId = (string?)null, fromLocationId = (string?)null,
            toLocationId = (string?)null, operatorId = (string?)null, durationDays = (int?)null,
            description = "araçsız hareket", activityDate = (long?)null,
        });

        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task B3_Reddedilen_Hareket_Listeye_SIZMAZ()
    {
        await SaveMovementAsync(_a, _vehicleB);   // reddedilir

        var list = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/daily"));
        Assert.Empty(list.EnumerateArray());
    }

    // ── B-4: liste JOIN'i yabancı araç bilgisini göstermemeli ───────────────────────────────

    [Fact]
    public async Task B4_Gecmiste_Olusmus_Yabanci_Referans_Muayene_Listesinde_GORUNMEZ()
    {
        // B-2 ÖNCESİ oluşmuş bir kaydı taklit et: doğrudan veritabanına yabancı araç referansı yaz.
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO vehicle_inspections(id, company_id, vehicle_id, doc_type, last_date, next_date, " +
                "result, place, note, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,@c,@v,'inspection',NULL,NULL,NULL,NULL,NULL,1,1,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", CompanyA);      // satır A firmasına ait
            cmd.AddWithValue("@v", _vehicleB);     // ama araç B firmasının
            cmd.ExecuteNonQuery();
        }

        var list = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/inspection"));

        // JOIN artık aracın firmasını da süzüyor → B firmasının iç kodu/plakası A'ya GÖSTERİLMEZ.
        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task B4_Gecmiste_Olusmus_Yabanci_Referans_Gunluk_Listesinde_Arac_Bilgisi_VERMEZ()
    {
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO daily_activities(id, company_id, activity_type, movement_kind, vehicle_id, " +
                "from_location_id, to_location_id, operator_id, duration_days, description, maintenance_id, " +
                "stock_processed, activity_date, operation_id, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,@c,'movement','movement',@v,NULL,NULL,NULL,NULL,'eski kayıt',NULL,0,1,@op,1,1,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", CompanyA);
            cmd.AddWithValue("@v", _vehicleB);
            cmd.AddWithValue("@op", Guid.NewGuid().ToString("N"));
            cmd.ExecuteNonQuery();
        }

        var list = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/daily"));

        // Kayıt A'nın olduğu için listede kalır (veri kaybı yok) — ama LEFT JOIN artık eşleşmediğinden
        // yabancı aracın iç kodu/plakası GÖRÜNMEZ.
        var row = Assert.Single(list.EnumerateArray().ToList());
        var vehicleText = row.GetProperty("vehicleText").GetString();
        Assert.Equal("—", vehicleText);
        Assert.DoesNotContain("B-01", vehicleText);
        Assert.DoesNotContain("34BBB01", vehicleText);
    }
}
