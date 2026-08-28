using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ EXL-01 (ADR-176) — EXCEL MERKEZİ TESTLERİ ═══
///
/// Kilitler: 15 kaynaklı ORTAK liste (web/masaüstü paritesi yapısaldır — ikisi de
/// ExcelCenterService.Sources'ı kullanır) · kaynak modül yetkisi olmadan merkezden veri SIZMAZ
/// (çift kapının ikinci kapısı) · tenant · BranchAccess · silinmiş kayıt export'a çıkmaz ·
/// boş veri + Türkçe karakter + dosyanın GERİ OKUNARAK açılabilirliği · export salt-okunurdur
/// (kaynak satırlar bit-bit değişmez) · import MEVCUT KAYDI ASLA GÜNCELLEMEZ (PK-M5:
/// "zaten var → atla" — tekrar import'ta Added=0 ve satır bit-bit aynı).
/// MIGRATION YOK — bu turda şema 81'de kalır.
/// </summary>
public class ExcelMerkeziTests : IDisposable
{
    private const string Co = "EXL";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly string _uid, _sube1, _sube2;
    private readonly SessionContext _admin;
    private readonly MaterialService _materials;
    private readonly ExcelCenterService _center;
    private readonly MaterialImportService _materialImport;
    private readonly ExcelExportService _excel = new();

    public ExcelMerkeziTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_exl_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _sube1 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye B", "site"));

        _materials = new MaterialService(_f);
        _center = Center(_f);
        var lookups = new LookupService(_f);
        _materialImport = new MaterialImportService(_materials, lookups,
            new OpeningStockService(_f), new DepoWise.Infrastructure.Vehicles.VehicleService(_f));
    }

    /// <summary>Merkez, ÜRETİMDEKİ bağlamayla aynı gerçek servislerden kurulur (sahte/mock yok).</summary>
    private static ExcelCenterService Center(SqliteConnectionFactory f)
    {
        var materials = new MaterialService(f);
        var vehicles = new DepoWise.Infrastructure.Vehicles.VehicleService(f);
        var scope = new DepoWise.Infrastructure.Org.ScopeResolver(f);
        var personnel = new DepoWise.Infrastructure.Org.PersonnelService(f, scope);
        var titles = new DepoWise.Infrastructure.Org.PersonnelTitleService(f);
        var inspection = new DepoWise.Infrastructure.Maintenance.InspectionService(f);
        var maintenance = new DepoWise.Infrastructure.Maintenance.MaintenanceService(f);
        var fuel = new DepoWise.Infrastructure.Operations.FuelService(f);
        var requests = new DepoWise.Infrastructure.Requests.RequestService(f, new StockService(f));
        var users = new UserService(f);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(f);
        var lookups = new LookupService(f);
        return new ExcelCenterService(materials, vehicles, personnel, inspection, maintenance, fuel,
            requests, users, branches,
            new DepoWise.Infrastructure.Equipment.EquipmentService(f),
            new DepoWise.Infrastructure.Assignments.AssignmentService(f),
            new DepoWise.Infrastructure.WorkOrders.WorkOrderService(f),
            new DepoWise.Infrastructure.Purchasing.PurchaseOrderService(f),
            new DepoWise.Infrastructure.Calendars.CalendarService(f),
            new DepoWise.Infrastructure.Announcements.AnnouncementService(f),
            new DepoWise.Infrastructure.Accounting.CostCenterService(f),
            new VehicleImportService(vehicles, lookups),
            new PersonnelImportService(personnel, titles, users, lookups),
            new FuelImportService(fuel, vehicles, lookups),
            new FuelDepotImportService(fuel, lookups));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, false, false, false))))
        { ScopeBranchIds = kapsam };

    /// <summary>Kaynak tabloların satır fotoğrafı (DYR11 deseni) — export'un salt-okunurluğunu kanıtlar.</summary>
    private string Foto(params string[] tablolar)
    {
        var sb = new System.Text.StringBuilder();
        using var conn = _f.Create();
        foreach (var t in tablolar)
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

    // ══════════════ KAYNAK LİSTESİ / PARİTE ══════════════

    /// <summary>⭐ 15 kaynak (PK-M2=A) — anahtarlar benzersiz; web (/api/export/entities) ve masaüstü
    /// (ExportItems) AYNI listeden beslendiği için parite yapısal olarak kilitlidir.</summary>
    [Fact]
    public void EXL1_Kaynak_Listesi_15_Ortak()
    {
        Assert.Equal(15, ExcelCenterService.Sources.Count);
        Assert.Equal(15, ExcelCenterService.Sources.Select(x => x.Key).Distinct().Count());
        Assert.Equal(new[]
        {
            "materials", "vehicles", "personnel", "inspection", "maintenance", "requests",
            "fuel", "fuel-depot", "equipment", "assignments", "work-orders", "purchasing",
            "calendar", "announcements", "cost-centers",
        }, ExcelCenterService.Sources.Select(x => x.Key).ToArray());
        Assert.All(ExcelCenterService.Sources, x => Assert.EndsWith(".xlsx", x.FileName));
        Assert.Throws<ArgumentException>(() => ExcelCenterService.Find("bilinmeyen"));
    }

    /// <summary>Boş veride 15 kaynağın TAMAMI başlıklı tablo üretir (admin) — boş veri exportu çökmez.</summary>
    [Fact]
    public void EXL2_Bos_Veride_15_Kaynak_Uretilir()
    {
        foreach (var src in ExcelCenterService.Sources)
        {
            var t = _center.Build(_admin, src.Key);
            Assert.True(t.Headers.Count > 0, src.Key + ": başlık yok");
            // Dosya gerçekten üretilebilir (ClosedXML) — boş listede de geçerli .xlsx çıkar.
            Assert.True(_excel.Export(t).Length > 0, src.Key + ": dosya üretilemedi");
        }
    }

    /// <summary>Türkçe karakterli veri Excel'e yazılır ve dosya GERİ OKUNARAK doğrulanır
    /// (açılabilirlik + kolon doğruluğu kanıtı).</summary>
    [Fact]
    public void EXL3_Turkce_Karakter_Ve_Geri_Okuma()
    {
        _materials.Create(_admin, new NewMaterial("MLZ-Ğ1", "Çimento Ölçüm ĞÜŞİÖÇ ığüşiöç"));
        var bytes = _excel.Export(_center.Build(_admin, "materials"));
        var rows = _excel.ReadRows(bytes);
        var row = Assert.Single(rows);
        Assert.Equal("MLZ-Ğ1", row.Values["Kod"]);
        Assert.Equal("Çimento Ölçüm ĞÜŞİÖÇ ığüşiöç", row.Values["Ad"]);
    }

    // ══════════════ GÜVENLİK — ÇİFT KAPININ İKİNCİ KAPISI ══════════════

    /// <summary>⭐ Kaynak modül yetkisi OLMAYAN kullanıcı merkezden o kaynağı ALAMAZ (403; sessiz sızma yok).
    /// Yetkili olduğu kaynak ise çalışır — merkez bir yetki bypass noktası DEĞİLDİR.</summary>
    [Fact]
    public void EXL4_Kaynak_Yetkisi_Olmadan_Sizma_Yok()
    {
        _materials.Create(_admin, new NewMaterial("M-1", "Çimento"));
        var s = Personel(izinler: ("materials", true));   // yalnız malzeme görebilir
        var t = _center.Build(s, "materials");
        Assert.Contains(t.Rows, r => Equals(r[0], "M-1"));
        // Araç/ekipman/iş emri... yetkisi yok → kaynak servisi fırlatır (API'de 403'e çevrilir).
        foreach (var key in new[] { "vehicles", "equipment", "work-orders", "purchasing", "cost-centers", "assignments" })
            Assert.Throws<ForbiddenException>(() => _center.Build(s, key));
    }

    /// <summary>⭐ Tenant: A firmasının exportu B firmasının kaydını İÇERMEZ.</summary>
    [Fact]
    public void EXL5_Tenant_Izolasyonu()
    {
        _materials.Create(_admin, new NewMaterial("A-MAT", "A Malzemesi"));
        const string CoB = "EXL-B";
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", CoB);
            cmd.ExecuteNonQuery();
        }
        var uidB = new UserService(_f).EnsureInitialAdmin(CoB, "adminb", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(uidB, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _materials.Create(adminB, new NewMaterial("B-MAT", "B Malzemesi"));

        var tA = _center.Build(_admin, "materials");
        Assert.Contains(tA.Rows, r => Equals(r[0], "A-MAT"));
        Assert.DoesNotContain(tA.Rows, r => Equals(r[0], "B-MAT"));
        var tB = _center.Build(adminB, "materials");
        Assert.Contains(tB.Rows, r => Equals(r[0], "B-MAT"));
        Assert.DoesNotContain(tB.Rows, r => Equals(r[0], "A-MAT"));
    }

    /// <summary>⭐ BranchAccess: şube kapsamı kısıtlı kullanıcı, merkezden yalnız KAPSAMINDAKİ şubeye
    /// hedeflenmiş duyuruları aktarır (kapsam süzmesi SERVİSTE — DYR3 kuralının export yolu).
    /// Not: personel kaynağının kapsamı user_scopes tablosundan gelir ve PRS-01 testlerinde kilitlidir;
    /// merkez o kaynağı da AYNI servis üzerinden okur.</summary>
    [Fact]
    public void EXL6_BranchAccess_Kapsam_Suzer()
    {
        var duyuru = new DepoWise.Infrastructure.Announcements.AnnouncementService(_f);
        duyuru.Create(_admin, new DepoWise.Infrastructure.Announcements.NewAnnouncement("Kapsamda", BranchId: _sube1));
        duyuru.Create(_admin, new DepoWise.Infrastructure.Announcements.NewAnnouncement("Dışarıda", BranchId: _sube2));

        var s = Personel(new[] { _sube1 });   // duyuru OKUMA herkese açık; süzme şube kapsamından
        var t = _center.Build(s, "announcements");
        Assert.Contains(t.Rows, r => Equals(r[0], "Kapsamda"));
        Assert.DoesNotContain(t.Rows, r => Equals(r[0], "Dışarıda"));

        var tAdmin = _center.Build(_admin, "announcements");
        Assert.Equal(2, tAdmin.Rows.Count);
    }

    /// <summary>Silinmiş kayıt (soft delete / Çöp Kutusu) export'a ÇIKMAZ.</summary>
    [Fact]
    public void EXL7_Silinmis_Kayit_Exportta_Yok()
    {
        _materials.Create(_admin, new NewMaterial("KAL-1", "Kalan"));
        var silinecek = _materials.Create(_admin, new NewMaterial("SIL-1", "Silinecek"));
        _materials.Delete(_admin, silinecek);
        var t = _center.Build(_admin, "materials");
        Assert.Contains(t.Rows, r => Equals(r[0], "KAL-1"));
        Assert.DoesNotContain(t.Rows, r => Equals(r[0], "SIL-1"));
    }

    /// <summary>⭐ Export SALT-OKUNURDUR: 15 kaynağın tamamı üretilirken kaynak satırlar bit-bit değişmez.</summary>
    [Fact]
    public void EXL8_Export_Kaynaklari_BitBit_Degistirmez()
    {
        _materials.Create(_admin, new NewMaterial("M-1", "Çimento"));
        new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f))
            .Create(_admin, new DepoWise.Infrastructure.Org.NewPersonnel("Ali", null, null, _sube1, true, false));
        var tablolar = new[] { "materials", "personnel", "branches", "users", "user_permissions" };
        var once = Foto(tablolar);
        foreach (var src in ExcelCenterService.Sources)
            _ = _excel.Export(_center.Build(_admin, src.Key));
        Assert.Equal(once, Foto(tablolar));
    }

    // ══════════════ IMPORT — PK-M5: MEVCUT KAYIT ASLA GÜNCELLENMEZ ══════════════

    private static IReadOnlyList<ImportRow> Satir(params (string Kod, string Ad)[] rows)
        => rows.Select((r, i) => new ImportRow(i + 2, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [MaterialImportService.ColCode] = r.Kod,
            [MaterialImportService.ColName] = r.Ad,
        })).ToList();

    /// <summary>⭐ Aynı import İKİNCİ kez koşunca: eklenen 0, "zaten mevcut (atlandı)" dolu ve mevcut
    /// satır BİT-BİT AYNI kalır — dosyada adı değiştirip tekrar aktarmak da kaydı DEĞİŞTİRMEZ
    /// (import'ta güncelleme yolu YOKTUR; UI'daki eski "güncellenen" etiketi bu yüzden düzeltildi).</summary>
    [Fact]
    public void EXL9_Import_Mevcudu_Guncellemez_Atlar()
    {
        var (res1, _) = _materialImport.CommitWithLookups(_admin, Satir(("IMP-1", "İlk Ad")));
        Assert.Equal(1, res1.Added);
        Assert.Equal(0, res1.Updated);
        var once = Foto("materials");

        // Aynı kod, DEĞİŞTİRİLMİŞ ad → yine atlanır, mevcut kayıt bit-bit aynı kalır.
        var (res2, _) = _materialImport.CommitWithLookups(_admin, Satir(("IMP-1", "Değiştirilmiş Ad")));
        Assert.Equal(0, res2.Added);
        Assert.Equal(1, res2.Updated);   // "Updated" alanı = zaten mevcut (atlandı) sayısı
        Assert.Equal(0, res2.Failed);
        Assert.Equal(once, Foto("materials"));
    }

    /// <summary>Ön kontrol (dry-run) hiçbir şey yazmaz — mevcut davranış korunur.</summary>
    [Fact]
    public void EXL10_DryRun_Yazmaz()
    {
        var once = Foto("materials");
        var dry = _materialImport.DryRun(_admin, Satir(("DRY-1", "Deneme")));
        Assert.Equal(1, dry.Valid);
        Assert.Equal(once, Foto("materials"));
    }
}
