using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G8 (2026-08-18) — <b>SNK-A6: DÜZENLEMEDE SİLİNEN ÇOCUK SATIRLAR KARŞI TARAFTA KALIYORDU.</b>
///
/// Senkron yalnız upsert'tir; silme yalnız <c>is_deleted=1</c> ile taşınır. Ama şu çocuk tablolarda
/// <c>is_deleted</c> YOK ve uygulama onları düzenlemede <b>fiziksel silip yeniden yazıyor</b>:
/// <c>material_request_items</c> (RequestService.Update), <c>vehicle_template_materials</c>
/// (VehicleTemplateService.ReplaceMaterials), <c>material_equivalents</c> /
/// <c>material_compatible_vehicles</c> / <c>maintenance_definition_vehicles</c> (Set* metotları).
/// Sonuç: bir tarafta silinen kalem karşı tarafta KALIYOR → <b>mükerrer kalem</b>.
///
/// Çözüm uygulamanın gerçek davranışını senkrona taşır: bir EBEVEYN paket içinde geldiğinde o
/// ebeveynin çocuk kümesi paketteki hâliyle DEĞİŞTİRİLİR. Ebeveyn pakette yoksa çocuklarına
/// DOKUNULMAZ (delta senkronunda bilinmeyen ebeveynin çocukları silinmez).
/// </summary>
public class ChildDeletePropagationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private const string Co = "CDP-CO";
    private const string Digeri = "CDP-DIGER";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public ChildDeletePropagationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_cdp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','A',1,1,1,0);");
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Digeri}','B',1,1,1,0);");
        new UserService(_factory, _clock).EnsureInitialAdmin(Co, "root", "root123", RoleKeys.SuperAdmin);

        // İki malzeme + bir muadil eşleşmesi (A1 ↔ A2) ve ikinci bir eşleşme (A1 ↔ A3).
        foreach (var (id, code) in new[] { ("A1", "K1"), ("A2", "K2"), ("A3", "K3") })
            Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
                $"VALUES('{id}','{Co}','{code}','{code}',NULL,10,10,1,0);");
        Sql("INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('A1','A2');");
        Sql("INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('A1','A3');");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Count(string table, string where = "1=1")
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE {where};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private void Uygula(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json.Replace("CO_ID", Co));
        new BusinessSyncService(_factory, _clock).ApplyPull(Co, doc.RootElement, null);
    }

    /// <summary>⭐ ASIL HATA: karşı tarafta A3 eşleşmesi kaldırılmışsa burada da DÜŞMELİ.</summary>
    [Fact]
    public void Ebeveyn_Gelince_Eksik_Cocuk_SILINIR()
    {
        Assert.Equal(2, Count("material_equivalents", "material_id='A1'"));

        // Paket: A1 malzemesi (ebeveyn) + YALNIZ A2 eşleşmesi → A3 kaldırılmış.
        Uygula("""
        {"companyId":"CO_ID","tables":{
          "materials":[{"id":"A1","company_id":"CO_ID","code":"K1","name":"K1","updated_at":20,"version":2,"is_deleted":0}],
          "material_equivalents":[{"material_id":"A1","equivalent_material_id":"A2"}]}}
        """);

        Assert.Equal(1, Count("material_equivalents", "material_id='A1'"));
        Assert.Equal(1, Count("material_equivalents", "material_id='A1' AND equivalent_material_id='A2'"));
        Assert.Equal(0, Count("material_equivalents", "equivalent_material_id='A3'"));
    }

    /// <summary>Ebeveyn geldiği hâlde HİÇ çocuk gelmediyse hepsi temizlenir (tümü kaldırılmış hâli).</summary>
    [Fact]
    public void Ebeveyn_Gelip_Cocuk_Gelmezse_Hepsi_SILINIR()
    {
        Uygula("""
        {"companyId":"CO_ID","tables":{
          "materials":[{"id":"A1","company_id":"CO_ID","code":"K1","name":"K1","updated_at":20,"version":2,"is_deleted":0}]}}
        """);

        Assert.Equal(0, Count("material_equivalents", "material_id='A1'"));
    }

    /// <summary>⭐ FAIL-SAFE: ebeveyn PAKETTE YOKSA çocuklarına DOKUNULMAZ (delta senkronu güvenliği).</summary>
    [Fact]
    public void Ebeveyn_Pakette_Yoksa_Cocuklar_KORUNUR()
    {
        Uygula("""
        {"companyId":"CO_ID","tables":{
          "materials":[{"id":"A2","company_id":"CO_ID","code":"K2","name":"K2","updated_at":20,"version":2,"is_deleted":0}]}}
        """);

        // A1 pakette gelmedi → onun eşleşmeleri aynen durmalı.
        Assert.Equal(2, Count("material_equivalents", "material_id='A1'"));
    }

    /// <summary>⭐ TENANT: başka firmanın ebeveyni gönderilse bile o firmanın çocukları silinmez.</summary>
    [Fact]
    public void Baska_Firmanin_Cocuklari_SILINMEZ()
    {
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('B1','{Digeri}','KB1','KB1',NULL,10,10,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('B2','{Digeri}','KB2','KB2',NULL,10,10,1,0);");
        Sql("INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('B1','B2');");

        // Kötü niyetli paket: Co oturumunda, DİĞER firmanın malzemesini ebeveyn gibi gönderip
        // çocuk kümesini boşaltmaya çalışır.
        Uygula("""
        {"companyId":"CO_ID","tables":{
          "materials":[{"id":"B1","company_id":"CO_ID","code":"KB1","name":"KB1","updated_at":30,"version":3,"is_deleted":0}]}}
        """);

        Assert.Equal(1, Count("material_equivalents", "material_id='B1'"));   // fail-closed
    }

    /// <summary>Talep kalemleri: web'de kalem çıkarıldığında masaüstünde de düşmeli.</summary>
    [Fact]
    public void Talep_Kalemleri_Eksik_Gelince_Duser()
    {
        Sql($"INSERT INTO material_requests(id,company_id,doc_no,request_date,status,created_at,updated_at,version,is_deleted) " +
            $"VALUES('R1','{Co}','T-1',10,'draft',10,10,1,0);");
        Sql($"INSERT INTO material_request_items(id,company_id,request_id,material_id,quantity) " +
            $"VALUES('I1','{Co}','R1','A1','5');");
        Sql($"INSERT INTO material_request_items(id,company_id,request_id,material_id,quantity) " +
            $"VALUES('I2','{Co}','R1','A2','3');");

        Uygula("""
        {"companyId":"CO_ID","tables":{
          "material_requests":[{"id":"R1","company_id":"CO_ID","doc_no":"T-1","request_date":10,"status":"draft","updated_at":20,"version":2,"is_deleted":0}],
          "material_request_items":[{"id":"I1","company_id":"CO_ID","request_id":"R1","material_id":"A1","quantity":"5"}]}}
        """);

        Assert.Equal(1, Count("material_request_items", "request_id='R1'"));
        Assert.Equal(0, Count("material_request_items", "id='I2'"));   // eskiden karşı tarafta KALIYORDU
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
