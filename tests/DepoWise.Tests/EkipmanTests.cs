using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ EKP-01 (ADR-166, 2026-08-28) — VARLIK / EKİPMAN TESTLERİ ═══
///
/// Ürün kararları: PK-E1 AYRI tablo (araç zincirine sıfır dokunuş) · PK-E2 bakım entegrasyonu yok ·
/// PK-E3 yakıt/muayene kapsam dışı. Kilitler: tenant · şube kapsamı · kod benzersizliği · soft delete ·
/// senkron (FK sırası + yetki kapısı + uçtan uca taşıma + idempotent tekrar) · migration canlı-veri kanıtı.
/// </summary>
public class EkipmanTests : IDisposable
{
    private const string Co = "EKP";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly EquipmentService _svc;
    private readonly string _uid, _sube1, _sube2, _tip;
    private readonly SessionContext _admin;

    public EkipmanTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ekp_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        SeedCompany(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _tip = new LookupService(_f).AddEquipmentType(_admin, "Jeneratör");
        _svc = new EquipmentService(_f);
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
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

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    // ══════════════ TEMEL CRUD ══════════════

    [Fact]
    public void EKP1_Olustur_Listele_Duzenle()
    {
        var id = _svc.Create(_admin, new NewEquipment("EKP-001", "Jeneratör 60kVA", _tip, "active",
            BranchId: _sube1, SerialNo: "SN-123", Location: "Depo arkası"));
        var e = Assert.Single(_svc.List(_admin), x => x.Id == id);
        Assert.Equal("Jeneratör", e.TypeDisplay);
        Assert.Equal("Şantiye A", e.BranchDisplay);
        Assert.Equal("Aktif", e.StatusDisplay);

        var v1 = e.Version;
        _svc.Update(_admin, id, new NewEquipment("EKP-001", "Jeneratör 60kVA", _tip, "maintenance",
            StatusNote: "yağ değişimi", BranchId: _sube2), v1);
        var g = _svc.List(_admin).Single(x => x.Id == id);
        Assert.Equal("Bakımda", g.StatusDisplay);
        Assert.Equal("Şantiye B", g.BranchDisplay);

        // düzenleme kilidi: eski sürümle ikinci yazma reddedilir
        Assert.Throws<ConcurrencyException>(() => _svc.Update(_admin, id, new NewEquipment("EKP-001", "X"), v1));
    }

    /// <summary>Aynı kodla ikinci AKTİF ekipman anlaşılır hatayla reddedilir (ham UNIQUE ihlali değil).</summary>
    [Fact]
    public void EKP2_Kod_Benzersiz()
    {
        _svc.Create(_admin, new NewEquipment("EKP-001", "Birinci"));
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewEquipment("EKP-001", "İkinci")));
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewEquipment(" EKP-001 ", "Boşluklu da aynı")));
    }

    /// <summary>Bilinmeyen durum fail-safe 'active' yazılır; kod+ad dışındaki alanlar opsiyoneldir.</summary>
    [Fact]
    public void EKP3_Opsiyonel_Alanlar_Ve_Durum_FailSafe()
    {
        var id = _svc.Create(_admin, new NewEquipment("K-1", "Konteyner", Status: "uydurma"));
        var e = _svc.List(_admin).Single(x => x.Id == id);
        Assert.Equal("active", e.Status);
        Assert.Null(e.BranchId);
        Assert.Equal("—", e.TypeDisplay);
    }

    // ══════════════ YETKİ + KAPSAM + TENANT ══════════════

    [Fact]
    public void EKP4_Yetkisiz_Reddedilir()
    {
        var yetkisiz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewEquipment("X", "X")));
    }

    /// <summary>⭐ ŞUBE KAPSAMI: yalnız Şantiye A'ya yetkili kullanıcı B'nin ekipmanını görmez/değiştiremez;
    /// şubesiz ekipman gizlenmez; kapsam dışına yazamaz.</summary>
    [Fact]
    public void EKP5_Sube_Kapsami()
    {
        var eA = _svc.Create(_admin, new NewEquipment("A-1", "A Ekipmanı", BranchId: _sube1));
        var eB = _svc.Create(_admin, new NewEquipment("B-1", "B Ekipmanı", BranchId: _sube2));
        var serbest = _svc.Create(_admin, new NewEquipment("G-1", "Genel Ekipman"));

        var dar = Personel(kapsam: new[] { _sube1 },
            izinler: new[] { ("equipment", true, true, true, true) });
        var gorulen = _svc.List(dar).Select(x => x.Id).ToHashSet();
        Assert.Contains(eA, gorulen);
        Assert.DoesNotContain(eB, gorulen);
        Assert.Contains(serbest, gorulen);

        Assert.Throws<ForbiddenException>(() => _svc.Update(dar, eB, new NewEquipment("B-1", "Ele Geçirildi")));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(dar, eB));
        Assert.Throws<ForbiddenException>(() => _svc.Create(dar, new NewEquipment("S-1", "Sızma", BranchId: _sube2)));
    }

    /// <summary>⭐ TENANT: başka firma göremez/yazamaz; başka firmanın şubesine bağlayamaz.</summary>
    [Fact]
    public void EKP6_Firma_Izolasyonu()
    {
        SeedCompany(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var id = _svc.Create(_admin, new NewEquipment("GZ-1", "Gizli", BranchId: _sube1));
        Assert.DoesNotContain(_svc.List(yabanci), x => x.Id == id);
        Assert.Throws<ArgumentException>(() => _svc.Update(yabanci, id, new NewEquipment("GZ-1", "Çalındı")));
        Assert.Throws<ArgumentException>(() => _svc.Delete(yabanci, id));
        Assert.Throws<ArgumentException>(() => _svc.Create(yabanci, new NewEquipment("X-1", "X", BranchId: _sube1)));
        // Aynı kod BAŞKA firmada serbesttir (benzersizlik firma içidir):
        var digerinki = _svc.Create(yabanci, new NewEquipment("GZ-1", "Onların Gizlisi"));
        Assert.NotEqual(id, digerinki);
    }

    // ══════════════ SİLME + AUDIT ══════════════

    [Fact]
    public void EKP7_Soft_Delete_Cop_Kutusu_Audit()
    {
        var id = _svc.Create(_admin, new NewEquipment("S-9", "Silinecek"));
        _svc.Delete(_admin, id);
        Assert.DoesNotContain(_svc.List(_admin), x => x.Id == id);

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT is_deleted FROM equipment WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // satır duruyor (soft)
        }
        var trash = new TrashService(_f);
        Assert.Contains(trash.List(_admin, reauthenticated: true), t => t.Table == "equipment" && t.Id == id);
        trash.Restore(_admin, "equipment", id, reauthenticated: true);
        Assert.Contains(_svc.List(_admin), x => x.Id == id);

        Assert.Contains("equipment", ScreenAuditMap.EntityTypes("equipment"));   // ekran logu kapsar
    }

    // ══════════════ SENKRON ══════════════

    /// <summary>Tablolar senkron listesinde ve FK sırası doğru (tür tanımı ekipmandan ÖNCE gider);
    /// push yetki kapısı doğru modüllere bağlı.</summary>
    [Fact]
    public void EKP8_Senkron_Listesi_Sira_Ve_Yetki_Kapisi()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.Contains("equipment_types", t);
        Assert.Contains("equipment", t);
        Assert.True(t.IndexOf("equipment_types") < t.IndexOf("equipment"),
            "equipment_types, equipment'tan ÖNCE gönderilmelidir (FK).");
        Assert.Equal("definitions", BusinessSyncService.ModuleOf("equipment_types"));
        Assert.Equal(EquipmentService.Module, BusinessSyncService.ModuleOf("equipment"));
    }

    /// <summary>⭐ UÇTAN UCA: kaynak DB'de açılan ekipman senkron paketiyle hedef DB'ye birebir taşınır;
    /// AYNI paket ikinci kez uygulanınca kopya oluşmaz (idempotent) ve başka firmaya karışmaz.</summary>
    [Fact]
    public void EKP9_Senkron_Uctan_Uca_Ve_Idempotent()
    {
        var dstPath = Path.Combine(Path.GetTempPath(), "dw_ekp_dst_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var dst = new SqliteConnectionFactory(dstPath);
            new MigrationRunner(dst).Run();
            SeedCompany(dst, Co);
            // hedefte şube/tür FK'ları için: şubeler sunucu-otoriteli aynayla gelir — testte doğrudan yazıyoruz
            using (var conn = dst.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                                  "VALUES(@b,@c,'Şantiye A','site',1,1,1,0);";
                cmd.AddWithValue("@b", _sube1);
                cmd.AddWithValue("@c", Co);
                cmd.ExecuteNonQuery();
            }

            var id = _svc.Create(_admin, new NewEquipment("SNK-1", "Senkron Jeneratörü", _tip, "active",
                BranchId: _sube1, SerialNo: "SER-42"));

            var clock = new SystemClock();
            using var snap = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co));
            var dstSvc = new BusinessSyncService(dst, clock);
            var r1 = dstSvc.ApplyPull(Co, snap.RootElement);
            Assert.Empty(r1.Errors);

            string Say(SqliteConnectionFactory f, string sql)
            {
                using var conn = f.Create();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToString(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            Assert.Equal("1", Say(dst, $"SELECT COUNT(*) FROM equipment WHERE id='{id}' AND company_id='{Co}' AND code='SNK-1' AND serial_no='SER-42'"));
            Assert.Equal("1", Say(dst, "SELECT COUNT(*) FROM equipment_types WHERE name='Jeneratör'"));

            // İDEMPOTENT: aynı paket ikinci kez → kopya YOK
            dstSvc.ApplyPull(Co, snap.RootElement);
            Assert.Equal("1", Say(dst, "SELECT COUNT(*) FROM equipment"));
            // Başka firmaya SIZMADI:
            Assert.Equal("0", Say(dst, $"SELECT COUNT(*) FROM equipment WHERE company_id<>'{Co}'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dstPath); } catch { }
        }
    }

    // ══════════════ ⭐⭐ CANLI VERİ GÜVENLİĞİ — MIGRATION075 KANITI ══════════════

    /// <summary>v74 şemasında canlı benzeri ARAÇ + bakım verisi varken yalnız Migration075 uygulanır →
    /// araç zinciri bit-bit AYNI kalır; yeni tablolar boş doğar.</summary>
    [Fact]
    public void EKP10_Migration075_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ekp_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 74)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status,created_at,updated_at,version,is_deleted)
    VALUES('V1','C1','ARC-01','06 ABC 01','1500','km','active',11,11,2,0);
INSERT INTO fuel_distributions(id,company_id,vehicle_id,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
    VALUES('FD1','C1','V1','40','42.5','TRY',12,'op-fd1',12,12,1,0);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "vehicles", "fuel_distributions", "companies" })
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
            var uygulanan = new MigrationRunner(f, new IMigration[] { new Migration075_Equipment() }).Run();
            Assert.Equal(new[] { 75 }, uygulanan);
            Assert.Equal(once, Foto(f));
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM equipment) + (SELECT COUNT(*) FROM equipment_types);";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    /// <summary>Statik kanıt: Migration075 yalnız CREATE içerir.</summary>
    [Fact]
    public void EKP11_Migration075_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration075_Equipment.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }

    /// <summary>PK-E1 kilidi: bu geliştirme ARAÇ zincirine dokunmadı — vehicles şemasında ekipman izi yok.</summary>
    [Fact]
    public void EKP12_Arac_Semasina_Dokunulmadi()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name IN ('vehicles','vehicle_maintenances','fuel_distributions','vehicle_inspections');";
        using var r = cmd.ExecuteReader();
        int n = 0;
        while (r.Read())
        {
            n++;
            Assert.DoesNotContain("equipment", (r.GetString(0) ?? "").ToLowerInvariant());
        }
        Assert.Equal(4, n);
    }
}
