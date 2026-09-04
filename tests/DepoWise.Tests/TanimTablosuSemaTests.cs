using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TANIM TABLOSU ŞEMA NÖBETİ (kullanıcı bildirimi 2026-09-04) ═══
///
/// <b>Yaşanan hata:</b> Tanımlar ekranında <c>SQLite Error 1: 'no such column: is_locked'</c>.
///
/// <b>Kök neden — sıra hatası:</b> Migration051 tanım tablolarına <c>is_locked</c> ekledi, ama o
/// tarihte var olan 8 tabloyu kapsıyordu. <c>equipment_types</c> DAHA SONRA (Migration075) eklendi
/// ve sütun unutuldu. <c>LookupService.List</c> ise HER tanım tablosunda <c>is_locked</c> okur →
/// ekran açılmıyordu. Migration088 sütunu ekledi.
///
/// <b>Bu test neden var:</b> aynı sınıf hata, gelecekte eklenecek her yeni tanım tablosunda tekrar
/// edebilir ve ancak KULLANICI ekranı açınca fark edilir. Test bunu derleme/CI aşamasında yakalar.
///
///  TNM1 — Tanımlar ekranındaki HER tablo LookupService ile okunabilir (gerçek sorgu çalışır)
///  TNM2 — equipment_types özelinde: eskiden patlayan çağrı artık çalışır (regresyon kilidi)
///  TNM3 — Her tanım tablosunda is_locked sütunu VAR
/// </summary>
public class TanimTablosuSemaTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly LookupService _lookups;
    private readonly SessionContext _admin;
    private const string Co = "TNMSEMA";

    /// <summary>
    /// Tanımlar ekranının (SettingsViewModel + web Tanım Düzenle) okuduğu TÜM tanım tabloları.
    /// Yeni bir tanım tablosu eklenirse buraya da eklenmeli — test o zaman şemayı doğrular.
    /// </summary>
    public static readonly string[] TanimTablolari =
    {
        "material_categories", "brands", "units", "suppliers",
        "vehicle_types", "vehicle_categories", "vehicle_models", "branches",
        "equipment_types",   // 2026-09-04: eksik olan buydu
    };

    public TanimTablosuSemaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_tnmsema_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        _lookups = new LookupService(_f);
        var users = new UserService(_f);
        var uid = users.EnsureInitialAdmin(Co, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Theory]
    [MemberData(nameof(Tablolar))]
    public void TNM1_Tanim_Tablosu_LookupService_ile_Okunabilir(string tablo)
    {
        // Gerçek sorguyu çalıştırır: eksik sütun varsa BURADA patlar (kullanıcı ekranda değil).
        var kayitlar = _lookups.List(_admin, tablo);
        Assert.NotNull(kayitlar);   // boş olabilir; önemli olan SORGUNUN çalışması
    }

    [Fact]
    public void TNM2_EquipmentTypes_Artik_Patlamiyor()
    {
        // 2026-09-04'te bu çağrı "no such column: is_locked" ile patlıyordu.
        var ex = Record.Exception(() => _lookups.List(_admin, "equipment_types"));
        Assert.Null(ex);
    }

    [Theory]
    [MemberData(nameof(Tablolar))]
    public void TNM3_Her_Tanim_Tablosunda_is_locked_Var(string tablo)
    {
        using var conn = _f.Create();
        Assert.True(DbIntrospect.ColumnExists(conn, null, tablo, "is_locked"),
            $"'{tablo}' tablosunda is_locked sütunu YOK — Tanımlar ekranı bu tabloyu açamaz. " +
            "Yeni tanım tablosu eklerken is_locked sütununu da ekleyin (bkz. Migration051/Migration088).");
    }

    public static TheoryData<string> Tablolar()
    {
        var d = new TheoryData<string>();
        foreach (var t in TanimTablolari) d.Add(t);
        return d;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
