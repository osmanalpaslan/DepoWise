using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ STN-01 (ADR-169, 2026-08-28) — SATIN ALMA TESTLERİ ═══
///
/// Kilitler: sipariş+satırlar · talep bağı OPSİYONEL · MAL KABUL mevcut stok girişiyle (STOK ARTAR) ·
/// kısmi kabul + otomatik kapanış · İDEMPOTENT kabul (İKİNCİ STOK GİRİŞİ YOK) · kalan aşımı engeli ·
/// yetki (purchasing + stok kapısı ayrı) · tenant · şube kapsamı · maliyet merkezi bağı stok belgesine ·
/// senkron sıra/kapı/uçtan uca · migration canlı-veri kanıtı.
/// </summary>
public class SatinAlmaTests : IDisposable
{
    private const string Co = "STN";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly PurchaseOrderService _svc;
    private readonly StockService _stock;
    private readonly string _uid, _depo, _depo2, _mat, _mat2, _tedarikci;
    private readonly SessionContext _admin;
    private static readonly long Gun = 1_700_000_000_000;

    public SatinAlmaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_stn_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _depo = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _depo2 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye B", "site"));
        var mats = new MaterialService(_f);
        _mat = mats.Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
        _mat2 = mats.Create(_admin, new NewMaterial("M-2", "Demir", UnitPrice: 25m));
        _tedarikci = new LookupService(_f).AddSupplier(_admin, "ABC Yapı Malzemeleri");
        _stock = new StockService(_f);
        _svc = new PurchaseOrderService(_f);
    }

    private static void Firma(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private decimal Stok(string materialId, string branchId)
        => _stock.GetBalancesByLocation(_admin, materialId).TryGetValue(branchId, out var q) ? q : 0m;

    private string Siparis(decimal qty = 10m, string? branch = null, string? cc = null, string no = "PO-1")
        => _svc.Create(_admin, new NewPurchaseOrder(no, _tedarikci, null, branch ?? _depo, cc, Gun, null,
            new[] { new NewPurchaseOrderLine(_mat, qty, 10m, "TRY") }));

    // ══════════════ TEMEL ══════════════

    [Fact]
    public void STN1_Siparis_Olusur_Listelenir_Toplam_Dogru()
    {
        _svc.Create(_admin, new NewPurchaseOrder("PO-1", _tedarikci, null, _depo, null, Gun, "acil",
            new[] { new NewPurchaseOrderLine(_mat, 10m, 10m, "TRY"), new NewPurchaseOrderLine(_mat2, 4m, 25m, "TRY") }));
        var o = Assert.Single(_svc.List(_admin));
        Assert.Equal("Açık", o.StatusDisplay);
        Assert.Equal("ABC Yapı Malzemeleri", o.SupplierDisplay);
        Assert.Equal(200m, o.TotalAmount);   // 10×10 + 4×25 — C# decimal
        Assert.Equal(2, _svc.Lines(_admin, o.Id).Count);

        // Sipariş no firma içinde benzersiz (anlaşılır hata):
        Assert.Throws<ArgumentException>(() => Siparis(no: "PO-1"));
        // Satırsız sipariş açılamaz:
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewPurchaseOrder("PO-2")));
    }

    // ══════════════ ⭐ MAL KABUL → STOK ══════════════

    /// <summary>Kabul MEVCUT stok girişini kullanır: stok ARTAR, satır received ilerler, tam kabulde kapanır.</summary>
    [Fact]
    public void STN2_Mal_Kabul_Stok_Girisi_Ve_Otomatik_Kapanis()
    {
        var id = Siparis(10m);
        var line = _svc.Lines(_admin, id).Single();
        Assert.Equal(0m, Stok(_mat, _depo));

        // KISMİ kabul (6/10):
        _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 6m) }, "op-k1", Gun);
        Assert.Equal(6m, Stok(_mat, _depo));                              // stok defterinden geldi
        var l2 = _svc.Lines(_admin, id).Single();
        Assert.Equal(6m, l2.ReceivedQty);
        Assert.Equal("open", _svc.List(_admin).Single().Status);          // henüz açık

        // Kalan kabul (4/10) → otomatik kapanış:
        _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 4m) }, "op-k2", Gun);
        Assert.Equal(10m, Stok(_mat, _depo));
        Assert.Equal("Tamamlandı", _svc.List(_admin).Single().StatusDisplay);
    }

    /// <summary>⭐⭐ TEST 1 — İDEMPOTENT: aynı kabul işlemi iki kez → İKİNCİ stok girişi YOK,
    /// received bir kez ilerler (retry/senkron tekrarına dayanıklı).</summary>
    [Fact]
    public void STN3_Kabul_Idempotent()
    {
        var id = Siparis(10m);
        var line = _svc.Lines(_admin, id).Single();
        _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 5m) }, "op-r1", Gun);
        _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 5m) }, "op-r1", Gun);   // AYNI operationId
        Assert.Equal(5m, Stok(_mat, _depo));                                  // BİR kez girdi
        Assert.Equal(5m, _svc.Lines(_admin, id).Single().ReceivedQty);        // BİR kez ilerledi
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'po:op-r1%';";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void STN4_Kalan_Asimi_Ve_Iptal_Sonrasi_Kabul_Engellenir()
    {
        var id = Siparis(10m);
        var line = _svc.Lines(_admin, id).Single();
        Assert.Throws<ArgumentException>(() =>
            _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 11m) }, "op-a1", Gun));
        Assert.Equal(0m, Stok(_mat, _depo));   // hiçbir şey işlenmedi (transaction geri alındı)

        _svc.Cancel(_admin, id);
        Assert.Equal("İptal", _svc.List(_admin).Single().StatusDisplay);
        Assert.Throws<ArgumentException>(() =>
            _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 1m) }, "op-a2", Gun));
        Assert.Throws<ArgumentException>(() => _svc.UpdateMeta(_admin, id, new NewPurchaseOrder("PO-X")));
    }

    // ══════════════ ⭐ YETKİ (TEST 2) ══════════════

    /// <summary>purchasing yetkisiz hiçbir şey yapamaz; purchasing VAR ama STOK yetkisi YOKSA mal kabul
    /// stok kapısına takılır (satın alma stok yan kapısı DEĞİL).</summary>
    [Fact]
    public void STN5_Yetki_Kapilari()
    {
        var id = Siparis(10m);
        var line = _svc.Lines(_admin, id).Single();

        var yetkisiz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewPurchaseOrder("X",
            Lines: new[] { new NewPurchaseOrderLine(_mat, 1m) })));

        var stoksuz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("purchasing", true, true, true, true) }));
        Assert.Throws<ForbiddenException>(() =>
            _svc.Receive(stoksuz, id, new[] { new ReceiveLine(line.Id, 1m) }, "op-y1", Gun));
        Assert.Equal(0m, Stok(_mat, _depo));   // stok oynamadı
    }

    /// <summary>⭐ TEST 3 — TENANT: başka firma sipariş göremez/yazamaz; bu firmanın kaynaklarına bağ kuramaz.</summary>
    [Fact]
    public void STN6_Firma_Izolasyonu()
    {
        var id = Siparis(10m);
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_svc.List(yabanci));
        Assert.Throws<ArgumentException>(() => _svc.Lines(yabanci, id));
        Assert.Throws<ArgumentException>(() => _svc.Cancel(yabanci, id));
        Assert.Throws<ArgumentException>(() => _svc.Create(yabanci, new NewPurchaseOrder("X", _tedarikci,
            Lines: new[] { new NewPurchaseOrderLine(_mat, 1m) })));   // başka firmanın tedarikçisi
    }

    /// <summary>⭐ TEST 4 — ŞUBE KAPSAMI: kapsam dışı teslim deposunun siparişi görünmez/işlenemez;
    /// kapsam dışına sipariş açılamaz.</summary>
    [Fact]
    public void STN7_Sube_Kapsami()
    {
        var s1 = Siparis(5m, _depo, no: "PO-A");
        var s2 = Siparis(5m, _depo2, no: "PO-B");

        var dar = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("purchasing", true, true, true, true),
                new ModulePermission("stock", true, true, true, false),
            })) { ScopeBranchIds = new[] { _depo } };

        var gorulen = _svc.List(dar).Select(x => x.Id).ToHashSet();
        Assert.Contains(s1, gorulen);
        Assert.DoesNotContain(s2, gorulen);
        Assert.Throws<ForbiddenException>(() => _svc.Lines(dar, s2));
        Assert.Throws<ForbiddenException>(() => _svc.Cancel(dar, s2));
        Assert.Throws<ForbiddenException>(() => _svc.Create(dar, new NewPurchaseOrder("PO-S", null, null, _depo2,
            Lines: new[] { new NewPurchaseOrderLine(_mat, 1m) })));
    }

    // ══════════════ MALİYET MERKEZİ (D) BAĞI ══════════════

    /// <summary>Siparişte merkez seçiliyse KABULDE oluşan stok belgesi D'nin dış-bağıyla merkeze bağlanır;
    /// merkez özeti alım maliyetini "Malzeme Girişi" olarak görür (çift sayım yok — kaynak tek: stok belgesi).</summary>
    [Fact]
    public void STN8_Maliyet_Merkezi_Bagi()
    {
        var ccSvc = new CostCenterService(_f);
        var cc = ccSvc.Create(_admin, new NewCostCenter("Asfalt İşi"));
        var id = Siparis(10m, _depo, cc, "PO-CC");
        var line = _svc.Lines(_admin, id).Single();
        var docId = _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 10m) }, "op-cc1", Gun);

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM cost_center_links WHERE entity_type='stock_document' AND entity_id=@d AND cost_center_id=@cc AND is_deleted=0;";
            cmd.AddWithValue("@d", docId);
            cmd.AddWithValue("@cc", cc);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        var ozet = ccSvc.Summary(_admin, Gun - 1000, Gun + 1000);
        Assert.Equal(100m, ozet.Single(x => x.Category == "Malzeme Girişi").Amount);   // 10×10
    }

    /// <summary>⭐ TEST 6 — REGRESYON: satın almasız mevcut stok akışı AYNEN; siparişler ona karışmaz.</summary>
    [Fact]
    public void STN9_Mevcut_Stok_Akisi_Degismedi()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 50m) }, "op-eski", branchId: _depo, docDate: Gun);
        Assert.Equal(50m, Stok(_mat, _depo));
        Siparis(10m, no: "PO-R");             // sipariş AÇILDI ama kabul yok → stok DEĞİŞMEZ
        Assert.Equal(50m, Stok(_mat, _depo));
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements;";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // yalnız eski giriş
    }

    // ══════════════ SENKRON ══════════════

    [Fact]
    public void STN10_Senkron_Listesi_Sira_Ve_Kapisi()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.Contains("purchase_orders", t);
        Assert.Contains("purchase_order_lines", t);
        Assert.True(t.IndexOf("purchase_orders") < t.IndexOf("purchase_order_lines"), "başlık satırdan ÖNCE (FK).");
        Assert.True(t.IndexOf("suppliers") < t.IndexOf("purchase_orders"));
        Assert.True(t.IndexOf("materials") < t.IndexOf("purchase_order_lines"));
        Assert.True(t.IndexOf("material_requests") < t.IndexOf("purchase_orders"));
        Assert.True(t.IndexOf("cost_centers") < t.IndexOf("purchase_orders"));
        Assert.Equal(PurchaseOrderService.Module, BusinessSyncService.ModuleOf("purchase_orders"));
        Assert.Equal(PurchaseOrderService.Module, BusinessSyncService.ModuleOf("purchase_order_lines"));
    }

    /// <summary>⭐ TEST 7 — UÇTAN UCA: sipariş + satırlar + kabul stok hareketi AYNI pakette taşınır;
    /// paket ikinci kez uygulanınca kopya oluşmaz.</summary>
    [Fact]
    public void STN11_Senkron_Uctan_Uca_Idempotent()
    {
        var dstPath = Path.Combine(Path.GetTempPath(), "dw_stn_dst_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var dst = new SqliteConnectionFactory(dstPath);
            new MigrationRunner(dst).Run();
            Firma(dst, Co);
            using (var conn = dst.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                                  "VALUES(@b,@c,'Merkez','branch',1,1,1,0);";
                cmd.AddWithValue("@b", _depo);
                cmd.AddWithValue("@c", Co);
                cmd.ExecuteNonQuery();
            }
            var id = Siparis(10m, no: "PO-SNK");
            var line = _svc.Lines(_admin, id).Single();
            _svc.Receive(_admin, id, new[] { new ReceiveLine(line.Id, 10m) }, "op-snk", Gun);

            var clock = new SystemClock();
            using var snap = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co));
            var dstSvc = new BusinessSyncService(dst, clock);
            var r1 = dstSvc.ApplyPull(Co, snap.RootElement);
            Assert.Empty(r1.Errors);

            long Say(string sql)
            {
                using var conn = dst.Create();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            Assert.Equal(1, Say("SELECT COUNT(*) FROM purchase_orders WHERE order_no='PO-SNK' AND status='closed'"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM purchase_order_lines WHERE received_qty='10'"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'po:op-snk%'"));

            dstSvc.ApplyPull(Co, snap.RootElement);
            Assert.Equal(1, Say("SELECT COUNT(*) FROM purchase_orders WHERE order_no='PO-SNK'"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'po:op-snk%'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dstPath); } catch { }
        }
    }

    // ══════════════ ⭐⭐ MIGRATION078 KANITI (TEST 5) ══════════════

    [Fact]
    public void STN12_Migration078_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_stn_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 77)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('S1','C1','Tedarikçi',11,11,1,0);
INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,currency_code,created_at,updated_at,version,is_deleted)
    VALUES('M1','C1','K-1','Çimento','0','10','TRY',12,12,1,0);
INSERT INTO material_requests(id,company_id,doc_no,request_date,status,created_at,updated_at,version)
    VALUES('R1','C1','TLP-1',13,'approved',13,13,1);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "suppliers", "materials", "material_requests", "companies" })
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
            var once = Foto(f);
            Assert.Equal(new[] { 78 }, new MigrationRunner(f, new IMigration[] { new Migration078_PurchaseOrders() }).Run());
            Assert.Equal(once, Foto(f));
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM purchase_orders) + (SELECT COUNT(*) FROM purchase_order_lines);";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    [Fact]
    public void STN13_Migration078_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration078_PurchaseOrders.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }
}
