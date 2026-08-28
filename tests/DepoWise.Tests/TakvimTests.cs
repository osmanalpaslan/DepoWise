using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Calendars;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TKV-01 (ADR-171, 2026-08-28) — TAKVİM TESTLERİ ═══
///
/// Kilitler: PK-H1 hibrit (el ile CRUD + beş türetilmiş kaynak) · PK-H4 gün bazlı/çok günlü ·
/// PK-H5 iş emri bağı YALNIZ gezinme (kaynak kayıt bit-bit değişmez; takvimde durum değiştirme
/// YOLU YOKTUR) · yan kapı yok (kaynak modül yetkisi olmadan o kaynak görünmez) · BranchAccess ·
/// tenant · soft delete + Çöp Kutusu · senkron sıra/kapı/uçtan uca idempotent · Migration080 kanıtı.
/// </summary>
public class TakvimTests : IDisposable
{
    private const string Co = "TKV";
    private readonly string _dbPath, _storeRoot;
    private readonly SqliteConnectionFactory _f;
    private readonly CalendarService _svc;
    private readonly WorkOrderService _wo;
    private readonly string _uid, _sube1, _sube2, _ali, _mat;
    private readonly SessionContext _admin;
    private static readonly long Gun = 1_700_000_000_000;
    private const long GunMs = 86_400_000;
    private static readonly long From = Gun - 30 * GunMs;
    private static readonly long To = Gun + 60 * GunMs;

    public TakvimTests()
    {
        var n = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_tkv_" + n + ".db");
        _storeRoot = Path.Combine(Path.GetTempPath(), "dw_tkv_store_" + n);
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _ali = new PersonnelService(_f, new ScopeResolver(_f)).Create(_admin, new NewPersonnel("Ali Usta", null, null, null));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
        _wo = new WorkOrderService(_f);
        _svc = new CalendarService(_f, new DocumentService(_f, new LocalFileStorageProvider(_storeRoot)));
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
        try { Directory.Delete(_storeRoot, recursive: true); } catch { }
    }

    /// <summary>Personel oturumu: istenen modül izinleri (admin bypass YOK).</summary>
    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    private IReadOnlyList<CalendarItem> Items(SessionContext? s = null, string? source = null, string? branch = null, string? search = null)
        => _svc.Items(s ?? _admin, From, To, source, branch, search);

    // ══════════════ EL İLE KAYIT (PK-H1/H4) ══════════════

    /// <summary>CRUD + doğrulama + çok günlü pencere kesişimi (PK-H4: gün bazlı, saat yok).</summary>
    [Fact]
    public void TKV1_ElIle_Kayit_CRUD_Ve_Pencere()
    {
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewCalendarEvent("", Gun)));            // başlık zorunlu
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewCalendarEvent("X", Gun, Gun - 1)));  // bitiş < başlangıç

        var id = _svc.Create(_admin, new NewCalendarEvent("Toplantı", Gun, Gun + 2 * GunMs,
            BranchId: _sube1, ResponsiblePersonnelId: _ali, Note: "saha turu"));
        var e = Assert.Single(Items(source: "event"));
        Assert.True(e.IsEvent);
        Assert.Equal("Toplantı", e.Title);
        Assert.Equal("Şantiye A", e.BranchName);
        Assert.Equal("Ali Usta", e.ResponsibleName);

        // Çok günlü kesişim: yalnız kaydın ORTA gününü kapsayan pencere de kaydı görür.
        Assert.Single(_svc.Items(_admin, Gun + GunMs, Gun + GunMs));
        // Pencere dışı: görünmez.
        Assert.Empty(_svc.Items(_admin, Gun + 10 * GunMs, Gun + 20 * GunMs));

        _svc.Update(_admin, id, new NewCalendarEvent("Toplantı 2", Gun + GunMs), expectedVersion: e.Version);
        var e2 = Assert.Single(Items(source: "event"));
        Assert.Equal("Toplantı 2", e2.Title);
        Assert.Null(e2.BranchId);   // güncelleme alanları tam yazar
        Assert.Throws<ConcurrencyException>(() =>
            _svc.Update(_admin, id, new NewCalendarEvent("X", Gun), expectedVersion: e.Version));   // düzenleme kilidi
    }

    /// <summary>Soft delete + Çöp Kutusu geri alma; fiziksel silme YOK.</summary>
    [Fact]
    public void TKV2_SoftDelete_Ve_CopKutusu()
    {
        var id = _svc.Create(_admin, new NewCalendarEvent("Silinecek", Gun));
        _svc.Delete(_admin, id);
        Assert.Empty(Items(source: "event"));
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT is_deleted FROM calendar_events WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // satır DURUYOR (fiziksel silme yok)
        }
        var trash = new TrashService(_f);
        Assert.Contains(trash.List(_admin, reauthenticated: true), t => t.Table == "calendar_events" && t.Id == id);
        trash.Restore(_admin, "calendar_events", id, reauthenticated: true);
        Assert.Single(Items(source: "event"));
    }

    // ══════════════ PK-H5 — İŞ EMRİ BAĞI YALNIZ GEZİNME ══════════════

    /// <summary>⭐ Bağ kurulur/taşınır ama İŞ EMRİ KAYDI BİT-BİT DEĞİŞMEZ; geçersiz/yabancı iş emri reddedilir.
    /// CalendarService'te iş emri durumunu değiştiren HİÇBİR yol yoktur — bağlı kayıt üzerinde create/update/
    /// delete döngüsünden sonra work_orders satırının aynı kalması bunun kanıtıdır.</summary>
    [Fact]
    public void TKV3_IsEmri_Bagi_Kaynagi_Degistirmez()
    {
        var woId = _wo.Create(_admin, new NewWorkOrder("IE-1", "Kazı", BranchId: _sube1));
        string Foto()
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM work_orders WHERE id=@id;";
            cmd.AddWithValue("@id", woId);
            using var r = cmd.ExecuteReader();
            r.Read();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.FieldCount; i++) sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            return sb.ToString();
        }
        var once = Foto();
        var id = _svc.Create(_admin, new NewCalendarEvent("Keşif", Gun, WorkOrderId: woId));
        var e = Items(source: "event").Single();
        Assert.Equal(woId, e.WorkOrderId);
        Assert.Equal("IE-1", e.WorkOrderNo);
        _svc.Update(_admin, id, new NewCalendarEvent("Keşif 2", Gun, WorkOrderId: woId));
        _svc.Delete(_admin, id);
        Assert.Equal(once, Foto());   // ⭐ iş emri BİT-BİT aynı — bağ yalnız gezinme (PK-H5)
        Assert.Equal("Taslak", _wo.List(_admin).Single().StatusDisplay);   // durum da değişmedi

        Assert.Throws<ArgumentException>(() =>
            _svc.Create(_admin, new NewCalendarEvent("X", Gun, WorkOrderId: "yok-boyle-emir")));
    }

    // ══════════════ TÜRETİLMİŞ KAYNAKLAR (PK-H2) ══════════════

    [Fact]
    public void TKV4_Turetilmis_IsEmri_Planlari()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-P", "Planlı", BranchId: _sube1,
            PlannedStart: Gun, PlannedEnd: Gun + 5 * GunMs));
        _wo.Create(_admin, new NewWorkOrder("IE-X", "Plansız", BranchId: _sube1));   // tarih yok → takvime giremez
        var wo = Assert.Single(Items(source: "work_order"));
        Assert.False(wo.IsEvent);
        Assert.Equal(0, wo.Version);            // türetilmiş: takvimden düzenlenemez
        Assert.Contains("IE-P", wo.Title);
        Assert.Equal("Taslak", wo.Detail);
        Assert.Equal(Gun, wo.StartDate);
        Assert.Equal(Gun + 5 * GunMs, wo.EndDate);
        Assert.Equal("Şantiye A", wo.BranchName);
    }

    [Fact]
    public void TKV5_Turetilmis_Muayene_Sigorta()
    {
        var arac = new VehicleService(_f).Create(_admin, new NewVehicle("ARC-1"));
        var insp = new InspectionService(_f);
        insp.Save(_admin, new NewInspection(arac, "inspection", Gun - 300 * GunMs, Gun + 10 * GunMs));
        insp.Save(_admin, new NewInspection(arac, "insurance", null, null));   // sonraki tarih boş → takvime giremez
        var i = Assert.Single(Items(source: "inspection"));
        Assert.Contains("ARC-1", i.Title);
        Assert.Contains("Muayene", i.Title);
        Assert.Equal(Gun + 10 * GunMs, i.StartDate);
    }

    [Fact]
    public void TKV6_Turetilmis_Evrak_Gecerlilik()
    {
        var docs = new DocumentService(_f, new LocalFileStorageProvider(_storeRoot));
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\ntest\n%%EOF");
        docs.Save(_admin, "material", _mat, new DocumentMeta("Garanti Belgesi", null, null, Gun + 20 * GunMs, null),
            "garanti.pdf", "application/pdf", pdf);
        docs.Save(_admin, "material", _mat, new DocumentMeta("Süresiz Belge", null, null, null, null),
            "suresiz.pdf", "application/pdf", pdf);   // geçerlilik yok → takvime giremez
        var d = Assert.Single(Items(source: "document"));
        Assert.Contains("Garanti Belgesi", d.Title);
        Assert.Equal(Gun + 20 * GunMs, d.StartDate);

        // Masaüstü çevrimdışı durumu: belge servisi YOKKEN (documents=null) kaynak sessizce boş — hata yok.
        var offline = new CalendarService(_f, documents: null);
        Assert.Empty(offline.Items(_admin, From, To, "document"));
    }

    [Fact]
    public void TKV7_Turetilmis_Proje_Tarihleri()
    {
        var prj = new ProjectService(_f);
        prj.Create(_admin, new NewProject("Köprü Projesi", "active", Gun - 5 * GunMs, Gun + 30 * GunMs,
            BranchIds: new[] { _sube1 }));
        prj.Create(_admin, new NewProject("Tarihsiz Proje"));   // tarih yok → takvime giremez
        var p = Assert.Single(Items(source: "project"));
        Assert.Equal("Köprü Projesi", p.Title);
        Assert.Equal(Gun - 5 * GunMs, p.StartDate);
        Assert.Equal(Gun + 30 * GunMs, p.EndDate);
    }

    /// <summary>Gün-bazlı bakım hedefi = son bakım tarihi + aralık günü; km bazlı tanım TARİHSİZDİR → giremez.</summary>
    [Fact]
    public void TKV8_Turetilmis_GunBazli_Bakim_Hedefi()
    {
        var arac = new VehicleService(_f).Create(_admin, new NewVehicle("ARC-2"));
        var defs = new MaintenanceDefinitionService(_f);
        var gunluk = defs.Create(_admin, new NewMaintenanceDefinition("Filtre", 30m, "day"));
        var kmlik = defs.Create(_admin, new NewMaintenanceDefinition("Yağ", 100m, "km"));
        var mnt = new MaintenanceService(_f);
        mnt.Save(_admin, new NewMaintenance(arac, gunluk, PerformedDate: Gun), "op-m1");
        mnt.Save(_admin, new NewMaintenance(arac, kmlik, PerformedKm: 50m), "op-m2");
        var m = Assert.Single(Items(source: "maintenance"));
        Assert.Contains("Filtre", m.Title);
        Assert.Equal(Gun + 30 * GunMs, m.StartDate);   // hedef = son bakım + 30 gün
    }

    // ══════════════ YETKİ + KAPSAM + TENANT ══════════════

    /// <summary>⭐ YAN KAPI YOK: calendar yetkisiz merkezi ekran kapalı; calendar VAR ama kaynak modül
    /// yetkisi YOKSA o kaynağın öğeleri takvimde GÖRÜNMEZ (bakım yetkisi olmayan bakım tarihlerini okuyamaz).</summary>
    [Fact]
    public void TKV9_Yetki_Kapilari_Yan_Kapi_Yok()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-G", "Gizli", PlannedStart: Gun));
        var arac = new VehicleService(_f).Create(_admin, new NewVehicle("ARC-3"));
        var defs = new MaintenanceDefinitionService(_f);
        var gunluk = defs.Create(_admin, new NewMaintenanceDefinition("Filtre", 30m, "day"));
        new MaintenanceService(_f).Save(_admin, new NewMaintenance(arac, gunluk, PerformedDate: Gun), "op-g1");
        _svc.Create(_admin, new NewCalendarEvent("Herkese", Gun));

        var yetkisiz = Personel();   // calendar yok
        Assert.Throws<ForbiddenException>(() => Items(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewCalendarEvent("X", Gun)));

        // calendar VAR, kaynak modüller YOK → yalnız el ile kayıt görünür; iş emri ve bakım öğesi SIZMAZ.
        var darYetki = Personel(null, ("calendar", true, true, true, true));
        var gorulen = Items(darYetki);
        Assert.Single(gorulen);
        Assert.All(gorulen, i => Assert.Equal("event", i.Source));

        // work_orders yetkisi eklenince iş emri görünür ama bakım hâlâ görünmez.
        var woYetkili = Personel(null, ("calendar", true, false, false, false), ("work_orders", true, false, false, false));
        var kaynaklar = Items(woYetkili).Select(i => i.Source).ToHashSet();
        Assert.Contains("work_order", kaynaklar);
        Assert.DoesNotContain("maintenance", kaynaklar);

        // Silme Delete yetkisi ister:
        var e = Items(darYetki, source: "event").Single();
        var silemez = Personel(null, ("calendar", true, true, true, false));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(silemez, e.Id));
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsam dışı şantiyenin el ile kaydı ve türetilmiş iş emri görünmez;
    /// şubesiz kayıt gizlenmez; kapsam dışına yazılamaz.</summary>
    [Fact]
    public void TKV10_Sube_Kapsami()
    {
        _svc.Create(_admin, new NewCalendarEvent("A kaydı", Gun, BranchId: _sube1));
        _svc.Create(_admin, new NewCalendarEvent("B kaydı", Gun, BranchId: _sube2));
        _svc.Create(_admin, new NewCalendarEvent("Şubesiz", Gun));
        _wo.Create(_admin, new NewWorkOrder("IE-B", "B işi", BranchId: _sube2, PlannedStart: Gun));

        var dar = Personel(new[] { _sube1 },
            ("calendar", true, true, true, true), ("work_orders", true, false, false, false));
        var basliklar = Items(dar).Select(i => i.Title).ToList();
        Assert.Contains("A kaydı", basliklar);
        Assert.Contains("Şubesiz", basliklar);
        Assert.DoesNotContain("B kaydı", basliklar);
        Assert.DoesNotContain(basliklar, t => t.Contains("IE-B"));   // türetilmiş de kapsama uyar

        Assert.Throws<ForbiddenException>(() =>
            _svc.Create(dar, new NewCalendarEvent("Sızma", Gun, BranchId: _sube2)));
        var bKaydi = Items(source: "event").Single(i => i.Title == "B kaydı");
        Assert.Throws<ForbiddenException>(() => _svc.Delete(dar, bKaydi.Id));
    }

    /// <summary>⭐ TENANT: başka firma hiçbir öğeyi göremez/yazamaz.</summary>
    [Fact]
    public void TKV11_Firma_Izolasyonu()
    {
        var id = _svc.Create(_admin, new NewCalendarEvent("Bizim", Gun));
        _wo.Create(_admin, new NewWorkOrder("IE-T", "Bizim iş", PlannedStart: Gun));
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_svc.Items(yabanci, From, To));
        Assert.Throws<ArgumentException>(() => _svc.Update(yabanci, id, new NewCalendarEvent("Çalıntı", Gun)));
        Assert.Throws<ArgumentException>(() => _svc.Delete(yabanci, id));
    }

    // ══════════════ SENKRON ══════════════

    [Fact]
    public void TKV12_Senkron_Listesi_Sira_Ve_Kapisi()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.Contains("calendar_events", t);
        Assert.Equal(CalendarService.Module, BusinessSyncService.ModuleOf("calendar_events"));
        Assert.True(t.IndexOf("work_orders") < t.IndexOf("calendar_events"));   // FK: work_order_id hedefi önce
    }

    /// <summary>⭐ UÇTAN UCA: el ile kayıt (iş emri bağıyla) pakette taşınır; tekrar uygulama kopya üretmez;
    /// silme karşı tarafa geçer.</summary>
    [Fact]
    public void TKV13_Senkron_Uctan_Uca_Idempotent()
    {
        var dstPath = Path.Combine(Path.GetTempPath(), "dw_tkv_dst_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var dst = new SqliteConnectionFactory(dstPath);
            new MigrationRunner(dst).Run();
            Firma(dst, Co);
            using (var conn = dst.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                                  "VALUES(@b,@c,'Şantiye A','site',1,1,1,0);";
                cmd.AddWithValue("@b", _sube1);
                cmd.AddWithValue("@c", Co);
                cmd.ExecuteNonQuery();
            }
            var woId = _wo.Create(_admin, new NewWorkOrder("IE-SNK", "İş", BranchId: _sube1));
            var evId = _svc.Create(_admin, new NewCalendarEvent("Senkron Toplantı", Gun, Gun + GunMs,
                BranchId: _sube1, WorkOrderId: woId));

            var clock = new SystemClock();
            using (var snap = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co)))
            {
                var dstSvc = new BusinessSyncService(dst, clock);
                Assert.Empty(dstSvc.ApplyPull(Co, snap.RootElement).Errors);
                long Say(string sql)
                {
                    using var conn = dst.Create();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
                Assert.Equal(1, Say("SELECT COUNT(*) FROM calendar_events WHERE title='Senkron Toplantı' AND is_deleted=0"));
                Assert.Equal(1, Say("SELECT COUNT(*) FROM work_orders WHERE wo_no='IE-SNK'"));
                dstSvc.ApplyPull(Co, snap.RootElement);   // tekrar → kopya yok
                Assert.Equal(1, Say("SELECT COUNT(*) FROM calendar_events WHERE title='Senkron Toplantı'"));

                // Silme de taşınır (soft delete version+1 ile LWW'yi kazanır):
                _svc.Delete(_admin, evId);
                using var snap2 = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co));
                Assert.Empty(dstSvc.ApplyPull(Co, snap2.RootElement).Errors);
                Assert.Equal(1, Say("SELECT COUNT(*) FROM calendar_events WHERE title='Senkron Toplantı' AND is_deleted=1"));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dstPath); } catch { }
        }
    }

    // ══════════════ ⭐⭐ MIGRATION080 KANITI ══════════════

    [Fact]
    public void TKV14_Migration080_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_tkv_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 79)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) VALUES('P1','C1','Ali',1,11,11,1,0);
INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,currency_code,created_at,updated_at,version,is_deleted)
    VALUES('M1','C1','K-1','Çimento','0','10','TRY',12,12,1,0);
INSERT INTO work_orders(id,company_id,wo_no,title,status,priority,created_by,created_at,updated_at,version,is_deleted)
    VALUES('W1','C1','IE-1','Kazı','draft','normal','U1',12,12,1,0);
INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,created_at)
    VALUES('SM1','C1','M1',NULL,'in',1,'5','op-1',13);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "personnel", "materials", "work_orders", "stock_movements", "companies" })
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
            Assert.Equal(new[] { 80 }, new MigrationRunner(f, new IMigration[] { new Migration080_CalendarEvents() }).Run());
            Assert.Equal(once, Foto(f));   // ⭐ mevcut veri BİT-BİT aynı
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM calendar_events;";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));   // yeni tablo BOŞ doğar
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    [Fact]
    public void TKV15_Migration080_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration080_CalendarEvents.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }

    // ══════════════ EXCEL (liste kuralı 2) ══════════════

    [Fact]
    public void TKV16_Excel_Modeli()
    {
        _svc.Create(_admin, new NewCalendarEvent("Toplantı", Gun, BranchId: _sube1, Note: "not"));
        _wo.Create(_admin, new NewWorkOrder("IE-E", "Excel işi", PlannedStart: Gun));
        var model = CalendarService.ToTableModel(Items());
        Assert.Equal(new[] { "Tarih", "Kaynak", "Başlık", "Şantiye/Saha", "Sorumlu", "Durum/Detay", "Not" }, model.Headers);
        Assert.Equal(2, model.Rows.Count);
        Assert.Contains(model.Rows, r => Equals(r[1], "Takvim Kaydı") && Equals(r[6], "not"));
        Assert.Contains(model.Rows, r => Equals(r[1], "İş Emri"));
    }
}
