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
/// ═══ FIN-B1 (ADR-179) — FİRMA KAPSAMLI operation_id SÖZLEŞMESİ (Migration082 + kod birlikte) ═══
///
/// FINAL simülasyonunun bulgusu (ADR-178) karara bağlandı: 6 eski tabloda operation_id benzersizliği
/// KÜRESELDEN FİRMA KAPSAMINA alındı (Migration082 — indeks adları korunarak (company_id, operation_id))
/// ve idempotency kontrollerine company_id süzgeci eklendi. Bu testler YENİ sözleşmeyi kilitler:
///  • AYNI firma + aynı operation_id → retry idempotent (İKİNCİ işlem/duplicate YOK — davranış aynen).
///  • FARKLI firma + aynı operation_id → artık birbirini ENGELLEMEZ (eskiden sessiz no-op'tu).
///  • Tenant izolasyonu bozulmaz; senkron tekilleştirmesi bu indekse bağlı değildir (satırlar id ile
///    upsert edilir); sync_inbox/outbox KAPSAM DIŞI bırakıldı (senkron sözleşmesi değişmedi).
/// ⚠️ Migration082 PRODUCTION'DA ÇALIŞTIRILMADI — canlı şema 81 (yayın ayrı açık onay ister).
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

    // ══════════════ SÖZLEŞME: aynı firma idempotent · farklı firma ENGELLENMEZ ══════════════

    /// <summary>⭐ Mal kabul: B firması, A'nın kullandığı operation_id ile ENGELLENMEZ; A'da retry çift işlemez.</summary>
    [Fact]
    public void FIN1_PoMalKabul_FirmaKapsamli_Idempotent()
    {
        var mats = new MaterialService(_f);
        var po = new PurchaseOrderService(_f);
        const string Op = "FIN-OP-AYNI";

        string Kabul(SessionContext s, string sube)
        {
            var mat = mats.Create(s, new NewMaterial("PO-M-" + s.CompanyId, "Malzeme"));
            var id = po.Create(s, new NewPurchaseOrder("PO-" + s.CompanyId, BranchId: sube,
                Lines: new List<NewPurchaseOrderLine> { new(mat, 10m, 5m) }));
            var lineId = po.Lines(s, id)[0].Id;
            po.Receive(s, id, new[] { new ReceiveLine(lineId, 4m) }, Op);
            Assert.Equal(4m, po.Lines(s, id)[0].ReceivedQty);   // kabul GERÇEKTEN işledi (sessiz no-op YOK)
            return id;
        }

        var poA = Kabul(_a, _subeA);
        Kabul(_b, _subeB);   // eskiden sessiz no-op (0 teslim) olurdu — artık işler

        // AYNI firmada retry: ikinci kabul UYGULANMAZ (teslim 4 kalır — 8 olmaz).
        var lineA = po.Lines(_a, poA)[0].Id;
        po.Receive(_a, poA, new[] { new ReceiveLine(lineA, 4m) }, Op);
        Assert.Equal(4m, po.Lines(_a, poA)[0].ReceivedQty);
    }

    /// <summary>⭐ Yakıt depo + dağıtım: firma kapsamlı; aynı-firma retry aynen ("" / mevcut id döner).</summary>
    [Fact]
    public void FIN2_Yakit_FirmaKapsamli_Idempotent()
    {
        var fuel = new FuelService(_f);
        const string OpDepo = "FIN-YKT-DEPO", OpDag = "FIN-YKT-DAG";

        var idA = fuel.AddDepotEntry(_a, new NewDepotEntry(100m, 40m), OpDepo);
        Assert.NotEqual("", idA);
        var idB = fuel.AddDepotEntry(_b, new NewDepotEntry(70m, 40m), OpDepo);   // eskiden "" dönerdi
        Assert.NotEqual("", idB);
        Assert.NotEqual(idA, idB);
        Assert.Equal("", fuel.AddDepotEntry(_a, new NewDepotEntry(100m, 40m), OpDepo));   // retry → ""

        var arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f);
        var vA = arac.Create(_a, new DepoWise.Infrastructure.Vehicles.NewVehicle("FIN-ARC-A"));
        var vB = arac.Create(_b, new DepoWise.Infrastructure.Vehicles.NewVehicle("FIN-ARC-B"));
        var dA = fuel.Distribute(_a, new NewDistribution(vA, 10m, 100), OpDag);
        var dB = fuel.Distribute(_b, new NewDistribution(vB, 10m, 100), OpDag);   // eskiden A'nın id'si dönerdi!
        Assert.NotEqual(dA, dB);
        Assert.Equal(dA, fuel.Distribute(_a, new NewDistribution(vA, 10m, 100), OpDag));   // retry → mevcut id
    }

    /// <summary>⭐ Zimmet: firma kapsamlı; aynı-firma retry ikinci hareket üretmez.</summary>
    [Fact]
    public void FIN3_Zimmet_FirmaKapsamli_Idempotent()
    {
        var zmt = new AssignmentService(_f);
        var ekp = new EquipmentService(_f);
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f));
        const string Op = "FIN-ZMT-AYNI";

        var eA = ekp.Create(_a, new NewEquipment("FIN-E-A", "Jeneratör"));
        var pA = per.Create(_a, new DepoWise.Infrastructure.Org.NewPersonnel("Ali", null, null, _subeA, true, false));
        var eB = ekp.Create(_b, new NewEquipment("FIN-E-B", "Kompresör"));
        var pB = per.Create(_b, new DepoWise.Infrastructure.Org.NewPersonnel("Veli", null, null, _subeB, true, false));

        zmt.Issue(_a, "equipment", eA, pA, 1m, _subeA, null, null, Op);
        Assert.Single(zmt.Holdings(_a, assetType: "equipment"));
        zmt.Issue(_b, "equipment", eB, pB, 1m, _subeB, null, null, Op);   // eskiden sessizce atlanırdı
        Assert.Single(zmt.Holdings(_b, assetType: "equipment"));
        zmt.Issue(_a, "equipment", eA, pA, 1m, _subeA, null, null, Op);   // retry
        var h = Assert.Single(zmt.Holdings(_a, assetType: "equipment"));
        Assert.Equal(1m, h.Quantity);
    }

    /// <summary>⭐ Açılış stoğu: firma kapsamlı (B'de hareket OLUŞUR); aynı-firma retry aynen.</summary>
    [Fact]
    public void FIN4_AcilisStogu_FirmaKapsamli_Idempotent()
    {
        var mats = new MaterialService(_f);
        var acilis = new OpeningStockService(_f);
        const string Op = "FIN-ACL-AYNI";

        var mA = mats.Create(_a, new NewMaterial("ACL-A", "Çimento"));
        var mB = mats.Create(_b, new NewMaterial("ACL-B", "Demir"));
        acilis.RecordOpening(_a, mA, 50m, Op);
        Assert.Equal(1, HareketSayisi("FIN-A"));
        acilis.RecordOpening(_b, mB, 30m, Op);   // eskiden sessizce atlanırdı — artık işler
        Assert.Equal(1, HareketSayisi("FIN-B"));
        acilis.RecordOpening(_a, mA, 50m, Op);   // retry → ikinci hareket YOK
        Assert.Equal(1, HareketSayisi("FIN-A"));
    }

    /// <summary>⭐ AYNI firmada aynı operation_id ile DOĞRUDAN ikinci satır denemesi indekste TAKILIR —
    /// firma-içi benzersizlik gevşemedi (yalnız firmalar-arası engel kalktı).</summary>
    [Fact]
    public void FIN5_AyniFirma_Duplicate_Indekste_Reddedilir()
    {
        void Satir(string co)
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO daily_activities(id,company_id,activity_type,activity_date,operation_id,created_at,updated_at,version,is_deleted)
VALUES(@id,@c,'movement',1,'FIN-DUP-OP',1,1,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", co);
            cmd.ExecuteNonQuery();
        }
        Satir("FIN-A");
        Satir("FIN-B");   // farklı firma → İZİNLİ (yeni sözleşme)
        Assert.ThrowsAny<Exception>(() => Satir("FIN-A"));   // aynı firma duplicate → UNIQUE reddeder
    }

    // ══════════════ MIGRATION082 KANITLARI ══════════════

    /// <summary>⭐ Migration082 mevcut veriye DOKUNMAZ (bit-bit) ve indeksleri (company_id, operation_id)
    /// yapar; başka indeks/tablo değişmez; runner tekrarında çift uygulanmaz (idempotent).</summary>
    [Fact]
    public void FIN6_Migration082_BitBit_Ve_Indeks_Dogru()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_fin_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 81)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO daily_activities(id,company_id,activity_type,activity_date,operation_id,created_at,updated_at,version,is_deleted)
    VALUES('DA1','C1','movement',11,'OP-1',11,11,1,0);
INSERT INTO fuel_depot_entries(id,company_id,liters,unit_price,currency_code,entry_date,operation_id,created_at,updated_at,version,is_deleted)
    VALUES('FD1','C1','100','40','TRY',12,'OP-2',12,12,1,0);";
                cmd.ExecuteNonQuery();
            }

            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "companies", "daily_activities", "fuel_depot_entries", "stock_movements" })
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT * FROM {t} ORDER BY 1;";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        for (int i = 0; i < r.FieldCount; i++)
                            sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                }
                return sb.ToString();
            }
            // 082 DIŞINDAKİ indeks envanteri de değişmemeli (yalnız 6 hedef indeksin sql'i değişir).
            string DigerIndeksler(SqliteConnectionFactory ff)
            {
                var hedefler = Migration082_OperationIdCompanyScope.Targets.Select(t => t.Index).ToHashSet();
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name, COALESCE(sql,'') FROM sqlite_master WHERE type='index' ORDER BY name;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!hedefler.Contains(r.GetString(0))) sb.Append(r.GetString(0)).Append('=').Append(r.GetString(1)).Append('\n');
                return sb.ToString();
            }

            var onceVeri = Foto(f);
            var onceDiger = DigerIndeksler(f);
            Assert.Equal(new[] { 82 }, new MigrationRunner(f, new IMigration[] { new Migration082_OperationIdCompanyScope() }).Run());
            Assert.Equal(onceVeri, Foto(f));         // ⭐ mevcut satırlar BİT-BİT aynı
            Assert.Equal(onceDiger, DigerIndeksler(f));   // ⭐ hedef dışı hiçbir indeks değişmedi

            // 6 hedef indeks: AYNI adla, TAM (company_id, operation_id) üzerinde ve UNIQUE.
            using (var conn = f.Create())
            {
                foreach (var (index, table) in Migration082_OperationIdCompanyScope.Targets)
                {
                    using var info = conn.CreateCommand();
                    info.CommandText = $"PRAGMA index_info({index});";
                    var cols = new List<string>();
                    using (var r = info.ExecuteReader())
                        while (r.Read()) cols.Add(r.GetString(2));
                    Assert.Equal(new[] { "company_id", "operation_id" }, cols);

                    using var uniq = conn.CreateCommand();
                    uniq.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{index}' AND tbl_name='{table}' AND sql LIKE 'CREATE UNIQUE INDEX%';";
                    Assert.Equal(1L, Convert.ToInt64(uniq.ExecuteScalar()));
                }
            }

            // Idempotent: runner yeniden koşunca 082 TEKRAR uygulanmaz (schema_migrations kilidi).
            Assert.Empty(new MigrationRunner(f).Run());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    // ══════════════ KARAR PAKETİ DİĞER KİLİTLERİ (STK-B2 · SNK-05) ══════════════

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

    /// <summary>Migration082 statik kilidi: gövde YALNIZ "DROP INDEX IF EXISTS" + "CREATE UNIQUE INDEX"
    /// içerir — ALTER/UPDATE/DELETE/INSERT/DROP TABLE yasak (yalnız indeks değişimi, veri dokunuşu yok).</summary>
    [Fact]
    public void FIN7_Migration082_Yalniz_Indeks_Degistirir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration082_OperationIdCompanyScope.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "INSERT ", "DROP TABLE", "DROP COLUMN" })
            Assert.DoesNotContain(yasak, govde);
        Assert.Contains("DROP INDEX IF EXISTS", govde);
        Assert.Contains("CREATE UNIQUE INDEX", govde);
        // Kapsam kilidi: 6 hedef, sync tablolarına dokunulmaz.
        Assert.Equal(6, Migration082_OperationIdCompanyScope.Targets.Count);
        Assert.DoesNotContain(Migration082_OperationIdCompanyScope.Targets, t => t.Table.StartsWith("sync_"));
    }
}
