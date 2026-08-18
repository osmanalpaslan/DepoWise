using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// SIF-06 (2026-08-18) — ŞABLONLAR SENKRONDA HİÇ TAŞINMIYORDU.
///
/// <c>material_templates</c> (Malzeme Şablonları) ve <c>vehicle_templates</c> (Araç Genel Tanım)
/// ne <see cref="BusinessSyncService.Tables"/> içinde ne de <c>/api/lookups/sync</c> yanıtındaydı.
/// Sonuç: masaüstünde açılan şablon web'e, web'de açılan şablon masaüstüne <b>ulaşmıyordu</b> —
/// kullanıcı her makinede şablonu yeniden tanımlamak zorundaydı.
///
/// Bu testler taşımanın uçtan uca (snapshot → apply) çalıştığını ve yetki kapısının ATLANMADIĞINI
/// doğrular.
/// </summary>
public class TemplateSyncTests : IDisposable
{
    private readonly string _kaynakDb, _hedefDb;
    private readonly SqliteConnectionFactory _kaynak, _hedef;
    private readonly TestClock _clock = new();
    private const string Co = "DEPOWISE";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public TemplateSyncTests()
    {
        _kaynakDb = Path.Combine(Path.GetTempPath(), "dw_tmplsrc_" + Guid.NewGuid().ToString("N") + ".db");
        _hedefDb = Path.Combine(Path.GetTempPath(), "dw_tmpldst_" + Guid.NewGuid().ToString("N") + ".db");
        _kaynak = new SqliteConnectionFactory(_kaynakDb);
        _hedef = new SqliteConnectionFactory(_hedefDb);
        new MigrationRunner(_kaynak).Run();
        new MigrationRunner(_hedef).Run();
    }

    private static void Sql(IDbConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Count(IDbConnectionFactory f, string table, string where = "1=1")
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE {where};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private SessionContext TamYetkiliOturum(IDbConnectionFactory f)
    {
        var users = new UserService(f, _clock);
        var auth = new AuthService(f, _clock);
        users.EnsureInitialAdmin(Co, "root", "root123", RoleKeys.SuperAdmin);
        return auth.Login(Co, "root", "root123").Session!;
    }

    [Fact]
    public void Sablonlar_SenkronListesinde()
    {
        Assert.Contains("material_templates", BusinessSyncService.Tables);
        Assert.Contains("vehicle_templates", BusinessSyncService.Tables);
        Assert.Contains("vehicle_template_materials", BusinessSyncService.Tables);
    }

    /// <summary>Yetki kapısı ATLANMAZ: her şablon tablosu kendi modülüne bağlıdır.</summary>
    [Fact]
    public void Sablon_Tablolari_Kendi_Modulune_Bagli()
    {
        Assert.Equal("material_templates", BusinessSyncService.ModuleOf("material_templates"));
        Assert.Equal("vehicle_templates", BusinessSyncService.ModuleOf("vehicle_templates"));
        Assert.Equal("vehicle_templates", BusinessSyncService.ModuleOf("vehicle_template_materials"));
    }

    /// <summary>FK sırası: şablonlar, referans verdikleri tanımlardan SONRA gelmeli;
    /// satır tablosu da ebeveynlerinden (vehicle_templates + materials) sonra.</summary>
    [Fact]
    public void Sablonlarin_Sirasi_YabanciAnahtar_Guvenli()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("material_categories") < t.IndexOf("material_templates"));
        Assert.True(t.IndexOf("vehicle_models") < t.IndexOf("vehicle_templates"));
        Assert.True(t.IndexOf("vehicle_templates") < t.IndexOf("vehicle_template_materials"));
        Assert.True(t.IndexOf("materials") < t.IndexOf("vehicle_template_materials"));
    }

    /// <summary>Uçtan uca: kaynakta açılan şablon, snapshot → apply ile hedefe TAŞINIR.</summary>
    [Fact]
    public void Sablon_Kaynaktan_Hedefe_Tasinir()
    {
        var oturum = TamYetkiliOturum(_kaynak);
        TamYetkiliOturum(_hedef);

        Sql(_kaynak, $"INSERT INTO material_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('MT1','{Co}','Çimento Şablonu',10,10,1,0);");
        Sql(_kaynak, $"INSERT INTO vehicle_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('VT1','{Co}','Kamyon Genel Tanım',10,10,1,0);");

        var svc = new BusinessSyncService(_kaynak);
        var snapshot = svc.BuildSnapshot(Co, "TEST-PC", 0, oturum);

        using var doc = System.Text.Json.JsonDocument.Parse(snapshot);
        new BusinessSyncService(_hedef).ApplyPull(Co, doc.RootElement, null);

        Assert.Equal(1, Count(_hedef, "material_templates", "id='MT1'"));
        Assert.Equal(1, Count(_hedef, "vehicle_templates", "id='VT1'"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_kaynakDb); } catch { }
        try { File.Delete(_hedefDb); } catch { }
    }
}
