using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Firma "yerel sıfırlama" isteği (ADR-084) — SUNUCU tarafı. company_purges'ten (ADR-083, kalıcı silme)
/// FARKI: bu YIKICI/erişim-engelleyici DEĞİLDİR — firma sunucuda durur, yalnız "bir kerelik sıfırlama
/// isteği" kaydı bırakılır ve GetStatus ile okunabilir. Masaüstünün bunu nasıl uyguladığı (LocalPurgeService
/// + LocalResetService) Desktop projesindedir, bu testler yalnız SUNUCU tarafı davranışı doğrular.
/// </summary>
public class CompanyLocalResetTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly AuthService _auth;
    private readonly CompanyLocalResetService _reset;

    public CompanyLocalResetTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_lreset_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _reset = new CompanyLocalResetService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext SuperAdmin()
    {
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        return _auth.Login("A", "root", "root123").Session!;
    }

    [Fact]
    public void IstekYoksa_DurumNull()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        Assert.Null(_reset.GetStatus("B"));
    }

    [Fact]
    public void Istek_DurumaYansir()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        var res = _reset.RequestReset(su, "B");

        Assert.Equal("B", res.CompanyId);
        Assert.Equal(su.UserId, res.RequestedBy);
        var st = _reset.GetStatus("B");
        Assert.NotNull(st);
        Assert.Equal(res.RequestedAt, st!.RequestedAt);
    }

    [Fact]
    public void TekrarIstek_ZamaniGunceller()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");

        var first = _reset.RequestReset(su, "B");
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        var second = _reset.RequestReset(su, "B");

        Assert.True(second.RequestedAt > first.RequestedAt);
        Assert.Equal(second.RequestedAt, _reset.GetStatus("B")!.RequestedAt);
    }

    /// <summary>Masaüstünün karşılaştırma mantığı bu davranışa dayanır: yeni istek eskisinden büyük zaman
    /// damgasıyla gelir, böylece "zaten uyguladım mı?" kıyası doğru çalışır.</summary>
    [Fact]
    public void SuperAdminOlmayan_IstekBirakamaz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = new DepoWise.Infrastructure.Organization.BranchService(_factory, _clock)
            .Create(su, new NewBranch("B-Merkez"), companyId: "B");
        var badmId = _users.CreateUser(su, new NewUser("badm", "p12345", null, new[] { RoleKeys.CompanyAdmin },
            CompanyId: "B", BranchId: bBranch));
        var admin = _auth.Login("B", "badm", "p12345").Session!;

        Assert.Throws<ForbiddenException>(() => _reset.RequestReset(admin, "B"));
        Assert.Null(_reset.GetStatus("B"));
    }

    [Fact]
    public void OlmayanFirma_IstekReddedilir()
    {
        var su = SuperAdmin();
        Assert.Throws<InvalidOperationException>(() => _reset.RequestReset(su, "yok-boyle-firma"));
    }

    /// <summary>Kendi firmanı sıfırlaman YASAK DEĞİL (bu, ADR-083 kalıcı silmeden farklı) — çünkü sunucu
    /// verisi etkilenmez; süper admin kendi firmasının makinelerini de bu şekilde sıfırlatabilir.</summary>
    [Fact]
    public void KendiFirmasiIcinDeIstekBirakabilir()
    {
        var su = SuperAdmin();
        var res = _reset.RequestReset(su, su.CompanyId);
        Assert.Equal(su.CompanyId, res.CompanyId);
    }

    /// <summary>Tenant izolasyonu: B için istek, A'nın (veya başka firmanın) durumunu etkilemez.</summary>
    [Fact]
    public void BaskaFirmayaSizmaz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        _companies.Create(su, new NewCompany("C Firma"), explicitId: "C");

        _reset.RequestReset(su, "B");

        Assert.NotNull(_reset.GetStatus("B"));
        Assert.Null(_reset.GetStatus("C"));
        Assert.Null(_reset.GetStatus(su.CompanyId));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  SIF-02 (2026-08-25) — AÇIK OTURUMDA SIFIRLAMA İSTEĞİ
    //
    //  Sorun: ADR-084 isteği YALNIZ giriş anında kontrol ediliyordu. Program açıkken sıfırlama
    //  yapılırsa 15 saniyelik eşitleme turu dönmeye ve AZ ÖNCE SİLİNEN veriyi sunucuya GERİ
    //  GÖNDERMEYE devam ediyordu → sıfırlama fiilen geri alınıyordu.
    //
    //  Test projesi masaüstü projesine referans VERMEZ (mimari karar); bu yüzden kural, davranışı
    //  üreten kaynak satırları üzerinden kilitlenir — SyncPermanentSkipTests'teki (P3/P4) desenin aynısı.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    private static string ShellSource()
    {
        var dir = AppContext.BaseDirectory;
        for (int k = 0; k < 8 && dir is not null; k++)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "DepoWise.sln"))) break;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        return System.IO.File.ReadAllText(System.IO.Path.Combine(
            dir!, "src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"));
    }

    /// <summary>⭐ SIF-02a — eşitleme turu, sıfırlama isteğini PUSH'tan ÖNCE kontrol etmeli.</summary>
    [Fact]
    public void SIF02a_Esitleme_Turu_Sifirlama_Istegini_Kontrol_Eder()
    {
        var src = ShellSource();
        Assert.Contains("RefreshLocalResetFlagAsync", src);
        Assert.Contains("_localResetPending", src);

        // Kontrol, gönderimden ÖNCE olmalı — sonrasında olsaydı eski veri çoktan gitmiş olurdu.
        // Karşılaştırma YALNIZ periyodik tur metodunun gövdesinde yapılır; dosyanın başındaki manuel
        // "Eşitle" komutu da PushAsync çağırır ve dosya-geneli arama yanıltıcı olurdu.
        var tur = src.IndexOf("private async System.Threading.Tasks.Task MaybePushBusinessAsync", StringComparison.Ordinal);
        Assert.True(tur > 0, "MaybePushBusinessAsync bulunamadı");
        var govde = src.Substring(tur);

        var kontrol = govde.IndexOf("if (_localResetPending)", StringComparison.Ordinal);
        var push = govde.IndexOf("await BusinessSyncPushService.PushAsync();", StringComparison.Ordinal);
        Assert.True(kontrol >= 0, "sıfırlama kapısı periyodik turda bulunamadı");
        Assert.True(push >= 0, "push çağrısı periyodik turda bulunamadı");
        Assert.True(kontrol < push, "SIF-02: sıfırlama kontrolü PUSH'tan SONRA kalmış — veri yine gider.");
    }

    /// <summary>SIF-02b — kapı devreye girdiğinde tur GERİ DÖNER (push/pull çalışmaz) ve oturum kapanır.</summary>
    [Fact]
    public void SIF02b_Kapi_Devredeyse_Tur_Durur_Ve_Oturum_Kapanir()
    {
        var src = ShellSource();
        Assert.Contains("if (_localResetPending) { await WarnLocalResetOnceAsync(); return; }", src);
        Assert.Contains("WarnLocalResetOnceAsync", src);
        // Bilgilendirme + güvenli çıkış (makine pasife alındığındaki desenle aynı).
        Assert.Contains("App.Current?.Logout();", src);
    }

    /// <summary>
    /// SIF-02c — ÇEVRİMDIŞI FAIL-SAFE: sunucuya ulaşılamadığında bayrak AÇILMAZ. Aksi hâlde internet
    /// kesikken uygulama kendini kilitlerdi (çevrimdışı çalışma bu ürünün temel özelliği).
    /// </summary>
    [Fact]
    public void SIF02c_Cevrimdisi_Bayrak_Acilmaz()
    {
        var src = ShellSource();
        var i = src.IndexOf("private async System.Threading.Tasks.Task RefreshLocalResetFlagAsync", StringComparison.Ordinal);
        Assert.True(i > 0, "RefreshLocalResetFlagAsync bulunamadı");
        var govde = src.Substring(i, Math.Min(1200, src.Length - i));
        Assert.Contains("if (serverAt is null) return;", govde);      // uç erişilemedi → dokunma
        Assert.Contains("catch", govde);                              // ağ hatası yutulur, bayrak açılmaz
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
