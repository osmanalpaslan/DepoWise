using DepoWise.Application.Approvals;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Approvals;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 — Migration085 YÜKSELTME/ROLLBACK + ESKİ İSTEMCİ ═══
///
/// Kilitlenenler:
///  • 084 → 085 GERÇEK yükseltme provası; mevcut veri KORUNUR.
///  • Migration başarısız olursa şema <b>84'te kalır</b> (runner tek transaction).
///  • <b>ESKİ İSTEMCİ:</b> Migration085 uygulanmamış yerel veritabanında onay tabloları YOKTUR;
///    servisler bu durumu <c>TableExists</c> ile geçer → eski istemci ÇALIŞMAYA DEVAM EDER.
///  • <b>Ama sunucuda onay zorunluysa eski istemci onu BYPASS EDEMEZ</b>: mal kabul kapısı
///    sunucudaki servistedir; istek nereden gelirse gelsin aynı kapıdan geçer.
/// </summary>
public class OnayMigrationVeEskiIstemciTests
{
    /// <summary>OM01 — 084 → 085 yükseltmesi: üç tablo kurulur, mevcut veri korunur.</summary>
    [Fact]
    public void OM01_Yukseltme_84ten85e_Mevcut_Veri_Korunur()
    {
        var yol = Yol("dw_on84_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 84)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('UP','UP',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }
            Assert.Equal(84L, Sema(f));
            Assert.Equal(0L, TabloVar(f, "approval_instance"));

            var uygulanan = new MigrationRunner(f).Run();

            Assert.Contains(85, uygulanan);
            Assert.Equal(1L, TabloVar(f, "user_hierarchy"));
            Assert.Equal(1L, TabloVar(f, "approval_instance"));
            Assert.Equal(1L, TabloVar(f, "approval_step"));
            Assert.Equal(1L, TabloVar(f, "teams"));        // ALT FAZ 1 tabloları bozulmadı
            Assert.Equal(1L, Say(f, "SELECT COUNT(*) FROM companies WHERE id='UP';"));
        }
        finally { Temizle(yol); }
    }

    /// <summary>OM02 — ROLLBACK: 085 başarısız olursa şema 84'te kalır, hiçbir tablo oluşmaz.</summary>
    [Fact]
    public void OM02_Migration_Basarisiz_Olursa_Sema_84te_Kalir()
    {
        var yol = Yol("dw_onrb_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 84)).Run();
            Assert.Equal(84L, Sema(f));

            Assert.ThrowsAny<Exception>(() =>
                new MigrationRunner(f, new IMigration[] { new BozukMigration85() }).Run());

            Assert.Equal(84L, Sema(f));
            foreach (var t in new[] { "user_hierarchy", "approval_instance", "approval_step" })
                Assert.Equal(0L, TabloVar(f, t));
        }
        finally { Temizle(yol); }
    }

    /// <summary>OM03 — <b>ESKİ İSTEMCİ (şema 84):</b> onay tabloları yoktur; talep oluşturma ve
    /// tek-adımlı onay ESKİSİ GİBİ çalışır, mal kabul kapısı da hata vermez. Motor bağlı olsa bile
    /// <c>TableExists</c> kapısı sayesinde eski şemaya dokunulmaz.</summary>
    [Fact]
    public void OM03_Eski_Sema_84_Istemci_Calismaya_Devam_Eder()
    {
        var yol = Yol("dw_onold_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 84)).Run();

            using (var conn = f.Create())
            {
                Assert.False(DbIntrospect.TableExists(conn, null, "approval_instance"));
                Assert.True(DbIntrospect.TableExists(conn, null, "teams"));          // ALT FAZ 1 var
                Assert.True(DbIntrospect.TableExists(conn, null, "material_requests"));
            }

            var appr = new ApprovalService(f);
            var requests = new RequestService(f, new StockService(f)) { Approvals = appr };
            appr.Register(ApprovalEntityTypes.MaterialRequest,
                (conn, tx, s, _, id, ok, reason, now) => requests.ApplyChainDecision(conn, tx, s, id, ok, reason, now));

            var s = Firma(f, "OLD", "admin_old");
            var talep = requests.Create(s, new NewRequest(
                new[] { new RequestItemInput(Malzeme(f, "OLD"), 1m) }, SubmitImmediately: true)).Id;

            requests.Approve(s, talep);                                              // eski akış bozulmadı
            Assert.Equal("approved", Say(f, "SELECT status FROM material_requests WHERE id='" + talep + "';", metin: true));
        }
        finally { Temizle(yol); }
    }

    /// <summary>OM04 — <b>ESKİ İSTEMCİ ONAYI BYPASS EDEMEZ.</b> Sunucu şeması 085'tir ve siparişin
    /// onay süreci beklemededir; istek nereden gelirse gelsin mal kabul AYNI servis kapısından geçer
    /// ve reddedilir. (Eski istemcinin yerelinde tablo olmaması kapıyı zayıflatmaz — kapı sunucudadır.)</summary>
    [Fact]
    public void OM04_Eski_Istemci_Onaysiz_Mal_Kabulu_Bypass_Edemez()
    {
        var yol = Yol("dw_onbyp_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f).Run();                                            // SUNUCU şeması: 085

            var appr = new ApprovalService(f);
            var hier = new UserHierarchyService(f);
            var po = new PurchaseOrderService(f) { Approvals = appr };
            appr.Register(ApprovalEntityTypes.PurchaseOrder, (_, _, _, _, _, _, _, _) => { });

            var s = Firma(f, "BYP", "admin_byp");
            var ust = Kullanici(f, "BYP", "ust");
            hier.SetManager(s, s.UserId, ust);

            var mat = Malzeme(f, "BYP");
            var sube = Sube(f, "BYP");
            var orderId = po.Create(s, new NewPurchaseOrder(
                OrderNo: "BYP-1", BranchId: sube, Lines: new[] { new NewPurchaseOrderLine(mat, 3m) }));
            var lineId = Say(f, $"SELECT id FROM purchase_order_lines WHERE order_id='{orderId}';", metin: true);

            var hata = Assert.Throws<ArgumentException>(() =>
                po.Receive(s, orderId, new[] { new ReceiveLine(lineId, 1m) }, "byp-op"));
            Assert.Contains("onay", hata.Message.ToLowerInvariant());

            // Hiçbir stok hareketi oluşmadı → bypass gerçekten engellendi.
            Assert.Equal(0L, Say(f, "SELECT COUNT(*) FROM stock_movements WHERE company_id='BYP';"));
        }
        finally { Temizle(yol); }
    }

    /// <summary>OM05 — <b>ÇEVRİMDIŞI ONAY YAZILAMAZ:</b> onay/ret <c>sync_outbox</c>'a HİÇBİR kayıt
    /// düşürmez. Onay sunucu otoritesinde yürür; kuyruğa alınıp sonradan gönderilen bir onay YOKTUR.</summary>
    [Fact]
    public void OM05_Onay_SyncOutboxa_Yazilmaz()
    {
        var yol = Yol("dw_onout_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f).Run();

            var appr = new ApprovalService(f);
            var hier = new UserHierarchyService(f);
            var requests = new RequestService(f, new StockService(f)) { Approvals = appr };
            appr.Register(ApprovalEntityTypes.MaterialRequest,
                (conn, tx, s2, _, id, ok, reason, now) => requests.ApplyChainDecision(conn, tx, s2, id, ok, reason, now));

            var s = Firma(f, "OUT", "admin_out");
            var ust = Kullanici(f, "OUT", "ust");
            hier.SetManager(s, s.UserId, ust);

            var talep = requests.Create(s, new NewRequest(
                new[] { new RequestItemInput(Malzeme(f, "OUT"), 1m) }, SubmitImmediately: true)).Id;

            var oncekiOutbox = Say(f, "SELECT COUNT(*) FROM sync_outbox;");

            using var conn2 = f.Create();
            var inst = ApprovalService.OpenInstanceId(conn2, null, "OUT", ApprovalEntityTypes.MaterialRequest, talep)!;
            var stepId = appr.Steps(s, inst).Single().Id;
            var ustOturum = new SessionContext(ust, "OUT", new[] { RoleKeys.Staff },
                new PermissionSet(new[] { new ModulePermission("request_approval", true, true, true, true) }));
            appr.Approve(ustOturum, stepId);

            // Onay eylemi hiçbir senkron kuydu üretmedi (approval tabloları senkron dışıdır).
            Assert.Equal(oncekiOutbox, Say(f, "SELECT COUNT(*) FROM sync_outbox;"));
            Assert.Equal(0L, Say(f,
                "SELECT COUNT(*) FROM sync_outbox WHERE entity_type IN ('approval_instance','approval_step','user_hierarchy');"));
        }
        finally { Temizle(yol); }
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private sealed class BozukMigration85 : IMigration
    {
        public int Version => 85;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE user_hierarchy(id TEXT); CREATE TABLE approval_instance(id TEXT); " +
                              "CREATE TABLE approval_step(id TEXT); CREATE INDEX x ON olmayan_tablo(id);";
            cmd.ExecuteNonQuery();
        }
    }

    private static string Yol(string on) => Path.Combine(Path.GetTempPath(), on + Guid.NewGuid().ToString("N") + ".db");

    private static SessionContext Firma(SqliteConnectionFactory f, string co, string user)
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

    private static string Kullanici(SqliteConnectionFactory f, string co, string username)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@u,'x',1,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static string Malzeme(SqliteConnectionFactory f, string co)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES(@i,@c,@k,@k,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@k", "M" + id[..6]);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static string Sube(SqliteConnectionFactory f, string co)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,'Merkez','branch',1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static long Say(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string Say(SqliteConnectionFactory f, string sql, bool metin)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (string)cmd.ExecuteScalar()!;
    }

    private static long Sema(SqliteConnectionFactory f) => Say(f, "SELECT MAX(version) FROM schema_migrations;");

    private static long TabloVar(SqliteConnectionFactory f, string tablo)
        => Say(f, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tablo}';");

    private static void Temizle(string yol)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(yol); } catch { }
    }
}
