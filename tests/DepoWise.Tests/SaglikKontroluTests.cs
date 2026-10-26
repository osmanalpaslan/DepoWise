using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-72 (AR-GE 2026-10-25, F-067) — SUNUCU SAĞLIK KONTROLÜ + DİSK ALARMI ═══
///  SK1 — /health 200 + "disk" seviyesi (ok/warning/critical/unknown); sayı, yol, dosya adı SIZMAZ (herkese açık uç)
///  SK2 — fly.toml'da /health sağlık kontrolü tanımlı (kilitlenmede Fly makineyi yeniden başlatır)
///  SK3 — kesinti takibi disk "critical"ı hata (e-posta), "warning"i uyarı sayar
/// </summary>
[Collection("PostgresSchema")]
public class SaglikKontroluTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    [Fact]
    public async Task SK1_Health_Disk_Seviyesi_Doner_Ayrinti_Sizdirmaz()
    {
        var r = await _host.Anonymous().GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var govde = await r.Content.ReadAsStringAsync();
        var j = JsonDocument.Parse(govde).RootElement;
        Assert.Equal("ok", j.GetProperty("status").GetString());
        Assert.Contains(j.GetProperty("disk").GetString(), new[] { "ok", "warning", "critical", "unknown" });
        Assert.Equal(new[] { "disk", "status", "time" }, j.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.DoesNotContain("depowise_apitest_", govde);                         // veri dizini yolu sızmaz
    }

    [Fact]
    public void SK2_Fly_Saglik_Kontrolu_Tanimli()
    {
        var s = Oku("fly.toml");
        var i = s.IndexOf("[[http_service.checks]]", StringComparison.Ordinal);
        Assert.True(i > s.IndexOf("[http_service]", StringComparison.Ordinal), "sağlık kontrolü http_service altında olmalı");
        Assert.Contains("path = \"/health\"", s[i..]);
        Assert.Contains("method = \"GET\"", s[i..]);
    }

    [Fact]
    public void SK3_Kesinti_Takibi_Disk_Seviyesini_Okur()
    {
        var s = Oku(".github/workflows/kesinti-takibi.yml");
        Assert.Contains("critical) echo \"::error::", s);
        Assert.Contains("hata=1 ;;", s);
        Assert.Contains("warning)  echo \"::warning::", s);
    }
}
