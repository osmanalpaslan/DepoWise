using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM SNK-A7 (2026-08-18) — <b>SENKRON ŞUBE KAPSAMI YALNIZ ÖN MUHASEBEDE VARDI.</b>
///
/// <c>BranchScopedTables</c> GAP-6'da yalnız cari/fatura/kasa-banka için doldurulmuştu.
/// <c>branch_id</c> taşıdığı hâlde kapsam dışı kalan iş tabloları yüzünden, yalnız "Şube A"ya
/// yetkili bir kullanıcının bilgisayarına <b>TÜM şubelerin</b> araç, personel, stok hareketi ve
/// talep verisi iniyordu. Ekranda filtrelense bile veri fiziksel olarak o makinededir → gizlilik.
///
/// ⚠️ <b><c>materials</c> BİLİNÇLİ OLARAK KAPSAM DIŞI:</b> KARAR-7 = A (2026-08-11) gereği
/// <b>malzeme kartı FİRMA GENELİDİR</b>; <c>materials.branch_id</c> "kartın ait olduğu şube"dir,
/// stok lokasyonu DEĞİLDİR. Kapsama alınması o ürün kararını ihlal ederdi. İlk denetim raporunda
/// bu tablo yanlışlıkla listelenmişti — düzeltildi.
///
/// Korunan ilkeler: NULL şubeli (eski/şubesiz) kayıtlar GİZLENMEZ; kısıtsız kullanıcı ETKİLENMEZ.
/// </summary>
public class SyncBranchScopeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly SessionContext _admin;
    private readonly string _subeA, _subeB;
    private const string Co = "SBS-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public SyncBranchScopeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_sbs_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_factory, _clock);
        _subeA = branches.Create(_admin, new NewBranch("ŞUBE A"));
        _subeB = branches.Create(_admin, new NewBranch("ŞUBE B"));

        // Her şubede bir araç ve bir personel + şubesiz (eski) bir araç.
        Sql($"INSERT INTO vehicles(id,company_id,internal_code,plate,branch_id,created_at,updated_at,version,is_deleted) VALUES('VA','{Co}','AR-A','34A','{_subeA}',10,10,1,0);");
        Sql($"INSERT INTO vehicles(id,company_id,internal_code,plate,branch_id,created_at,updated_at,version,is_deleted) VALUES('VB','{Co}','AR-B','34B','{_subeB}',10,10,1,0);");
        Sql($"INSERT INTO vehicles(id,company_id,internal_code,plate,branch_id,created_at,updated_at,version,is_deleted) VALUES('VN','{Co}','AR-N','34N',NULL,10,10,1,0);");
        Sql($"INSERT INTO personnel(id,company_id,full_name,branch_id,created_at,updated_at,version,is_deleted) VALUES('PA','{Co}','A Personel','{_subeA}',10,10,1,0);");
        Sql($"INSERT INTO personnel(id,company_id,full_name,branch_id,created_at,updated_at,version,is_deleted) VALUES('PB','{Co}','B Personel','{_subeB}',10,10,1,0);");
        // Malzeme: KARAR-7=A → firma geneli. branch_id dolu olsa BİLE kapsam dışı kalmamalı.
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,branch_id,created_at,updated_at,version,is_deleted) VALUES('MB','{Co}','KB','B Malzeme',NULL,'{_subeB}',10,10,1,0);");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        try { cmd.ExecuteNonQuery(); } catch { /* kurulum yardımcı satırı */ }
    }

    /// <summary>Yalnız ŞUBE A'ya yetkili personel (admin bypass YOK).</summary>
    private SessionContext SadeceA() => new("kul", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty)
    { ScopeBranchIds = new[] { _subeA } };

    private string Snapshot(SessionContext s) => new BusinessSyncService(_factory, _clock).BuildSnapshot(Co, "TEST", 0, s);

    // ── Katalog kilidi ───────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("vehicles")]
    [InlineData("personnel")]
    [InlineData("stock_movements")]
    [InlineData("material_requests")]
    public void Tablo_Sube_Kapsamina_Alindi(string table)
        => Assert.True(BusinessSyncService.IsBranchScoped(table), $"{table} şube kapsamında değil.");

    /// <summary>⚠️ KARAR-7 = A: malzeme kartı FİRMA GENELİDİR → kapsama ALINMAMALI.</summary>
    [Fact]
    public void Malzeme_Sube_Kapsamina_ALINMADI()
        => Assert.False(BusinessSyncService.IsBranchScoped("materials"),
            "materials şube kapsamına alınmış — KARAR-7=A (malzeme kartı firma geneli) ihlali.");

    // ── Davranış ─────────────────────────────────────────────────────────────────────────────────
    /// <summary>⭐ ASIL HATA: kapsam dışı şubenin aracı/personeli artık cihaza İNMEMELİ.</summary>
    [Fact]
    public void Kapsam_Disi_Subenin_Verisi_Cihaza_INMEZ()
    {
        var snapshot = Snapshot(SadeceA());

        Assert.Contains("\"VA\"", snapshot);       // kendi şubesi
        Assert.DoesNotContain("\"VB\"", snapshot); // ⭐ diğer şube
        Assert.Contains("\"PA\"", snapshot);
        Assert.DoesNotContain("\"PB\"", snapshot);
    }

    /// <summary>Şubesiz (eski) kayıtlar GİZLENMEZ — BranchAccess ile aynı ilke.</summary>
    [Fact]
    public void Subesiz_Kayitlar_Gizlenmez()
        => Assert.Contains("\"VN\"", Snapshot(SadeceA()));

    /// <summary>KARAR-7=A: şubesi ŞUBE B olan malzeme kartı bile İNMELİ (firma geneli katalog).</summary>
    [Fact]
    public void Malzeme_Karti_Firma_Geneli_Olarak_Iner()
        => Assert.Contains("\"MB\"", Snapshot(SadeceA()));

    /// <summary>Kısıtsız kullanıcıda (admin) davranış DEĞİŞMEZ — hepsi iner.</summary>
    [Fact]
    public void Kisitsiz_Kullanicida_Hepsi_Iner()
    {
        var snapshot = Snapshot(_admin);
        Assert.Contains("\"VA\"", snapshot);
        Assert.Contains("\"VB\"", snapshot);
        Assert.Contains("\"PB\"", snapshot);
    }

    /// <summary>PUSH kapısı: kapsam dışı şubenin satırı UYGULANMAZ (manipüle edilmiş branch_id ile de).</summary>
    [Fact]
    public void Kapsam_Disi_Sube_Satiri_UYGULANMAZ()
    {
        var json = """
        {"companyId":"CO_ID","tables":{"personnel":[
          {"id":"PX","company_id":"CO_ID","full_name":"Sizma","branch_id":"SUBE_B","created_at":50,"updated_at":50,"version":1,"is_deleted":0}]}}
        """.Replace("CO_ID", Co).Replace("SUBE_B", _subeB);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        new BusinessSyncService(_factory, _clock).Apply(SadeceA(), doc.RootElement);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM personnel WHERE id='PX';";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
