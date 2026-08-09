using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ÇOK MALZEMELİ STOK İŞLEMİ — API HATTI (İş #8, 2026-08-09).
///
/// Servis testleri (<see cref="MultiMaterialStockTests"/>) çekirdeği kanıtlar. Web ve masaüstü ise HTTP'den
/// gider; burada <c>lines</c> alanının bağlanması, eski tek malzemeli gövdenin bozulmaması ve doğrulamaların
/// iki yolda da aynı çalışması test edilir.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiMultiMaterialTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "COKMAT-A";
    private const string Admin = "cokmat_admin";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private ServerServices _svc = null!;
    private SessionContext _s = null!;
    private string _m1 = "", _m2 = "", _personnel = "", _branchA = "", _branchB = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = _svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _branchA = _svc.Branches.Create(_s, new NewBranch("Merkez"));
        _branchB = _svc.Branches.Create(_s, new NewBranch("Şantiye"));
        _personnel = _svc.Personnel.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Depocu", null, null, null));

        _m1 = _svc.Materials.Create(_s, new NewMaterial("CM-1", "Filtre"));
        _m2 = _svc.Materials.Create(_s, new NewMaterial("CM-2", "Yağ"));
        _svc.OpeningStock.RecordOpening(_s, _m1, 100m, "op-1", branchId: _branchA);
        _svc.OpeningStock.RecordOpening(_s, _m2, 100m, "op-2", branchId: _branchA);

        _client = await _host.LoginAsync(Admin, Pass, Company, _branchA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private decimal Balance(string materialId) => _svc.Stock.GetBalance(_s, materialId);

    private int DocumentCount()
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_documents WHERE company_id=@c;";
        cmd.AddWithValue("@c", Company);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task Cikis_lines_ile_TEK_belge_olusturur()
    {
        var before = DocumentCount();
        var r = await _client.PostAsJsonAsync("/api/stock/issue", new
        {
            lines = new[] { new { materialId = _m1, quantity = 5m }, new { materialId = _m2, quantity = 3m } },
            branchId = _branchA, personnelId = _personnel,
        });
        r.EnsureSuccessStatusCode();

        Assert.Equal(before + 1, DocumentCount());   // iki malzeme → tek belge
        Assert.Equal(95m, Balance(_m1));
        Assert.Equal(97m, Balance(_m2));
    }

    [Fact]
    public async Task Cikis_ESKI_tek_malzemeli_govdeyle_calismaya_devam_eder()
    {
        var r = await _client.PostAsJsonAsync("/api/stock/issue", new
        {
            materialId = _m1, quantity = 5m, branchId = _branchA, personnelId = _personnel,
        });
        r.EnsureSuccessStatusCode();
        Assert.Equal(95m, Balance(_m1));
    }

    [Fact]
    public async Task Ayni_malzeme_iki_kez_gonderilirse_TEK_satirda_toplanir()
    {
        var r = await _client.PostAsJsonAsync("/api/stock/issue", new
        {
            lines = new[] { new { materialId = _m1, quantity = 5m }, new { materialId = _m1, quantity = 2m } },
            branchId = _branchA, personnelId = _personnel,
        });
        r.EnsureSuccessStatusCode();
        Assert.Equal(93m, Balance(_m1));   // 5 + 2 = 7 düşüldü

        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id=@c AND material_id=@m AND direction=-1;";
        cmd.AddWithValue("@c", Company); cmd.AddWithValue("@m", _m1);
        Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));   // iki değil TEK hareket
    }

    [Fact]
    public async Task Transfer_lines_ile_TEK_belge_olusturur()
    {
        var before = DocumentCount();
        var r = await _client.PostAsJsonAsync("/api/stock/transfer", new
        {
            lines = new[] { new { materialId = _m1, quantity = 10m }, new { materialId = _m2, quantity = 4m } },
            fromBranchId = _branchA, toBranchId = _branchB, personnelId = _personnel,
        });
        r.EnsureSuccessStatusCode();

        Assert.Equal(before + 1, DocumentCount());
        Assert.Equal(100m, Balance(_m1));   // transfer firma toplamını değiştirmez
    }

    [Fact]
    public async Task Yetersiz_stoklu_satir_TUM_belgeyi_reddeder()
    {
        var before = DocumentCount();
        var r = await _client.PostAsJsonAsync("/api/stock/issue", new
        {
            lines = new[] { new { materialId = _m1, quantity = 5m }, new { materialId = _m2, quantity = 500m } },
            branchId = _branchA, personnelId = _personnel,
        });
        Assert.False(r.IsSuccessStatusCode);

        Assert.Equal(100m, Balance(_m1));   // yarım belge YOK
        Assert.Equal(before, DocumentCount());
    }

    [Fact]
    public async Task Gecersiz_satir_reddedilir()
    {
        var bosMalzeme = await _client.PostAsJsonAsync("/api/stock/issue", new
        {
            lines = new[] { new { materialId = "", quantity = 5m } },
            branchId = _branchA, personnelId = _personnel,
        });
        Assert.Equal(HttpStatusCode.BadRequest, bosMalzeme.StatusCode);

        var sifirMiktar = await _client.PostAsJsonAsync("/api/stock/issue", new
        {
            lines = new[] { new { materialId = _m1, quantity = 0m } },
            branchId = _branchA, personnelId = _personnel,
        });
        Assert.Equal(HttpStatusCode.BadRequest, sifirMiktar.StatusCode);
    }
}
