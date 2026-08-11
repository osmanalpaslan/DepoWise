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
/// STK-04 (FAZ C, 2026-08-11) — WEB EKRANLARININ DAYANDIĞI SÖZLEŞME.
///
/// Web (Blazor Server) ekranları HTTP üzerinden çalışır; bu testler ekranların gönderdiği/okuduğu
/// alanların gerçekten çalıştığını kilitler. Razor dosyaları derlenip test edilemediği için
/// <b>ekranın kullandığı uç + gövde biçimi</b> birebir taklit edilir.
///
/// ⚠️ EN KRİTİK KURAL — <b>"Tüm Şubeler" ≠ "Atanmamış"</b>:
/// Tüm Şubeler = firmanın TÜM lokasyonlarının toplamı (Atanmamış dahil).
/// Atanmamış = yalnız <c>locationId=""</c>, lokasyonu BİLİNMEYEN geçmiş stok.
/// Bu ikisi karışırsa kullanıcı stoğunun nerede olduğunu yanlış bilir.
/// </summary>
[Collection("PostgresSchema")]
public class WebStockLocationContractTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "WEB-LOK";
    private const string User = "web_lok";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private string _mat = "", _mat2 = "", _depoA = "", _depoB = "", _personel = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = svc.Materials.Create(s, new NewMaterial("WEB-M1", "Web malzeme 1"));
        _mat2 = svc.Materials.Create(s, new NewMaterial("WEB-M2", "Web malzeme 2"));
        _depoA = svc.Branches.Create(s, new NewBranch("Depo A"));
        _depoB = svc.Branches.Create(s, new NewBranch("Depo B"));
        _personel = svc.Personnel.Create(s, new NewPersonnel("Depocu", null, null, null));
        _client = await _host.LoginAsync(User, Pass, Company);   // "Tüm Şubeler" oturumu
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── yardımcılar (Web ekranlarının gönderdiği gövdelerin birebir aynısı) ────────────────

    private Task<HttpResponseMessage> Receive(string? branchId, decimal qty, string op, string? mat = null) =>
        _client.PostAsJsonAsync("/api/stock/receive", new
        {
            operationId = op, materialId = mat ?? _mat, code = "WEB-M1", name = "Web malzeme 1",
            quantity = qty, unitPrice = 0m, branchId, personnelId = _personel,
        });

    private Task<HttpResponseMessage> Issue(string? branchId, decimal qty, string op) =>
        _client.PostAsJsonAsync("/api/stock/issue", new
        { operationId = op, materialId = _mat, quantity = qty, branchId, personnelId = _personel });

    private Task<HttpResponseMessage> Transfer(string? from, string? to, decimal qty, string op) =>
        _client.PostAsJsonAsync("/api/stock/transfer", new
        { operationId = op, materialId = _mat, quantity = qty, fromBranchId = from, toBranchId = to, personnelId = _personel });

    private async Task<JsonElement> Get(string url)
    {
        var r = await _client.GetAsync(url);
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    private async Task<decimal> Total(string? mat = null)
        => (await Get($"/api/stock/balance/{mat ?? _mat}")).GetProperty("balance").GetDecimal();

    private async Task<decimal> At(string loc, string? mat = null)
        => (await Get($"/api/stock/balance/{mat ?? _mat}/location?locationId={loc}")).GetProperty("balance").GetDecimal();

    // ── 1-3. Tüm Şubeler · belirli lokasyon · ATANMAMIŞ ───────────────────────────────────

    /// <summary>1 + 2 + 3 — ÜÇ KAVRAMIN AYRIMI. Tüm Şubeler (firma toplamı) = 15;
    /// Depo A = 10; Atanmamış = 1. Toplam, Atanmamış'ı DA içerir; Atanmamış tek başına bir depo değildir.</summary>
    [Fact]
    public async Task TumSubeler_BelirliLokasyon_ve_ATANMAMIS_farkli_degerler_doner()
    {
        (await Receive(_depoA, 10m, "web-1a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 4m, "web-1b")).EnsureSuccessStatusCode();
        (await Receive(null, 1m, "web-1c")).EnsureSuccessStatusCode();   // lokasyonsuz (eski istemci/geçmiş)

        Assert.Equal(15m, await Total());              // 🌐 Tüm Şubeler = firma toplamı (Atanmamış DAHİL)
        Assert.Equal(10m, await At(_depoA));           // belirli lokasyon
        Assert.Equal(4m, await At(_depoB));
        Assert.Equal(1m, await At(""));                // 📦 Atanmamış = YALNIZ lokasyonsuz olan

        Assert.NotEqual(await Total(), await At(""));  // ⚠️ İKİSİ ASLA AYNI DEĞİL
    }

    /// <summary>4 — MALZEME KARTI kırılımı: her depo tek satır, ad dolu, Atanmamış EN SONDA,
    /// ve <c>total</c> ile kırılım toplamı KOPMAZ (kart "Toplam" satırı buradan gelir).</summary>
    [Fact]
    public async Task Malzeme_Karti_Kirilimi_Toplamla_Kopmaz_ve_Atanmamis_Sonda()
    {
        (await Receive(_depoA, 10m, "web-4a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 4m, "web-4b")).EnsureSuccessStatusCode();
        (await Receive(null, 1m, "web-4c")).EnsureSuccessStatusCode();

        var j = await Get($"/api/stock/balance/{_mat}/locations");
        var rows = j.GetProperty("locations").EnumerateArray().ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(await Total(), j.GetProperty("total").GetDecimal());
        Assert.Equal(15m, rows.Sum(x => x.GetProperty("quantity").GetDecimal()));
        Assert.All(rows, x => Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("locationName").GetString())));
        Assert.Equal("", rows[^1].GetProperty("locationId").GetString());
        Assert.Equal("Atanmamış", rows[^1].GetProperty("locationName").GetString());
    }

    // ── 5-8. Yazma yolları ────────────────────────────────────────────────────────────────

    /// <summary>5 — TRANSFER: Web kaynak/hedefi ayrı alanlarda gönderir; iki depo da doğru değişir.</summary>
    [Fact]
    public async Task Transfer_Kaynak_ve_Hedef_Ayri_Degisir()
    {
        (await Receive(_depoA, 10m, "web-5")).EnsureSuccessStatusCode();
        (await Transfer(_depoA, _depoB, 4m, "web-5t")).EnsureSuccessStatusCode();

        Assert.Equal(6m, await At(_depoA));
        Assert.Equal(4m, await At(_depoB));
        Assert.Equal(10m, await Total());
    }

    /// <summary>6 — SAYIM: Web artık <c>branchId</c> GÖNDERİYOR (eskiden hiç göndermiyordu → fark
    /// Atanmamış'a yazılıyor, kullanıcının saydığı depo hiç düzelmiyordu).</summary>
    [Fact]
    public async Task Sayim_Web_Govdesi_Sayilan_Depoyu_Tasir()
    {
        (await Receive(_depoA, 10m, "web-6a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 5m, "web-6b")).EnsureSuccessStatusCode();

        var r = await _client.PostAsJsonAsync("/api/stock/count", new
        {
            operationId = "web-6c", reason = "yıl sonu", branchId = _depoA,
            lines = new[] { new { materialId = _mat, countedQuantity = 12m } },
        });
        r.EnsureSuccessStatusCode();

        Assert.Equal(12m, await At(_depoA));   // sayılan depo düzeldi
        Assert.Equal(5m, await At(_depoB));    // diğer depo DOKUNULMADI
        Assert.Equal(0m, await At(""));        // Atanmamış'a fark YAZILMADI (eski hatanın nöbetçisi)
    }

    /// <summary>7 — SAYIM LİSTESİ (<c>/count-sheet</c>): "sistem stoğu" SAYILAN DEPONUN miktarıdır,
    /// firma toplamı DEĞİL. Ekran bu rakamı gösterir; yanlış olsaydı kullanıcı yanlış fark görürdü.</summary>
    [Fact]
    public async Task Sayim_Listesi_Firma_Toplamini_Degil_Deponun_Miktarini_Doner()
    {
        (await Receive(_depoA, 10m, "web-7a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 5m, "web-7b")).EnsureSuccessStatusCode();
        Assert.Equal(15m, await Total());   // firma toplamı 15

        var j = await Get($"/api/stock/count-sheet?locationId={_depoA}&search=WEB-M1");
        Assert.Equal("Depo A", j.GetProperty("locationName").GetString());
        var row = Assert.Single(j.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("id").GetString() == _mat).ToList());
        Assert.Equal(10m, row.GetProperty("systemStock").GetDecimal());   // 15 DEĞİL
    }

    /// <summary>8 — GİRİŞ + ÇIKIŞ lokasyonu: çıkış yalnız kendi deposunu düşürür.</summary>
    [Fact]
    public async Task Giris_ve_Cikis_Kendi_Deposunu_Etkiler()
    {
        (await Receive(_depoA, 10m, "web-8a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 5m, "web-8b")).EnsureSuccessStatusCode();
        (await Issue(_depoA, 3m, "web-8c")).EnsureSuccessStatusCode();

        Assert.Equal(7m, await At(_depoA));
        Assert.Equal(5m, await At(_depoB));
    }

    // ── 9-10. Hatalı / yetkisiz lokasyon ──────────────────────────────────────────────────

    /// <summary>9 — BİLİNMEYEN lokasyon reddedilir (403) ve hiçbir kayıt oluşmaz.
    /// Web bu hatayı kullanıcıya olduğu gibi gösterir (ortak hata modeli: <c>{"error": …}</c>).</summary>
    [Fact]
    public async Task Bilinmeyen_Lokasyon_Reddedilir_ve_Hicbir_Kayit_Olusmaz()
    {
        var r = await Receive("yok-boyle-depo", 5m, "web-9");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("error", await r.Content.ReadAsStringAsync());
        Assert.Equal(0m, await Total());
    }

    /// <summary>10 — YETKİSİZ kullanıcı sayım listesini ve kırılımı çekemez (deny-by-default).</summary>
    [Fact]
    public async Task Yetkisiz_Kullanici_Lokasyon_Verisini_Cekemez()
    {
        var svc = _host.Services.GetRequiredService<ServerServices>();
        svc.Users.EnsureInitialAdmin(Company, "web_yetkisiz", Pass, RoleKeys.Staff);
        var yetkisiz = await _host.LoginAsync("web_yetkisiz", Pass, Company, branchId: _depoA);

        Assert.Equal(HttpStatusCode.Forbidden, (await yetkisiz.GetAsync($"/api/stock/balance/{_mat}/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await yetkisiz.GetAsync($"/api/stock/count-sheet?locationId={_depoA}")).StatusCode);
    }

    // ── 11-13. Dashboard · hareket listesi · filtre ───────────────────────────────────────

    /// <summary>11 — DASHBOARD: iki depolu malzeme KPI'ları şişirmez. Malzeme sayısı 2 kalır,
    /// düşük stok FİRMA TOPLAMINA göre değerlendirilir (satır çoğaltma olsaydı ikisi de bozulurdu).</summary>
    [Fact]
    public async Task Dashboard_Iki_Depolu_Malzemede_Toplamlari_Sismez()
    {
        (await Receive(_depoA, 10m, "web-11a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 4m, "web-11b")).EnsureSuccessStatusCode();

        var j = await Get("/api/dashboard");
        var summary = j.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("materialCount").GetInt32());   // 2 malzeme → 2 (3 değil)
        Assert.True(summary.GetProperty("lowStockCount").GetInt32() <= 2, "Düşük stok sayısı malzeme sayısını aşamaz (satır çoğaltma göstergesi).");
        var lowStock = j.GetProperty("alerts").EnumerateArray()
            .Count(a => a.GetProperty("kind").GetString() == "LowStock" && a.GetProperty("title").GetString() == "Web malzeme 1");
        Assert.True(lowStock <= 1, "Aynı malzeme için birden çok düşük stok uyarısı üretilmemeli.");
    }

    /// <summary>12 — HAREKET LİSTESİ: her satırda lokasyon alanları var; transferde kaynak da dolu
    /// (ekran "Kaynak → Hedef" gösterebilsin diye).</summary>
    [Fact]
    public async Task Hareket_Listesi_Lokasyon_Alanlarini_Tasir()
    {
        (await Receive(_depoA, 10m, "web-12a")).EnsureSuccessStatusCode();
        (await Transfer(_depoA, _depoB, 4m, "web-12b")).EnsureSuccessStatusCode();

        var rows = (await Get("/api/stock/movements")).EnumerateArray().ToList();
        Assert.All(rows, x => Assert.True(x.TryGetProperty("locationId", out _) && x.TryGetProperty("locationName", out _)));

        var trIn = rows.First(x => x.GetProperty("movementType").GetString() == "transfer" && x.GetProperty("direction").GetInt32() > 0);
        Assert.Equal("Depo B", trIn.GetProperty("locationName").GetString());
        Assert.Equal("Depo A", trIn.GetProperty("fromLocationName").GetString());
    }

    /// <summary>13 — HAREKET FİLTRESİ (ekran istemcide süzer): lokasyonsuz hareket boş kimlikle gelir →
    /// "Atanmamış" süzgeci onu bulur, "Depo A" süzgeci bulmaz.</summary>
    [Fact]
    public async Task Hareket_Filtresi_Icin_Lokasyonsuz_Hareket_Bos_Kimlikle_Gelir()
    {
        (await Receive(_depoA, 10m, "web-13a")).EnsureSuccessStatusCode();
        (await Receive(null, 2m, "web-13b")).EnsureSuccessStatusCode();

        var rows = (await Get("/api/stock/movements")).EnumerateArray().ToList();
        var atanmamis = rows.Where(x => x.GetProperty("locationId").ValueKind == JsonValueKind.Null
                                        || x.GetProperty("locationId").GetString() == "").ToList();
        var depoA = rows.Where(x => x.GetProperty("locationId").ValueKind == JsonValueKind.String
                                    && x.GetProperty("locationId").GetString() == _depoA).ToList();
        Assert.Single(atanmamis);
        Assert.Single(depoA);
    }

    // ── 14-16. Açılış stoğu · eski istemci · performans ───────────────────────────────────

    /// <summary>14 — AÇILIŞ STOĞU LOKASYONU: Web artık <c>openingLocationId</c> gönderir.
    /// Eskiden HİÇ gönderilmiyordu → her açılış "Atanmamış"a düşüyordu (canlıdaki 664 kayıt).</summary>
    [Fact]
    public async Task Acilis_Stogu_Secilen_Depoya_Yazilir()
    {
        var r = await _client.PostAsJsonAsync("/api/materials", new
        {
            code = "WEB-ACILIS", name = "Açılışlı malzeme", unitId = (string?)null,
            minStock = 0m, unitPrice = 0m, openingStock = 25m, openingLocationId = _depoB,
        });
        r.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;

        Assert.Equal(25m, await At(_depoB, id));
        Assert.Equal(0m, await At("", id));    // Atanmamış'a DÜŞMEDİ
        Assert.Equal(25m, await Total(id));
    }

    /// <summary>15 — ESKİ İSTEMCİ UYUMU: <c>openingLocationId</c> göndermeyen eski web/masaüstü
    /// sürümü REDDEDİLMEZ — açılış Atanmamış'a düşer (bugünkü davranış). Sözleşme geriye dönük uyumlu.</summary>
    [Fact]
    public async Task Eski_Istemci_Lokasyonsuz_Acilis_Gonderirse_Reddedilmez()
    {
        var r = await _client.PostAsJsonAsync("/api/materials", new
        {
            code = "WEB-ESKI", name = "Eski istemci malzemesi", unitId = (string?)null,
            minStock = 0m, unitPrice = 0m, openingStock = 7m,   // openingLocationId YOK
        });
        r.EnsureSuccessStatusCode();
        var id = (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;

        Assert.Equal(7m, await At("", id));    // Atanmamış
        Assert.Equal(7m, await Total(id));
    }

    /// <summary>16 — PERFORMANS SÖZLEŞMESİ: liste ekranları kırılım İSTEMEZ. Malzeme listesi TEK istekte
    /// tüm satırların FİRMA TOPLAMINI verir → "100 malzeme × 5 depo = 500 istek" yapısı oluşamaz.
    /// (Kırılım yalnız malzeme KARTI açılınca, tek malzeme için çekilir.)</summary>
    [Fact]
    public async Task Liste_Tek_Istekte_Toplamlari_Doner_Kirilim_Istemez()
    {
        (await Receive(_depoA, 10m, "web-16a")).EnsureSuccessStatusCode();
        (await Receive(_depoB, 4m, "web-16b")).EnsureSuccessStatusCode();
        (await Receive(_depoA, 3m, "web-16c", _mat2)).EnsureSuccessStatusCode();

        var rows = (await Get("/api/materials")).EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);                                     // satır ÇOĞALMADI
        Assert.Equal(14m, rows.First(x => x.GetProperty("id").GetString() == _mat).GetProperty("stock").GetDecimal());
        Assert.Equal(3m, rows.First(x => x.GetProperty("id").GetString() == _mat2).GetProperty("stock").GetDecimal());
    }
}
