using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-70 (AR-GE 2026-10-26, F-021) — MASAÜSTÜ ↔ WEB SONUÇ EŞİTLİĞİ (AYNI TOHUM VERİ) ═══
/// Parite testlerinin çoğu kaynak taramasıydı. Burada AYNI veride masaüstünün çağırdığı servis sonucu ile web'in aldığı API yanıtı
/// karşılaştırılır (masaüstü yerel servisi, web API'yi kullanır; ikisi aynı servis koduna gider — fark uçtaki dönüşümdedir).
///  SP1 — stok: depo kırılımı ve toplam servis = API
///  SP2 — yakıt "Toplam Alınan / Dağıtılan": 205 dağıtım + 1 iptal → servis = API = gerçek toplam (eskiden web son 200 kaydın,
///        masaüstü görünen sayfanın toplamını gösteriyordu — bu test o ayrışmayı yakaladı)
///  SP3 — bakım listesi, cari bakiye, yakıt toplamı: masaüstü ve API AYNI servis yöntemini çağırır (dönüşümsüz geçiş)
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ServisApiPariteTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "PAR-A";
    private const string User = "par_yonetici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private SessionContext _s = null!;
    private string _mat = "", _depoA = "", _depoB = "", _personel = "";

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        using (var conn = Svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = Svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depoA = Svc.Branches.Create(_s, new NewBranch("Depo A"));
        _depoB = Svc.Branches.Create(_s, new NewBranch("Şantiye B"));
        _mat = Svc.Materials.Create(_s, new NewMaterial("PAR-1", "Parite malzemesi"));
        _personel = Svc.Personnel.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Depocu", null, null, null));
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static string Op() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SP1_Stok_Depo_Kirilimi_Ve_Toplam_Servis_Esit_API()
    {
        Svc.Stock.ReceiveIn(_s, new[] { new StockLine(_mat, 12.5m) }, Op(), branchId: _depoA);
        Svc.Stock.ReceiveIn(_s, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);
        Svc.Stock.IssueOut(_s, new[] { new StockLine(_mat, 2.25m) }, Op(), branchId: _depoA);

        var servis = Svc.Stock.GetLocationBalances(_s, _mat);
        var api = await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/stock/balance/{_mat}/locations"));
        var apiSatirlar = api.GetProperty("locations").EnumerateArray()
            .Select(x => (x.GetProperty("locationId").GetString(), x.GetProperty("quantity").GetDecimal())).ToList();
        Assert.Equal(servis.Select(x => ((string?)x.LocationId, x.Quantity)).ToList(), apiSatirlar);
        Assert.Equal(Svc.Stock.GetBalance(_s, _mat), api.GetProperty("total").GetDecimal());
        Assert.Equal(14.25m, api.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task SP2_Yakit_Toplamlari_Tum_Kayitlar_Servis_Esit_API()
    {
        var arac = Svc.Vehicles.Create(_s, new NewVehicle("PAR-01", "34PAR01", 2020, 0, "km", _depoA));
        Svc.Fuel.AddDepotEntry(_s, new NewDepotEntry(5000m, 40m), Op());
        Svc.Fuel.AddDepotEntry(_s, new NewDepotEntry(1000m, 41m), Op());
        var ids = new List<string>();
        for (var i = 1; i <= 206; i++)
            ids.Add(Svc.Fuel.Distribute(_s, new NewDistribution(arac, 2m, i * 10m, PersonnelId: _personel), Op()));
        Svc.Fuel.CancelDistribution(_s, ids[^1], "yanlış giriş");          // iptal toplamlara girmez → 205 × 2 = 410

        var (alinan, dagitilan) = Svc.Fuel.Toplamlar(_s);                    // masaüstünün çağırdığı hesap
        var api = await ApiTestHost.JsonAsync(await _client.GetAsync("/api/fuel/summary"));
        Assert.Equal(6000m, alinan);
        Assert.Equal(410m, dagitilan);                                       // eskiden web: son 200 kayıt = 400
        Assert.Equal(alinan, api.GetProperty("totalReceived").GetDecimal());
        Assert.Equal(dagitilan, api.GetProperty("totalDistributed").GetDecimal());
        Assert.Equal(alinan - dagitilan, api.GetProperty("depotBalance").GetDecimal());   // bakiye ile toplamlar tutarlı
    }

    [Fact]
    public void SP3_Masaustu_Ve_API_Ayni_Servis_Yontemini_Cagirir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        string Oku(string rel) => File.ReadAllText(Path.Combine(kok!.FullName, rel));
        var api = Oku("src/DepoWise.Api/Program.cs");
        var yakit = Oku("src/DepoWise.Desktop/ViewModels/FuelViewModel.cs");
        Assert.Contains("(TotalReceived, TotalDistributed) = DesktopServices.Fuel.Toplamlar(_session);", yakit);
        Assert.Contains("var (received, distributed) = svc.Fuel.Toplamlar(s);", api);
        Assert.DoesNotContain("Distributions.Where(x => !x.IsCancelled).Sum(", yakit);   // sayfa toplamı geri gelmesin
        Assert.Matches(new Regex(@"svc\.Maintenance\.SearchMaintenancesGrid\(s,"), api);
        Assert.Contains("DesktopServices.Maintenance.SearchMaintenancesGrid(_session,", Oku("src/DepoWise.Desktop/ViewModels/MaintenanceViewModel.cs"));
        Assert.Contains("svc.PartyLedger.Balance(s, id,", api);
        Assert.Contains("DesktopServices.PartyLedger.Balance(_session,", Oku("src/DepoWise.Desktop/ViewModels/PartiesViewModel.cs"));
    }
}
