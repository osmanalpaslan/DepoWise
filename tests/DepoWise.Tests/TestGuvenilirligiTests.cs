using System.Net;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-66 (AR-GE 2026-10-25, F-046 · F-047) — "TESTLER GEÇTİ" GERÇEKTEN GÜVENİLİR ═══
///  TG1 — sunucu çökmesi (500) "erişim reddedildi" sayılmaz; 400/401/403/404 sayılır, 200 asla
///  TG2 — test betiği atlanan (çalıştırılmamış) testi "geçti" saymaz: EKSİK KOŞU + çıkış 3 (-PostgresAtla bilinçliyse uyarı)
/// </summary>
public class TestGuvenilirligiTests
{
    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void TG1_Cokme_Ret_Sayilmaz(HttpStatusCode kod, bool ret)
        => Assert.Equal(ret, ApiTestHost.IsDenied(new HttpResponseMessage(kod)));

    [Fact]
    public void TG2_Betik_Atlanan_Testi_Gecti_Saymaz()
    {
        var s = Oku("scripts/run_tests.ps1");
        Assert.Contains("$_.outcome -eq 'NotExecuted'", s);
        Assert.Contains("SONUC: EKSIK KOSU", s);
        var eksik = s.IndexOf("SONUC: EKSIK KOSU", StringComparison.Ordinal);
        var gecti = s.IndexOf("SONUC: TUM TESTLER GECTI", StringComparison.Ordinal);
        Assert.True(eksik > 0 && eksik < gecti, "atlanan test kontrolü 'geçti' yazılmadan ÖNCE yapılmalı");
        Assert.Contains("exit 3", s[eksik..gecti]);
        Assert.Contains("if ($PostgresAtla)", s[..gecti]);
    }
}
