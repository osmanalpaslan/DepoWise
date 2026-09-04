using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 7b — Migration086 YÜKSELTME/ROLLBACK + İŞ EMRİ + ESKİ İSTEMCİ (PK-F9, ADR-191) ═══
///
/// Kilitlenenler: 085 → 086 gerçek yükseltme (mevcut ARAÇ BAKIM VERİSİ korunur) · rollback'te şema
/// 85'te kalır · eski istemci (şema 85) çalışmaya devam eder · iş emri hem araç hem ekipman bakımını
/// bağlar ve maliyete katar · araç iş emri davranışı BOZULMAZ.
/// </summary>
public class EkipmanMigrationVeIsEmriTests
{
    // ══════════════════════ MIGRATION ══════════════════════

    /// <summary>EM01 — 085 → 086: dört tablo kurulur, MEVCUT ARAÇ BAKIM KAYDI birebir korunur.</summary>
    [Fact]
    public void EM01_Yukseltme_85ten86ya_Arac_Verisi_Korunur()
    {
        var yol = Yol("dw_eq85_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 85)).Run();
            Assert.Equal(85L, Sema(f));

            // Şema-85 üzerinde GERÇEK araç bakım verisi oluştur.
            //
            // ⚠️ 2026-09-04 (MUH-01b) — KAYIT ARTIK SERVİSLE DEĞİL, DOĞRUDAN SQL İLE atılıyor.
            // Neden: bu test "şema 85" kuruyor ama BUGÜNKÜ servis kodunu çağırıyordu. Migration089
            // `vehicle_maintenances.invoice_no` ekleyince o bileşim derlenmez oldu ("no such column").
            // Bileşim ZATEN GERÇEKTE OLUŞAMAZ: istemci kendi migration kataloğunu açılışta uygular,
            // yani "89'u bilen kod + şema 85" diye bir istemci yoktur. Testin İDDİASI ise hâlâ
            // geçerli ve değerli: *migration mevcut araç bakım verisini korur*. O iddia, kaydı
            // dönemin şemasıyla (SQL) atıp migration'ı çalıştırarak DAHA DOĞRU ölçülür.
            // Assertion'lar zayıflatılmadı; aksine altta "yükseltmeden sonra servis okuyabiliyor mu"
            // kontrolü de eklendi.
            var (s, arac, def) = AracBakimKur(f, "UP");
            var bakimId = Guid.NewGuid().ToString("N");
            Calistir(f, "INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id," +
                        "operation_id,is_cancelled,created_at,updated_at,version,is_deleted) " +
                        $"VALUES('{bakimId}','UP','{arac}','{def}','op-85',0,1,1,1,0);");
            Assert.Equal(1L, Say(f, $"SELECT COUNT(*) FROM vehicle_maintenances WHERE id='{bakimId}';"));

            var uygulanan = new MigrationRunner(f).Run();

            Assert.Contains(86, uygulanan);
            Assert.Equal(4L, Say(f,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN " +
                "('equipment_maintenances','equipment_maintenance_materials','equipment_inspections','maintenance_definition_equipment');"));

            // ⭐ Mevcut araç kaydı BİREBİR duruyor ve YÜKSELTMEDEN SONRA servisle okunabiliyor
            // (asıl kullanıcı vaadi bu: babanın eski veritabanı yükseltilince kayıtları kaybolmaz).
            Assert.Equal(1L, Say(f, $"SELECT COUNT(*) FROM vehicle_maintenances WHERE id='{bakimId}';"));
            var svc = new MaintenanceService(f);
            Assert.Single(svc.ListMaintenances(s));
            // Migration089 sonrası yeni alan NULL'dur — eski kayıtlara backfill YAPILMADI.
            Assert.Equal(1L, Say(f, $"SELECT COUNT(*) FROM vehicle_maintenances WHERE id='{bakimId}' AND invoice_no IS NULL;"));
            // Yeni tablolar BOŞ (backfill yok).
            Assert.Equal(0L, Say(f, "SELECT COUNT(*) FROM equipment_maintenances;"));
        }
        finally { Temizle(yol); }
    }

    /// <summary>EM02 — ROLLBACK: bozuk 086'da şema 85'te kalır ve hiçbir tablo oluşmaz.</summary>
    [Fact]
    public void EM02_Migration_Basarisiz_Olursa_Sema_85te_Kalir()
    {
        var yol = Yol("dw_eqrb_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 85)).Run();
            Assert.Equal(85L, Sema(f));

            Assert.ThrowsAny<Exception>(() =>
                new MigrationRunner(f, new IMigration[] { new BozukMigration86() }).Run());

            Assert.Equal(85L, Sema(f));
            Assert.Equal(0L, Say(f,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN " +
                "('equipment_maintenances','equipment_inspections','maintenance_definition_equipment');"));
        }
        finally { Temizle(yol); }
    }

    /// <summary>EM03 — <b>ESKİ VERİTABANI YÜKSELTİLİNCE ARAÇ BAKIMI SÜRER.</b>
    ///
    /// ⚠️ 2026-09-04 (MUH-01b) — testin KURULUŞU değişti, İDDİASI değişmedi. Eskiden şema 85'te
    /// KALIP bugünkü servisi çağırıyordu; Migration089 sonrası bu bileşim imkânsız hâle geldi
    /// (istemci kendi kataloğunu açılışta uygular → "89'u bilen kod + şema 85" diye bir istemci yok).
    /// Ölçülen gerçek vaat şudur: <b>ekipman hattı eklenmeden önce kurulmuş bir veritabanı
    /// yükseltildiğinde araç bakımı eskisi gibi çalışır</b> — kaydetme ve iptal dâhil.
    /// (ARA İŞ 5'teki OM03 yaklaşımının aynısı.)</summary>
    [Fact]
    public void EM03_Eski_Veritabani_Yukseltilince_Arac_Bakimi_Surer()
    {
        var yol = Yol("dw_eqold_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 85)).Run();

            // ESKİ HÂL: ekipman tabloları henüz yok.
            using (var conn = f.Create())
            {
                Assert.False(DbIntrospect.TableExists(conn, null, "equipment_maintenances"));
                Assert.True(DbIntrospect.TableExists(conn, null, "vehicle_maintenances"));
                Assert.True(DbIntrospect.TableExists(conn, null, "equipment"));   // ekipman tablosu 075'ten beri var
            }

            var (s, arac, def) = AracBakimKur(f, "OLD");

            // Eski şemayla GERÇEK bir araç bakım kaydı (dönemin kolonlarıyla).
            var eskiKayit = Guid.NewGuid().ToString("N");
            Calistir(f, "INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id," +
                        "operation_id,is_cancelled,created_at,updated_at,version,is_deleted) " +
                        $"VALUES('{eskiKayit}','OLD','{arac}','{def}','op-eski',0,1,1,1,0);");

            // YÜKSELTME — babanın veritabanının güncelleme sonrası yaşadığı şey.
            new MigrationRunner(f).Run();

            var svc = new MaintenanceService(f);
            Assert.Single(svc.ListMaintenances(s));   // eski kayıt duruyor ve okunabiliyor

            // Araç bakımı yükseltmeden SONRA da eskisi gibi çalışır: kaydet + iptal.
            var id = svc.Save(s, new NewMaintenance(arac, def), "op-old");
            Assert.Equal(2, svc.ListMaintenances(s).Count);
            svc.Cancel(s, id, "iptal");
            Assert.Equal(1L, Say(f, $"SELECT is_cancelled FROM vehicle_maintenances WHERE id='{id}';"));
        }
        finally { Temizle(yol); }
    }

    // ══════════════════════ İŞ EMRİ ══════════════════════

    /// <summary>EM04 — İş emri HEM araç HEM ekipman bakımını bağlar; maliyet özetinde ikisi de
    /// "Bakım Malzemesi" olarak toplanır. Araç davranışı DEĞİŞMEDİ.</summary>
    [Fact]
    public void EM04_Is_Emri_Iki_Bakim_Turunu_De_Baglar()
    {
        var yol = Yol("dw_eqwo_");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f).Run();
            var (s, arac, def) = AracBakimKur(f, "WO");
            var sube = Sube(f, "WO");
            var malzeme = Malzeme(f, "WO");
            var ekipman = Ekipman(f, "WO");

            var aracBakim = new MaintenanceService(f).Save(s, new NewMaintenance(arac, def,
                Materials: new[] { new MaintenanceMaterialLine(malzeme, 2m) }, StockLocationId: sube), "op-wo-a");
            var eqmBakim = new EquipmentMaintenanceService(f).Save(s, new NewEquipmentMaintenance(ekipman, def,
                Materials: new[] { new MaintenanceMaterialLine(malzeme, 3m) }, StockLocationId: sube), "op-wo-e");

            var wo = new WorkOrderService(f);
            var emirId = wo.Create(s, new NewWorkOrder("IE-1", "Test iş emri"));

            wo.LinkExisting(s, emirId, "vehicle_maintenance", aracBakim);
            wo.LinkExisting(s, emirId, "equipment_maintenance", eqmBakim);

            var baglar = wo.Links(s, emirId);
            Assert.Equal(2, baglar.Count);
            Assert.Contains(baglar, b => b.EntityType == "equipment_maintenance" && b.EntityTypeDisplay == "Ekipman Bakımı");
            Assert.Contains(baglar, b => b.EntityType == "vehicle_maintenance" && b.EntityTypeDisplay == "Bakım");

            // Maliyet: 2×10 + 3×10 = 50, tek "Bakım Malzemesi" kategorisinde.
            var maliyet = wo.CostSummary(s, emirId).Where(x => x.Category == "Bakım Malzemesi").ToList();
            Assert.Equal(50m, maliyet.Sum(x => x.Amount));

            // Kapsam dışı varlık türü hâlâ reddedilir.
            Assert.Throws<ArgumentException>(() => wo.LinkExisting(s, emirId, "work_order", emirId));
        }
        finally { Temizle(yol); }
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private sealed class BozukMigration86 : IMigration
    {
        public int Version => 86;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE equipment_maintenances(id TEXT); CREATE INDEX x ON olmayan_tablo(id);";
            cmd.ExecuteNonQuery();
        }
    }

    private static (SessionContext, string Arac, string Def) AracBakimKur(SqliteConnectionFactory f, string co)
    {
        Calistir(f, $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0);");
        var uid = new UserService(f).EnsureInitialAdmin(co, "admin_" + co.ToLowerInvariant(), "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var arac = Guid.NewGuid().ToString("N");
        Calistir(f, "INSERT INTO vehicles(id,company_id,internal_code,status,created_at,updated_at,version,is_deleted) " +
                    $"VALUES('{arac}','{co}','ARC','active',1,1,1,0);");
        var def = new MaintenanceDefinitionService(f).Create(s,
            new NewMaintenanceDefinition("Periyodik", 30m, "day", null, null));
        return (s, arac, def);
    }

    private static string Sube(SqliteConnectionFactory f, string co)
    {
        var id = Guid.NewGuid().ToString("N");
        Calistir(f, "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                    $"VALUES('{id}','{co}','Merkez','branch',1,1,1,0);");
        return id;
    }

    private static string Malzeme(SqliteConnectionFactory f, string co)
    {
        var id = Guid.NewGuid().ToString("N");
        Calistir(f, "INSERT INTO materials(id,company_id,code,name,unit_price,created_at,updated_at,version,is_deleted) " +
                    $"VALUES('{id}','{co}','M{id[..6]}','Malzeme','10',1,1,1,0);");
        return id;
    }

    private static string Ekipman(SqliteConnectionFactory f, string co)
    {
        var id = Guid.NewGuid().ToString("N");
        Calistir(f, "INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
                    $"VALUES('{id}','{co}','EKP','Ekipman','active',1,1,1,0);");
        return id;
    }

    private static void Calistir(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Say(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long Sema(SqliteConnectionFactory f) => Say(f, "SELECT MAX(version) FROM schema_migrations;");

    private static string Yol(string on) => Path.Combine(Path.GetTempPath(), on + Guid.NewGuid().ToString("N") + ".db");

    private static void Temizle(string yol)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(yol); } catch { }
    }
}
