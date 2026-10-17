using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-05 (AR-GE 2026-10-17, 04 B1-4) — DEFTER SATIRI EŞİTLEMEYLE DEĞİŞTİRİLEMEZ ═══
///
/// CLAUDE.md §3: operasyonel kayıt değişmez; düzeltme = iptal + ters kayıt. Eskiden var olan defter satırının
/// miktarı (ve diğer alanları) push ile iz bırakmadan değiştirilebiliyordu. Artık push'ta mevcut defter satırında
/// yalnız iptal işareti / gerekçe / damga güncellenir; iptal geri alınamaz. Yeni satır eklemek etkilenmez.
/// </summary>
public class DefterDegismezlikTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly string _mat;
    private const string F = "LDG-F";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public DefterDegismezlikTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ldg_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              $"VALUES('{F}','{F}',1,1,1,0,5,20,5);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_factory, _clock).EnsureInitialAdmin(F, "ldg", "Test!2026", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, F, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = new MaterialService(_factory, _clock).Create(s, new NewMaterial("LDG-MLZ", "Defter Malzemesi"));
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { } }

    private BusinessSyncService.ApplyResult Push(string tablesJson)
        => new BusinessSyncService(_factory, _clock).Apply(F,
            JsonDocument.Parse("{ \"machineId\": \"TEST\", \"tables\": " + tablesJson + " }").RootElement.Clone());

    private string Hareket(string qty, int reversed, long updatedAt) => $$"""
        { "stock_movements": [ { "id": "MV-1", "material_id": "{{_mat}}", "movement_type": "in", "direction": 1,
          "quantity": "{{qty}}", "operation_id": "OP-MV-1", "created_at": 1700000000000,
          "updated_at": {{updatedAt}}, "is_reversed": {{reversed}} } ] }
        """;

    private string Tek(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    [Fact]
    public void LDG1_Mevcut_Hareketin_Miktari_Esitlemeyle_Degismez()
    {
        Push(Hareket("10", 0, 1700000000000));
        Assert.Equal("10", Tek("SELECT quantity FROM stock_movements WHERE id='MV-1';"));

        Push(Hareket("999", 0, 1800000000000));   // daha yeni damgayla miktar değiştirme girişimi
        Assert.Equal("10", Tek("SELECT quantity FROM stock_movements WHERE id='MV-1';"));
    }

    [Fact]
    public void LDG2_Iptal_Isareti_Esitlemeyle_Uygulanir()
    {
        Push(Hareket("10", 0, 1700000000000));
        Push(Hareket("10", 1, 1800000000000));
        Assert.Equal("1", Tek("SELECT is_reversed FROM stock_movements WHERE id='MV-1';"));
    }

    [Fact]
    public void LDG3_Iptal_Geri_Alinamaz()
    {
        Push(Hareket("10", 1, 1700000000000));
        Push(Hareket("10", 0, 1800000000000));   // daha yeni damgayla iptali geri alma girişimi
        Assert.Equal("1", Tek("SELECT is_reversed FROM stock_movements WHERE id='MV-1';"));
    }

    [Fact]
    public void LDG4_Yeni_Hareket_Eklenmeye_Devam_Eder()
    {
        var r = Push(Hareket("7", 0, 1700000000000));
        Assert.Equal(0, r.PermanentSkipped);
        Assert.Equal("7", Tek("SELECT quantity FROM stock_movements WHERE id='MV-1';"));
    }
}
