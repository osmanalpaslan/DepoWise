using System.Net;
using System.Net.Http.Headers;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Net;
using DepoWise.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-84 (AR-GE 2026-10-17) — YANIT SIKIŞTIRMASI GERÇEKTEN KULLANILIYOR MU ═══
///
///  SKS1 — Sunucu, sıkıştırma isteyen istemciye eşitleme paketini gzip ile verir ve paket en az %70 küçülür
///  SKS2 — Masaüstü istemci işleyicisi sıkıştırmayı İSTER ve AÇAR (düz HttpClient istemiyordu — bulgunun kendisi)
///  SKS3 — Masaüstünde düz <c>new HttpClient</c> kalmadı; web istemcisi de sıkıştırmalı işleyici kullanıyor (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class SikistirmaTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "SKS-A", User = "sks_kullanici", Pass = "Test!2026";
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        for (int i = 0; i < 200; i++) svc.Materials.Create(s, new NewMaterial($"SKS-{i:D4}", $"Sıkıştırma deneme malzemesi {i}"));
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    [Fact]
    public async Task SKS1_Esitleme_Paketi_Gzip_Ile_Iner_Ve_Kuculur()
    {
        var duz = await _client.GetAsync("/api/sync/business-pull-v2?after=0");
        var duzBoy = (await duz.Content.ReadAsByteArrayAsync()).Length;
        Assert.Empty(duz.Content.Headers.ContentEncoding);   // istemeyen istemciye sıkıştırma yok (eski davranış)

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/sync/business-pull-v2?after=0");
        req.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        var sik = await _client.SendAsync(req);
        sik.EnsureSuccessStatusCode();
        Assert.Contains("gzip", sik.Content.Headers.ContentEncoding);
        var sikBoy = (await sik.Content.ReadAsByteArrayAsync()).Length;
        Assert.True(sikBoy < duzBoy * 0.3, $"gzip {sikBoy} B, düz {duzBoy} B — en az %70 küçülmeliydi");
    }

    [Fact]
    public void SKS2_Masaustu_Isleyicisi_Sikistirma_Ister_Ve_Acar()
    {
        using var h = HttpIstemci.Isleyici();
        Assert.Equal(DecompressionMethods.All, h.AutomaticDecompression);
        using var c = HttpIstemci.Yeni(TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), c.Timeout);
    }

    [Fact]
    public void SKS3_Duz_HttpClient_Kalmadi()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var kalan = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(dir, "src", "DepoWise.Desktop"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var t = File.ReadAllText(f);
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"HttpClient\s+\w+\s*=\s*new\(\)|new\s+(System\.Net\.Http\.)?HttpClient\s*[({]"))
                kalan.Add(Path.GetFileName(f));
        }
        Assert.True(kalan.Count == 0, "Sıkıştırmasız HttpClient: " + string.Join(", ", kalan) + " → HttpIstemci.Yeni kullanın");
        var web = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Web", "Program.cs"));
        Assert.Contains("AutomaticDecompression = System.Net.DecompressionMethods.All", web);
    }
}
