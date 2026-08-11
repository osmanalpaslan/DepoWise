using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-03 (FAZ C, 2026-08-11) — API LOKASYON SÖZLEŞMESİ, GERÇEK HTTP HATTI.
///
/// Servis testleri (<see cref="StockLocationTests"/>) bakiyenin veritabanı katmanında depo bazlı
/// çalıştığını kanıtlar. Web bunu HTTP üzerinden kullanır: JSON gövdedeki <c>branchId</c> servise
/// bağlanıyor mu, yabancı lokasyon <b>403</b> ile mi reddediliyor, yeni kırılım uçları doğru mu?
///
/// ⚠️ Masaüstü stok uçlarını KULLANMAZ (çevrimdışı; yerel servis + sync). Bu yüzden buradaki
/// sözleşme yalnız Web'i bağlar — masaüstü tarafı <see cref="StockLocationTests"/> ile kanıtlanır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiStockLocationTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "LOK-A";
    private const string Other = "LOK-B";
    private const string User = "lok_kullanici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private string _mat = "", _depoA = "", _depoB = "", _yabanciDepo = "", _personel = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        Company_(svc, Company); Company_(svc, Other);

        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = svc.Materials.Create(s, new NewMaterial("MAT-LOK", "Lokasyon malzemesi"));
        _depoA = svc.Branches.Create(s, new NewBranch("Depo A"));
        _depoB = svc.Branches.Create(s, new NewBranch("Depo B"));
        _personel = svc.Personnel.Create(s, new NewPersonnel("Depo Sorumlusu", null, null, null));

        // BAŞKA firmanın deposu — istemci bunu gönderirse sunucu reddetmeli.
        var otherUid = svc.Users.EnsureInitialAdmin(Other, "lok_b", Pass, RoleKeys.CompanyAdmin);
        var so = new SessionContext(otherUid, Other, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _yabanciDepo = svc.Branches.Create(so, new NewBranch("Yabancı Depo"));

        _client = await _host.LoginAsync(User, Pass, Company);
    }

    private static void Company_(ServerServices svc, string id)
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

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> ReceiveAsync(string? branchId, decimal qty, string op) =>
        _client.PostAsJsonAsync("/api/stock/receive", new
        {
            operationId = op, materialId = _mat, code = "MAT-LOK", name = "Lokasyon malzemesi",
            quantity = qty, unitPrice = 0m, branchId, personnelId = _personel,
        });

    private Task<HttpResponseMessage> IssueAsync(string? branchId, decimal qty, string op) =>
        _client.PostAsJsonAsync("/api/stock/issue", new
        {
            operationId = op, materialId = _mat, quantity = qty, branchId, personnelId = _personel,
        });

    private Task<HttpResponseMessage> TransferAsync(string? from, string? to, decimal qty, string op) =>
        _client.PostAsJsonAsync("/api/stock/transfer", new
        {
            operationId = op, materialId = _mat, quantity = qty,
            fromBranchId = from, toBranchId = to, personnelId = _personel,
        });

    private Task<HttpResponseMessage> CountAsync(string? branchId, decimal counted, string op) =>
        _client.PostAsJsonAsync("/api/stock/count", new
        {
            operationId = op, reason = "sayım", branchId,
            lines = new[] { new { materialId = _mat, countedQuantity = counted } },
        });

    private async Task<decimal> TotalAsync()
        => (await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/stock/balance/{_mat}")))
            .GetProperty("balance").GetDecimal();

    private async Task<decimal> AtAsync(string? locationId)
    {
        var url = $"/api/stock/balance/{_mat}/location" + (locationId is null ? "" : $"?locationId={locationId}");
        var r = await _client.GetAsync(url);
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("balance").GetDecimal();
    }

    private async Task<JsonElement> LocationsAsync()
    {
        var r = await _client.GetAsync($"/api/stock/balance/{_mat}/locations");
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    // ── 1-2. Giriş / çıkış lokasyonu ───────────────────────────────────────────────────────

    /// <summary>1 — Giriş, gövdedeki depoya yazılır (sunucu lokasyonu gerçekten kullanıyor).</summary>
    [Fact]
    public async Task Giris_GovdedekiDepoya_Yazilir()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-1")).EnsureSuccessStatusCode();
        Assert.Equal(10m, await AtAsync(_depoA));
        Assert.Equal(0m, await AtAsync(_depoB));
        Assert.Equal(10m, await TotalAsync());
    }

    /// <summary>2 — Çıkış yalnız kendi deposunu düşürür; diğer depo etkilenmez.</summary>
    [Fact]
    public async Task Cikis_YalnizKendiDeposunu_Dusurur()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-2a")).EnsureSuccessStatusCode();
        (await ReceiveAsync(_depoB, 5m, "api-in-2b")).EnsureSuccessStatusCode();
        (await IssueAsync(_depoA, 3m, "api-out-2")).EnsureSuccessStatusCode();

        Assert.Equal(7m, await AtAsync(_depoA));
        Assert.Equal(5m, await AtAsync(_depoB));
        Assert.Equal(12m, await TotalAsync());
    }

    // ── 3. Sayım (STK-02'de yakalanan hatanın API seviyesinde nöbetçisi) ───────────────────

    /// <summary>3 — Sayım, SAYILAN deponun miktarıyla karşılaştırır ve sonucu AYNI depoya yazar.
    /// Depo A=10, Depo B=5 iken A'da 12 sayılırsa fark 12−10=+2 olmalı (12−15=−3 DEĞİL).</summary>
    [Fact]
    public async Task Sayim_SayilanDeponun_Miktarini_Kullanir()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-3a")).EnsureSuccessStatusCode();
        (await ReceiveAsync(_depoB, 5m, "api-in-3b")).EnsureSuccessStatusCode();

        (await CountAsync(_depoA, 12m, "api-count-3")).EnsureSuccessStatusCode();

        Assert.Equal(12m, await AtAsync(_depoA));   // sayılan değere oturur
        Assert.Equal(5m, await AtAsync(_depoB));    // diğer depo DOKUNULMAZ
        Assert.Equal(17m, await TotalAsync());
    }

    // ── 4. Transfer ────────────────────────────────────────────────────────────────────────

    /// <summary>4 — Transfer kaynak/hedefi ayrı taşır; toplam sabit kalır.</summary>
    [Fact]
    public async Task Transfer_KaynakVeHedefi_AyriTasir()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-4")).EnsureSuccessStatusCode();
        (await TransferAsync(_depoA, _depoB, 4m, "api-trf-4")).EnsureSuccessStatusCode();

        Assert.Equal(6m, await AtAsync(_depoA));
        Assert.Equal(4m, await AtAsync(_depoB));
        Assert.Equal(10m, await TotalAsync());
    }

    /// <summary>12 — Aynı operationId ile TEKRAR gönderilen transfer ikinci kez UYGULANMAZ
    /// (idempotency korunuyor — ağ zaman aşımında istemci güvenle yeniden gönderebilir).</summary>
    [Fact]
    public async Task Transfer_AyniOperationId_Ikinci_Kez_Uygulanmaz()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-12")).EnsureSuccessStatusCode();
        (await TransferAsync(_depoA, _depoB, 4m, "api-trf-12")).EnsureSuccessStatusCode();
        (await TransferAsync(_depoA, _depoB, 4m, "api-trf-12")).EnsureSuccessStatusCode();   // tekrar

        Assert.Equal(6m, await AtAsync(_depoA));   // 10-4 (ikinci kez düşmedi)
        Assert.Equal(4m, await AtAsync(_depoB));
        Assert.Equal(10m, await TotalAsync());
    }

    /// <summary>13 — Negatif stok kalkanı API'de de geçerli: başka depodaki stok bu deponun
    /// çıkışını finanse edemez. Mevcut hata standardı: iş kuralı ihlali → <b>400</b>.</summary>
    [Fact]
    public async Task Baska_Depodaki_Stok_Bu_Deponun_Cikisini_Karsilamaz()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-13")).EnsureSuccessStatusCode();

        var r = await IssueAsync(_depoB, 1m, "api-out-13");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        Assert.Equal(10m, await AtAsync(_depoA));   // reddedilen çıkış hiçbir kovayı değiştirmedi
        Assert.Equal(0m, await AtAsync(_depoB));
    }

    // ── 5-7. Bakiye okuma uçları ───────────────────────────────────────────────────────────

    /// <summary>5 — Tek lokasyon ucu (<c>/location</c>) doğru depoyu ve ADINI döner.</summary>
    [Fact]
    public async Task Tek_Lokasyon_Ucu_Depo_Ve_Adini_Doner()
    {
        (await ReceiveAsync(_depoA, 8m, "api-in-5")).EnsureSuccessStatusCode();

        var j = await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/stock/balance/{_mat}/location?locationId={_depoA}"));
        Assert.Equal(_depoA, j.GetProperty("locationId").GetString());
        Assert.Equal("Depo A", j.GetProperty("locationName").GetString());
        Assert.Equal(8m, j.GetProperty("balance").GetDecimal());
    }

    /// <summary>6 — Kırılım ucu (<c>/locations</c>): her depo TEK satır, ad dolu,
    /// ve <c>total</c> kırılımın toplamıyla KOPMAZ.</summary>
    [Fact]
    public async Task Kirilim_Ucu_Her_Depoyu_Tek_Satir_Doner_Ve_Toplamla_Kopmaz()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-6a")).EnsureSuccessStatusCode();
        (await ReceiveAsync(_depoB, 4m, "api-in-6b")).EnsureSuccessStatusCode();
        (await ReceiveAsync(null, 1m, "api-in-6c")).EnsureSuccessStatusCode();   // ATANMAMIŞ

        var j = await LocationsAsync();
        var rows = j.GetProperty("locations").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(15m, j.GetProperty("total").GetDecimal());
        Assert.Equal(15m, rows.Sum(x => x.GetProperty("quantity").GetDecimal()));
        Assert.Equal(await TotalAsync(), j.GetProperty("total").GetDecimal());   // genel toplamla da kopmaz
        Assert.All(rows, x => Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("locationName").GetString())));
        // ATANMAMIŞ en SONDA (kullanıcı önce gerçek depolarını görür)
        Assert.Equal("", rows[^1].GetProperty("locationId").GetString());
    }

    /// <summary>7 — Genel toplam ucu (<c>/balance/{id}</c>) DEĞİŞMEDİ: eski web sürümü aynen çalışır.</summary>
    [Fact]
    public async Task Genel_Toplam_Ucu_Degismedi()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-7a")).EnsureSuccessStatusCode();
        (await ReceiveAsync(_depoB, 4m, "api-in-7b")).EnsureSuccessStatusCode();

        var j = await ApiTestHost.JsonAsync(await _client.GetAsync($"/api/stock/balance/{_mat}"));
        Assert.Equal(14m, j.GetProperty("balance").GetDecimal());
        Assert.False(j.TryGetProperty("locations", out _));   // eski sözleşme genişletilmedi
    }

    // ── 8-10. Lokasyon güvenliği ───────────────────────────────────────────────────────────

    /// <summary>8 — BAŞKA FİRMANIN deposu yazma yolunda reddedilir (403) ve hiçbir şey yazılmaz.
    /// Bu, STK-02'den sonra yapısal hâle gelen sızıntının kapısıdır: lokasyon artık bakiyenin
    /// BİRİNCİL ANAHTAR kolonudur; yabancı kimlik yazılsaydı o satır hiçbir ekranda düzeltilemezdi.</summary>
    [Fact]
    public async Task Yabanci_Firmanin_Deposu_Yazmada_Reddedilir()
    {
        var r = await ReceiveAsync(_yabanciDepo, 5m, "api-in-8");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0m, await TotalAsync());   // hiçbir bakiye oluşmadı
    }

    /// <summary>9 — Yabancı depo OKUMA yolunda da reddedilir (403) — okuma kapısı gevşek bırakılmadı.</summary>
    [Fact]
    public async Task Yabanci_Firmanin_Deposu_Okumada_Reddedilir()
    {
        var r = await _client.GetAsync($"/api/stock/balance/{_mat}/location?locationId={_yabanciDepo}");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    /// <summary>10 — HİÇ VAR OLMAYAN lokasyon da reddedilir (403). Mevcut proje standardı: "var mı yok mu"
    /// ayrımı yapılmaz (bilgi sızdırmamak için) — yeni bir hata modeli icat edilmedi.</summary>
    [Fact]
    public async Task Bilinmeyen_Lokasyon_Reddedilir()
    {
        var r = await ReceiveAsync("olmayan-depo-kimligi", 5m, "api-in-10");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        var r2 = await TransferAsync(_depoA, "olmayan-depo-kimligi", 1m, "api-trf-10");
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
        Assert.Equal(0m, await TotalAsync());
    }

    // ── 11 + 14. ATANMAMIŞ ve eski istemci davranışı ───────────────────────────────────────

    /// <summary>11 + 14 — ESKİ İSTEMCİ SÖZLEŞMESİ: gövdede lokasyon HİÇ YOKSA istek reddedilmez,
    /// ATANMAMIŞ ('') kovasına yazılır — bugünkü davranışın birebir aynısı (geriye dönük uyum).
    /// "Tüm Şubeler" ile çalışan kullanıcı lokasyonu gerçekten bilmiyordur; uydurulmaz.</summary>
    [Fact]
    public async Task Lokasyonsuz_Eski_Istek_ATANMAMIS_Kovasina_Yazilir()
    {
        (await ReceiveAsync(null, 7m, "api-in-11")).EnsureSuccessStatusCode();

        Assert.Equal(7m, await AtAsync(null));       // locationId parametresi hiç verilmedi
        Assert.Equal(7m, await TotalAsync());
        var j = await LocationsAsync();
        var row = Assert.Single(j.GetProperty("locations").EnumerateArray().ToList());
        Assert.Equal("", row.GetProperty("locationId").GetString());
        Assert.Equal("Atanmamış", row.GetProperty("locationName").GetString());
    }

    /// <summary>9 — YETKİSİZ KULLANICI: stok okuma yetkisi olmayan kullanıcı yeni lokasyon uçlarını
    /// çağıramaz (deny-by-default). Yeni uçlar eski ucun gevşekliğini devralmadı.</summary>
    [Fact]
    public async Task Stok_Yetkisi_Olmayan_Kullanici_Lokasyon_Uclarini_Cagiramaz()
    {
        // Yetkisi HİÇ olmayan personel rolü (deny-by-default: stok modülü verilmedi).
        var svc = _host.Services.GetRequiredService<ServerServices>();
        svc.Users.EnsureInitialAdmin(Company, "lok_yetkisiz", "Test!2026", RoleKeys.Staff);
        // Personel "Tüm Şubeler" ile giremez (mevcut kural) → gerçek şubesiyle girer.
        var yetkisiz = await _host.LoginAsync("lok_yetkisiz", "Test!2026", Company, branchId: _depoA);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await yetkisiz.GetAsync($"/api/stock/balance/{_mat}/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await yetkisiz.GetAsync($"/api/stock/balance/{_mat}/location?locationId={_depoA}")).StatusCode);
    }

    // ── 15. Web istemci sözleşmesi ─────────────────────────────────────────────────────────

    /// <summary>15 — WEB SÖZLEŞMESİ: hareket listesi (Stock.razor / StockMovements.razor) artık
    /// lokasyon alanlarını görebilir. Transferde kaynak depo da dolu gelir; aksi hâlde ekranda
    /// "hangi depodan hangi depoya" gösterilemezdi.</summary>
    [Fact]
    public async Task Hareket_Listesi_Lokasyon_Alanlarini_Doner()
    {
        (await ReceiveAsync(_depoA, 10m, "api-in-15")).EnsureSuccessStatusCode();
        (await TransferAsync(_depoA, _depoB, 4m, "api-trf-15")).EnsureSuccessStatusCode();

        var rows = (await ApiTestHost.JsonAsync(await _client.GetAsync("/api/stock"))).EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, x => Assert.True(x.TryGetProperty("locationId", out _)));

        var giris = rows.First(x => x.GetProperty("movementType").GetString() == "in");
        Assert.Equal(_depoA, giris.GetProperty("locationId").GetString());
        Assert.Equal("Depo A", giris.GetProperty("locationName").GetString());

        var transferGiris = rows.First(x => x.GetProperty("movementType").GetString() == "transfer"
                                            && x.GetProperty("direction").GetInt32() > 0);
        Assert.Equal("Depo B", transferGiris.GetProperty("locationName").GetString());
        Assert.Equal("Depo A", transferGiris.GetProperty("fromLocationName").GetString());
    }
}
