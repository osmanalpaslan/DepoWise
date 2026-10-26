using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 2026-10-26 — İKİ ORTAM DENETİMİ: WEB'İN ÇALIŞMA ŞUBESİ SUNUCUDA (kullanıcı istekleri 2 ve 3) ═══
/// Masaüstü oturumu girişte seçilen şubeyi taşır; web oturumu taşımıyordu → birden çok şubeye yetkili kullanıcı web'de "Şube X"
/// ile girse de stok FİRMA toplamı görünüyor, "gönderen taraf teslim alamaz" kuralı sunucuda çalışmıyordu. Web artık
/// <c>X-Calisma-Subesi</c> başlığını gönderir; yalnız stok toplamı ve teslim/kayıp uçları kullanır.
///  CS1 — malzeme listesi + kart + araç kartı stoğu başlıktaki şubenin stoğu; başlık yoksa eski davranış (yetkili şubeler toplamı)
///  CS2 — başlık kapsamı GENİŞLETEMEZ: başka firmanın şubesi / uydurma kimlik yok sayılır (eski davranış, hata değil)
///  CS3 — gönderen şubeyle (başlık) teslim alma ve kayıp kapatma REDDEDİLİR (403); hedef şubeyle teslim alınır
///  CS5 — yalnız Şube A'ya yetkili personel başlıkla Şantiye B'yi isteyemez: başlık yok sayılır (403 değil, kendi stoğu)
///  CS4 — web her oturumlu istekte başlığı gönderir ("Tüm Şubeler"de göndermez); başlık yalnız seçili uçlarda okunur
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class CalismaSubesiApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "CS-A";
    private const string Other = "CS-B";
    private const string User = "cs_yonetici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!, _personelA = null!;
    private string _mat = "", _subeA = "", _subeB = "", _yabanci = "", _personel = "", _arac = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        Firma(svc, Company); Firma(svc, Other);

        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);   // tüm şubelere yetkili
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = svc.Materials.Create(s, new NewMaterial("MAT-CS", "Çalışma şubesi malzemesi"));
        _subeA = svc.Branches.Create(s, new NewBranch("Şube A"));
        _subeB = svc.Branches.Create(s, new NewBranch("Şantiye B"));
        _personel = svc.Personnel.Create(s, new DepoWise.Infrastructure.Org.NewPersonnel("Depo Sorumlusu", null, null, null));
        _arac = svc.Vehicles.Create(s, new NewVehicle("CS-01", "34CS01", 2020, 100, "km", _subeA));
        svc.Materials.SetCompatibleVehicles(s, _mat, new[] { _arac });

        var otherUid = svc.Users.EnsureInitialAdmin(Other, "cs_b", Pass, RoleKeys.CompanyAdmin);
        var so = new SessionContext(otherUid, Other, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _yabanci = svc.Branches.Create(so, new NewBranch("Yabancı Şube"));

        _client = await _host.LoginAsync(User, Pass, Company);
        var pid = svc.Users.CreateUser(s, new NewUser("cs_depo_a", Pass, "Depo A", new[] { RoleKeys.Staff }, Company, BranchId: _subeA));
        svc.Permissions.SaveForUser(s, pid, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());
        _personelA = await _host.LoginAsync("cs_depo_a", Pass, Company, _subeA);
        // Stok: Şube A'da 10, Şantiye B'de 0.
        (await _client.PostAsJsonAsync("/api/stock/receive", new
        {
            operationId = "cs-giris", materialId = _mat, code = "MAT-CS", name = "Çalışma şubesi malzemesi",
            quantity = 10m, unitPrice = 0m, branchId = _subeA, personnelId = _personel,
        })).EnsureSuccessStatusCode();
    }

    private static void Firma(ServerServices svc, string id)
    {
        using var conn = svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<HttpResponseMessage> Gonder(HttpMethod m, string url, string? sube, object? govde = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (sube is not null) r.Headers.TryAddWithoutValidation("X-Calisma-Subesi", sube);
        if (govde is not null) r.Content = JsonContent.Create(govde);
        return await _client.SendAsync(r);
    }

    private async Task<(decimal Liste, decimal Kart, decimal AracKarti)> Stoklar(string? sube)
    {
        var liste = await ApiTestHost.JsonAsync(await Gonder(HttpMethod.Get, "/api/materials/grid?page=1&pageSize=25&code=MAT-CS", sube));
        var kart = await ApiTestHost.JsonAsync(await Gonder(HttpMethod.Get, $"/api/materials/{_mat}", sube));
        var arac = await ApiTestHost.JsonAsync(await Gonder(HttpMethod.Get, $"/api/vehicles/{_arac}/materials", sube));
        return (liste.GetProperty("items")[0].GetProperty("stock").GetDecimal(),
                kart.GetProperty("stock").GetDecimal(),
                arac.EnumerateArray().Single(x => x.GetProperty("materialId").GetString() == _mat).GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task CS1_Stok_Calisma_Subesine_Gore()
    {
        Assert.Equal((10m, 10m, 10m), await Stoklar(_subeA));
        Assert.Equal((0m, 0m, 0m), await Stoklar(_subeB));          // stoğu olmayan şantiye → 0 (eskiden firma toplamı 10)
        Assert.Equal((10m, 10m, 10m), await Stoklar(null));         // başlık yok → eski davranış (yetkili şubeler toplamı)
    }

    [Fact]
    public async Task CS2_Baslik_Kapsami_Genisletemez_Gecersizi_Yok_Sayar()
    {
        Assert.Equal((10m, 10m, 10m), await Stoklar(_yabanci));     // başka firmanın şubesi → yok sayılır
        Assert.Equal((10m, 10m, 10m), await Stoklar("uydurma-sube"));
    }

    [Fact]
    public async Task CS3_Gonderen_Sube_Teslim_Alamaz_Kayip_Kapatamaz()
    {
        (await Gonder(HttpMethod.Post, "/api/stock/transfer", _subeA, new
        {
            operationId = "cs-transfer", materialId = _mat, quantity = 4m, fromBranchId = _subeA, toBranchId = _subeB, personnelId = _personel,
        })).EnsureSuccessStatusCode();
        var belge = (await ApiTestHost.JsonAsync(await Gonder(HttpMethod.Get, "/api/stock/in-transit", null))).EnumerateArray()
            .Single(t => t.GetProperty("toBranchId").GetString() == _subeB).GetProperty("documentId").GetString();

        var teslim = new { operationId = "cs-teslim", lines = new[] { new { materialId = _mat, quantity = 4m } } };
        var r1 = await Gonder(HttpMethod.Post, $"/api/stock/in-transit/{belge}/receive", _subeA, teslim);
        Assert.Equal(HttpStatusCode.Forbidden, r1.StatusCode);
        Assert.Contains("gönderen taraf teslim alamaz", await r1.Content.ReadAsStringAsync());
        var r2 = await Gonder(HttpMethod.Post, $"/api/stock/in-transit/{belge}/close-loss", _subeA, new { reason = "kayboldu" });
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
        Assert.Contains("gönderen taraf kapatamaz", await r2.Content.ReadAsStringAsync());
        Assert.Equal((6m, 6m, 6m), await Stoklar(_subeA));          // reddedilen işlemler stoğa dokunmadı
        Assert.Equal((0m, 0m, 0m), await Stoklar(_subeB));

        (await Gonder(HttpMethod.Post, $"/api/stock/in-transit/{belge}/receive", _subeB, teslim)).EnsureSuccessStatusCode();
        Assert.Equal((4m, 4m, 4m), await Stoklar(_subeB));
    }

    [Fact]
    public async Task CS5_Kisitli_Kullanici_Baska_Subeyi_Isteyemez()
    {
        foreach (var sube in new[] { _subeB, _subeA, null })
        {
            var r = new HttpRequestMessage(HttpMethod.Get, "/api/materials/grid?page=1&pageSize=25&code=MAT-CS");
            if (sube is not null) r.Headers.TryAddWithoutValidation("X-Calisma-Subesi", sube);
            var yanit = await _personelA.SendAsync(r);
            Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);                     // yok sayılır → hata değil
            Assert.Equal(10m, (await ApiTestHost.JsonAsync(yanit)).GetProperty("items")[0].GetProperty("stock").GetDecimal());   // kendi şubesi
        }
    }

    [Fact]
    public void CS4_Web_Basligi_Gonderir_Sunucu_Yalniz_Secili_Uclarda_Okur()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        var istemci = File.ReadAllText(Path.Combine(kok!.FullName, "src/DepoWise.Web/Services/ApiClient.cs"));
        var req = istemci[istemci.IndexOf("private HttpRequestMessage Req(", StringComparison.Ordinal)..];
        req = req[..req.IndexOf("return r;", StringComparison.Ordinal)];
        Assert.Contains("if (!string.IsNullOrEmpty(_auth.BranchId)) r.Headers.TryAddWithoutValidation(\"X-Calisma-Subesi\", _auth.BranchId);", req);

        var program = File.ReadAllText(Path.Combine(kok.FullName, "src/DepoWise.Api/Program.cs"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(program, "X-Calisma-Subesi\"\\]").Count);   // tek okuma noktası
        // Oturumun geneline uygulanmadı: Session()/S() başlığı okumaz.
        var oturum = program[program.IndexOf("SessionContext? Session(HttpContext ctx)", StringComparison.Ordinal)..];
        oturum = oturum[..oturum.IndexOf("return svc.SessionFor(company, userId);", StringComparison.Ordinal)];
        Assert.DoesNotContain("Calisma", oturum);
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(program, @"CalismaSubesi\((c|ctx), s0?\)").Count);   // pano, liste, dışa aktarım, kart, araç kartı, teslim, kayıp
    }
}
