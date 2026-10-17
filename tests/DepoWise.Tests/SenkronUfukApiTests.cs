using System.Net;
using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Sync;
using DepoWise.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-03 (AR-GE 2026-10-17) — <c>GET /api/sync/business-pull-v2?after=</c> GERÇEK HTTP UCU ═══
/// Masaüstünün yeni çekme döngüsü: after=0 → tam paket + nextAfter; sonraki tur after=nextAfter → yalnız değişen.
///
///  UFA1 — Uç oturum ister (401); ilk tur tam, değişiklik yokken ikinci tur BOŞ; yeni kayıt tek başına gelir
///  UFA2 — Masaüstüne uygulanan paket yerel veriyi oluşturur; eski uç (business-pull) aynen çalışmaya devam eder
/// </summary>
[Collection("PostgresSchema")]
public class SenkronUfukApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "UFA-A", User = "ufa_kullanici", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        using (var conn = Svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = Svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private async Task<(string Body, List<string> Mats, long Next)> V2(long after)
    {
        var r = await _client.GetAsync("/api/sync/business-pull-v2?after=" + after);
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var mats = doc.RootElement.GetProperty("tables").TryGetProperty("materials", out var rows)
            ? rows.EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList() : new List<string>();
        return (body, mats, doc.RootElement.GetProperty("nextAfter").GetInt64());
    }

    [Fact]
    public async Task UFA1_Oturum_Ister_Ilk_Tam_Sonra_Yalniz_Degisen()
    {
        var anonim = _host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/api/sync/business-pull-v2?after=0")).StatusCode);

        var m1 = Svc.Materials.Create(_s, new NewMaterial("UFA-1", "Bir"));
        var (_, ilk, n1) = await V2(0);
        Assert.Contains(m1, ilk);

        var (_, bos, n2) = await V2(n1);
        Assert.Empty(bos);                     // ⭐ değişiklik yokken veri inmez (eski uçta her tur tam paketti)

        var m2 = Svc.Materials.Create(_s, new NewMaterial("UFA-2", "İki"));
        var (_, yeni, _) = await V2(n2);
        Assert.Equal(new[] { m2 }, yeni);
    }

    [Fact]
    public async Task UFA2_Paket_Masaustune_Uygulanir_Eski_Uc_Calisir()
    {
        var m = Svc.Materials.Create(_s, new NewMaterial("UFA-9", "Masaüstüne"));
        var (body, _, _) = await V2(0);

        var yol = Path.Combine(Path.GetTempPath(), "dw_ufa_" + Guid.NewGuid().ToString("N") + ".db");
        var yerel = new SqliteConnectionFactory(yol);
        try
        {
            new MigrationRunner(yerel).Run();
            using (var conn = yerel.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Company}','{Company}',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }
            using var doc = JsonDocument.Parse(body);
            new BusinessSyncService(yerel).ApplyPull(Company, doc.RootElement,
                new HashSet<string>(StringComparer.Ordinal) { "stock_balances" });
            using (var conn = yerel.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM materials WHERE id=@id;";
                cmd.AddWithValue("@id", m);
                Assert.Equal("Masaüstüne", cmd.ExecuteScalar() as string);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(yol); } catch { } }

        // Eski uç (güncellenmemiş masaüstleri) aynen hizmet verir.
        var eski = await _client.GetAsync("/api/sync/business-pull");
        Assert.Equal(HttpStatusCode.OK, eski.StatusCode);
        Assert.Contains(m, await eski.Content.ReadAsStringAsync());
    }
}
