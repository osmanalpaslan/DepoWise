using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — <c>POST /api/assignments/issue-many</c> (web toplu zimmet teslimi) HTTP sözleşmesi:
/// başarılı toplu teslim · stok yetmezse 4xx + HİÇBİR kalem yazılmaz · kimliksiz istek reddedilir ·
/// başka firmanın personeline teslim edilemez (tenant).
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiZimmetTopluTeslimTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "ZTA", CoB = "ZTB", Pass = "Test!2026";
    private ServerServices _svc = null!;
    private HttpClient _a = null!;
    private SessionContext _sa = null!;
    private string _depo = "", _m1 = "", _m2 = "", _ali = "", _yabanci = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        var uidA = _svc.Users.EnsureInitialAdmin(CoA, "zta_admin", Pass, RoleKeys.CompanyAdmin);
        var uidB = _svc.Users.EnsureInitialAdmin(CoB, "ztb_admin", Pass, RoleKeys.CompanyAdmin);
        _sa = new SessionContext(uidA, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(uidB, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depo = _svc.Branches.Create(_sa, new NewBranch("ZT Depo"));
        _m1 = _svc.Materials.Create(_sa, new NewMaterial("ZT-1", "Matkap"));
        _m2 = _svc.Materials.Create(_sa, new NewMaterial("ZT-2", "Eldiven"));
        _ali = _svc.Personnel.Create(_sa, new NewPersonnel("Ali ZT", null, null, null));
        _yabanci = _svc.Personnel.Create(sb, new NewPersonnel("Yabancı ZT", null, null, null));
        _svc.Stock.ReceiveIn(_sa, new[] { new StockLine(_m1, 10m), new StockLine(_m2, 10m) }, "zt-acilis", branchId: _depo);
        _a = await _host.LoginAsync("zta_admin", Pass, CoA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private object Govde(string personnelId, decimal q2, string op) => new
    {
        personnelId,
        lines = new object[] { new { assetType = "material", assetId = _m1, quantity = 2m }, new { assetType = "material", assetId = _m2, quantity = q2 } },
        branchId = _depo, docDate = (long?)null, note = "api toplu", operationId = op,
    };

    [Fact]
    public async Task Toplu_teslim_basarili_ve_iki_kalem_zimmetlenir()
    {
        var r = await _a.PostAsJsonAsync("/api/assignments/issue-many", Govde(_ali, 3m, "zt-op-1"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var kimde = _svc.Assignments.Holdings(_sa, personnelId: _ali);
        Assert.Equal(2, kimde.Count);
    }

    [Fact]
    public async Task Stok_yetmezse_hata_ve_hicbir_kalem_yazilmaz()
    {
        var r = await _a.PostAsJsonAsync("/api/assignments/issue-many", Govde(_ali, 999m, "zt-op-2"));
        Assert.False(r.IsSuccessStatusCode);
        Assert.Empty(_svc.Assignments.Holdings(_sa, personnelId: _ali));
    }

    [Fact]
    public async Task Baska_firmanin_personeline_teslim_edilemez()
    {
        var r = await _a.PostAsJsonAsync("/api/assignments/issue-many", Govde(_yabanci, 1m, "zt-op-3"));
        Assert.False(r.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Kimliksiz_istek_reddedilir()
    {
        var r = await _host.CreateClient().PostAsJsonAsync("/api/assignments/issue-many", Govde(_ali, 1m, "zt-op-4"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
