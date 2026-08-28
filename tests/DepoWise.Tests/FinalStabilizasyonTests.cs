using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FIN-01 (ADR-178) — FINAL STABİLİZASYON SÖZLEŞME KİLİTLERİ ═══
///
/// FINAL çok-makineli simülasyonunun (izole PG) ortaya çıkardığı MEVCUT sözleşme burada KİLİTLENİR:
///
/// 1) AYNI FİRMADA retry idempotenttir — aynı operation_id ikinci kez İKİNCİ işlem üretmez
///    (mal kabul · yakıt · zimmet · açılış stoğu). Bu, korunması gereken değerli değişmezdir.
///
/// 2) ⚠️ BİLİNEN SINIR — FIN-B1 (KARAR BEKLİYOR, KNOWN_ISSUES): eski tablolarda (stock_movements,
///    fuel_*, daily_activities, vehicle_maintenances, assignment_movements) operation_id benzersizliği
///    ŞEMA GEREĞİ FİRMA-ÜSTÜDÜR (Migration005/008/009/076) ve idempotency kontrolleri de buna uygun
///    olarak firma süzgeçsizdir → BAŞKA firmada kullanılmış bir operation_id ile gelen işlem SESSİZCE
///    atlanır (hata YOK, kayıt YOK). Gerçek istemciler GUID ürettiği için pratik olasılık ~sıfırdır;
///    KÖKTEN çözüm canlı tablolarda indeks migration'ı ister (firma-kapsamlı benzersizliğe geçiş —
///    Migration066-068'deki yeni desen) ve KULLANICI KARARINA bırakılmıştır. Bu test, o karar verilene
///    kadar davranışın KAZAYLA değişmemesini (ör. yarım düzeltmeyle UNIQUE-ihlali 500'üne dönüşmesini)
///    engeller; karar uygulanırsa test BİLİNÇLİ güncellenecektir.
/// </summary>
public class FinalStabilizasyonTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _a, _b;
    private readonly string _subeA, _subeB;

    public FinalStabilizasyonTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_fin_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        _a = Kur("FIN-A", "admina", out _subeA);
        _b = Kur("FIN-B", "adminb", out _subeB);
    }

    private SessionContext Kur(string co, string user, out string subeId)
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        subeId = new DepoWise.Infrastructure.Organization.BranchService(_f).Create(s,
            new DepoWise.Infrastructure.Organization.NewBranch("Merkez", "branch"));
        return s;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private long HareketSayisi(string co)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id=@c;";
        cmd.AddWithValue("@c", co);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>⭐ Mal kabul: aynı firmada aynı operation_id ile retry İKİNCİ kabul işlemez (teslim 4 kalır).</summary>
    [Fact]
    public void FIN1_PoMalKabul_AyniFirma_Retry_Idempotent()
    {
        var mats = new MaterialService(_f);
        var po = new PurchaseOrderService(_f);
        var mat = mats.Create(_a, new NewMaterial("PO-M-1", "Malzeme"));
        var id = po.Create(_a, new NewPurchaseOrder("PO-1", BranchId: _subeA,
            Lines: new List<NewPurchaseOrderLine> { new(mat, 10m, 5m) }));
        var lineId = po.Lines(_a, id)[0].Id;

        po.Receive(_a, id, new[] { new ReceiveLine(lineId, 4m) }, "FIN-OP-1");
        Assert.Equal(4m, po.Lines(_a, id)[0].ReceivedQty);
        po.Receive(_a, id, new[] { new ReceiveLine(lineId, 4m) }, "FIN-OP-1");   // retry
        Assert.Equal(4m, po.Lines(_a, id)[0].ReceivedQty);                        // 8 OLMADI
    }

    /// <summary>⭐ Yakıt: aynı firmada retry idempotent — depo girişi "" döner, dağıtım MEVCUT id'yi döner.</summary>
    [Fact]
    public void FIN2_Yakit_AyniFirma_Retry_Idempotent()
    {
        var fuel = new FuelService(_f);
        var idA = fuel.AddDepotEntry(_a, new NewDepotEntry(100m, 40m), "FIN-YKT-D");
        Assert.NotEqual("", idA);
        Assert.Equal("", fuel.AddDepotEntry(_a, new NewDepotEntry(100m, 40m), "FIN-YKT-D"));

        var arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f);
        var v = arac.Create(_a, new DepoWise.Infrastructure.Vehicles.NewVehicle("FIN-ARC-A"));
        var d1 = fuel.Distribute(_a, new NewDistribution(v, 10m, 100), "FIN-YKT-G");
        var d2 = fuel.Distribute(_a, new NewDistribution(v, 10m, 100), "FIN-YKT-G");
        Assert.Equal(d1, d2);
    }

    /// <summary>⭐ Zimmet: aynı firmada retry ikinci hareket üretmez (miktar 1 kalır).</summary>
    [Fact]
    public void FIN3_Zimmet_AyniFirma_Retry_Idempotent()
    {
        var zmt = new AssignmentService(_f);
        var ekp = new EquipmentService(_f);
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f));
        var e = ekp.Create(_a, new NewEquipment("FIN-E-A", "Jeneratör"));
        var p = per.Create(_a, new DepoWise.Infrastructure.Org.NewPersonnel("Ali", null, null, _subeA, true, false));

        zmt.Issue(_a, "equipment", e, p, 1m, _subeA, null, null, "FIN-ZMT-1");
        zmt.Issue(_a, "equipment", e, p, 1m, _subeA, null, null, "FIN-ZMT-1");   // retry
        var h = Assert.Single(zmt.Holdings(_a, assetType: "equipment"));
        Assert.Equal(1m, h.Quantity);
    }

    /// <summary>⭐ Açılış stoğu: aynı firmada retry ikinci hareket üretmez.</summary>
    [Fact]
    public void FIN4_AcilisStogu_AyniFirma_Retry_Idempotent()
    {
        var mats = new MaterialService(_f);
        var acilis = new OpeningStockService(_f);
        var m = mats.Create(_a, new NewMaterial("ACL-A", "Çimento"));
        acilis.RecordOpening(_a, m, 50m, "FIN-ACL-1");
        Assert.Equal(1, HareketSayisi("FIN-A"));
        acilis.RecordOpening(_a, m, 50m, "FIN-ACL-1");   // retry
        Assert.Equal(1, HareketSayisi("FIN-A"));
    }

    /// <summary>⚠️ FIN-B1 SÖZLEŞME KİLİDİ (bilinen sınır — karar bekliyor): BAŞKA firmada kullanılmış
    /// operation_id ile gelen işlem bugün SESSİZCE atlanır (hata fırlamaz, kayıt oluşmaz). Şema gereği
    /// (operation_id firma-üstü UNIQUE) kayıt zaten oluşamazdı; bu test davranışın KAZAYLA
    /// değişmemesini (ör. UNIQUE-ihlali 500'üne dönüşmesini) engeller. Karar uygulanırsa
    /// (firma-kapsamlı benzersizlik migration'ı) bu test bilinçli güncellenir.</summary>
    [Fact]
    public void FIN5_FarkliFirma_AyniOperationId_Bugun_Sessiz_Atlanir()
    {
        var fuel = new FuelService(_f);
        Assert.NotEqual("", fuel.AddDepotEntry(_a, new NewDepotEntry(100m, 40m), "FIN-B1-OP"));
        // B firması AYNI op-id ile: istisna YOK, kayıt YOK (mevcut sözleşme — FIN-B1).
        var idB = fuel.AddDepotEntry(_b, new NewDepotEntry(70m, 40m), "FIN-B1-OP");
        Assert.Equal("", idB);
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM fuel_depot_entries WHERE company_id='FIN-B';";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }
}
