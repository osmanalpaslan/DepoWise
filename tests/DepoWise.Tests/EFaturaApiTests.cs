using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-52 — e-FATURA İÇE ALMA API (web'in yolu) + İKİ ORTAM PARİTESİ ═══
///  EA1 — önizleme özeti döner; içe alma faturayı oluşturur; ikinci içe alma kopya üretmez; bozuk dosya 400
///  EA2 — iki ortam aynı özet metnini (servisteki Ozet) ve aynı onay düğmesini kullanır
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class EFaturaApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "EF-API";
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
        svc.Users.EnsureInitialAdmin(Company, "ef_api", "Test!2026", RoleKeys.CompanyAdmin);
        _client = await _host.LoginAsync("ef_api", "Test!2026", Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    [Fact]
    public async Task EA1_Onizleme_Iceal_Tekrar_Ve_Bozuk_Dosya()
    {
        var xml = EFaturaTests.Ornek(uuid: "6f1f6a0e-2222-4c3b-9d8e-0000000000a1");
        var on = await ApiTestHost.JsonAsync(await _client.PostAsJsonAsync("/api/invoices/efatura/preview", new { xml }));
        Assert.Equal("ABC2026000000123", on.GetProperty("faturaNo").GetString());
        Assert.Contains("Yıldız Hırdavat Ltd. Şti.", on.GetProperty("ozet").GetString());
        Assert.False(on.GetProperty("dahaOnceAlindi").GetBoolean());

        var r1 = await ApiTestHost.JsonAsync(await _client.PostAsJsonAsync("/api/invoices/efatura/import", new { xml }));
        Assert.False(r1.GetProperty("alreadyExisted").GetBoolean());
        var r2 = await ApiTestHost.JsonAsync(await _client.PostAsJsonAsync("/api/invoices/efatura/import", new { xml }));
        Assert.True(r2.GetProperty("alreadyExisted").GetBoolean());
        Assert.Equal(r1.GetProperty("id").GetString(), r2.GetProperty("id").GetString());

        var bozuk = await _client.PostAsJsonAsync("/api/invoices/efatura/preview", new { xml = "<bozuk" });
        Assert.Equal(HttpStatusCode.BadRequest, bozuk.StatusCode);
    }

    [Fact]
    public void EA2_Iki_Ortam_Ayni_Ozet_Ve_Onay()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        string Oku(string rel) => File.ReadAllText(Path.Combine(d!.FullName, rel));
        var mas = Oku("src/DepoWise.Desktop/ViewModels/InvoicesViewModel.cs");
        var web = Oku("src/DepoWise.Web/Components/Pages/Invoices.razor");
        Assert.Contains("ConfirmService.AskAsync(t.Ozet, \"e-Fatura İçe Al\", \"Evet, İçe Al\")", mas);
        Assert.Contains("ozet = t.Ozet", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("Dialog.Confirm(ozet, \"Evet, İçe Al\")", web);
        foreach (var k in new[] { "daha önce içe alınmış.", "e-Fatura içe alındı: " }) { Assert.Contains(k, mas); Assert.Contains(k, web); }
    }
}
