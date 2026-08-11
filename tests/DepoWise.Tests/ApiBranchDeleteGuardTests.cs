using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-08 (PRT-01 Grup 6, 2026-08-11) — BAĞLI KAYDI OLAN ŞUBE SİLİNEMEZ (KARAR-G6-C).
///
/// Bulunan durum: şube silme yalnız KULLANICILARIN branch_id'sini boşaltıyordu; personel ve araç
/// referanslarına dokunmuyor, uyarı da vermiyordu. Araçta branch_id ZORUNLU olduğu için (RequireVehicleFields)
/// sonuç, silinmiş şubeye bakan ve düzenlenmesi zorlaşan araçlardı.
///
/// Karar: bağlı araç/personel varsa silme reddedilir ve KAÇ tane olduğu söylenir. Kullanıcı davranışı
/// (şubesi boşaltılır) bilinçli istisnadır — şubesiz kullanıcı geçerli bir durumdur.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiBranchDeleteGuardTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "SUB-A";
    private const string AdminA = "sub_a";
    private const string StaffA = "sub_a_personel";
    private const string CompanyB = "SUB-B";
    private const string AdminB = "sub_b";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _a = null!;
    private SessionContext _sa = null!, _sb = null!;
    private string _mainBranchA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        var uidA = _svc.Users.EnsureInitialAdmin(CompanyA, AdminA, Pass, RoleKeys.CompanyAdmin);
        _svc.Users.EnsureInitialAdmin(CompanyA, StaffA, Pass, RoleKeys.Staff);
        var uidB = _svc.Users.EnsureInitialAdmin(CompanyB, AdminB, Pass, RoleKeys.CompanyAdmin);
        _sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sb = new SessionContext(uidB, CompanyB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mainBranchA = _svc.Branches.Create(_sa, new NewBranch("A Ana Şube"));
        _a = await _host.LoginAsync(AdminA, Pass, CompanyA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private string NewBranchA(string name) => _svc.Branches.Create(_sa, new NewBranch(name));

    private Task<HttpResponseMessage> DeleteBranchAsync(HttpClient c, string id)
        => c.DeleteAsync($"/api/branches/{id}");

    private static async Task<string> ErrorAsync(HttpResponseMessage r)
        => (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString() ?? "";

    [Fact]
    public async Task Bos_Sube_SILINEBILIR()
    {
        var id = NewBranchA("Boş Şube");
        (await DeleteBranchAsync(_a, id)).EnsureSuccessStatusCode();

        var list = (await ApiTestHost.JsonAsync(await _a.GetAsync("/api/branches"))).ToString();
        Assert.DoesNotContain("Boş Şube", list);
    }

    [Fact]
    public async Task Personelli_Sube_SILINEMEZ()
    {
        var id = NewBranchA("Personelli Şube");
        _svc.Personnel.Create(_sa, new NewPersonnel("Ali Veli", null, null, id, true, false));

        var r = await DeleteBranchAsync(_a, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var err = await ErrorAsync(r);
        Assert.Contains("1 personel", err);
        Assert.DoesNotContain("araç", err);   // olmayan bağımlılık sayılmaz
    }

    [Fact]
    public async Task Aracli_Sube_SILINEMEZ()
    {
        var id = NewBranchA("Araçlı Şube");
        (await _a.PostAsJsonAsync("/api/vehicles", new
        {
            internalCode = "SUB-V1", plate = (string?)null, productionYear = 2020, currentMeter = 0m,
            meterUnit = "km", branchId = id, driverPersonnelId = (string?)null,
        })).EnsureSuccessStatusCode();

        var r = await DeleteBranchAsync(_a, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("1 araç", await ErrorAsync(r));
    }

    [Fact]
    public async Task Arac_Ve_Personelli_Subede_IKISI_DE_Sayilir()
    {
        var id = NewBranchA("Dolu Şube");
        _svc.Personnel.Create(_sa, new NewPersonnel("Ali Veli", null, null, id, true, false));
        _svc.Personnel.Create(_sa, new NewPersonnel("Ayşe Yılmaz", null, null, id, true, false));
        (await _a.PostAsJsonAsync("/api/vehicles", new
        {
            internalCode = "SUB-V2", plate = (string?)null, productionYear = 2020, currentMeter = 0m,
            meterUnit = "km", branchId = id, driverPersonnelId = (string?)null,
        })).EnsureSuccessStatusCode();

        var err = await ErrorAsync(await DeleteBranchAsync(_a, id));

        Assert.Contains("1 araç", err);
        Assert.Contains("2 personel", err);
    }

    [Fact]
    public async Task SILINMIS_Arac_Ve_Personel_Engel_OLUSTURMAZ()
    {
        var id = NewBranchA("Temizlenmiş Şube");
        var pid = _svc.Personnel.Create(_sa, new NewPersonnel("Giden Kişi", null, null, id, true, false));
        _svc.Personnel.SoftDelete(_sa, pid);

        (await DeleteBranchAsync(_a, id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Baska_Firmanin_Kayitlari_SAYILMAZ()
    {
        // B firmasında aynı ada sahip bir şube ve ona bağlı personel — A'nın şubesini etkilememeli.
        var idA = NewBranchA("Ortak Ad");
        var idB = _svc.Branches.Create(_sb, new NewBranch("Ortak Ad"));
        _svc.Personnel.Create(_sb, new NewPersonnel("B Personeli", null, null, idB, true, false));

        (await DeleteBranchAsync(_a, idA)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Kullanicili_Sube_SILINEBILIR_Ve_Kullanicinin_Subesi_BOSALIR()
    {
        // Bilinçli istisna: şubesiz kullanıcı geçerli bir durumdur; mevcut davranış korunur.
        var id = NewBranchA("Kullanıcılı Şube");
        var r = await _a.PostAsJsonAsync("/api/users", new
        {
            username = "sube_kullanicisi", password = "Kul!2026", fullName = (string?)null,
            roleKeys = new[] { RoleKeys.Staff }, companyId = (string?)null, branchId = id, canViewAllBranches = false,
        });
        r.EnsureSuccessStatusCode();

        (await DeleteBranchAsync(_a, id)).EnsureSuccessStatusCode();

        var row = (await ApiTestHost.JsonAsync(await _a.GetAsync("/api/users"))).EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == "sube_kullanicisi");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("branchId").ValueKind);
    }

    [Fact]
    public async Task Yetkisiz_Kullanici_Sube_SILEMEZ()
    {
        var id = NewBranchA("Korunan Şube");
        var staff = await _host.LoginAsync(StaffA, Pass, CompanyA, _mainBranchA);

        Assert.True(ApiTestHost.IsDenied(await DeleteBranchAsync(staff, id)));

        var list = (await ApiTestHost.JsonAsync(await _a.GetAsync("/api/branches"))).ToString();
        Assert.Contains("Korunan Şube", list);
    }
}
