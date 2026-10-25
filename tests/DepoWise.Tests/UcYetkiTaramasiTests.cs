using System.Text;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-67 (AR-GE 2026-10-25, F-018) — TÜM API UÇLARINA OTOMATİK YETKİ TARAMASI ═══
///
/// <b>Neden:</b> "arayüzde gizli = API'de kapalı" kuralı bugüne kadar uç uç yazılmış testlerle ve kaynak metni
/// taramasıyla korunuyordu. Yeni bir uç eklenip yetkisi unutulursa bunu yakalayan GENEL bir tarama yoktu.
/// Bu sınıf uçları elle listelemez: API'nin kendi uç kataloğundan (<see cref="EndpointDataSource"/>) okur — yeni uç
/// eklendiği an taramaya girer.
///
///  UY1 — GİRİŞSİZ (anonim) istek: her uç reddeder (400/401/403/404/405). Bilerek herkese açık uçlar aşağıdaki
///        <see cref="HerkeseAcik"/> listesindedir, her biri gerekçesiyle; listede olmayan açık uç = test hatası.
///  UY2 — HİÇ YETKİSİ OLMAYAN personel: hiçbir uç ÇÖKMEZ (500 yok) — çöken uç bilgi sızdırabilir ve "ret" sayılmaz (M-66).
/// Kayıt değiştiren uçlar boş gövdeyle çağrılır; yetkisiz kullanıcı için hiçbir kayıt oluşmaz (ayrı tenant testleri var).
/// </summary>
[Collection("PostgresSchema")]
public class UcYetkiTaramasiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private readonly ITestOutputHelper _out;
    private const string Pass = "Test!2026";
    private const string Co = "UYT-A";

    public UcYetkiTaramasiTests(ITestOutputHelper output) => _out = output;

    /// <summary>Bilerek GİRİŞSİZ erişilebilen uçlar — "YÖNTEM desen" → gerekçe. Yeni bir açık uç ancak buraya
    /// gerekçesiyle eklenerek kabul edilir.</summary>
    private static readonly Dictionary<string, string> HerkeseAcik = new(StringComparer.Ordinal)
    {
        ["GET /"] = "kök — yalnız uygulama adı ve 'ok'",
        ["GET /health"] = "sağlık kontrolü (Fly.io) — veri yok",
        ["GET /api/releases/latest"] = "masaüstü güncelleme denetimi giriş öncesi çalışır — yalnız sürüm bilgisi",
        ["POST /api/auth/login"] = "giriş",
        ["GET /api/public/companies"] = "giriş ekranındaki firma listesi — IP hız sınırı (120/dk); kesinti takibi DB yoklaması bunu kullanır",
        ["POST /api/public/verify-branch"] = "girişte şube şifresi doğrulama — yalnız doğru/yanlış döner; giriş kadar sıkı IP sınırı (30/5 dk)",
        ["POST /api/machines/register"] = "yeni makinenin kendini kaydı (giriş öncesi) — kayıt YÖNETİCİ ONAYINA düşer, IP hız sınırı var. "
            + "Not (M-67, 11_kalan_riskler): boş gövdeyle varsayılan firmaya bekleyen kayıt açar — davranış değişikliği ayrı karar",
    };

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    /// <summary>Uç kataloğu: (yöntem, desen, çağrılacak adres). Rota parametreleri kukla değerle doldurulur.</summary>
    private List<(string Yontem, string Desen, string Adres)> Uclar()
    {
        _ = _host.CreateClient();
        var kaynak = _host.Services.GetRequiredService<EndpointDataSource>();
        var l = new List<(string, string, string)>();
        foreach (var e in kaynak.Endpoints.OfType<RouteEndpoint>())
        {
            var desen = "/" + (e.RoutePattern.RawText ?? "").TrimStart('/');
            if (desen.StartsWith("/_", StringComparison.Ordinal)) continue;      // çatının iç uçları
            var adres = System.Text.RegularExpressions.Regex.Replace(desen, @"\{\*?([^}:=?]+)[^}]*\}", "uyt-kukla");
            var yontemler = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            foreach (var y in yontemler.Where(y => y != "OPTIONS" && y != "HEAD")) l.Add((y, desen, adres));
        }
        return l.Distinct().OrderBy(x => x.Item2).ThenBy(x => x.Item1).ToList();
    }

    private static HttpRequestMessage Istek(string yontem, string adres)
    {
        var r = new HttpRequestMessage(new HttpMethod(yontem), adres);
        if (yontem is "POST" or "PUT" or "PATCH" or "DELETE") r.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return r;
    }

    [Fact]
    public async Task UY1_Girissiz_Istek_Her_Ucta_Reddedilir()
    {
        var uclar = Uclar();
        Assert.True(uclar.Count > 150, $"uç kataloğu okunamadı ({uclar.Count})");
        var anonim = _host.Anonymous();
        var acik = new List<string>();
        foreach (var (y, desen, adres) in uclar)
        {
            if (HerkeseAcik.ContainsKey($"{y} {desen}")) continue;
            using var res = await anonim.SendAsync(Istek(y, adres));
            var kod = (int)res.StatusCode;
            if (!(ApiTestHost.IsDenied(res) || kod == 405)) acik.Add($"{y} {desen} → {kod}");
        }
        _out.WriteLine($"{uclar.Count} uç tarandı.");
        Assert.True(acik.Count == 0, "GİRİŞSİZ erişilebilen (ve listede olmayan) uçlar:\n  " + string.Join("\n  ", acik));
    }

    [Fact]
    public async Task UY2_Yetkisiz_Personelde_Hicbir_Uc_Cokmez()
    {
        var uclar = Uclar();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, 'UYT Firma', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Co);
            cmd.ExecuteNonQuery();
        }
        var adminId = svc.Users.EnsureInitialAdmin(Co, "uyt_admin", Pass, RoleKeys.CompanyAdmin);
        var sa = new SessionContext(adminId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sube = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("UYT-MERKEZ"));
        svc.Users.CreateUser(sa, new NewUser("uyt_yetkisiz", Pass, "Yetkisiz Personel", new[] { RoleKeys.Staff }, Co, BranchId: sube));
        var yetkisiz = await _host.LoginAsync("uyt_yetkisiz", Pass, Co, sube);

        var coken = new List<string>();
        foreach (var (y, desen, adres) in uclar)
        {
            if (desen.StartsWith("/api/auth/", StringComparison.Ordinal)) continue;   // oturumu kapatma/yenileme taramayı bozmasın
            if (desen == "/api/sync/wait") continue;                                  // uzun bekleme (long-poll) — UY1'de girişsiz hâli taranır
            using var res = await yetkisiz.SendAsync(Istek(y, adres));
            if ((int)res.StatusCode >= 500) coken.Add($"{y} {desen} → {(int)res.StatusCode}");
        }
        _out.WriteLine($"{uclar.Count} uç tarandı.");
        Assert.True(coken.Count == 0, "YETKİSİZ personelin isteğinde ÇÖKEN uçlar (500):\n  " + string.Join("\n  ", coken));
    }
}
