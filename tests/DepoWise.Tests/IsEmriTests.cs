using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ EMR-01 (ADR-170, 2026-08-28) — İŞ EMRİ TESTLERİ ═══
///
/// Kilitler: PK-F1 durum matrisi + geçmiş defteri · PK-F2 terminalden çıkış YOK · PK-F3 tüketim mevcut
/// stok çıkışıyla + idempotent + negatif stok kalkanı + stok kapısı · atamalar (zimmet değildir) ·
/// PK-F5 yalnız şantiye bağı + BranchAccess · tenant · maliyet özeti/merkez bağı · bakım yalnız BAĞ
/// (PK-F9, kaynak kayıt bit-bit değişmez) · senkron sıra/kapı/uçtan uca · migration canlı-veri kanıtı.
/// </summary>
public class IsEmriTests : IDisposable
{
    private const string Co = "EMR";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly WorkOrderService _svc;
    private readonly StockService _stock;
    private readonly string _uid, _depo, _depo2, _mat, _ali, _ekp;
    private readonly SessionContext _admin;
    private static readonly long Gun = 1_700_000_000_000;

    public IsEmriTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_emr_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _depo = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye A", "site"));
        _depo2 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye B", "site"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
        _ali = new PersonnelService(_f, new ScopeResolver(_f)).Create(_admin, new NewPersonnel("Ali Usta", null, null, null));
        _ekp = new EquipmentService(_f).Create(_admin, new NewEquipment("EKP-1", "Jeneratör"));
        _stock = new StockService(_f);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 100m) }, "op-acilis", branchId: _depo, docDate: Gun);
        _svc = new WorkOrderService(_f);
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

    private decimal Stok() => _stock.GetBalancesByLocation(_admin, _mat).TryGetValue(_depo, out var q) ? q : 0m;

    private string Emir(string no = "IE-1", string? branch = null)
        => _svc.Create(_admin, new NewWorkOrder(no, "Kazı işi", BranchId: branch ?? _depo, AssigneePersonnelId: _ali));

    // ══════════════ TEMEL + PK-F1/F2 ══════════════

    [Fact]
    public void EMR1_Olustur_No_Benzersiz_Gecmis_Baslar()
    {
        var id = Emir();
        var w = Assert.Single(_svc.List(_admin));
        Assert.Equal("Taslak", w.StatusDisplay);
        Assert.Equal("Ali Usta", w.AssigneeDisplay);
        Assert.Single(_svc.History(_admin, id));   // draft girişi deftere düştü
        Assert.Throws<ArgumentException>(() => Emir(no: "IE-1"));   // benzersiz no
    }

    /// <summary>PK-F1 matrisi: geçerli zincir çalışır; geçersiz sıçrama reddedilir; her adım deftere düşer;
    /// Devam Ediyor'a ilk geçiş actual_start, Tamamlandı actual_end yazar.</summary>
    [Fact]
    public void EMR2_Durum_Matrisi_Ve_Defter()
    {
        var id = Emir();
        Assert.Throws<ArgumentException>(() => _svc.SetStatus(_admin, id, "completed"));   // draft→completed YASAK
        _svc.SetStatus(_admin, id, "assigned");
        _svc.SetStatus(_admin, id, "in_progress", docDate: Gun);
        _svc.SetStatus(_admin, id, "on_hold");
        _svc.SetStatus(_admin, id, "in_progress");   // ⇄ geri dönüş serbest
        _svc.SetStatus(_admin, id, "completed", "iş bitti", Gun + 86_400_000);

        var w = _svc.List(_admin).Single();
        Assert.Equal("Tamamlandı", w.StatusDisplay);
        Assert.Equal(Gun, w.ActualStart);                    // İLK in_progress günü
        Assert.Equal(Gun + 86_400_000, w.ActualEnd);
        Assert.Equal(6, _svc.History(_admin, id).Count);     // draft + 5 geçiş
    }

    /// <summary>⭐ PK-F2: TERMİNALDEN ÇIKIŞ YOK — tamamlanan/iptal edilen hiçbir yolla değiştirilemez.</summary>
    [Fact]
    public void EMR3_Terminal_Kilitli()
    {
        var id = Emir();
        _svc.SetStatus(_admin, id, "in_progress");
        _svc.SetStatus(_admin, id, "completed");
        Assert.Throws<ArgumentException>(() => _svc.SetStatus(_admin, id, "in_progress"));   // yeniden açma YOK
        Assert.Throws<ArgumentException>(() => _svc.SetStatus(_admin, id, "cancelled"));
        Assert.Throws<ArgumentException>(() => _svc.UpdateMeta(_admin, id, new NewWorkOrder("IE-1", "X")));
        Assert.Throws<ArgumentException>(() => _svc.AddAssignment(_admin, id, "personnel", _ali));
        Assert.Throws<ArgumentException>(() =>
            _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 1m) }, "op-t1"));
    }

    // ══════════════ ATAMALAR ══════════════

    /// <summary>Atamalar (personel/araç/ekipman) — zimmet DEĞİLDİR: zimmet defteri hiç etkilenmez.</summary>
    [Fact]
    public void EMR4_Atamalar_Ve_Zimmet_Etkilenmez()
    {
        var id = Emir();
        _svc.AddAssignment(_admin, id, "personnel", _ali);
        _svc.AddAssignment(_admin, id, "equipment", _ekp);
        _svc.AddAssignment(_admin, id, "equipment", _ekp);   // tekrar → sessiz, kopya yok
        var atamalar = _svc.Assignments(_admin, id);
        Assert.Equal(2, atamalar.Count);

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM assignment_movements;";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));   // ZİMMET defteri BOŞ
        }

        _svc.RemoveAssignment(_admin, atamalar.First(a => a.ResourceType == "equipment").Id);
        Assert.Single(_svc.Assignments(_admin, id));
        Assert.Throws<ArgumentException>(() => _svc.AddAssignment(_admin, id, "material", _mat));   // izinsiz tür
    }

    // ══════════════ PK-F3 — TÜKETİM ══════════════

    /// <summary>⭐ Tüketim = MEVCUT stok çıkışı: stok düşer, wo: izi + iş emri bağı oluşur, maliyet özeti okur.</summary>
    [Fact]
    public void EMR5_Tuketim_Stok_Dusurur_Ve_Ozet_Okur()
    {
        var id = Emir();
        _svc.SetStatus(_admin, id, "in_progress");
        _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 8m) }, "op-c1", Gun);
        Assert.Equal(92m, Stok());
        Assert.Single(_svc.Links(_admin, id), l => l.EntityType == "stock_document");
        var ozet = _svc.CostSummary(_admin, id);
        Assert.Equal(80m, ozet.Single(x => x.Category == "Malzeme Tüketimi").Amount);   // 8×10 (kart fiyatı)
    }

    /// <summary>⭐⭐ İDEMPOTENT: aynı tüketim iki kez → İKİNCİ stok çıkışı YOK, ikinci bağ YOK.</summary>
    [Fact]
    public void EMR6_Tuketim_Idempotent()
    {
        var id = Emir();
        _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 5m) }, "op-r1", Gun);
        _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 5m) }, "op-r1", Gun);   // retry
        Assert.Equal(95m, Stok());   // BİR kez düştü
        Assert.Single(_svc.Links(_admin, id));
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'wo:op-r1%';";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>Negatif stok kalkanı AYNEN çalışır; hata her şeyi geri alır (bağ da oluşmaz).</summary>
    [Fact]
    public void EMR7_Negatif_Stok_Kalkani()
    {
        var id = Emir();
        Assert.ThrowsAny<Exception>(() =>
            _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 500m) }, "op-n1", Gun));
        Assert.Equal(100m, Stok());
        Assert.Empty(_svc.Links(_admin, id));
    }

    /// <summary>Maliyet merkezi seçiliyse tüketim belgesi D dış-bağıyla merkeze bağlanır (çift sayım yok).</summary>
    [Fact]
    public void EMR8_Maliyet_Merkezi_Bagi()
    {
        var ccSvc = new DepoWise.Infrastructure.Accounting.CostCenterService(_f);
        var cc = ccSvc.Create(_admin, new DepoWise.Infrastructure.Accounting.NewCostCenter("Asfalt"));
        var id = _svc.Create(_admin, new NewWorkOrder("IE-CC", "Merkezli iş", BranchId: _depo, CostCenterId: cc));
        var docId = _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 4m) }, "op-cc1", Gun);
        var ozet = ccSvc.Summary(_admin, Gun - 1000, Gun + 1000);
        Assert.Equal(40m, ozet.Single(x => x.Category == "Malzeme Çıkışı").Amount);   // merkez özeti de görüyor
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cost_center_links WHERE entity_id=@d AND is_deleted=0;";
        cmd.AddWithValue("@d", docId);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ══════════════ YETKİ + KAPSAM + TENANT ══════════════

    /// <summary>⭐ work_orders yetkisiz her şey kapalı; work_orders VAR ama STOK yetkisi YOKSA tüketim
    /// stok kapısına takılır — iş emri stok yan kapısı DEĞİL.</summary>
    [Fact]
    public void EMR9_Yetki_Kapilari()
    {
        var id = Emir();
        var yetkisiz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewWorkOrder("X", "X")));

        var stoksuz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("work_orders", true, true, true, true) }));
        Assert.Throws<ForbiddenException>(() =>
            _svc.ConsumeMaterial(stoksuz, id, new[] { new StockLine(_mat, 1m) }, "op-y1", Gun));
        Assert.Equal(100m, Stok());
        // İptal DELETE ister; yalnız Edit olan iptal EDEMEZ:
        var editci = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("work_orders", true, true, true, false) }));
        Assert.Throws<ForbiddenException>(() => _svc.SetStatus(editci, id, "cancelled"));
        _svc.SetStatus(editci, id, "in_progress");   // ilerletme Edit ile serbest
    }

    /// <summary>⭐ ŞUBE KAPSAMI (PK-F5): kapsam dışı şantiyenin iş emri görünmez/işlenemez; kapsam dışına açılamaz.</summary>
    [Fact]
    public void EMR10_Sube_Kapsami()
    {
        var w1 = Emir(no: "IE-A");
        var w2 = Emir(no: "IE-B", branch: _depo2);
        var dar = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("work_orders", true, true, true, true),
                new ModulePermission("stock", true, true, true, false),
            })) { ScopeBranchIds = new[] { _depo } };
        var gorulen = _svc.List(dar).Select(x => x.Id).ToHashSet();
        Assert.Contains(w1, gorulen);
        Assert.DoesNotContain(w2, gorulen);
        Assert.Throws<ForbiddenException>(() => _svc.SetStatus(dar, w2, "in_progress"));
        Assert.Throws<ForbiddenException>(() => _svc.Create(dar, new NewWorkOrder("IE-S", "Sızma", BranchId: _depo2)));
    }

    /// <summary>⭐ TENANT: başka firma göremez/yazamaz.</summary>
    [Fact]
    public void EMR11_Firma_Izolasyonu()
    {
        var id = Emir();
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_svc.List(yabanci));
        Assert.Throws<ArgumentException>(() => _svc.SetStatus(yabanci, id, "in_progress"));
        Assert.Throws<ArgumentException>(() => _svc.AddAssignment(yabanci, id, "personnel", _ali));
    }

    // ══════════════ PK-F9 — BAKIM YALNIZ BAĞ ══════════════

    /// <summary>Mevcut bakım kaydı iş emrine BAĞLANIR; bakım kaydı BİT-BİT değişmez; maliyet özetine girer.</summary>
    [Fact]
    public void EMR12_Bakim_Yalniz_Bag_Kaynak_Degismez()
    {
        // Gerçek bir bakım kaydı (mevcut zincirle — araca):
        var arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f).Create(_admin,
            new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        var defId = new DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService(_f).Create(_admin,
            new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition("Yağ", 100m, "km"));
        var mntId = new DepoWise.Infrastructure.Maintenance.MaintenanceService(_f).Save(_admin,
            new DepoWise.Infrastructure.Maintenance.NewMaintenance(
            arac, defId, null, null, null, null, null, null, Gun,
            new[] { new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(_mat, 2m, false) }.ToList(),
            StockLocationId: _depo), "op-mnt1");

        string Foto()
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM vehicle_maintenances WHERE id=@id;";
            cmd.AddWithValue("@id", mntId);
            using var r = cmd.ExecuteReader();
            r.Read();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.FieldCount; i++) sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            return sb.ToString();
        }
        var once = Foto();
        var id = Emir();
        _svc.LinkExisting(_admin, id, "vehicle_maintenance", mntId);
        Assert.Equal(once, Foto());   // kaynak bakım kaydı BİT-BİT aynı (yalnız dış bağ)
        Assert.Single(_svc.Links(_admin, id), l => l.EntityType == "vehicle_maintenance");
        Assert.Equal(20m, _svc.CostSummary(_admin, id).Single(x => x.Category == "Bakım Malzemesi").Amount);   // 2×10
    }

    // ══════════════ SENKRON ══════════════

    [Fact]
    public void EMR13_Senkron_Listesi_Sira_Ve_Kapisi()
    {
        var t = BusinessSyncService.Tables.ToList();
        foreach (var tablo in new[] { "work_orders", "work_order_assignments", "work_order_links", "work_order_status_history" })
        {
            Assert.Contains(tablo, t);
            Assert.Equal(WorkOrderService.Module, BusinessSyncService.ModuleOf(tablo));
        }
        Assert.True(t.IndexOf("work_orders") < t.IndexOf("work_order_assignments"));
        Assert.True(t.IndexOf("work_orders") < t.IndexOf("work_order_links"));
        Assert.True(t.IndexOf("stock_documents") < t.IndexOf("work_order_links"));   // bağ hedefi önce
        Assert.True(t.IndexOf("purchase_orders") < t.IndexOf("work_orders"));
    }

    /// <summary>⭐ UÇTAN UCA: iş emri + atama + tüketim (stok belgesi) + geçmiş AYNI pakette taşınır;
    /// tekrar uygulama kopya üretmez.</summary>
    [Fact]
    public void EMR14_Senkron_Uctan_Uca_Idempotent()
    {
        var dstPath = Path.Combine(Path.GetTempPath(), "dw_emr_dst_" + Guid.NewGuid().ToString("N") + ".db");
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
                cmd.AddWithValue("@b", _depo);
                cmd.AddWithValue("@c", Co);
                cmd.ExecuteNonQuery();
            }
            var id = Emir(no: "IE-SNK");
            _svc.AddAssignment(_admin, id, "personnel", _ali);
            _svc.SetStatus(_admin, id, "in_progress", docDate: Gun);
            _svc.ConsumeMaterial(_admin, id, new[] { new StockLine(_mat, 3m) }, "op-snk", Gun);

            var clock = new SystemClock();
            using var snap = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co));
            var dstSvc = new BusinessSyncService(dst, clock);
            Assert.Empty(dstSvc.ApplyPull(Co, snap.RootElement).Errors);

            long Say(string sql)
            {
                using var conn = dst.Create();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            Assert.Equal(1, Say("SELECT COUNT(*) FROM work_orders WHERE wo_no='IE-SNK' AND status='in_progress'"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM work_order_assignments WHERE is_deleted=0"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM work_order_links"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'wo:op-snk%'"));
            Assert.Equal(2, Say("SELECT COUNT(*) FROM work_order_status_history"));   // draft + in_progress

            dstSvc.ApplyPull(Co, snap.RootElement);   // tekrar
            Assert.Equal(1, Say("SELECT COUNT(*) FROM work_orders WHERE wo_no='IE-SNK'"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'wo:op-snk%'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dstPath); } catch { }
        }
    }

    // ══════════════ ⭐⭐ MIGRATION079 KANITI ══════════════

    [Fact]
    public void EMR15_Migration079_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_emr_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 78)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) VALUES('P1','C1','Ali',1,11,11,1,0);
INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,currency_code,created_at,updated_at,version,is_deleted)
    VALUES('M1','C1','K-1','Çimento','0','10','TRY',12,12,1,0);
INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,created_at)
    VALUES('SM1','C1','M1',NULL,'in',1,'5','op-1',13);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "personnel", "materials", "stock_movements", "companies" })
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
            Assert.Equal(new[] { 79 }, new MigrationRunner(f, new IMigration[] { new Migration079_WorkOrders() }).Run());
            Assert.Equal(once, Foto(f));
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM work_orders) + (SELECT COUNT(*) FROM work_order_assignments) " +
                                  "+ (SELECT COUNT(*) FROM work_order_links) + (SELECT COUNT(*) FROM work_order_status_history);";
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
    public void EMR16_Migration079_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration079_WorkOrders.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }
}
