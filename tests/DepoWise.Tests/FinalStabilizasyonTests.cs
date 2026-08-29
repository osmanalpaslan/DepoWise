using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Maintenance;
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
/// 2) ⭐ FIN-B1 ÇÖZÜLDÜ (ADR-185, Migration082 ile birlikte) — ESKİ SINIR ARTIK YOK: eski tablolarda
///    (stock_movements, fuel_*, daily_activities, vehicle_maintenances, assignment_movements) ve
///    ayrıca sync_inbox'ta operation_id benzersizliği FİRMA KAPSAMINA alındı
///    → (company_id, operation_id). Farklı firmalarda aynı operation_id artık BİRBİRİNDEN BAĞIMSIZ
///    meşru işlemlerdir (FIN5 · FIN11–FIN13 · FIN16–FIN17); aynı firmada retry idempotentliği
///    DEĞİŞMEDEN korunur (FIN1–FIN4). Eski "sessiz atlama" kilidi bilinçli olarak yeni sözleşmeye
///    çevrildi (PK-FIN-04=A) — bu bir sözleşme değişikliğidir, test gevşetmesi DEĞİLDİR.
///    ⭐ PK-FIN-02=B: sync_inbox de kapsamdadır — Push akışında InboxHas servis katmanından ÖNCE
///    çalıştığı için yalnız servisleri düzeltmek yeterli olmazdı (çevrimdışı masaüstü birincil istemci).
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

    /// <summary>⭐ FIN-B1 YENİ SÖZLEŞME KİLİDİ (ADR-185 / PK-FIN-04=A · Migration082 ile birlikte):
    /// farklı firmalarda AYNI operation_id, BİRBİRİNDEN BAĞIMSIZ meşru işlemlerdir — ikisi de kaydolur.
    ///
    /// <b>Tarihçe (sözleşme değişikliği, test GEVŞETMESİ DEĞİL):</b> bu test daha önce
    /// <c>FIN5_FarkliFirma_AyniOperationId_Bugun_Sessiz_Atlanir</c> adıyla HATALI davranışı (B firmasının
    /// meşru işlemi sessizce atlanıyor, kayıt oluşmuyor) bilinen sınır olarak KİLİTLİYORDU. FIN-B1
    /// uygulandığı için kilit yeni ve DOĞRU sözleşmeye çevrildi: artık atlama YOK, iki ayrı kayıt VAR.
    /// Aynı-firma retry idempotentliği FIN1–FIN4'te aynen korunur.</summary>
    [Fact]
    public void FIN5_FarkliFirma_AyniOperationId_Iki_Ayri_Kayit_Olusur()
    {
        var fuel = new FuelService(_f);
        var idA = fuel.AddDepotEntry(_a, new NewDepotEntry(100m, 40m), "FIN-B1-OP");
        Assert.NotEqual("", idA);

        // B firması AYNI op-id ile: ARTIK ATLANMAZ — kendi kaydı oluşur (FIN-B1 düzeltmesi).
        var idB = fuel.AddDepotEntry(_b, new NewDepotEntry(70m, 40m), "FIN-B1-OP");
        Assert.NotEqual("", idB);
        Assert.NotEqual(idA, idB);

        Assert.Equal(1L, DepoSayisi("FIN-A"));
        Assert.Equal(1L, DepoSayisi("FIN-B"));

        // Aynı firmada retry HÂLÂ idempotent (yeni sözleşme eski korumayı bozmaz).
        Assert.Equal("", fuel.AddDepotEntry(_b, new NewDepotEntry(70m, 40m), "FIN-B1-OP"));
        Assert.Equal(1L, DepoSayisi("FIN-B"));
    }

    private long DepoSayisi(string co)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM fuel_depot_entries WHERE company_id=@c;";
        cmd.AddWithValue("@c", co);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>⭐ FIN-B1 çapraz-firma kilidi — AÇILIŞ STOĞU (stock_movements): A firmasında kullanılmış
    /// operation_id, B firmasının açılış hareketini engellemez; her firma kendi hareketini yazar.</summary>
    [Fact]
    public void FIN11_FarkliFirma_AyniOperationId_AcilisStogu_Engellenmez()
    {
        var mats = new MaterialService(_f);
        var acilis = new OpeningStockService(_f);
        var mA = mats.Create(_a, new NewMaterial("XF-A", "Çimento"));
        var mB = mats.Create(_b, new NewMaterial("XF-B", "Çimento"));

        acilis.RecordOpening(_a, mA, 50m, "FIN-XF-ACL");
        acilis.RecordOpening(_b, mB, 30m, "FIN-XF-ACL");   // AYNI op-id, FARKLI firma → engellenmez

        Assert.Equal(1, HareketSayisi("FIN-A"));
        Assert.Equal(1, HareketSayisi("FIN-B"));

        acilis.RecordOpening(_b, mB, 30m, "FIN-XF-ACL");   // aynı firma retry → hâlâ idempotent
        Assert.Equal(1, HareketSayisi("FIN-B"));
    }

    /// <summary>⭐ FIN-B1 çapraz-firma kilidi — ZİMMET (assignment_movements) ve devir çifti (:out/:in).</summary>
    [Fact]
    public void FIN12_FarkliFirma_AyniOperationId_Zimmet_Engellenmez()
    {
        var zmt = new AssignmentService(_f);
        var ekp = new EquipmentService(_f);
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f));

        var eA = ekp.Create(_a, new NewEquipment("XF-E-A", "Jeneratör"));
        var pA = per.Create(_a, new DepoWise.Infrastructure.Org.NewPersonnel("Ali", null, null, _subeA, true, false));
        var eB = ekp.Create(_b, new NewEquipment("XF-E-B", "Jeneratör"));
        var pB = per.Create(_b, new DepoWise.Infrastructure.Org.NewPersonnel("Veli", null, null, _subeB, true, false));

        zmt.Issue(_a, "equipment", eA, pA, 1m, _subeA, null, null, "FIN-XF-ZMT");
        zmt.Issue(_b, "equipment", eB, pB, 1m, _subeB, null, null, "FIN-XF-ZMT");   // AYNI op-id → engellenmez

        Assert.Equal(1m, Assert.Single(zmt.Holdings(_a, assetType: "equipment")).Quantity);
        Assert.Equal(1m, Assert.Single(zmt.Holdings(_b, assetType: "equipment")).Quantity);

        zmt.Issue(_b, "equipment", eB, pB, 1m, _subeB, null, null, "FIN-XF-ZMT");   // aynı firma retry
        Assert.Equal(1m, Assert.Single(zmt.Holdings(_b, assetType: "equipment")).Quantity);
    }

    /// <summary>⭐ FIN-B1 çapraz-firma kilidi — BAKIM (vehicle_maintenances): B firmasının bakımı,
    /// A'da kullanılmış operation_id yüzünden A'nın kaydının id'sini DÖNDÜRMEZ (yabancı kayıt sızıntısı).</summary>
    [Fact]
    public void FIN13_FarkliFirma_AyniOperationId_Bakim_Yabanci_Kayit_Dondurmez()
    {
        var arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f);
        var bkm = new MaintenanceService(_f);
        var defs = new MaintenanceDefinitionService(_f);

        var vA = arac.Create(_a, new DepoWise.Infrastructure.Vehicles.NewVehicle("XF-ARC-A"));
        var dA = defs.Create(_a, new NewMaintenanceDefinition("Yağ", 10000m, "km"));
        var vB = arac.Create(_b, new DepoWise.Infrastructure.Vehicles.NewVehicle("XF-ARC-B"));
        var dB = defs.Create(_b, new NewMaintenanceDefinition("Yağ", 10000m, "km"));

        var idA = bkm.Save(_a, new NewMaintenance(vA, dA, PerformedKm: 1000m), "FIN-XF-BKM");
        var idB = bkm.Save(_b, new NewMaintenance(vB, dB, PerformedKm: 2000m), "FIN-XF-BKM");

        Assert.NotEqual(idA, idB);                       // B, A'nın kaydını ALMADI
        Assert.Equal(idB, bkm.Save(_b, new NewMaintenance(vB, dB, PerformedKm: 2000m), "FIN-XF-BKM")); // retry aynen
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

    // ══════════════ ⭐ FIN-B1 / PK-FIN-02=B — sync_inbox FİRMA KAPSAMI (ADR-185) ══════════════

    /// <summary>Bir firma için etkin cihaz token'ı üretir (kayıt + admin onayı).</summary>
    private string CihazTokeni(SessionContext s, string ad)
    {
        var enroll = new DepoWise.Infrastructure.Sync.EnrollmentService(_f);
        var key = enroll.CreateEnrollmentKey(s);
        var dev = enroll.Enroll(s.CompanyId, key, ad);
        return enroll.ApproveDevice(s, dev.DeviceId).Token;
    }

    private static DepoWise.Application.Sync.SyncOperation Op(string opId, string entityId)
        => new(opId, "material", entityId, "{}", null);

    /// <summary>⭐ sync_inbox — AYNI firma, aynı operation_id: ikinci push İDEMPOTENT ("zaten uygulandı").
    /// Bu, FIN-B1 düzeltmesinin bozmaması gereken değerli davranıştır.</summary>
    [Fact]
    public void FIN16_SyncInbox_AyniFirma_AyniOperationId_Idempotent()
    {
        var server = new DepoWise.Infrastructure.Sync.SyncServer(_f);
        var token = CihazTokeni(_a, "PC-A");

        var ilk = Assert.Single(server.Push(token, new[] { Op("FIN-SNK-OP", "m1") }));
        Assert.NotEqual(DepoWise.Application.Sync.SyncOpResult.AlreadyApplied, ilk.Result);

        var tekrar = Assert.Single(server.Push(token, new[] { Op("FIN-SNK-OP", "m1") }));
        Assert.Equal(DepoWise.Application.Sync.SyncOpResult.AlreadyApplied, tekrar.Result);
    }

    /// <summary>⭐ FIN-B1 ANA KİLİDİ — sync_inbox FARKLI firma, aynı operation_id: B firmasının işlemi
    /// ENGELLENMEZ. Düzeltmeden önce <c>InboxHas</c> firma-kördü ve Push akışında servis katmanından
    /// ÖNCE çalıştığı için B'nin meşru işlemi "AlreadyApplied" sayılıp alt katmana hiç inmeden düşüyordu
    /// — yalnız 6 tabloyu düzeltmek bu yolu KAPATMAZDI (çevrimdışı masaüstü birincil istemcidir).</summary>
    [Fact]
    public void FIN17_SyncInbox_FarkliFirma_AyniOperationId_Engellenmez()
    {
        var server = new DepoWise.Infrastructure.Sync.SyncServer(_f);
        var tokenA = CihazTokeni(_a, "PC-A");
        var tokenB = CihazTokeni(_b, "PC-B");

        var a = Assert.Single(server.Push(tokenA, new[] { Op("FIN-SNK-XF", "m1") }));
        Assert.NotEqual(DepoWise.Application.Sync.SyncOpResult.AlreadyApplied, a.Result);

        // AYNI op-id, FARKLI firma → "zaten uygulandı" DEĞİL; B kendi işlemini işler.
        var b = Assert.Single(server.Push(tokenB, new[] { Op("FIN-SNK-XF", "m1") }));
        Assert.NotEqual(DepoWise.Application.Sync.SyncOpResult.AlreadyApplied, b.Result);

        // İki firmanın da kendi inbox satırı var.
        Assert.Equal(1L, InboxSayisi("FIN-A", "FIN-SNK-XF"));
        Assert.Equal(1L, InboxSayisi("FIN-B", "FIN-SNK-XF"));

        // B'nin kendi tekrarı yine idempotent.
        var bTekrar = Assert.Single(server.Push(tokenB, new[] { Op("FIN-SNK-XF", "m1") }));
        Assert.Equal(DepoWise.Application.Sync.SyncOpResult.AlreadyApplied, bTekrar.Result);
        Assert.Equal(1L, InboxSayisi("FIN-B", "FIN-SNK-XF"));
    }

    private long InboxSayisi(string co, string opId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox WHERE company_id=@c AND operation_id=@op;";
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@op", opId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ══════════════ ⭐ MIGRATION082 KİLİTLERİ (ADR-185) ══════════════

    /// <summary>⭐ Migration082 — 7 hedef indeksin tamamı AYNI ADLA, UNIQUE ve kolonları
    /// (company_id, operation_id) SIRASIYLA olmalı. İndeks adlarının korunması bilinçlidir:
    /// <c>StockBalanceWriter.IsDocumentNumberRace</c> indeks ADINA bakar.</summary>
    [Fact]
    public void FIN18_Migration082_Indeksler_FirmaKapsamli_ve_Adlar_Korundu()
    {
        using var conn = _f.Create();
        foreach (var (index, table) in Migration082_OperationIdCompanyScope.Targets)
        {
            using var bilgi = conn.CreateCommand();
            bilgi.CommandText = $"SELECT \"unique\" FROM pragma_index_list('{table}') WHERE name='{index}';";
            var benzersiz = bilgi.ExecuteScalar();
            Assert.True(benzersiz is not null, $"{index} indeksi {table} üzerinde YOK (ad korunmalıydı).");
            Assert.Equal(1L, Convert.ToInt64(benzersiz));

            using var kol = conn.CreateCommand();
            kol.CommandText = $"SELECT name FROM pragma_index_info('{index}') ORDER BY seqno;";
            var kolonlar = new List<string>();
            using var r = kol.ExecuteReader();
            while (r.Read()) kolonlar.Add(r.GetString(0));
            Assert.Equal(new[] { "company_id", "operation_id" }, kolonlar);
        }
    }

    /// <summary>⭐ Migration082 YALNIZ-İNDEKS kilidi: hedef tablolarda kolon eklenmedi/çıkarılmadı.
    /// (Veri dönüşümü/backfill YOK kararının yapısal kanıtı — PK-FIN-01/02 sınırları.)</summary>
    [Fact]
    public void FIN19_Migration082_Kolonlara_Dokunmadi()
    {
        using var conn = _f.Create();
        foreach (var (_, table) in Migration082_OperationIdCompanyScope.Targets)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name IN ('company_id','operation_id');";
            Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));   // ikisi de DURUYOR
        }
    }

    /// <summary>⭐ Katalog azamisi 82 ve Migration082 uygulanmış olmalı (şema kayıt dışı değişmez).</summary>
    [Fact]
    public void FIN20_Katalog_Azamisi_82()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal(82L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>⭐ GERÇEK YÜKSELTME YOLU (81 → 82): MEVCUT VERİSİ OLAN bir şema-81 veritabanı üzerinde
    /// Migration082 uygulanır. Kanıtlanan: (1) migration başarılı, (2) mevcut satırlar KORUNUR
    /// (silinmez/dönüştürülmez), (3) indeks firma kapsamına geçer, (4) yükseltmeden SONRA çapraz-firma
    /// işlem artık engellenmez. Bu, canlıda yaşanacak senaryonun birebir provasıdır.</summary>
    [Fact]
    public void FIN21_Yukseltme_81den82ye_Mevcut_Veri_Korunur()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_fin81_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f81 = new SqliteConnectionFactory(yol);
            // 1) ŞEMA 81 (Migration082 HARİÇ) + veri
            new MigrationRunner(f81, MigrationCatalog.All().Where(m => m.Version <= 81)).Run();
            var s = KurDb(f81, "UP-A", "upadmin");
            var fuel = new FuelService(f81);
            var eskiId = fuel.AddDepotEntry(s, new NewDepotEntry(100m, 40m), "UP-OP-1");
            Assert.NotEqual("", eskiId);
            Assert.Equal(81L, SemaSurumu(f81));

            // 2) YÜKSELTME → 82
            var uygulanan = new MigrationRunner(f81).Run();
            Assert.Contains(82, uygulanan);
            Assert.Equal(82L, SemaSurumu(f81));

            // 3) MEVCUT SATIR KORUNDU (aynı id, aynı operation_id)
            using (var conn = f81.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM fuel_depot_entries WHERE id=@i AND operation_id='UP-OP-1';";
                cmd.AddWithValue("@i", eskiId);
                Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
            }

            // 4) Yükseltmeden SONRA: başka firma aynı op-id ile artık engellenmiyor
            var s2 = KurDb(f81, "UP-B", "upadminb");
            Assert.NotEqual("", fuel.AddDepotEntry(s2, new NewDepotEntry(70m, 40m), "UP-OP-1"));
            // aynı firma retry hâlâ idempotent
            Assert.Equal("", fuel.AddDepotEntry(s2, new NewDepotEntry(70m, 40m), "UP-OP-1"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    /// <summary>⭐ ROLLBACK KİLİDİ: migration başarısız olursa runner'ın transaction'ı her şeyi geri alır —
    /// <c>schema_migrations</c> yazılmaz ve şema ÖNCEKİ sürümde (81) kalır. PK-FIN-01/05'in güvenlik
    /// dayanağı budur: canlıda 082 patlarsa veritabanı 81'de sağlam kalır, API yeniden dener.</summary>
    [Fact]
    public void FIN22_Migration_Basarisiz_Olursa_Sema_81de_Kalir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_finrb_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 81)).Run();
            var s = KurDb(f, "RB-A", "rbadmin");
            var fuel = new FuelService(f);
            Assert.NotEqual("", fuel.AddDepotEntry(s, new NewDepotEntry(100m, 40m), "RB-OP-1"));
            Assert.Equal(81L, SemaSurumu(f));

            // 82 yerine BOZUK bir migration çalıştır → istisna beklenir
            Assert.ThrowsAny<Exception>(() =>
                new MigrationRunner(f, new IMigration[] { new BozukMigration82() }).Run());

            // Şema 81'de KALDI, veri duruyor
            Assert.Equal(81L, SemaSurumu(f));
            using var conn = f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM fuel_depot_entries WHERE operation_id='RB-OP-1';";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    /// <summary>Yalnız FIN22 için: kasten başarısız olan 82 numaralı migration (rollback kanıtı).</summary>
    private sealed class BozukMigration82 : IMigration
    {
        public int Version => 82;
        public string Name => "bozuk_test_migration";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE UNIQUE INDEX ux_bozuk ON olmayan_tablo(company_id);";
            cmd.ExecuteNonQuery();
        }
    }

    private static long SemaSurumu(SqliteConnectionFactory f)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static SessionContext KurDb(SqliteConnectionFactory f, string co, string user)
    {
        using (var conn = f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }
}
