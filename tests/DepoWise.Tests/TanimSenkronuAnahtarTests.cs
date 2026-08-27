using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TSN — TANIM SENKRONU ALAN ADI SÖZLEŞMESİ ═══ (kullanıcı bildirimi 2026-08-27)
///
/// <b>KULLANICININ GÖRDÜĞÜ.</b> "Yeni araç kayıt formunda model alanında yeni kayıt oluşturuyorum
/// ama farklı bir kayıt gireceğim zaman daha önce eklemiş olduğum model listelenmiyor."
///
/// <b>KÖK NEDEN.</b> <c>GET /api/lookups/sync</c> satırları <c>Dictionary&lt;string, object?&gt;</c>
/// olarak döndürür ve sözlük ANAHTARLARI veritabanı sütun adlarıdır: <c>brand_id</c>, <c>parent_id</c>,
/// <c>brand_type</c>. ASP.NET Core'un web varsayılanları özellik adlarını camelCase'e çevirir ama
/// <b>sözlük anahtarlarına dokunmaz</b> (<c>DictionaryKeyPolicy</c> ayarlı değildir). Masaüstündeki
/// <c>LookupSyncService</c> ise camelCase anahtar arıyordu (<c>brandId</c> / <c>parentId</c> /
/// <c>brandType</c>). <c>JsonElement.TryGetProperty</c> BÜYÜK-KÜÇÜK HARF DUYARLIDIR → alan hiç
/// bulunamıyor, <c>StrOrNull</c> null dönüyor ve tanım senkronu ilgili sütunu
/// <c>UPDATE … SET brand_id=NULL</c> ile <b>SİLİYORDU</b>.
///
/// <b>SONUÇ ZİNCİRİ.</b> Model kaydı doğru <c>brand_id</c> ile açılıyor → her girişte tanım senkronu
/// <c>brand_id</c>'yi NULL yapıyor ve <c>updated_at</c>'i "şimdi" olarak damgalıyor → LWW gereği
/// yerel satır sunucudakinden YENİ sayıldığı için iş senkronu doğru değeri geri yazamıyor → bir
/// sonraki push NULL değeri SUNUCUYA da taşıyor. <c>ListVehicleModels</c> markaya göre süzdüğü için
/// (<c>AND brand_id=@b</c>) model listede kayboluyor.
///
/// <b>Neden fark edilmedi:</b> <c>ListBrands</c> <c>(brand_type=@t OR brand_type IS NULL)</c> ile
/// NULL'a toleranslı — markalar kaybolmuyor, yalnız iki listede birden görünüyor. Modelde böyle bir
/// tolerans yok, o yüzden hata orada gözle görülür oldu.
///
/// Bu testler <b>gerçek HTTP hattı</b> üzerinden sözleşmeyi kilitler: sunucunun hangi adı gönderdiği
/// ve masaüstünün o adı gerçekten okuyabildiği.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class TanimSenkronuAnahtarTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "TSNAPI-CO";
    private const string Super = "tsn_super";
    private const string Pass = "Test!2026";

    private HttpClient _c = null!;
    private string _markaId = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        var uid = svc.Users.EnsureInitialAdmin(Co, Super, Pass, RoleKeys.SuperAdmin);
        var sa = new SessionContext(uid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var sube = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _c = await _host.LoginAsync(Super, Pass, Co, sube);

        // Araç markası + o markaya bağlı bir model (kullanıcının yaptığı işin aynısı).
        _markaId = await IdAl(await _c.PostAsJsonAsync("/api/lookups/vehicle_brands", new { Name = "TSN Marka" }));
        await _c.PostAsJsonAsync("/api/vehicles/models", new { BrandId = _markaId, Name = "TSN Model" });

        // Alt kategori (parent_id alanı için).
        _ = await IdAl(await _c.PostAsJsonAsync("/api/lookups/material_categories", new { Name = "TSN Üst" }));
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static async Task<string> IdAl(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> SenkronAsync()
    {
        var r = await _c.GetAsync("/api/lookups/sync");
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    /// <summary>⭐ ASIL HATA: masaüstünün okuduğu ad ile sunucunun gönderdiği ad AYNI olmalı.
    /// Aksi hâlde alan sessizce null okunur ve tanım senkronu onu veritabanından SİLER.</summary>
    [Fact]
    public async Task TSN1_Model_Satirinda_Marka_Alani_Okunabiliyor()
    {
        var kok = await SenkronAsync();
        var satir = kok.GetProperty("vehicleModels").EnumerateArray().First();

        var marka = DepoWise.Application.Common.JsonAlan.AlanOku(satir, "brand_id");

        Assert.False(string.IsNullOrEmpty(marka),
            "Model satırında marka alanı okunamadı → tanım senkronu brand_id'yi NULL yapar ve model " +
            "markasına göre listelenemez. Sunucunun gönderdiği anahtarlar: " +
            string.Join(", ", satir.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(_markaId, marka);
    }

    /// <summary>Aynı kusur kategori ağacında da vardı: <c>parent_id</c> silinince alt kategoriler
    /// üst seviyeye çıkar. Marka türünde ise iki listede birden görünmeye başlar.</summary>
    [Theory]
    [InlineData("materialCategories", "parent_id", false)]   // üst kategoride null olabilir → yalnız okunabilirlik
    [InlineData("brands", "brand_type", true)]               // her markada DOLU olmalı
    public async Task TSN2_Diger_Alanlar_Da_Ayni_Adla_Okunuyor(string dizi, string alan, bool doluOlmali)
    {
        var kok = await SenkronAsync();
        var satirlar = kok.GetProperty(dizi).EnumerateArray().ToList();
        Assert.NotEmpty(satirlar);

        foreach (var satir in satirlar)
        {
            // Alan sunucu yanıtında BU ADLA var mı — masaüstü onu bu adla arıyor.
            Assert.True(satir.EnumerateObject().Any(p => p.Name == alan),
                $"{dizi}: '{alan}' alanı yanıtta yok. Gelen anahtarlar: " +
                string.Join(", ", satir.EnumerateObject().Select(p => p.Name)));

            if (doluOlmali)
                Assert.False(string.IsNullOrEmpty(DepoWise.Application.Common.JsonAlan.AlanOku(satir, alan)),
                    $"{dizi}: '{alan}' boş okundu → tanım senkronu bu sütunu NULL'a çeker.");
        }
    }

    /// <summary>⭐ Okuyucu her iki yazımı da kabul etmeli: sunucu sürümü değişse bile (snake_case ↔
    /// camelCase) masaüstü alanı kaybetmemeli. Sahada güncellenmemiş sunucu/istemci karışımı olabilir.</summary>
    [Theory]
    [InlineData("{\"brand_id\":\"X1\"}")]
    [InlineData("{\"brandId\":\"X1\"}")]
    [InlineData("{\"BrandId\":\"X1\"}")]
    public void TSN3_Okuyucu_Iki_Yazimi_Da_Kabul_Eder(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("X1", DepoWise.Application.Common.JsonAlan.AlanOku(doc.RootElement, "brand_id"));
    }

    /// <summary>Alan gerçekten yoksa null döner — "yok" ile "boş" ayrımı korunur (üst kategoride
    /// <c>parent_id</c> meşru şekilde null'dır).</summary>
    [Fact]
    public void TSN4_Olmayan_Alan_Null_Doner()
    {
        using var doc = JsonDocument.Parse("{\"id\":\"1\",\"name\":\"A\"}");
        Assert.Null(DepoWise.Application.Common.JsonAlan.AlanOku(doc.RootElement, "brand_id"));
    }

    // ══════════════ KAYNAK SEVİYESİ KİLİT ══════════════
    // Test projesi Avalonia'ya bağımlı olmasın diye masaüstü sınıfı çağrılamaz; bu yüzden hatanın
    // geri dönüş YOLU kaynak metniyle kapatılır. (Diğer masaüstü testleri de aynı yöntemi kullanır.)

    private static string LookupSyncKaynagi()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        if (d is null) throw new InvalidOperationException("Depo kökü bulunamadı.");
        return File.ReadAllText(Path.Combine(d.FullName, "src", "DepoWise.Desktop", "LookupSyncService.cs"));
    }

    /// <summary>⭐ Tanım senkronu alanı VERİTABANI SÜTUN adıyla ve toleranslı okuyucuyla okumalı.
    /// camelCase sabitler geri gelirse alan yine sessizce null okunur ve sütun NULL'a çekilir.</summary>
    [Fact]
    public void TSN5_Tanim_Senkronu_Toleransli_Okuyucu_Kullanir()
    {
        var x = LookupSyncKaynagi();

        Assert.Contains("JsonAlan.AlanOku(row,", x);

        foreach (var yanlis in new[] { "\"brandId\"", "\"parentId\"", "\"brandType\"" })
            Assert.False(x.Contains(yanlis, StringComparison.Ordinal),
                $"LookupSyncService içinde {yanlis} kalmış — sunucu bu alanı sütun adıyla " +
                "(brand_id / parent_id / brand_type) gönderir; camelCase aranınca alan bulunamaz.");

        // Sütun adları çağrı yerlerinde geçmeli (hem giriş hem "Eşitle" yolunda → her biri 2 kez).
        foreach (var dogru in new[] { "\"brand_id\"", "\"parent_id\"", "\"brand_type\"" })
            Assert.True(System.Text.RegularExpressions.Regex.Matches(x, System.Text.RegularExpressions.Regex.Escape(dogru)).Count >= 2,
                $"{dogru} her iki senkron yolunda da geçmeli (PullAsync + SyncNowAsync).");
    }

    /// <summary>⭐ Marka listesi NULL <c>brand_type</c>'a toleranslıdır — bu tolerans AYNI hatanın
    /// gizlenmiş hâliydi. Tolerans kalsın (eski kayıtlar kaybolmasın) ama model listesi markaya göre
    /// SIKI süzmeye devam etmeli; gevşetilirse her modelin her markada görünmesine yol açar.</summary>
    [Fact]
    public void TSN6_Model_Listesi_Markaya_Gore_Siki_Suzer()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        var kaynak = File.ReadAllText(Path.Combine(d!.FullName, "src", "DepoWise.Infrastructure",
            "Materials", "LookupService.cs"));

        Assert.Contains("AND brand_id=@b ORDER BY name;", kaynak);
        Assert.DoesNotContain("brand_id=@b OR brand_id IS NULL", kaynak);
    }
}
