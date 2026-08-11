using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-10a — GERÇEK HTTP HATTI: rapor ucu + XLSX export ucu.
///
/// Web, raporu <c>/api/reports/catalog</c> + <c>/api/reports/{type}</c> üzerinden sürer; export
/// <c>/api/reports/{type}/export</c>'tan gelir. Bu testler o hattın gerçekten çalıştığını ve
/// <b>ekran ile export'un aynı filtrelenmiş kümeyi</b> ürettiğini kanıtlar (RPR-01'de dolaylı
/// bırakılan nokta burada GERÇEK XLSX ile kapatılıyor).
/// </summary>
[Collection("PostgresSchema")]
public class ApiStockMovementsReportTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "HRK-A";
    private const string User = "hrk_kullanici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private string _mat = "", _depoA = "", _depoB = "";

    // API sunucusu GERÇEK saati kullanır (masaüstü testlerindeki dondurulmuş saat değil) → pencere
    // tüm zamanları kapsayacak kadar geniş tutulur; dar aralık senaryosu ayrıca Gunes..Gunes+1 ile sınanır.
    private const long Gunes = 0L, Batis = 4_102_444_800_000L;   // 1970 → 2100

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
        _depoA = svc.Branches.Create(s, new NewBranch("Depo A"));
        _depoB = svc.Branches.Create(s, new NewBranch("Depo B"));
        _mat = svc.Materials.Create(s, new NewMaterial("HRK-API", "Hareket malzemesi"));

        svc.OpeningStock.RecordOpening(s, _mat, 100m, "op-open", branchId: _depoA);
        svc.Stock.ReceiveIn(s, new[] { new StockLine(_mat, 20m) }, "op-in", branchId: _depoA);
        svc.Stock.Transfer(s, _mat, 10m, _depoA, _depoB, "op-trf");
        svc.OpeningStock.RecordOpening(s, _mat, 5m, "op-unassigned");   // ATANMAMIŞ

        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private object Govde(params string[] locations) => new
    {
        fromDate = Gunes, toDate = Batis,
        locationIds = locations.Length == 0 ? new List<string>() : locations.ToList(),
    };

    private async Task<JsonElement> RaporAsync(params string[] locations)
    {
        var r = await _client.PostAsJsonAsync("/api/reports/stock-movements", Govde(locations));
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    private async Task<byte[]> ExportAsync(params string[] locations)
    {
        var r = await _client.PostAsJsonAsync("/api/reports/stock-movements/export", Govde(locations));
        r.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            r.Content.Headers.ContentType?.MediaType);
        return await r.Content.ReadAsByteArrayAsync();
    }

    // ── 1. Katalog ve rapor ucu ────────────────────────────────────────────────────────────

    /// <summary>1 — Rapor katalog ucunda görünüyor ve filtre bayrakları doğru (Web bunu sürüyor).</summary>
    [Fact]
    public async Task Rapor_Katalog_Ucunda_Gorunuyor()
    {
        var arr = (await ApiTestHost.JsonAsync(await _client.GetAsync("/api/reports/catalog"))).EnumerateArray().ToList();
        var rapor = arr.Single(e => e.GetProperty("key").GetString() == "stock-movements");
        Assert.Equal("Stok Hareketleri", rapor.GetProperty("name").GetString());
        Assert.True(rapor.GetProperty("usesDate").GetBoolean());
        Assert.True(rapor.GetProperty("usesLocation").GetBoolean());
        // STK-10b filtreleri bu artımda KAPALI.
        Assert.False(rapor.GetProperty("usesBranch").GetBoolean());
        Assert.False(rapor.GetProperty("usesStatus").GetBoolean());
    }

    /// <summary>2 — Rapor ucu gerçek veriyi Kaynak/Hedef kolonlarıyla döndürüyor.</summary>
    [Fact]
    public async Task Rapor_Ucu_Kaynak_Hedef_ile_Calisiyor()
    {
        var doc = await RaporAsync();
        var headers = doc.GetProperty("headers").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("Kaynak", headers);
        Assert.Contains("Hedef", headers);
        Assert.Equal("Stok Hareketleri Raporu", doc.GetProperty("title").GetString());

        var rows = doc.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(5, rows.Count);   // opening + in + transfer×2 + atanmamış opening
    }

    /// <summary>3 — Lokasyon filtresi HTTP hattında da uygulanıyor.</summary>
    [Fact]
    public async Task Lokasyon_Filtresi_HTTP_Hattinda_Uygulaniyor()
    {
        var hepsi = (await RaporAsync()).GetProperty("rows").GetArrayLength();
        var depoB = (await RaporAsync(_depoB)).GetProperty("rows").GetArrayLength();
        var atanmamis = (await RaporAsync("")).GetProperty("rows").GetArrayLength();

        Assert.Equal(5, hepsi);
        Assert.Equal(1, depoB);        // yalnız transferin giriş bacağı
        Assert.Equal(1, atanmamis);    // yalnız lokasyonsuz açılış
    }

    // ── 2. 🔴 EKRAN ↔ XLSX PARİTESİ (gerçek HTTP + gerçek XLSX) ────────────────────────────

    /// <summary>4 — 🔴 Rapor ucu ile export ucu AYNI kümeyi üretir — XLSX gerçekten açılıp
    /// satır satır karşılaştırılır. Altı filtre kombinasyonu.</summary>
    [Theory]
    [InlineData("filtresiz")]
    [InlineData("depoA")]
    [InlineData("depoB")]
    [InlineData("atanmamis")]
    [InlineData("ikiDepo")]
    [InlineData("darTarih")]
    public async Task Rapor_Ucu_ve_Export_Ucu_Ayni_Satirlari_Uretiyor(string senaryo)
    {
        string[] loc = senaryo switch
        {
            "depoA" => new[] { _depoA },
            "depoB" => new[] { _depoB },
            "atanmamis" => new[] { "" },
            "ikiDepo" => new[] { _depoA, _depoB },
            _ => Array.Empty<string>(),
        };

        var doc = senaryo == "darTarih"
            ? await DarTarihRaporAsync()
            : await RaporAsync(loc);
        var bytes = senaryo == "darTarih"
            ? await DarTarihExportAsync()
            : await ExportAsync(loc);

        var headers = doc.GetProperty("headers").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var numeric = doc.GetProperty("numeric").ValueKind == JsonValueKind.Array
            ? doc.GetProperty("numeric").EnumerateArray().Select(x => x.GetBoolean()).ToList()
            : new List<bool>();
        var ekranRows = doc.GetProperty("rows").EnumerateArray()
            .Select(r => r.EnumerateArray().Select(HucreMetni).ToList()).ToList();

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        for (int c = 0; c < headers.Count; c++)
            Assert.Equal(headers[c], ws.Cell(1, c + 1).GetString());

        for (int r = 0; r < ekranRows.Count; r++)
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                var xlsx = c < numeric.Count && numeric[c]
                    ? (cell.IsEmpty() ? "" : cell.GetDouble().ToString("0.####"))
                    : cell.GetString();
                Assert.Equal(ekranRows[r][c], xlsx);
            }

        // Export'ta FAZLA satır yok: ekran satır sayısından sonraki ilk satır (toplam satırı hariç) boş.
        var fazlaSatirIdx = ekranRows.Count + 2 + (ekranRows.Count > 0 ? 1 : 0);
        Assert.True(string.IsNullOrEmpty(ws.Cell(fazlaSatirIdx, 1).GetString()),
            $"{senaryo}: XLSX'te ekranda olmayan fazladan satır var.");
    }

    private async Task<JsonElement> DarTarihRaporAsync()
    {
        var r = await _client.PostAsJsonAsync("/api/reports/stock-movements",
            new { fromDate = Gunes, toDate = Gunes + 1, locationIds = new List<string>() });
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    private async Task<byte[]> DarTarihExportAsync()
    {
        var r = await _client.PostAsJsonAsync("/api/reports/stock-movements/export",
            new { fromDate = Gunes, toDate = Gunes + 1, locationIds = new List<string>() });
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync();
    }

    /// <summary>API'nin rapor hücresi biçimi: <c>NumCell</c> → <c>{n,t}</c> nesnesi, diğerleri metin.</summary>
    private static string HucreMetni(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Null => "",
        JsonValueKind.Object when cell.TryGetProperty("n", out var n) => n.GetDouble().ToString("0.####"),
        JsonValueKind.String => cell.GetString() ?? "",
        _ => cell.ToString(),
    };

    // ── 3. Güvenlik ────────────────────────────────────────────────────────────────────────

    // ── STK-10b-1: HAREKET TÜRÜ FİLTRESİ (gerçek HTTP) ─────────────────────────────────────

    private object TurGovde(params string[] turler) => new
    {
        fromDate = Gunes, toDate = Batis,
        locationIds = new List<string>(),
        movementTypes = turler.ToList(),
    };

    /// <summary>STK-10b-1 — katalog ucu yeni bayrağı yayınlıyor (Web bunu sürer).</summary>
    [Fact]
    public async Task Katalog_Ucu_usesMovementType_Yayinliyor()
    {
        var arr = (await ApiTestHost.JsonAsync(await _client.GetAsync("/api/reports/catalog"))).EnumerateArray().ToList();
        var rapor = arr.Single(e => e.GetProperty("key").GetString() == "stock-movements");
        Assert.True(rapor.GetProperty("usesMovementType").GetBoolean());
        // Diğer raporlarda AÇILMADI (kapsam sızmasının nöbetçisi).
        Assert.False(arr.Single(e => e.GetProperty("key").GetString() == "stock").GetProperty("usesMovementType").GetBoolean());
    }

    /// <summary>STK-10b-1 — tür filtresi HTTP hattında uygulanıyor; seçilmeyen tür gelmiyor.</summary>
    [Fact]
    public async Task Tur_Filtresi_HTTP_Hattinda_Uygulaniyor()
    {
        var hepsi = await ApiTestHost.JsonAsync(await _client.PostAsJsonAsync("/api/reports/stock-movements", TurGovde()));
        Assert.Equal(5, hepsi.GetProperty("rows").GetArrayLength());

        var r = await _client.PostAsJsonAsync("/api/reports/stock-movements", TurGovde("transfer"));
        r.EnsureSuccessStatusCode();
        var transfer = await ApiTestHost.JsonAsync(r);
        Assert.Equal(2, transfer.GetProperty("rows").GetArrayLength());   // iki bacak

        // Bilinmeyen tür → fail-closed (boş), "filtre yok" gibi davranmıyor.
        var yok = await ApiTestHost.JsonAsync(
            await _client.PostAsJsonAsync("/api/reports/stock-movements", TurGovde("uydurma_tur")));
        Assert.Equal(0, yok.GetProperty("rows").GetArrayLength());
    }

    /// <summary>STK-10b-1 — 🔴 tür filtresi EXPORT ucuna da uygulanıyor; XLSX ekranla BİREBİR aynı.</summary>
    [Theory]
    [InlineData("transfer")]
    [InlineData("opening")]
    [InlineData("in")]
    public async Task Tur_Filtresi_Export_Ucuna_da_Uygulaniyor(string tur)
    {
        var r = await _client.PostAsJsonAsync("/api/reports/stock-movements", TurGovde(tur));
        r.EnsureSuccessStatusCode();
        var doc = await ApiTestHost.JsonAsync(r);

        var e = await _client.PostAsJsonAsync("/api/reports/stock-movements/export", TurGovde(tur));
        e.EnsureSuccessStatusCode();
        var bytes = await e.Content.ReadAsByteArrayAsync();

        var headers = doc.GetProperty("headers").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var numeric = doc.GetProperty("numeric").EnumerateArray().Select(x => x.GetBoolean()).ToList();
        var ekranRows = doc.GetProperty("rows").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(HucreMetni).ToList()).ToList();

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        for (int i = 0; i < ekranRows.Count; i++)
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(i + 2, c + 1);
                var xlsx = c < numeric.Count && numeric[c]
                    ? (cell.IsEmpty() ? "" : cell.GetDouble().ToString("0.####"))
                    : cell.GetString();
                Assert.Equal(ekranRows[i][c], xlsx);
            }
        // Export'ta fazladan satır yok.
        var fazla = ekranRows.Count + 2 + (ekranRows.Count > 0 ? 1 : 0);
        Assert.True(string.IsNullOrEmpty(ws.Cell(fazla, 1).GetString()));
    }

    /// <summary>5 — Export özel buton yetkisi ister; yetkisiz kullanıcı 403 alır (mevcut standart).</summary>
    [Fact]
    public async Task Export_Yetkisiz_Kullaniciya_403()
    {
        var svc = _host.Services.GetRequiredService<ServerServices>();
        var uid = svc.Users.EnsureInitialAdmin(Company, "hrk_yetkisiz", Pass, RoleKeys.Staff);
        Assert.False(string.IsNullOrEmpty(uid));

        // Şubeye bağlı kullanıcı giriş yaparken GERÇEK şube seçmek zorundadır ("Tüm Şubeler" yetkisi yok).
        var kisitli = await _host.LoginAsync("hrk_yetkisiz", Pass, Company, _depoA);
        var r = await kisitli.PostAsJsonAsync("/api/reports/stock-movements/export", Govde());
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    /// <summary>6 — Kimliksiz istek reddedilir.</summary>
    [Fact]
    public async Task Kimliksiz_Istek_Reddedilir()
    {
        var anon = _host.CreateClient();
        var r = await anon.PostAsJsonAsync("/api/reports/stock-movements", Govde());
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }
}
