using System.Net;
using System.Net.Http.Json;
using DepoWise.Application.Common;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YOL-01 · FİRMA KİMLİĞİ DOSYA YOLUNA DOĞRUDAN GİRİYORDU ═══ (denetim 2026-08-26, ikinci tur)
///
/// <b>Bulunan durum.</b> Firma kimliği <c>POST /api/companies</c> gövdesinden (<c>dto.Id</c>) geliyor ve
/// <b>hiç doğrulanmıyordu</b> — masaüstünün çevrimdışı ürettiği kimliği koruyabilmek için bilinçli olarak
/// serbest bırakılmıştı. Aynı kimlik daha sonra <b>dosya yoluna</b> giriyor:
/// <code>Path.Combine(dataDir, "files", companyId)  →  Directory.Delete(dir, recursive: true)</code>
/// (firma kalıcı silme ve firma iş verisi sıfırlama uçlarında).
///
/// Kimlik <c>".."</c> olsaydı silinecek klasör <c>dataDir/files/..</c> = <b><c>dataDir</c>'in kendisi</b>
/// olurdu → <b>bütün firmaların</b> fotoğrafları, makine yedekleri, yayın paketleri ve (SQLite'a düşülmüşse)
/// sunucu veritabanı birlikte silinirdi. Silme işlemini yapan süper admin ise <b>tek bir firmayı</b>
/// sildiğini sanırdı — klasik "kandırılmış vekil" (confused deputy) durumu.
///
/// <b>Not:</b> aynı dosyada zaten DOĞRU desen vardı (<c>LocalFileStorageProvider</c> hem karakter temizliği
/// hem "kökün altında mı" kontrolü yapar); iki silme çağrısı bu korumayı KULLANMIYORDU.
///
/// <b>Düzeltme iki katmanlı:</b>
/// <list type="number">
///   <item><b>Giriş:</b> firma kimliği yalnız harf/rakam/<c>-</c>/<c>_</c> içerebilir (üretimdeki tek firma
///     kimliği onaltılık bir GUID'dir; masaüstünün ürettiği kimlikler de öyledir → davranış değişmez).</item>
///   <item><b>İşlem:</b> silinecek klasör <see cref="SafePath.UnderRoot"/> ile çözülür; kökün dışına
///     çıkıyorsa <b>hiçbir şey silinmez</b>. Kimlik doğrulaması bir gün atlansa bile yıkım olmaz.</item>
/// </list>
/// </summary>
public class FirmaKimligiYolGuvenligiTests
{
    // ⚠️ TEST ALTYAPISI NOTU: bu sınıf yalnız SAF birim testleri içerir (API sunucusu ayağa kaldırmaz).
    // HTTP tarafı ayrı bir sınıftadır (<see cref="FirmaKimligiOlusturmaApiTests"/>) ve TEK bir sunucuyu
    // paylaşır — ilk sürümde her teori durumu için AYRI sunucu açılıyordu; tam takımla paralel koşunca
    // giriş isteği 100 sn zaman aşımına uğradı. Test yeniden koşturularak "geçirilmedi", sebebi düzeltildi.

    // ── Katman 2: yol çözümleyici (saf birim testi; dosya sistemi gerektirmez) ──────────────────

    [Theory]
    [InlineData("..")]
    [InlineData("../ust")]
    [InlineData("..\\ust")]
    [InlineData("../../kok")]
    [InlineData("a/../..")]
    public void YOL01a_Kok_Disina_Cikan_Kimlik_Reddedilir(string kimlik)
    {
        var kok = Path.Combine(Path.GetTempPath(), "depowise_yol_test");
        Assert.Null(SafePath.UnderRoot(kok, "files", kimlik));
    }

    [Theory]
    [InlineData("ed271d0ca2b04a73b97f5025a53a04b4")]   // üretimdeki gerçek biçim
    [InlineData("DEPOWISE")]
    [InlineData("A-1_B")]
    public void YOL01b_Normal_Kimlik_Cozulur(string kimlik)
    {
        var kok = Path.Combine(Path.GetTempPath(), "depowise_yol_test");
        var yol = SafePath.UnderRoot(kok, "files", kimlik);

        Assert.NotNull(yol);
        Assert.EndsWith(kimlik, yol!, StringComparison.Ordinal);
        Assert.StartsWith(Path.GetFullPath(kok), yol!, StringComparison.Ordinal);
    }

    [Fact]
    public void YOL01c_Bos_Kimlik_Kok_Dondurmez()
    {
        var kok = Path.Combine(Path.GetTempPath(), "depowise_yol_test");
        // Boş kimlik "files" klasörünün KENDİSİNE denk gelir → silme çağrısı tüm firmaları vururdu.
        Assert.Null(SafePath.UnderRoot(kok, "files", ""));
        Assert.Null(SafePath.UnderRoot(kok, "files", "   "));
    }

}

/// <summary>YOL-01 · Katman 1 — giriş doğrulaması, GERÇEK HTTP üzerinden (tek paylaşılan sunucu).</summary>
[Collection("PostgresSchema")]
public class FirmaKimligiOlusturmaApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private HttpClient _super = null!;

    public async Task InitializeAsync() => _super = await _host.LoginSeedAsync();
    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    [Theory]
    [InlineData("..")]
    [InlineData("../kotu")]
    [InlineData("..\\kotu")]
    [InlineData("a/b")]
    [InlineData("C:\\Windows")]
    public async Task YOL01d_Yol_Karakterli_Firma_Kimligi_Olusturulamaz(string kimlik)
    {
        var r = await _super.PostAsJsonAsync("/api/companies", new { id = kimlik, name = "Kotu Firma" });

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>Regresyon kilidi: masaüstünün ÇEVRİMDIŞI ürettiği kimlikle oluşturma ÇALIŞMAYA devam eder.</summary>
    [Fact]
    public async Task YOL01e_Cevrimdisi_Uretilen_Kimlik_Calismaya_Devam_Eder()
    {
        var kimlik = Guid.NewGuid().ToString("N");

        var r = await _super.PostAsJsonAsync("/api/companies", new { id = kimlik, name = "Cevrimdisi Firma" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await ApiTestHost.JsonAsync(r);
        Assert.Equal(kimlik, j.GetProperty("id").GetString());
    }

    /// <summary>Regresyon kilidi: kimlik verilmezse sunucu üretir (web akışı) — davranış değişmez.</summary>
    [Fact]
    public async Task YOL01f_Kimliksiz_Olusturma_Sunucu_Uretir()
    {
        var r = await _super.PostAsJsonAsync("/api/companies", new { name = "Web Firmasi" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await ApiTestHost.JsonAsync(r);
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("id").GetString()));
    }
}
