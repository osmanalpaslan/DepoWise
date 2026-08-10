using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// B-7 (PRT-01 Grup 5, 2026-08-11) — ARAÇ KARTINDA YABANCI ŞUBE/PERSONEL REFERANSI.
///
/// Bulunan durum: <c>VehicleService.Create/Update</c> istemciden gelen <c>BranchId</c> ve
/// <c>DriverPersonnelId</c>'yi doğrulamıyordu. <c>PersonnelService</c> bunu ZATEN yapıyordu
/// (<c>ScopeResolver.EnsureBranchAllowed</c>) — araç tarafında eksikti. Ayrıca araç listelerindeki
/// <c>branches</c>/<c>personnel</c>/<c>vehicle_types</c>/<c>vehicle_categories</c> JOIN'leri aracın
/// firmasını süzmüyordu → yabancı kaydın ADI ekranda görünebiliyordu.
///
/// B-2/B-3/B-4 ile AYNI sınıf; aynı emsal desen kullanıldı (yeni erişim mimarisi yok).
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiVehicleTenantRefTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "ARAC-A";
    private const string UserA = "arac_a";
    private const string CompanyB = "ARAC-B";
    private const string UserB = "arac_b";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!;
    private ServerServices _svc = null!;
    private string _branchA = "", _branchB = "", _personA = "", _personB = "";

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

        var sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(uidB, CompanyB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _branchA = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("A Şantiye"));
        _branchB = _svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("B GİZLİ Şantiye"));
        _personA = _svc.Personnel.Create(sa, new NewPersonnel("A Sürücü", null, null, _branchA, true, false));
        _personB = _svc.Personnel.Create(sb, new NewPersonnel("B GİZLİ Sürücü", null, null, _branchB, true, false));

        _a = await _host.LoginAsync(UserA, Pass, CompanyA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private Task<HttpResponseMessage> CreateVehicleAsync(string code, string? branchId, string? driverId)
        => _a.PostAsJsonAsync("/api/vehicles", new
        {
            internalCode = code, plate = (string?)null, productionYear = 2020, currentMeter = 0m,
            meterUnit = "km", branchId, driverPersonnelId = driverId,
        });

    [Fact]
    public async Task Kendi_Sube_Ve_Surucusuyle_Arac_Olusur()
    {
        var r = await CreateVehicleAsync("A-OK", _branchA, _personA);
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Yabanci_Sube_Ile_Arac_Olusturulamaz()
    {
        var r = await CreateVehicleAsync("A-YABANCI-SUBE", _branchB, null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Şube bulunamadı veya başka firmaya ait.",
            (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Yabanci_Personel_Surucu_Olarak_Atanamaz()
    {
        var r = await CreateVehicleAsync("A-YABANCI-SRC", _branchA, _personB);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Personel bulunamadı veya başka firmaya ait.",
            (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Surucusuz_Arac_Calismaya_Devam_Eder()
    {
        // Sürücü OPSİYONELDİR → yeni kontrol bunu ENGELLEMEMELİ.
        // (Şube araçta ZORUNLUDUR — API'deki RequireVehicleFields kuralı; bu testin konusu değil.)
        var r = await CreateVehicleAsync("A-SURUCUSUZ", _branchA, null);
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Duzenlemede_De_Yabanci_Sube_Reddedilir()
    {
        (await CreateVehicleAsync("A-DUZ", _branchA, null)).EnsureSuccessStatusCode();
        var list = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/vehicles"));
        var v = list.EnumerateArray().First(x => x.GetProperty("internalCode").GetString() == "A-DUZ");
        var id = v.GetProperty("id").GetString();

        var r = await _a.PutAsJsonAsync($"/api/vehicles/{id}", new
        {
            plate = (string?)null, productionYear = 2020, status = "active", statusNote = (string?)null,
            branchId = _branchB, driverPersonnelId = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ── B-8: günlük faaliyette konum/operatör referansı ────────────────────────────────────

    private Task<HttpResponseMessage> SaveMovementAsync(string? fromId, string? toId, string? operatorId)
        => _a.PostAsJsonAsync("/api/daily/movement", new
        {
            movementKind = "movement", vehicleId = (string?)null, fromLocationId = fromId,
            toLocationId = toId, operatorId, durationDays = (int?)null,
            description = "B-8 testi", activityDate = (long?)null,
        });

    [Fact]
    public async Task B8_Kendi_Konum_Ve_Operatoruyle_Hareket_Olusur()
    {
        (await SaveMovementAsync(_branchA, _branchA, _personA)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task B8_Yabanci_Konum_Ile_Hareket_Olusturulamaz()
    {
        var r = await SaveMovementAsync(_branchB, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task B8_Yabanci_Operator_Ile_Hareket_Olusturulamaz()
    {
        var r = await SaveMovementAsync(_branchA, null, _personB);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task B8_Konumsuz_Operatorsuz_Hareket_Calismaya_Devam_Eder()
    {
        // Üçü de OPSİYONELDİR → yeni kontrol bunları ENGELLEMEMELİ.
        (await SaveMovementAsync(null, null, null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Gecmiste_Olusmus_Yabanci_Referans_Listede_ISIM_GOSTERMEZ()
    {
        // B-7 ÖNCESİ oluşmuş kaydı taklit et: aracı NORMAL yoldan oluştur (geçerli veri), sonra
        // doğrudan veritabanında yabancı şube/personele işaretle. Böylece satırın diğer alanları
        // gerçek servisin ürettiği biçimde kalır.
        (await CreateVehicleAsync("A-ESKI", _branchA, _personA)).EnsureSuccessStatusCode();
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "UPDATE vehicles SET branch_id=@br, driver_personnel_id=@drv " +
                "WHERE internal_code='A-ESKI' AND company_id=@c;";
            cmd.AddWithValue("@br", _branchB);    // B firmasının şubesi
            cmd.AddWithValue("@drv", _personB);   // B firmasının personeli
            cmd.AddWithValue("@c", CompanyA);
            cmd.ExecuteNonQuery();
        }

        // page/pageSize bu uçta ZORUNLUDUR (int, nullable değil) — web hep gönderir.
        var grid = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/vehicles/grid?page=1&pageSize=50"));
        var raw = grid.ToString();

        // Araç A firmasının olduğu için listede KALIR (veri kaybı yok) — ama yabancı ADLAR görünmez.
        Assert.Contains("A-ESKI", raw);
        Assert.DoesNotContain("B GİZLİ Şantiye", raw);
        Assert.DoesNotContain("B GİZLİ Sürücü", raw);
    }
}
