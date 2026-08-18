using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G4 (2026-08-18) — SENKRON KAPSAMINDA OLMAYAN İŞ VERİSİ.
///
/// <b>SNK-A3</b> — <c>vehicle_inspections</c> (Muayene / Sigorta) senkron listesinde YOKTU. Ekran iki
/// platformda da var ve <c>InspectionService</c> yerele yazıyor → masaüstünde girilen muayene kaydı
/// web'de HİÇ görünmüyordu. SIF-06 (şablonlar) ile aynı sınıf.
///
/// <b>SNK-A4</b> — <c>stock_count_lines</c> yoktu; ebeveyni <c>stock_documents</c> vardı → sayım belgesi
/// gidiyor, <b>satırları gitmiyordu</b> (belge var, içi boş).
///
/// <b>SNK-A5</b> — muadil malzeme, uyumlu araç, bakım↔araç eşleşmesi, talep durum geçmişi, sayaç geçmişi.
///
/// ⚠️ <b>YENİ TENANT RİSKİ VE KAPATILMASI:</b> bu tabloların beşinde <c>company_id</c> kolonu YOK.
/// Snapshot firma filtresini yalnız o kolonu olan tablolara uyguladığı için, olduğu gibi eklenseler
/// <c>SELECT * FROM tablo</c> ile TÜM firmaların satırları istemciye giderdi — Migration062'nin (M-S1a)
/// kapattığı sızıntının aynısı. Bu yüzden firma kapsamı <c>CompanyScopedChildren</c> ile EBEVEYN
/// üzerinden uygulanır (hem snapshot'ta hem push kapısında).
/// </summary>
public class SyncCoverageTests : IDisposable
{
    private readonly string _kaynakDb, _hedefDb;
    private readonly SqliteConnectionFactory _kaynak, _hedef;
    private readonly TestClock _clock = new();
    private const string Co = "SNK-CO";
    private const string Digeri = "SNK-DIGER";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public SyncCoverageTests()
    {
        _kaynakDb = Path.Combine(Path.GetTempPath(), "dw_snkcov_s_" + Guid.NewGuid().ToString("N") + ".db");
        _hedefDb = Path.Combine(Path.GetTempPath(), "dw_snkcov_h_" + Guid.NewGuid().ToString("N") + ".db");
        _kaynak = new SqliteConnectionFactory(_kaynakDb);
        _hedef = new SqliteConnectionFactory(_hedefDb);
        new MigrationRunner(_kaynak).Run();
        new MigrationRunner(_hedef).Run();
        foreach (var f in new[] { _kaynak, _hedef })
        {
            Sql(f, $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','A',1,1,1,0);");
            Sql(f, $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Digeri}','B',1,1,1,0);");
        }
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

    private SessionContext SuperAdmin(IDbConnectionFactory f)
    {
        var users = new UserService(f, _clock);
        var auth = new AuthService(f, _clock);
        users.EnsureInitialAdmin(Co, "root", "root123", RoleKeys.SuperAdmin);
        return auth.Login(Co, "root", "root123").Session!;
    }

    // ── Katalog kilidi ───────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("vehicle_inspections", "inspection")]
    [InlineData("vehicle_meter_logs", "vehicles")]
    [InlineData("stock_count_lines", "stock")]
    [InlineData("request_status_history", "requests")]
    [InlineData("material_equivalents", "materials")]
    [InlineData("material_compatible_vehicles", "materials")]
    [InlineData("maintenance_definition_vehicles", "maintenance")]
    public void Tablo_Senkronda_Ve_Modulune_Bagli(string table, string module)
    {
        Assert.Contains(table, BusinessSyncService.Tables);
        Assert.Equal(module, BusinessSyncService.ModuleOf(table));   // push yetki kapısı atlanmaz
    }

    /// <summary>FK sırası: çocuk tablo ebeveyninden SONRA gelmeli.</summary>
    [Theory]
    [InlineData("vehicles", "vehicle_inspections")]
    [InlineData("vehicles", "vehicle_meter_logs")]
    [InlineData("materials", "material_equivalents")]
    [InlineData("materials", "material_compatible_vehicles")]
    [InlineData("maintenance_definitions", "maintenance_definition_vehicles")]
    [InlineData("stock_documents", "stock_count_lines")]
    [InlineData("materials", "stock_count_lines")]
    [InlineData("material_requests", "request_status_history")]
    public void Cocuk_Ebeveyninden_Sonra_Gelir(string parent, string child)
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf(parent) < t.IndexOf(child), $"{child} '{parent}' tablosundan ÖNCE geliyor → FK hatası.");
    }

    // ── SNK-A3: uçtan uca muayene taşınması ──────────────────────────────────────────────────────
    [Fact]
    public void Muayene_Kaydi_Kaynaktan_Hedefe_Tasinir()
    {
        var oturum = SuperAdmin(_kaynak);
        SuperAdmin(_hedef);
        Sql(_kaynak, $"INSERT INTO vehicles(id,company_id,internal_code,plate,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('V1','{Co}','AR-1','34ABC01',10,10,1,0);");
        Sql(_kaynak, $"INSERT INTO vehicle_inspections(id,company_id,vehicle_id,doc_type,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('I1','{Co}','V1','inspection',10,10,1,0);");

        var snapshot = new BusinessSyncService(_kaynak).BuildSnapshot(Co, "TEST", 0, oturum);
        using var doc = System.Text.Json.JsonDocument.Parse(snapshot);
        new BusinessSyncService(_hedef).ApplyPull(Co, doc.RootElement, null);

        Assert.Equal(1, Count(_hedef, "vehicle_inspections", "id='I1'"));
    }

    // ── SNK-A4: sayım satırları ──────────────────────────────────────────────────────────────────
    [Fact]
    public void Sayim_Satirlari_Kaynaktan_Hedefe_Tasinir()
    {
        var oturum = SuperAdmin(_kaynak);
        SuperAdmin(_hedef);
        Sql(_kaynak, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('M1','{Co}','K1','Çimento',NULL,10,10,1,0);");
        Sql(_kaynak, $"INSERT INTO stock_documents(id,company_id,doc_type,doc_no,doc_date,status,created_at,version,is_deleted) " +
                     $"VALUES('D1','{Co}','count','S-1',10,'active',10,1,0);");
        Sql(_kaynak, "INSERT INTO stock_count_lines(id,document_id,material_id,system_qty,counted_qty,diff_qty) " +
                     "VALUES('L1','D1','M1','7','5','-2');");

        var snapshot = new BusinessSyncService(_kaynak).BuildSnapshot(Co, "TEST", 0, oturum);
        using var doc = System.Text.Json.JsonDocument.Parse(snapshot);
        new BusinessSyncService(_hedef).ApplyPull(Co, doc.RootElement, null);

        Assert.Equal(1, Count(_hedef, "stock_documents", "id='D1'"));
        Assert.Equal(1, Count(_hedef, "stock_count_lines", "id='L1'"));   // eskiden belge gidiyor, satır gitmiyordu
    }

    // ── ⭐ TENANT: company_id'siz çocuk tablo BAŞKA firmanın satırını SIZDIRMAMALI ────────────────
    [Fact]
    public void CompanyIdsiz_Cocuk_Tablo_Baska_Firmayi_SIZDIRMAZ()
    {
        var oturum = SuperAdmin(_kaynak);

        // İKİ firmanın malzemesi + her birinde bir muadil eşleşmesi.
        Sql(_kaynak, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) VALUES('A1','{Co}','KA1','A1',NULL,10,10,1,0);");
        Sql(_kaynak, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) VALUES('A2','{Co}','KA2','A2',NULL,10,10,1,0);");
        Sql(_kaynak, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) VALUES('B1','{Digeri}','KB1','B1',NULL,10,10,1,0);");
        Sql(_kaynak, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) VALUES('B2','{Digeri}','KB2','B2',NULL,10,10,1,0);");
        Sql(_kaynak, "INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('A1','A2');");
        Sql(_kaynak, "INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('B1','B2');");

        var snapshot = new BusinessSyncService(_kaynak).BuildSnapshot(Co, "TEST", 0, oturum);

        Assert.Contains("\"A1\"", snapshot);      // kendi firmasının eşleşmesi VAR
        Assert.DoesNotContain("\"B1\"", snapshot); // ⭐ diğer firmanın eşleşmesi YOK
    }

    /// <summary>Push kapısı: ebeveyni başka firmada olan çocuk satır UYGULANMAZ.</summary>
    [Fact]
    public void Ebeveyni_Baska_Firmada_Olan_Cocuk_Satir_UYGULANMAZ()
    {
        SuperAdmin(_hedef);
        Sql(_hedef, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) VALUES('B1','{Digeri}','KB1','B1',NULL,10,10,1,0);");
        Sql(_hedef, $"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) VALUES('B2','{Digeri}','KB2','B2',NULL,10,10,1,0);");

        // Kötü niyetli paket: Co firmasına, ebeveyni DIGER firmada olan bir eşleşme yazmaya çalışır.
        var kotu = """
        {"companyId":"SNK-CO","machineId":"HACK","tables":{"material_equivalents":[
          {"material_id":"B1","equivalent_material_id":"B2"}]}}
        """;
        using var doc = System.Text.Json.JsonDocument.Parse(kotu);
        new BusinessSyncService(_hedef).ApplyPull(Co, doc.RootElement, null);

        Assert.Equal(0, Count(_hedef, "material_equivalents"));   // fail-closed
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_kaynakDb); } catch { }
        try { File.Delete(_hedefDb); } catch { }
    }
}
