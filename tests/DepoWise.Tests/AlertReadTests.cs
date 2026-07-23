using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Reporting;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>#18 — Uyarı "okundu": kullanıcı bazlı upsert; imza değişince (kötüleşme) kayıt güncellenir.</summary>
public class AlertReadTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public AlertReadTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_alertread_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    [Fact]
    public void OkunduIsaretle_UpsertVe_ImzaDegisince_Guncellenir()
    {
        var dash = new DashboardService(_factory, new MaintenanceService(_factory), new InspectionService(_factory));
        var s = new SessionContext("u1", "A", new[] { RoleKeys.Staff }, new PermissionSet(Array.Empty<ModulePermission>()));

        dash.MarkAlertRead(s, "maintenance|v1|Yağ", "%85 (Warning)");
        Assert.Equal("%85 (Warning)", ReadSig("u1", "maintenance|v1|Yağ"));
        Assert.Equal(1, Rows("u1", "maintenance|v1|Yağ"));

        // Aynı anahtar, kötüleşen imza → tek satır güncellenir (yeniden görünme mekanizması)
        dash.MarkAlertRead(s, "maintenance|v1|Yağ", "%95 (Critical)");
        Assert.Equal("%95 (Critical)", ReadSig("u1", "maintenance|v1|Yağ"));
        Assert.Equal(1, Rows("u1", "maintenance|v1|Yağ"));

        // Farklı kullanıcı ayrı kayıt tutar
        var s2 = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, new PermissionSet(Array.Empty<ModulePermission>()));
        dash.MarkAlertRead(s2, "maintenance|v1|Yağ", "%95 (Critical)");
        Assert.Equal(1, Rows("u2", "maintenance|v1|Yağ"));
    }

    private string ReadSig(string userId, string key)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT signature FROM alert_reads WHERE user_id=$u AND alert_key=$k;";
        cmd.AddWithValue("$u", userId);
        cmd.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string ?? "";
    }

    private int Rows(string userId, string key)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM alert_reads WHERE user_id=$u AND alert_key=$k;";
        cmd.AddWithValue("$u", userId);
        cmd.AddWithValue("$k", key);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
