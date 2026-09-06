using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.8 — ARAÇ BAKIMLARI: PLAKA + SORGULAMA (2026-09-06) ═══
///
/// <b>Bildirilen iki eksik.</b> (1) Bakım listesinde PLAKA yok — kullanıcı aracı sahada plakasıyla
/// tanıyor. (2) Tarih / araç kodu / plaka ile sorgulama alanı ve butonları görünmüyor.
///
/// <b>Bulgu.</b> Servis (<c>SearchMaintenancesGrid</c>) tarih aralığı ve serbest metin süzmesini
/// (araç kodu · plaka · bakım adı · açıklama · belge no) ZATEN destekliyordu; eksik olan yalnız
/// ARAYÜZDÜ. Bu yüzden yeni bir sorgu altyapısı kurulmadı; plaka satıra eklendi ve iki ekran
/// mevcut servise bağlandı.
///
///  BL1 — Satır PLAKA taşır; plakasız araçta "—" gösterilir
///  BL2 — Plakayla arama kaydı bulur (kullanıcının verdiği örnek: 06 FZ 4146)
///  BL3 — Araç KODUYLA arama da çalışır
///  BL4 — Tarih aralığı süzer; aralık dışı kayıt gelmez
///  BL5 — Bitiş günü DAHİLDİR (gün sonu sınırı)
/// </summary>
public class BakimListesiSorgulamaTests : IDisposable
{
    private const string Co = "BLS";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly MaintenanceService _bakim;
    private readonly SessionContext _admin;
    private readonly string _aracA, _aracB, _tanim;

    // 10.03.2024 ve 20.03.2024 (UTC gün başı)
    private static readonly long Gun10 = new DateTimeOffset(2024, 3, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly long Gun20 = new DateTimeOffset(2024, 3, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public BakimListesiSorgulamaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_bls_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var uid = new UserService(_f).EnsureInitialAdmin(Co, "bls_admin", "Bls!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var araclar = new VehicleService(_f);
        _aracA = araclar.Create(_admin, new NewVehicle("KAM-ME 059", "06 FZ 4146"));
        _aracB = araclar.Create(_admin, new NewVehicle("KAM-ME 060", null));   // plakasız

        _bakim = new MaintenanceService(_f);
        _tanim = new MaintenanceDefinitionService(_f).Create(_admin, new NewMaintenanceDefinition("Yağ Bakımı", 10_000m, "km"));

        _bakim.Save(_admin, new NewMaintenance(_aracA, _tanim, PerformedKm: 1_000m, PerformedDate: Gun10), Op());
        _bakim.Save(_admin, new NewMaintenance(_aracB, _tanim, PerformedKm: 2_000m, PerformedDate: Gun20), Op());
    }

    private static string Op() => Guid.NewGuid().ToString("N");

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private IReadOnlyList<MaintenanceRow> Ara(string? q = null, long? from = null, long? to = null)
        => _bakim.SearchMaintenancesGrid(_admin, 1, 50, null, q, from, to).Items;

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ BL1 ══════════════════

    [Fact]
    public void BL1_Satir_Plaka_Tasir()
    {
        var hepsi = Ara();

        var a = hepsi.Single(r => r.VehicleCode == "KAM-ME 059");
        Assert.Equal("06 FZ 4146", a.VehiclePlate);
        Assert.Equal("06 FZ 4146", a.PlateDisplay);
        Assert.Equal("KAM-ME 059 (06 FZ 4146)", a.VehicleWithPlate);

        var b = hepsi.Single(r => r.VehicleCode == "KAM-ME 060");
        Assert.Null(b.VehiclePlate);
        Assert.Equal("—", b.PlateDisplay);              // plakasız araçta boş değil TİRE
        Assert.Equal("KAM-ME 060", b.VehicleWithPlate);
    }

    // ══════════════════ BL2 / BL3 — ARAMA ══════════════════

    [Fact]
    public void BL2_Plakayla_Arama_Kaydi_Bulur()
    {
        var sonuc = Ara("06 FZ 4146");

        Assert.Single(sonuc);
        Assert.Equal("KAM-ME 059", sonuc[0].VehicleCode);
    }

    [Fact]
    public void BL3_Arac_Koduyla_Arama_Calisir()
    {
        var sonuc = Ara("KAM-ME 060");

        Assert.Single(sonuc);
        Assert.Equal("KAM-ME 060", sonuc[0].VehicleCode);
    }

    // ══════════════════ BL4 / BL5 — TARİH ARALIĞI ══════════════════

    [Fact]
    public void BL4_Tarih_Araligi_Suzer()
    {
        var sonuc = Ara(from: Gun10, to: Gun10 + 86_399_999);

        Assert.Single(sonuc);
        Assert.Equal("KAM-ME 059", sonuc[0].VehicleCode);   // 20 Mart kaydı GELMEZ
    }

    /// <summary>
    /// 🔴 Klasik tuzak: bitiş günü gün BAŞI olarak gönderilirse o günün kayıtları düşer ve kullanıcı
    /// "kaydım kayboldu" der. Arayüz bitişe gün sonunu (+86.399.999 ms) ekler; bu test onu kilitler.
    /// </summary>
    [Fact]
    public void BL5_Bitis_Gunu_Dahildir()
    {
        Assert.Empty(Ara(from: Gun20 + 1, to: Gun20 + 86_399_999).Where(r => r.VehicleCode == "KAM-ME 060"));
        Assert.Single(Ara(from: Gun20, to: Gun20 + 86_399_999));
    }
}
