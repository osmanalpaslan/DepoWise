using System.Net;
using System.Net.Http.Headers;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YED-02 · SUNUCU YEDEK YÜKLEME UCU KİMLİĞİ DOĞRULAMIYOR ═══ (denetim 2026-08-26, ikinci tur)
///
/// <b>Bulunan durum.</b> <c>POST /api/backups</c> ucu yalnız şunu yapıyordu:
/// <code>if (DeviceToken(req) is null) return Results.Unauthorized();</code>
/// <c>DeviceToken</c> ise <b>yalnız <c>Authorization: Bearer …</c> başlığını ayrıştırır</b> — jetonu
/// HİÇBİR yerde doğrulamaz (kardeş uçlar <c>/sync/push</c> ve <c>/sync/pull</c> jetonu
/// <c>SyncServer.AuthDevice</c> ile veritabanından doğrular; burada o adım YOKTU).
///
/// Sonuç: <b>internetteki herhangi biri</b> uydurma bir jetonla dosya yükleyebiliyordu. Üstelik dosyanın
/// yazılacağı <b>firma ve makine adı da istekten</b> geliyordu (<c>form["company"]</c>, <c>form["machine"]</c>).
///
/// <b>Neden ciddi (etki):</b>
/// <list type="number">
///   <item><b>Erişilebilirlik.</b> Gövde sınırı 1 GB, hız sınırı yok, depo "üzerine yazmaz / otomatik
///     silmez" (<see cref="BackupStore"/>). Sunucu diski dolduğunda <b>tüm API 500 döner</b> —
///     bu daha önce yaşandı (ADR-070). Yani kimliksiz bir çağıran sistemi durdurabilirdi.</item>
///   <item><b>Yanıltma.</b> Sahte yedekler süper adminin "Makine Yedekleri" ekranında <b>gerçek bir
///     firmanın</b> yedeği gibi görünürdü.</item>
/// </list>
///
/// <b>Veri sızıntısı YOKTU:</b> uç yalnız yazar; okuma/listeleme uçları (SEC-04) zaten kapalıdır.
///
/// <b>Düzeltme.</b> Kimlik gerçekten doğrulanır: geçerli bir <b>JWT oturumu</b> (masaüstünün bugün
/// gönderdiği şey) VEYA geçerli bir <b>cihaz senkron jetonu</b>. Firma artık <b>formdan değil kimlikten</b>
/// alınır → başka firmanın klasörüne yazılamaz. Meşru akış (masaüstü günlük yedek) DEĞİŞMEZ.
/// </summary>
[Collection("PostgresSchema")]
public class SunucuYedekYuklemeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "YED-A";
    private const string CoB = "YED-B";
    private const string Pass = "Yedek!2026";
    private ServerServices _svc = null!;
    private HttpClient _adminA = null!;
    private HttpClient _super = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
        {
            using var conn = _svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                "VALUES(@c,@n,1,1,1,0,10,20,5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@c", id);
            cmd.AddWithValue("@n", ad);
            cmd.ExecuteNonQuery();
        }

        _svc.Users.EnsureInitialAdmin(CoA, "yed_admin_a", Pass, RoleKeys.CompanyAdmin);
        _adminA = await _host.LoginAsync("yed_admin_a", Pass, CoA);
        _super = await _host.LoginSeedAsync();
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────────

    private static MultipartFormDataContent Paket(string firma, string makine, int bayt = 512)
    {
        var form = new MultipartFormDataContent();
        var dosya = new ByteArrayContent(new byte[bayt]);
        dosya.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(dosya, "file", "yedek.db");
        form.Add(new StringContent(firma), "company");
        form.Add(new StringContent(makine), "machine");
        form.Add(new StringContent("yedek.db"), "filename");
        return form;
    }

    /// <summary>Sunucuda o firma için kayıtlı makine adları (diske gerçekten yazıldı mı?).</summary>
    private IReadOnlyList<string> DiskteMakineler(string firma)
        => _svc.Backups.List(firma, new DateOnly(2000, 1, 1), new DateOnly(2999, 1, 1))
               .Select(x => x.Machine).Distinct(StringComparer.Ordinal).ToList();

    // ── testler ────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ YED-02a — UYDURMA jetonla yükleme yapılamamalı (bu, bulunan hatanın kanıtıdır).</summary>
    [Fact]
    public async Task YED02a_Uydurma_Jetonla_Yedek_Yuklenemez()
    {
        var anon = _host.Anonymous();
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tamamen-uydurma-jeton");

        var r = await anon.PostAsync("/api/backups", Paket(CoA, "SAHTE-MAKINE"));

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>⭐ YED-02b — reddedilen istek DİSKE HİÇBİR ŞEY yazmamalı ("boş yanıt" yetmez, dosya da olmamalı).</summary>
    [Fact]
    public async Task YED02b_Uydurma_Jetonlu_Istek_Diske_Yazmaz()
    {
        var anon = _host.Anonymous();
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tamamen-uydurma-jeton");
        await anon.PostAsync("/api/backups", Paket(CoA, "SAHTE-MAKINE"));

        Assert.DoesNotContain("SAHTE-MAKINE", DiskteMakineler(CoA));
    }

    /// <summary>YED-02c — jeton HİÇ yoksa zaten reddediliyordu; bu davranış korunmalı (regresyon kilidi).</summary>
    [Fact]
    public async Task YED02c_Jetonsuz_Istek_Reddedilir()
    {
        var r = await _host.Anonymous().PostAsync("/api/backups", Paket(CoA, "JETONSUZ"));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>⭐ YED-02d — GEÇERLİ oturumla yükleme ÇALIŞMAYA DEVAM etmeli (masaüstü günlük yedeği bozulmasın).</summary>
    [Fact]
    public async Task YED02d_Gecerli_Oturumla_Yukleme_Calisir()
    {
        var r = await _adminA.PostAsync("/api/backups", Paket(CoA, "GERCEK-MAKINE"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("GERCEK-MAKINE", DiskteMakineler(CoA));
    }

    /// <summary>⭐ YED-02e — firma FORMDAN değil KİMLİKTEN alınmalı: A'nın admini B'nin klasörüne yazamaz.</summary>
    [Fact]
    public async Task YED02e_Baska_Firmanin_Klasorune_Yazilamaz()
    {
        // Form "B firması" diyor; oturum ise A firmasının.
        await _adminA.PostAsync("/api/backups", Paket(CoB, "SIZAN-MAKINE"));

        Assert.DoesNotContain("SIZAN-MAKINE", DiskteMakineler(CoB));
    }

    /// <summary>YED-02f — süper adminin listeleme ekranı çalışmaya devam eder (davranış değişmedi).</summary>
    [Fact]
    public async Task YED02f_Super_Admin_Listesi_Calisir()
    {
        var r = await _super.GetAsync($"/api/backups?company={CoA}&from=2000-01-01&to=2999-01-01");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    /// <summary>
    /// ⭐ YOL-01/b — <c>DELETE /api/backups?company=..</c> YEDEK KLASÖRÜNÜN DIŞINI silmemeli.
    ///
    /// Firma adı istekten gelir ve dosya yoluna girer. <c>".."</c> verilseydi taranan klasör yedeklerin
    /// ÜST klasörü (sunucu veri kökü) olur, tarih aralığına giren TÜM dosyalar silinirdi — fotoğraflar,
    /// yayın paketleri, SQLite'a düşülmüşse veritabanı. Süper admin gerektirir ama geri alınamaz.
    /// </summary>
    [Fact]
    public async Task YOL01b_Yedek_Silme_Kok_Disina_Cikamaz()
    {
        // Yedek kökünün DIŞINDA, ama "..".dan sonra taranacak bir klasörde işaret dosyası bırak.
        // (Mekanizma: dir = backups/.. = veriKökü → GetDirectories(veriKökü) = [backups, files, releases…]
        //  → her birinin İÇİNDEKİ dosyalar tarih aralığındaysa silinir. Yayın paketleri tam buradadır.)
        var klasor = Path.Combine(_svc.DataDir, "releases");
        Directory.CreateDirectory(klasor);
        var isaret = Path.Combine(klasor, "yol01-isaret.pkg");
        await File.WriteAllTextAsync(isaret, "dokunulmamali");

        await _super.DeleteAsync("/api/backups?company=..&from=2000-01-01&to=2999-01-01");

        Assert.True(File.Exists(isaret), "veri kökündeki dosya SİLİNDİ — yol koruması çalışmıyor");
    }
}
