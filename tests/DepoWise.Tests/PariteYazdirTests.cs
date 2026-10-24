using System.Net;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-55 (AR-GE 2026-10-20, 05 F-054 / F-055) — WEB ↔ MASAÜSTÜ ÇIKTI VE ZORUNLU ALAN EŞİTLİĞİ ═══
///  PY1 — Web'in Yazdır düğmelerinin çağırdığı uçlar (Excel ile aynı uç + format=pdf) gerçekten PDF döndürür; süzgeçli adreste de
///  PY2 — Web'de 6 listede Yazdır, Excel ile AYNI süzgeç yolunu kullanır (ortak Disari); masaüstü Muayene'de Yazdır (kaynak kilidi)
///  PY3 — İki ortamda zorunlu olan 4 alan masaüstünde de "*" ile işaretli (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class PariteYazdirTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "PYZ-A", Admin = "pyz_admin", Pass = "Test!2026";
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _client = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    [Theory]
    [InlineData("/api/materials/grid/export?format=pdf")]
    [InlineData("/api/vehicles/grid/export?format=pdf")]
    [InlineData("/api/daily/grid/export?format=pdf")]
    [InlineData("/api/personnel/export?format=pdf")]
    [InlineData("/api/purchasing/export?format=pdf")]
    [InlineData("/api/work-orders/export?format=pdf")]
    [InlineData("/api/inspection/export?format=pdf")]
    [InlineData("/api/personnel/export?search=yok-boyle-biri&format=pdf")]
    [InlineData("/api/work-orders/export?status=open&format=pdf")]
    public async Task PY1_Yazdir_Uclari_Pdf_Dondurur(string yol)
    {
        var r = await _client.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        var b = await r.Content.ReadAsByteArrayAsync();
        Assert.True(b.Length > 4 && b[0] == (byte)'%' && b[1] == (byte)'P' && b[2] == (byte)'D' && b[3] == (byte)'F');
        // Excel karşılığı aynı uçtan (format olmadan) gelir → iki çıktı aynı süzgeçten
        var excel = await _client.GetAsync(yol.Replace("&format=pdf", "").Replace("?format=pdf", ""));
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excel.Content.Headers.ContentType?.MediaType);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Theory]
    [InlineData("Materials")]
    [InlineData("Vehicles")]
    [InlineData("Daily")]
    [InlineData("Personnel")]
    [InlineData("Purchasing")]
    [InlineData("WorkOrders")]
    public void PY2_Web_Yazdir_Excel_Ile_Ayni_Yol(string sayfa)
    {
        var t = Oku($"src/DepoWise.Web/Components/Pages/{sayfa}.razor");
        Assert.Contains("OnClick=\"Yazdir\">Yazdır", t);
        Assert.Contains("private Task ExportExcel() => Disari(false);", t);
        Assert.Contains("private Task Yazdir() => Disari(true);", t);
        Assert.Contains("Api.GetFileAsync(Bicim(", t);
        Assert.Contains("\"format=pdf\"", t);
    }

    [Fact]
    public void PY2b_Masaustu_Muayene_Yazdir()
    {
        Assert.Contains("Command=\"{Binding YazdirCommand}\"", Oku("src/DepoWise.Desktop/Views/InspectionView.axaml"));
        Assert.Contains("YazdirmaYardimcisi.YazdirAsync(", Oku("src/DepoWise.Desktop/ViewModels/InspectionViewModel.cs"));
    }

    [Theory]
    [InlineData("VehiclesView.axaml", "<ctrl:FormField Label=\"Şantiye / Şube\" IsRequired=\"True\">")]
    [InlineData("StockEntryView.axaml", "<ctrl:FormField Label=\"Teslim Eden / Alan (Personel)\" Width=\"240\" IsVisible=\"{Binding ShowPersonnel}\" IsRequired=\"True\">")]
    [InlineData("FuelView.axaml", "<ctrl:FormField Label=\"Yakıtı Veren\" Width=\"180\" IsRequired=\"True\">")]
    [InlineData("StockCountView.axaml", "<ctrl:FormField Label=\"Sayım Gerekçesi\" Width=\"220\" IsRequired=\"True\">")]
    public void PY3_Zorunlu_Alan_Isaretleri(string dosya, string beklenen)
        => Assert.Contains(beklenen, Oku("src/DepoWise.Desktop/Views/" + dosya));
}
