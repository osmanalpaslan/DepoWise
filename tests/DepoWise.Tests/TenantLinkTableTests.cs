using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ DEN-2026-08-25 · BAĞLANTI TABLOLARINDA FİRMA SINIRI ═══
///
/// <b>Bulgu (uçtan uca denetim):</b> senkron gönderiminde (push) <c>company_id</c> kolonu OLMAYAN
/// bağlantı tabloları iki ayrı açık taşıyordu:
///
/// <list type="number">
///   <item><b>TNT-01 (kritik):</b> <c>vehicle_template_materials</c> firma kapısı listesinde
///     (<c>CompanyScopedChildren</c>) HİÇ yoktu. A firmasının makinesi, gönderdiği pakete B firmasının
///     şablon kimliğini yazarak <b>B'nin araç şablonuna malzeme satırı ekleyebiliyordu</b>
///     (başka firmanın verisine YAZMA).</item>
///   <item><b>TNT-02:</b> kapı yalnız <b>EBEVEYN</b> tarafını doğruluyordu. <c>material_equivalents</c>
///     satırında <c>material_id</c> kendi firmasınınken <c>equivalent_material_id</c> BAŞKA firmanın
///     malzemesi olabiliyordu; malzeme kartı bu muadili KOD ve ADIYLA gösteriyordu (okuma sızıntısı).</item>
/// </list>
///
/// Bu testler önce açığı ÜRETİR, sonra kapalı kaldığını kilitler. Meşru (aynı firma) satırların
/// çalışmaya devam ettiği de ayrıca doğrulanır — kapı fazla sıkı kapanmasın.
/// </summary>
public class TenantLinkTableTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly UserService _users;
    private readonly SessionContext _a, _b;
    private readonly string _matA, _matA2, _matB, _vehB;

    private const string A = "TNT-A";       // gönderen firma
    private const string B = "TNT-B";       // kurban firma

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public TenantLinkTableTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_tnt_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _materials = new MaterialService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _users = new UserService(_factory, _clock);

        Company(A); Company(B);
        _a = Session(A, "kul_a");
        _b = Session(B, "kul_b");

        _matA = _materials.Create(_a, new NewMaterial("A-KOD", "A Malzeme"));
        _matA2 = _materials.Create(_a, new NewMaterial("A-KOD2", "A Malzeme 2"));
        _matB = _materials.Create(_b, new NewMaterial("B-GIZLI", "B Gizli Malzeme"));
        _vehB = _vehicles.Create(_b, new NewVehicle("B-ARAC", CurrentMeter: 5m));

        // Araç şablonları (fixture — doğrudan yazılır, servis yolu bu testin konusu değil).
        Sql($"INSERT INTO vehicle_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
            $"VALUES('A-TPL','{A}','A Şablonu',1,1,1,0);");
        Sql($"INSERT INTO vehicle_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
            $"VALUES('B-TPL','{B}','B Şablonu',1,1,1,0);");
    }

    private void Company(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private SessionContext Session(string company, string user)
    {
        var uid = _users.EnsureInitialAdmin(company, user, "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private BusinessSyncService.ApplyResult Push(string companyId, string tablesJson)
        => new BusinessSyncService(_factory, _clock).Apply(companyId,
            Payload("{ \"machineId\": \"TEST\", \"tables\": " + tablesJson + " }"));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // TNT-01 · BAŞKA FİRMANIN ŞABLONUNA YAZMA
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ TNT-01 — A firmasının paketi, B firmasının araç şablonuna malzeme satırı EKLEYEMEZ.
    /// (Denetim öncesi bu satır sessizce uygulanıyordu.)
    /// </summary>
    [Fact]
    public void TNT01_Baska_Firmanin_Sablonuna_Malzeme_Eklenemez()
    {
        var res = Push(A, $$"""
        { "vehicle_template_materials": [ { "template_id": "B-TPL", "material_id": "{{_matA}}", "quantity": "1" } ] }
        """);

        Assert.Equal(0, res.Upserted);
        Assert.Equal(1, res.Skipped);
        Assert.Equal(1, res.PermanentSkipped);           // tekrar denemek anlamsız → kuyruk kilitlenmez
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM vehicle_template_materials WHERE template_id='B-TPL';"));
    }

    /// <summary>TNT-01b — kendi şablonuna yazma ETKİLENMEZ (kapı fazla sıkı kapanmadı).</summary>
    [Fact]
    public void TNT01b_Kendi_Sablonuna_Malzeme_Eklenebilir()
    {
        var res = Push(A, $$"""
        { "vehicle_template_materials": [ { "template_id": "A-TPL", "material_id": "{{_matA}}", "quantity": "2" } ] }
        """);

        Assert.Equal(0, res.PermanentSkipped);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM vehicle_template_materials WHERE template_id='A-TPL';"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // TNT-02 · İKİNCİL REFERANS (bağlantının KARŞI ucu)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ TNT-02 — muadil bağlantısının KARŞI ucu başka firmanın malzemesi olamaz.
    /// Ebeveyn (<c>material_id</c>) A firmasının olduğu için eski kapı bunu GEÇİRİYORDU.
    /// </summary>
    [Fact]
    public void TNT02_Muadilin_Karsi_Ucu_Baska_Firma_Olamaz()
    {
        var res = Push(A, $$"""
        { "material_equivalents": [ { "material_id": "{{_matA}}", "equivalent_material_id": "{{_matB}}" } ] }
        """);

        Assert.Equal(0, res.Upserted);
        Assert.Equal(1, res.PermanentSkipped);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM material_equivalents;"));
    }

    /// <summary>TNT-02b — aynı firmanın iki malzemesi arasındaki muadil normal şekilde uygulanır.</summary>
    [Fact]
    public void TNT02b_Ayni_Firma_Muadili_Uygulanir()
    {
        var res = Push(A, $$"""
        { "material_equivalents": [ { "material_id": "{{_matA}}", "equivalent_material_id": "{{_matA2}}" } ] }
        """);

        Assert.Equal(0, res.PermanentSkipped);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM material_equivalents;"));
    }

    /// <summary>TNT-02c — UYUMLU ARAÇ bağlantısının araç ucu da korunur (aynı desen, farklı tablo).</summary>
    [Fact]
    public void TNT02c_Uyumlu_Aracin_Arac_Ucu_Baska_Firma_Olamaz()
    {
        var res = Push(A, $$"""
        { "material_compatible_vehicles": [ { "material_id": "{{_matA}}", "vehicle_id": "{{_vehB}}" } ] }
        """);

        Assert.Equal(1, res.PermanentSkipped);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM material_compatible_vehicles;"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // TNT-03 · OKUMA SAVUNMASI (veri zaten bozuksa bile sızmasın)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ TNT-03 — <b>SAVUNMA KATMANI:</b> veritabanında (eski sürümden kalma ya da elle açılmış)
    /// firma ötesi bir muadil satırı BULUNSA BİLE malzeme kartı onu GÖSTERMEZ.
    ///
    /// Yazma kapısı kapatıldı; bu test okuma tarafının da bağımsız olarak korunduğunu kilitler
    /// (tek bir kapıya güvenmeme ilkesi — aynı savunma malzeme LİSTESİ sorgusunda zaten vardı).
    /// </summary>
    [Fact]
    public void TNT03_Bozuk_Satir_Olsa_Bile_Malzeme_Karti_Sizdirmaz()
    {
        // Kapıyı ATLAYARAK doğrudan veritabanına firma ötesi bağ yaz (gerçek dünyada eski veri).
        Sql($"INSERT INTO material_equivalents(material_id, equivalent_material_id) VALUES('{_matA}','{_matB}');");

        var detail = _materials.GetDetail(_a, _matA);

        Assert.DoesNotContain(detail.Equivalents, x => x.Id == _matB);
        Assert.DoesNotContain(detail.Equivalents, x => x.Code == "B-GIZLI");
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
