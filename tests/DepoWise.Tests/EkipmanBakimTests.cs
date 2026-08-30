using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 7b — EKİPMAN BAKIM HATTI (PK-F9, ADR-191) ═══
///
/// FAZ 2 kararı = <b>SEÇENEK B</b>: ekipman bakımı AYRI tablolarda. Bu dosya hem yeni hattı hem de
/// <b>araç bakımının bozulmadığını</b> kilitler.
///
/// Kilitlenenler: Migration086 yalnız CREATE (araç tabloları DEĞİŞMEDİ) · firma kapsamlı idempotency ·
/// stok defteri/bakiye davranışının araçla AYNI olması · iptalde ters kayıt · tenant/IDOR ·
/// tanım↔ekipman eşlemesi · ekipmanda SAYAÇ OLMAMASI (PK-F8).
/// </summary>
public class EkipmanBakimTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly EquipmentMaintenanceService _svc;
    private readonly EquipmentInspectionService _insp;
    private readonly MaintenanceService _aracBakim;
    private readonly MaintenanceDefinitionService _defs;
    private readonly StockService _stock;
    private readonly SessionContext _a, _b;
    private string _ekipmanA = "", _ekipmanB = "", _defA = "", _malzemeA = "", _subeA = "";

    public EkipmanBakimTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_eqm_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        _svc = new EquipmentMaintenanceService(_f);
        _insp = new EquipmentInspectionService(_f);
        _stock = new StockService(_f);
        _aracBakim = new MaintenanceService(_f);
        _defs = new MaintenanceDefinitionService(_f);

        _a = Firma("EQ-A", "admina");
        _b = Firma("EQ-B", "adminb");
        _subeA = Sube("EQ-A");
        _ekipmanA = Ekipman("EQ-A", "EKP-1");
        _ekipmanB = Ekipman("EQ-B", "EKP-B");
        _malzemeA = Malzeme("EQ-A");
        _defA = _defs.Create(_a, new NewMaintenanceDefinition("Yağ Değişimi", 100m, "day", null, null));
    }

    private SessionContext Firma(string co, string user)
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private string Sube(string co)
    {
        var id = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{id}','{co}','Merkez','branch',1,1,1,0);");
        return id;
    }

    private string Ekipman(string co, string kod)
    {
        var id = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{id}','{co}','{kod}','{kod} adi','active',1,1,1,0);");
        return id;
    }

    private string Malzeme(string co)
    {
        var id = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO materials(id,company_id,code,name,unit_price,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{id}','{co}','M{id[..6]}','Malzeme','10',1,1,1,0);");
        return id;
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // ══════════════════════ MIGRATION 086 ══════════════════════

    /// <summary>EQ01 — Migration086 dört tabloyu kurar; katalog azamisi 86 olur.</summary>
    [Fact]
    public void EQ01_Migration086_Tablolari_Kurar()
    {
        Assert.Equal(4L, Say(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN " +
            "('equipment_maintenances','equipment_maintenance_materials','equipment_inspections','maintenance_definition_equipment');"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM schema_migrations WHERE version=86;"));
        Assert.Equal((long)MigrationCatalog.All().Max(m => m.Version), Say("SELECT MAX(version) FROM schema_migrations;"));
    }

    /// <summary>EQ02 — <b>ARAÇ BAKIM ŞEMASI DEĞİŞMEDİ</b> (FAZ 2 kararının çekirdeği):
    /// <c>vehicle_id</c> hâlâ NOT NULL, <c>equipment_id</c> araç tablolarına EKLENMEDİ.</summary>
    [Fact]
    public void EQ02_Arac_Bakim_Semasi_Degismedi()
    {
        using var conn = _f.Create();
        foreach (var t in new[] { "vehicle_maintenances", "vehicle_inspections", "maintenance_definition_vehicles" })
        {
            Assert.False(DbIntrospect.ColumnExists(conn, null, t, "equipment_id"),
                $"{t} tablosuna equipment_id EKLENMEMELİYDİ (Seçenek B).");
            Assert.True(DbIntrospect.ColumnExists(conn, null, t, "vehicle_id"));
        }
        // vehicle_id NOT NULL korunuyor mu → NULL yazma denemesi REDDEDİLMELİ.
        Assert.ThrowsAny<Exception>(() => Calistir(
            "INSERT INTO vehicle_inspections(id,company_id,vehicle_id,doc_type,created_at,updated_at,version,is_deleted) " +
            "VALUES('x','EQ-A',NULL,'inspection',1,1,1,0);"));
    }

    /// <summary>EQ03 — İdempotency indeksi FİRMA KAPSAMLI kuruldu (FIN-B1/Migration082 sözleşmesi).</summary>
    [Fact]
    public void EQ03_Operation_Id_Firma_Kapsamli()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name='ux_equipment_maintenances_op';";
        var def = cmd.ExecuteScalar() as string;
        Assert.NotNull(def);
        Assert.Contains("company_id", def!);
        Assert.Contains("operation_id", def!);
    }

    // ══════════════════════ KAYIT ══════════════════════

    /// <summary>EQ04 — Bakım kaydı oluşur; sonraki hedef tanım aralığından hesaplanır.</summary>
    [Fact]
    public void EQ04_Kayit_Olusur()
    {
        var gun = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA, PerformedDate: gun), "op-1");
        Assert.NotEqual("", id);

        var satir = Assert.Single(_svc.List(_a));
        Assert.Equal("EKP-1", satir.EquipmentCode);
        Assert.Equal("Yağ Değişimi", satir.DefinitionName);
        Assert.False(satir.IsCancelled);
        Assert.NotNull(satir.NextDueDate);          // gün bazlı tanım → sonraki tarih hesaplandı
    }

    /// <summary>EQ05 — <b>İdempotent:</b> aynı firma + aynı op-id ikinci kez kayıt ve İKİNCİ stok
    /// düşümü ÜRETMEZ. Farklı firma aynı op-id'yi kullanabilir (FIN-B1 sözleşmesi).</summary>
    [Fact]
    public void EQ05_Idempotent_Ve_Firma_Kapsamli()
    {
        var id1 = _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA,
            Materials: new[] { new MaintenanceMaterialLine(_malzemeA, 2m) }, StockLocationId: _subeA), "op-x");
        var id2 = _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA,
            Materials: new[] { new MaintenanceMaterialLine(_malzemeA, 2m) }, StockLocationId: _subeA), "op-x");

        Assert.Equal(id1, id2);
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM equipment_maintenances;"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM stock_movements WHERE movement_type='usage';"));

        // Farklı FİRMA aynı op-id → engellenmez (yeni sözleşme).
        var defB = _defs.Create(_b, new NewMaintenanceDefinition("B Bakım", 10m, "day", null, null));
        var idB = _svc.Save(_b, new NewEquipmentMaintenance(_ekipmanB, defB), "op-x");
        Assert.NotEqual(id1, idB);
    }

    /// <summary>EQ06 — Malzeme tüketimi araç bakımıyla AYNI kurallarla defterе/bakiyeye yazılır;
    /// "ekip stoğu" işaretli satır merkez stoğa DOKUNMAZ.</summary>
    [Fact]
    public void EQ06_Malzeme_Stok_Davranisi_Aracla_Ayni()
    {
        var id = _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA, Materials: new[]
        {
            new MaintenanceMaterialLine(_malzemeA, 3m),
            new MaintenanceMaterialLine(_malzemeA, 5m, FromTeamStock: true),
        }, StockLocationId: _subeA), "op-mat");

        // İki satır da bakım kaydına yazıldı (maliyet), ama defterde YALNIZ biri var.
        Assert.Equal(2, _svc.Materials(_a, id).Count);
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM stock_movements WHERE movement_type='usage';"));
        Assert.Equal(-3m, _stock.GetBalanceAt(_a, _malzemeA, _subeA));   // negatif stok ENGELLENMEZ (ADR-086)
    }

    /// <summary>EQ07 — İptal: ters kayıt üretilir, bakiye geri gelir, kayıt SİLİNMEZ.
    /// Ekip stoğu satırı için ters kayıt üretilmez.</summary>
    [Fact]
    public void EQ07_Iptal_Ters_Kayit()
    {
        var id = _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA, Materials: new[]
        {
            new MaintenanceMaterialLine(_malzemeA, 4m),
            new MaintenanceMaterialLine(_malzemeA, 2m, FromTeamStock: true),
        }, StockLocationId: _subeA), "op-c");
        Assert.Equal(-4m, _stock.GetBalanceAt(_a, _malzemeA, _subeA));

        _svc.Cancel(_a, id, "Yanlış kayıt");

        Assert.Equal(0m, _stock.GetBalanceAt(_a, _malzemeA, _subeA));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM stock_movements WHERE movement_type='usage_reverse';"));
        Assert.Equal(1L, Say($"SELECT is_cancelled FROM equipment_maintenances WHERE id='{id}';"));
        Assert.Equal(1L, Say($"SELECT COUNT(*) FROM equipment_maintenances WHERE id='{id}';"));   // silinmedi

        _svc.Cancel(_a, id, "tekrar");    // idempotent — ikinci ters kayıt YOK
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM stock_movements WHERE movement_type='usage_reverse';"));
        Assert.Throws<ArgumentException>(() => _svc.Cancel(_a, id, "   "));   // gerekçe zorunlu
    }

    /// <summary>EQ08 — <b>PK-F8:</b> ekipmanda sayaç YOKTUR — hiçbir sayaç kaydı üretilmez.</summary>
    [Fact]
    public void EQ08_Sayac_Kaydi_Uretilmez()
    {
        _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA, PerformedKm: 500m), "op-m");
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM vehicle_meter_logs;"));
    }

    // ══════════════════════ GÜVENLİK ══════════════════════

    /// <summary>EQ09 — <b>Tenant/IDOR:</b> başka firmanın ekipmanına bakım açılamaz, kaydı görülemez,
    /// iptal edilemez. Yabancı malzeme de kullanılamaz.</summary>
    [Fact]
    public void EQ09_Tenant_Ve_IDOR()
    {
        Assert.Throws<ForbiddenException>(() => _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanB, _defA), "op-idor"));
        Assert.Throws<ArgumentException>(() => _svc.Save(_a, new NewEquipmentMaintenance("", _defA), "op-bos"));

        var id = _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA), "op-t");
        Assert.Empty(_svc.List(_b));                                        // B, A'nın kaydını görmez
        Assert.Throws<ForbiddenException>(() => _svc.Cancel(_b, id, "olmaz"));
        Assert.Throws<ForbiddenException>(() => _svc.UpdateMetadata(_b, id, "x", null, null));

        // Yabancı firmanın bakım TANIMI kullanılamaz.
        var defB = _defs.Create(_b, new NewMaintenanceDefinition("B", 1m, "day", null, null));
        Assert.Throws<ForbiddenException>(() => _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, defB), "op-def"));
    }

    /// <summary>EQ10 — Yetki: <c>maintenance</c> modülü olmadan kayıt/okuma yapılamaz
    /// (yeni yetki modülü AÇILMADI).</summary>
    [Fact]
    public void EQ10_Yetki_Kapisi()
    {
        var yetkisiz = new SessionContext("u1", "EQ-A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Save(yetkisiz, new NewEquipmentMaintenance(_ekipmanA, _defA), "op-y"));

        var okur = new SessionContext("u1", "EQ-A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("maintenance", true, false, false, false) }));
        _ = _svc.List(okur);
        Assert.Throws<ForbiddenException>(() => _svc.Save(okur, new NewEquipmentMaintenance(_ekipmanA, _defA), "op-y2"));
    }

    // ══════════════════════ TANIM ↔ EKİPMAN ══════════════════════

    /// <summary>EQ11 — Tanımın ekipman kapsamı ayarlanır; araç kapsamına DOKUNULMAZ.</summary>
    [Fact]
    public void EQ11_Tanim_Ekipman_Eslemesi()
    {
        _defs.SetEquipment(_a, _defA, new[] { _ekipmanA });
        Assert.Equal(new[] { _ekipmanA }, _defs.GetEquipmentIds(_a, _defA));
        Assert.Empty(_defs.GetVehicleIds(_a, _defA));          // araç eşlemesi etkilenmedi

        _defs.SetEquipment(_a, _defA, Array.Empty<string>());  // tam değiştirme
        Assert.Empty(_defs.GetEquipmentIds(_a, _defA));

        // Yabancı ekipman bağlanamaz; yabancı tanımın kapsamı okunamaz/yazılamaz.
        Assert.Throws<ForbiddenException>(() => _defs.SetEquipment(_a, _defA, new[] { _ekipmanB }));
        Assert.Throws<ForbiddenException>(() => _defs.GetEquipmentIds(_b, _defA));
    }

    // ══════════════════════ MUAYENE ══════════════════════

    /// <summary>EQ12 — Ekipman muayene kaydı: geçerli belge tipi, tenant kapısı, yumuşak silme.</summary>
    [Fact]
    public void EQ12_Ekipman_Muayene()
    {
        Assert.Throws<ArgumentException>(() => _insp.Save(_a, new NewEquipmentInspection(_ekipmanA, "gecersiz", null, null)));
        Assert.Throws<ForbiddenException>(() => _insp.Save(_a, new NewEquipmentInspection(_ekipmanB, "inspection", null, null)));

        var id = _insp.Save(_a, new NewEquipmentInspection(_ekipmanA, "inspection", null,
            DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds()));
        var satir = Assert.Single(_insp.List(_a));
        Assert.Equal(DateAlertLevel.Expired, satir.Level);      // geçmiş tarih → süresi geçti
        Assert.Empty(_insp.List(_b));                           // tenant

        _insp.Delete(_a, id);
        Assert.Empty(_insp.List(_a));
        Assert.Equal(1L, Say($"SELECT is_deleted FROM equipment_inspections WHERE id='{id}';"));   // fiziksel silme YOK
    }

    // ══════════════════════ ARAÇ REGRESYONU ══════════════════════

    /// <summary>EQ13 — <b>ARAÇ BAKIMI BOZULMADI:</b> ekipman hattı eklendikten sonra araç bakımı
    /// kaydedilir, malzemesi düşer, iptal edilir ve iki hat birbirini ETKİLEMEZ.</summary>
    [Fact]
    public void EQ13_Arac_Bakimi_Regresyonsuz()
    {
        var arac = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO vehicles(id,company_id,internal_code,status,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{arac}','EQ-A','ARC-1','active',1,1,1,0);");

        var aracId = _aracBakim.Save(_a, new NewMaintenance(arac, _defA,
            Materials: new[] { new MaintenanceMaterialLine(_malzemeA, 1m) }, StockLocationId: _subeA), "op-arac");
        Assert.Single(_aracBakim.ListMaintenances(_a));
        Assert.Equal(-1m, _stock.GetBalanceAt(_a, _malzemeA, _subeA));

        // Ekipman kaydı araç listesine SIZMAZ; araç kaydı ekipman listesine SIZMAZ.
        _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA), "op-eq");
        Assert.Single(_aracBakim.ListMaintenances(_a));
        Assert.Single(_svc.List(_a));

        _aracBakim.Cancel(_a, aracId, "iptal");
        Assert.Equal(0m, _stock.GetBalanceAt(_a, _malzemeA, _subeA));
    }

    /// <summary>EQ14 — İki hat AYNI stok defterini kullanır (ikinci stok mekanizması YOK):
    /// araç ve ekipman tüketimleri aynı bakiyeyi etkiler.</summary>
    [Fact]
    public void EQ14_Ortak_Stok_Defteri()
    {
        var arac = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO vehicles(id,company_id,internal_code,status,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{arac}','EQ-A','ARC-2','active',1,1,1,0);");

        _aracBakim.Save(_a, new NewMaintenance(arac, _defA,
            Materials: new[] { new MaintenanceMaterialLine(_malzemeA, 2m) }, StockLocationId: _subeA), "op-a1");
        _svc.Save(_a, new NewEquipmentMaintenance(_ekipmanA, _defA,
            Materials: new[] { new MaintenanceMaterialLine(_malzemeA, 3m) }, StockLocationId: _subeA), "op-e1");

        Assert.Equal(-5m, _stock.GetBalanceAt(_a, _malzemeA, _subeA));
        Assert.Equal(2L, Say("SELECT COUNT(*) FROM stock_movements WHERE movement_type='usage';"));
    }

    // ══════════════════════ SENKRON ══════════════════════

    /// <summary>EQ15 — Yeni tablolar senkron kapsamındadır (masaüstü bakımı çevrimdışı çalışır) ve
    /// FK sırası doğrudur: ebeveynler çocuklardan ÖNCE gelir.</summary>
    [Fact]
    public void EQ15_Senkron_Kapsami_Ve_Sirasi()
    {
        var t = DepoWise.Infrastructure.Sync.BusinessSyncService.Tables.ToList();
        foreach (var ad in new[] { "maintenance_definition_equipment", "equipment_maintenances",
                                   "equipment_maintenance_materials", "equipment_inspections" })
            Assert.Contains(ad, t);

        Assert.True(t.IndexOf("equipment") < t.IndexOf("equipment_maintenances"));
        Assert.True(t.IndexOf("maintenance_definitions") < t.IndexOf("equipment_maintenances"));
        Assert.True(t.IndexOf("equipment_maintenances") < t.IndexOf("equipment_maintenance_materials"));
        Assert.True(t.IndexOf("equipment") < t.IndexOf("equipment_inspections"));
        Assert.True(t.IndexOf("materials") < t.IndexOf("equipment_maintenance_materials"));
    }
}
