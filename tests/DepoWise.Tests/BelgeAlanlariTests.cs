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
/// ═══ MUH-01b (FAZ D, 2026-09-04) — PARA DOĞURAN KAYITLARDA BELGE NUMARASI ═══
///
/// Ön muhasebe (FAZ H) bir gideri kaynak belgesine bağlayamazsa, kullanıcı faturayı elinde tutup
/// sistemde karşılığını bulamaz. Belge alanı stok belgesinde ve yakıt depo girişinde ZATEN vardı;
/// <b>yakıt dağıtımı</b> ve <b>iki bakım tablosunda</b> yoktu. Migration089 yalnız ekleme yaptı.
///
///  BLG1 — Üç tabloya da sütun eklendi ve mevcut kayıtlar bozulmadı (migration kanıtı)
///  BLG2 — Yakıt dağıtımında belge no yazılır ve geri okunur
///  BLG3 — Araç bakımında belge no yazılır ve geri okunur
///  BLG4 — Ekipman bakımında belge no yazılır ve geri okunur
///  BLG5 — Alan OPSİYONEL: boş bırakılan kayıt aynen çalışır (mevcut akış zorunlu hâle gelmedi)
///  BLG6 — Boş metin NULL'a çevrilir ("" ile NULL iki ayrı "boş" olmasın) + baştaki/sondaki boşluk kırpılır
///  BLG7 — Belge no ARANABİLİR: eklemek yetmez, aranamayan alan pratikte yoktur
///  BLG8 — Migration089 yalnız EKLEME içerir (canlı veri kanıtı)
/// </summary>
public class BelgeAlanlariTests : IDisposable
{
    private const string Co = "BLG";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly FuelService _fuel;
    private readonly MaintenanceService _maint;
    private readonly EquipmentMaintenanceService _eqm;
    private readonly SessionContext _admin;
    private readonly string _arac, _def, _ekipman, _sube;
    private static readonly long Gun = 1_700_000_000_000;

    public BelgeAlanlariTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_blg_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        _def = new MaintenanceDefinitionService(_f)
            .Create(_admin, new NewMaintenanceDefinition("Yağ Değişimi", 100m, "day", null, null));

        _ekipman = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{_ekipman}','{Co}','EKP-1','Jeneratör','active',1,1,1,0);");

        _fuel = new FuelService(_f);
        _maint = new MaintenanceService(_f);
        _eqm = new EquipmentMaintenanceService(_f);

        // Dağıtım yapılabilmesi için depoda yakıt olmalı.
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(1000m, 40m, "TRY", null, "DEP-1", null, Gun), "op-depo");
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string? Oku(string table, string id)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT invoice_no FROM {table} WHERE id=@i;";
        cmd.AddWithValue("@i", id);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    // ══════════════ ŞEMA ══════════════

    /// <summary>1 — Üç tabloya da sütun eklendi. Sütun YOKSA aşağıdaki testlerin hepsi anlamsızdır,
    /// bu yüzden önce şema kanıtlanır.</summary>
    [Fact]
    public void BLG1_Uc_Tabloya_Da_Belge_Sutunu_Eklendi()
    {
        using var conn = _f.Create();
        foreach (var t in new[] { "fuel_distributions", "vehicle_maintenances", "equipment_maintenances" })
            Assert.True(DbIntrospect.ColumnExists(conn, null, t, "invoice_no"),
                $"{t}.invoice_no sütunu yok — Migration089 uygulanmamış.");
    }

    // ══════════════ YAZ / OKU ══════════════

    [Fact]
    public void BLG2_Yakit_Dagitiminda_Belge_No_Yazilir()
    {
        var id = _fuel.Distribute(_admin, new NewDistribution(_arac, 50m, 1000m, 42m, "TRY", null, Gun,
            InvoiceNo: "IRS-2026-001"), "op-f1");

        Assert.Equal("IRS-2026-001", Oku("fuel_distributions", id));

        // Okuma yolu da taşımalı: veritabanında olup ekrana gelmeyen alan kullanıcı için YOK demektir.
        var satir = _fuel.SearchDistributions(_admin, 1, 50).Items.Single(x => x.Id == id);
        Assert.Equal("IRS-2026-001", satir.InvoiceNo);
        Assert.Equal("IRS-2026-001", satir.InvoiceDisplay);
    }

    [Fact]
    public void BLG3_Arac_Bakiminda_Belge_No_Yazilir()
    {
        var id = _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun,
            StockLocationId: _sube, InvoiceNo: "SRV-77"), "op-m1");

        Assert.Equal("SRV-77", Oku("vehicle_maintenances", id));
        Assert.Equal("SRV-77", _maint.ListMaintenances(_admin).Single(x => x.Id == id).InvoiceNo);
    }

    [Fact]
    public void BLG4_Ekipman_Bakiminda_Belge_No_Yazilir()
    {
        var id = _eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun,
            StockLocationId: _sube, InvoiceNo: "EKP-FTR-9"), "op-e1");

        Assert.Equal("EKP-FTR-9", Oku("equipment_maintenances", id));
        Assert.Equal("EKP-FTR-9", _eqm.List(_admin).Single(x => x.Id == id).InvoiceNo);
    }

    // ══════════════ OPSİYONELLİK ══════════════

    /// <summary>5 — ⭐ EN ÖNEMLİ REGRESYON: alan OPSİYONELDİR. Yeni bir alan eklerken en sık yapılan
    /// hata onu sessizce zorunlu hâle getirmektir; o zaman babanın her gün kullandığı akış kırılır.
    /// Belge no VERİLMEDEN yapılan kayıtlar eskisi gibi çalışmalı ve NULL kalmalıdır.</summary>
    [Fact]
    public void BLG5_Belge_No_Opsiyoneldir_Mevcut_Akis_Kirilmaz()
    {
        var yakit = _fuel.Distribute(_admin, new NewDistribution(_arac, 10m, 1100m, 42m, "TRY", null, Gun), "op-f2");
        var bakim = _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, StockLocationId: _sube), "op-m2");
        var eqm = _eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun, StockLocationId: _sube), "op-e2");

        Assert.Null(Oku("fuel_distributions", yakit));
        Assert.Null(Oku("vehicle_maintenances", bakim));
        Assert.Null(Oku("equipment_maintenances", eqm));

        // Ekranda "—" görünür; boş metin ya da "null" yazısı DEĞİL.
        Assert.Equal("—", _fuel.SearchDistributions(_admin, 1, 50).Items.Single(x => x.Id == yakit).InvoiceDisplay);
    }

    /// <summary>6 — Boş/boşluklu metin NULL'a çevrilir ve kenar boşlukları kırpılır. Aksi hâlde
    /// veritabanında iki ayrı "boş" oluşur ("" ve NULL) ve raporlar ikisini farklı sayar.</summary>
    [Fact]
    public void BLG6_Bos_Metin_Null_Olur_Ve_Bosluk_Kirpilir()
    {
        var bos = _fuel.Distribute(_admin, new NewDistribution(_arac, 5m, 1200m, 42m, "TRY", null, Gun,
            InvoiceNo: "   "), "op-f3");
        Assert.Null(Oku("fuel_distributions", bos));

        var bosluklu = _fuel.Distribute(_admin, new NewDistribution(_arac, 5m, 1300m, 42m, "TRY", null, Gun,
            InvoiceNo: "  IRS-42  "), "op-f4");
        Assert.Equal("IRS-42", Oku("fuel_distributions", bosluklu));

        var bakimBos = _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun,
            StockLocationId: _sube, InvoiceNo: " "), "op-m3");
        Assert.Null(Oku("vehicle_maintenances", bakimBos));

        var eqmBosluklu = _eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun,
            StockLocationId: _sube, InvoiceNo: " SRV-1 "), "op-e3");
        Assert.Equal("SRV-1", Oku("equipment_maintenances", eqmBosluklu));
    }

    // ══════════════ ARANABİLİRLİK ══════════════

    /// <summary>7 — ⭐ Alanı eklemek YETMEZ: kullanıcının amacı "elimdeki faturayı sistemde bulmak".
    /// Aranamayan bir alan pratikte yok gibidir. Yakıt ekranının serbest metin araması belge no'yu
    /// da kapsamalıdır (ARA İŞ 6'da kurulan arama yolunun aynısı).</summary>
    [Fact]
    public void BLG7_Belge_No_Serbest_Metinle_Aranabilir()
    {
        var hedef = _fuel.Distribute(_admin, new NewDistribution(_arac, 7m, 1400m, 42m, "TRY", null, Gun,
            InvoiceNo: "FTR-BENZERSIZ-123"), "op-f5");
        _fuel.Distribute(_admin, new NewDistribution(_arac, 7m, 1500m, 42m, "TRY", null, Gun,
            InvoiceNo: "BASKA-999"), "op-f6");

        var sonuc = _fuel.SearchDistributions(_admin, 1, 50, freeText: "BENZERSIZ-123");
        Assert.Equal(1, sonuc.TotalCount);
        Assert.Equal(hedef, sonuc.Items.Single().Id);
    }

    // ══════════════ CANLI VERİ GÜVENLİĞİ ══════════════

    /// <summary>8 — Migration089 yalnız EKLEME içerir. Canlı veriye dokunan bir ifade (UPDATE/DELETE/
    /// DROP) sızarsa babanın verisi risk altına girer — bu test o yolu kapatır.
    /// (MLY11 ile aynı desen; oradaki kanıt yöntemi burada da uygulanıyor.)</summary>
    [Fact]
    public void BLG8_Migration089_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var kaynak = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration089_DocumentFields.cs"));

        // Yalnız çalıştırılan SQL'e bak: açıklama metinlerindeki kelimeler testi yanıltmasın.
        var i = kaynak.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var sql = kaynak[i..].ToUpperInvariant();
        Assert.Contains("ADD COLUMN", sql);
        foreach (var yasak in new[] { "UPDATE ", "DELETE ", "DROP ", "INSERT ", "NOT NULL" })
            Assert.DoesNotContain(yasak, sql);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
