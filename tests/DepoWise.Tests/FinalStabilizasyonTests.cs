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
/// 2) ⚠️ BİLİNEN SINIR — FIN-B1 (AYRI ONAY BEKLİYOR, KNOWN_ISSUES): eski tablolarda (stock_movements,
///    fuel_*, daily_activities, vehicle_maintenances, assignment_movements) operation_id benzersizliği
///    ŞEMA GEREĞİ FİRMA-ÜSTÜDÜR (Migration005/008/009/076) ve idempotency kontrolleri de buna uygun
///    olarak firma süzgeçsizdir → BAŞKA firmada kullanılmış bir operation_id ile gelen işlem SESSİZCE
///    atlanır (hata YOK, kayıt YOK). Gerçek istemciler GUID ürettiği için pratik olasılık ~sıfırdır.
///    KÖKTEN çözüm (firma-kapsamlı benzersizlik = Migration082 + kod çifti) ADR-179'da TASARLANDI ve
///    KANITLANDI, sonra ADR-180 ile MASTER'DAN GERİ ÇEKİLDİ (kullanıcı kararı PK-R4=B: Migration082
///    production'a onaysız gitmez; tasarım git geçmişinde `35d7bce`). Bu test, o onay verilene kadar
///    davranışın KAZAYLA değişmemesini (ör. yarım düzeltmeyle UNIQUE-ihlali 500'üne dönüşmesini)
///    engeller; onay uygulanırsa test BİLİNÇLİ güncellenecektir.
///
/// 3) ADR-179'un Migration082'den BAĞIMSIZ kilitleri korunur: STK-B2 (belge notu aranmaz — FIN8) ve
///    SNK-05 (online ilk-onay-kazanır — FIN9 · offline LWW + data_conflicts — FIN10).
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

    /// <summary>⚠️ FIN-B1 SÖZLEŞME KİLİDİ (bilinen sınır — ayrı onay bekliyor): BAŞKA firmada kullanılmış
    /// operation_id ile gelen işlem bugün SESSİZCE atlanır (hata fırlamaz, kayıt oluşmaz). Şema gereği
    /// (operation_id firma-üstü UNIQUE) kayıt zaten oluşamazdı; bu test davranışın KAZAYLA
    /// değişmemesini (ör. UNIQUE-ihlali 500'üne dönüşmesini) engeller. Onay uygulanırsa
    /// (firma-kapsamlı benzersizlik migration'ı — tasarım `35d7bce`'de) bu test bilinçli güncellenir.</summary>
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

    // ══════════════ KARAR PAKETİ KALICI KİLİTLERİ (ADR-179 — Migration082'den bağımsız) ══════════════

    /// <summary>⭐ STK-B2 (KARAR: HAYIR — ADR-179): stok BELGESİ notu global aramada ARANMAZ.
    /// Kimlik-alanı-aranır kuralı korunur; belge notunda geçen metin arama sonucu DÖNDÜRMEZ.</summary>
    [Fact]
    public void FIN8_StokBelgesiNotu_GlobalAramada_Aranmaz()
    {
        const string Benzersiz = "BELGENOTU-GIZLI-XQZ7";
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO stock_documents(id,company_id,doc_type,doc_no,doc_date,note,created_at,updated_at,version,is_deleted)
VALUES(@id,'FIN-A','in','SG-2026-0001',1,@n,1,1,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@n", "Şoföre teslim — " + Benzersiz);
            cmd.ExecuteNonQuery();
        }
        var gruplar = new DepoWise.Infrastructure.Search.SearchService(_f).Search(_a, Benzersiz);
        Assert.Empty(gruplar);
    }

    /// <summary>⭐ SNK-05/ONLINE (KARAR: a — ADR-179): İLK geçerli onay kazanır — durum makinesi ikinci
    /// onayı ve onay-sonrası reddi GEÇERSİZ GEÇİŞ olarak reddeder (çift onay yapısal olarak imkânsız).</summary>
    [Fact]
    public void FIN9_Snk05_Online_IlkOnay_Kazanir()
    {
        var reqId = Guid.NewGuid().ToString("N");
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO material_requests(id,company_id,doc_no,request_date,status,created_at,updated_at,version,is_deleted)
VALUES(@id,'FIN-A','TAL-FIN-1',1,'pending',1,1,1,0);";
            cmd.AddWithValue("@id", reqId);
            cmd.ExecuteNonQuery();
        }
        var req = new DepoWise.Infrastructure.Requests.RequestService(_f, new StockService(_f));
        req.Approve(_a, reqId);                                                    // İLK onay kazanır
        Assert.ThrowsAny<InvalidOperationException>(() => req.Approve(_a, reqId)); // ikinci onay REDDEDİLİR
        Assert.ThrowsAny<InvalidOperationException>(() => req.Reject(_a, reqId, "geç kalan ret")); // onay sonrası ret REDDEDİLİR
    }

    /// <summary>⭐ SNK-05/OFFLINE (KARAR: a — ADR-179): çevrimdışı çakışmada senkron LWW'dir — daha YENİ
    /// updated_at kazanır, daha eski gelen değişiklik uygulanmaz (kaybeden, mevcut data_conflicts
    /// mekanizmasına düşer). Bu MEVCUT sözleşmedir; "offline ilk-onay-kazanır"a çevrilmesi bilinçli olarak
    /// YAPILMADI (senkron protokolü değişikliği ister — kullanıcı kararı). SNK-13'e dokunulmadı.</summary>
    [Fact]
    public void FIN10_Snk05_Offline_LWW_Sozlesmesi()
    {
        var sync = new DepoWise.Infrastructure.Sync.BusinessSyncService(_f);
        var reqId = Guid.NewGuid().ToString("N");

        System.Text.Json.JsonElement Paket(string status, long updatedAt)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["tables"] = new Dictionary<string, object>
                {
                    ["material_requests"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["id"] = reqId, ["company_id"] = "FIN-A", ["doc_no"] = "TAL-LWW-1",
                            ["request_date"] = 1, ["status"] = status, ["created_at"] = 1,
                            ["updated_at"] = updatedAt, ["version"] = 1, ["is_deleted"] = 0,
                        },
                    },
                },
            });
            return System.Text.Json.JsonDocument.Parse(json).RootElement;
        }

        string Durum()
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status FROM material_requests WHERE id=@id;";
            cmd.AddWithValue("@id", reqId);
            return (string)cmd.ExecuteScalar()!;
        }

        Assert.Equal(1, sync.Apply("FIN-A", Paket("approved", 2000)).Upserted);   // makine 1: onay (T=2000)
        Assert.Equal("approved", Durum());
        sync.Apply("FIN-A", Paket("rejected", 1000));                              // makine 2: DAHA ESKİ ret
        Assert.Equal("approved", Durum());                                         // eski gelen KAZANAMAZ
        sync.Apply("FIN-A", Paket("rejected", 3000));                              // makine 2: DAHA YENİ ret
        Assert.Equal("rejected", Durum());                                         // LWW: son yazan kazanır
    }
}
