using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ K (2026-09-05) — BELGE NUMARASI ALANI: SINIR VE NORMALLEŞTİRME ═══
///
/// <b>Uçtan uca denetimde bulundu (protokol §5: "karakter limiti backend'de de korunuyor mu?"):</b>
/// belge/fatura/irsaliye numarası alanlarının <b>hiçbirinde uzunluk sınırı yoktu</b> — ne bu turda
/// eklenenlerde (yakıt dağıtımı, araç ve ekipman bakımı) ne de daha eskilerde (stok belgesinin üç
/// alanı, yakıt depo girişi). Yanlışlıkla yapıştırılan uzun bir metin sessizce kabul ediliyordu:
/// satır şişer, her senkron turunda taşınır, liste ve Excel hücresi okunamaz hâle gelir — ve hiçbir
/// uyarı çıkmaz.
///
/// <b>Kapı SERVİS katmanındadır</b>, arayüzde değil: masaüstü bu servisleri ÇEVRİMDIŞI da çağırır.
///
///  BN1 — Normalleştirme: kenar boşluğu kırpılır, boş metin NULL olur
///  BN2 — Sınırı aşan değer REDDEDİLİR (sessizce kırpılmaz — kullanıcı yanlışını öğrenmeli)
///  BN3 — Sınırdaki değer (tam 100) kabul edilir — gerçek belge numaraları kesilmez
///  BN4 — Kural gerçekten SERVİSTE: yakıt dağıtımı uzun belge no ile reddedilir
///  BN5 — Aynı kural bakım hattında da geçerli (araç + ekipman)
///  BN6 — Stok belgesinin ÜÇ alanı da aynı kapıdan geçer
/// </summary>
public class BelgeNoSinirTests : IDisposable
{
    private const string Co = "BNS";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;
    private readonly string _mat, _sube, _arac, _def, _ekipman;
    private static readonly long Gun = 1_700_000_000_000;
    private static readonly string CokUzun = new('X', BelgeNo.EnFazlaUzunluk + 1);

    public BelgeNoSinirTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_bns_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
        _arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        _def = new MaintenanceDefinitionService(_f)
            .Create(_admin, new NewMaintenanceDefinition("Yağ", 100m, "day", null, null));
        _ekipman = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{_ekipman}','{Co}','EKP-1','Jeneratör','active',1,1,1,0);");
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ══════════════ SAF KURAL ══════════════

    [Fact]
    public void BN1_Normallestirme_Kirpar_Ve_Bos_Metni_Null_Yapar()
    {
        Assert.Equal("IRS-1", BelgeNo.Normalize("  IRS-1  "));
        Assert.Null(BelgeNo.Normalize("   "));
        Assert.Null(BelgeNo.Normalize(""));
        Assert.Null(BelgeNo.Normalize(null));
    }

    /// <summary>⭐ Sessizce KIRPMIYORUZ. Kırpsaydık kullanıcı yanlış alana yazdığını hiç öğrenmez ve
    /// verisi habersiz budanmış olurdu — bu gece kapatılan "sessiz" kusur sınıfının aynısı.</summary>
    [Fact]
    public void BN2_Siniri_Asan_Deger_Reddedilir()
    {
        var ex = Assert.Throws<ArgumentException>(() => BelgeNo.Normalize(CokUzun, "Fatura numarası"));
        Assert.Contains("Fatura numarası", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public void BN3_Sinirdaki_Deger_Kabul_Edilir()
    {
        var tam = new string('A', BelgeNo.EnFazlaUzunluk);
        Assert.Equal(tam, BelgeNo.Normalize(tam));
    }

    // ══════════════ SERVİS KATMANINDA GERÇEKTEN UYGULANIYOR MU ══════════════

    [Fact]
    public void BN4_Yakit_Dagitiminda_Servis_Reddediyor()
    {
        var fuel = new FuelService(_f);
        fuel.AddDepotEntry(_admin, new NewDepotEntry(1000m, 40m, "TRY", null, "DEP-1", null, Gun), "op-depo");

        Assert.Throws<ArgumentException>(() =>
            fuel.Distribute(_admin, new NewDistribution(_arac, 10m, 100m, 42m, "TRY", null, Gun,
                InvoiceNo: CokUzun), "op-uzun"));

        // Reddedilen işlem YARIM KAYIT bırakmaz.
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM fuel_distributions;"));

        // Makul değer normal geçer.
        fuel.Distribute(_admin, new NewDistribution(_arac, 10m, 200m, 42m, "TRY", null, Gun,
            InvoiceNo: "  IRS-2026-1  "), "op-normal");
        Assert.Equal("IRS-2026-1", Metin("SELECT invoice_no FROM fuel_distributions LIMIT 1;"));
    }

    [Fact]
    public void BN5_Bakim_Hattinda_Da_Gecerli()
    {
        var maint = new MaintenanceService(_f);
        Assert.Throws<ArgumentException>(() =>
            maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun,
                StockLocationId: _sube, InvoiceNo: CokUzun), "op-m-uzun"));

        var eqm = new EquipmentMaintenanceService(_f);
        Assert.Throws<ArgumentException>(() =>
            eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun,
                StockLocationId: _sube, InvoiceNo: CokUzun), "op-e-uzun"));

        Assert.Equal(0L, Say("SELECT COUNT(*) FROM vehicle_maintenances;"));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM equipment_maintenances;"));
    }

    /// <summary>⭐ Kural yalnız BU TURDA eklenen alanlara değil, DAHA ÖNCEDEN var olan üç stok belgesi
    /// alanına da uygulandı. Yalnız yenilere uygulamak, aynı ekranda iki farklı davranış üretirdi.</summary>
    [Fact]
    public void BN6_Stok_Belgesinin_Uc_Alani_Da_Ayni_Kapidan_Gecer()
    {
        var stock = new StockService(_f);
        foreach (var (inv, ord, crd) in new[]
                 {
                     (CokUzun, (string?)null, (string?)null),
                     (null, CokUzun, null),
                     (null, (string?)null, CokUzun),
                 })
        {
            Assert.Throws<ArgumentException>(() =>
                stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 1m, 10m) }, Guid.NewGuid().ToString("N"),
                    branchId: _sube, docDate: Gun, invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd));
        }
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM stock_documents;"));
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private string? Metin(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
