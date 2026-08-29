using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 4 — FAZ 4: KATALOG GÖRÜNÜRLÜĞÜ + TASARIMCI SÖZLEŞMESİ ═══
///
/// FAZ 4'te custom raporlar mevcut rapor KATALOĞUNA katıldı (ikinci liste/motor YOK) ve tasarımcı
/// UI'nin beslendiği güvenli metadata ucu eklendi. Bu dosya şunları kilitler:
///  • Katalog görünürlüğü, sabit raporlarla AYNI kurallarla süzülür (deny-by-default).
///  • Tasarımcı kataloğu ile çalıştırma beyaz listesi AYNI kaynaktan gelir — ikinci liste yok.
///  • Tasarımcı metadata'sı SQL ifadesi / tablo adı / alias SIZDIRMAZ.
///  • Rapor anahtarı URL'de güvenli taşınır ve sabit rapor anahtarlarıyla çakışmaz.
/// </summary>
public class CustomRaporKatalogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly CustomReportService _custom;
    private readonly SessionContext _admin;

    public CustomRaporKatalogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_crkat_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        var mats = new MaterialService(_f);
        var veh = new VehicleService(_f);
        var daily = new DailyActivityService(_f, new MaintenanceService(_f));
        _custom = new CustomReportService(_f, mats, veh, daily);

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('KAT','KAT',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin("KAT", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "KAT", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static SessionContext Personel(params string[] moduller)
        => new("u1", "KAT", new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, false, false, false)).ToArray()));

    private string Rapor(string ad = "Malzeme Dökümü")
        => _custom.Create(_admin, ad, CustomReportSources.Materials,
            new[] { MaterialListColumns.Code, MaterialListColumns.Name },
            new[] { new CustomReportFilter(MaterialListColumns.Name, "Ç") }, null, false);

    // ══════════════════ KATALOG GÖRÜNÜRLÜĞÜ ══════════════════

    /// <summary>⭐ KAT01 — Yetkili kullanıcı kendi custom raporunu katalogda GÖRÜR; tanım
    /// <see cref="ReportDescriptor"/>'a doğru çevrilir (kategori/modül kaynak meta verisinden).</summary>
    [Fact]
    public void KAT01_Yetkili_Kullanici_Custom_Raporu_Katalogda_Gorur()
    {
        var id = Rapor();
        var s = Personel("reports", "report_material", CustomReportDefinition.PermissionKeyOf(id));

        var d = Assert.Single(_custom.Catalog(s));
        Assert.Equal(CustomReportDefinition.KeyOf(id), d.Key);
        Assert.Equal("Malzeme Dökümü", d.Name);
        Assert.Equal(ReportCategory.Material, d.Category);
        Assert.Equal("materials", d.DataModule);
        Assert.False(d.IsManager);
        Assert.False(d.RequiresDate);                 // ana veri → tarih yok
        Assert.Contains("en az bir filtre", d.InfoNote ?? "", StringComparison.Ordinal);
    }

    /// <summary>⭐ KAT02 — "reports" ÜST yetkisi olmayan HİÇBİR custom rapor görmez.</summary>
    [Fact]
    public void KAT02_Reports_Yetkisi_Yoksa_Katalog_Bos()
    {
        var id = Rapor();
        var s = Personel("report_material", CustomReportDefinition.PermissionKeyOf(id));
        Assert.Empty(_custom.Catalog(s));
    }

    /// <summary>⭐ KAT03 — KATEGORİ yetkisi olmayan o raporu katalogda görmez (ADR-181 paritesi).</summary>
    [Fact]
    public void KAT03_Kategori_Yetkisi_Yoksa_Gorunmez()
    {
        var id = Rapor();
        var s = Personel("reports", CustomReportDefinition.PermissionKeyOf(id));   // report_material YOK
        Assert.Empty(_custom.Catalog(s));
    }

    /// <summary>⭐ KAT04 — Rapora özel DİNAMİK anahtarı olmayan görmez (PK-CR-04=A, deny-by-default).</summary>
    [Fact]
    public void KAT04_Dinamik_Anahtar_Yoksa_Gorunmez()
    {
        Rapor();
        var s = Personel("reports", "report_material");
        Assert.Empty(_custom.Catalog(s));
    }

    /// <summary>⭐ KAT05 — Veri modülü role KAPATILMIŞSA rapor katalogda görünmez (RPR-15 paritesi).</summary>
    [Fact]
    public void KAT05_Kapatilmis_Modul_Katalogdan_Duser()
    {
        var id = Rapor();
        var s = new SessionContext("u2", "KAT", new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_material", true, false, false, false),
                new ModulePermission(CustomReportDefinition.PermissionKeyOf(id), true, false, false, false),
            }))
        {
            BlockedModules = new HashSet<string>(StringComparer.Ordinal) { "materials" },
        };
        Assert.Empty(_custom.Catalog(s));
    }

    /// <summary>⭐ KAT06 — PASİF ve SİLİNMİŞ tanımlar katalogda görünmez.</summary>
    [Fact]
    public void KAT06_Pasif_ve_Silinmis_Gorunmez()
    {
        var id = Rapor();
        var s = Personel("reports", "report_material", CustomReportDefinition.PermissionKeyOf(id));
        Assert.Single(_custom.Catalog(s));

        _custom.Update(_admin, id, "Malzeme Dökümü", CustomReportSources.Materials,
            new[] { MaterialListColumns.Code }, new[] { new CustomReportFilter(MaterialListColumns.Code, "M") },
            null, false, isActive: false);
        Assert.Empty(_custom.Catalog(s));
    }

    /// <summary>⭐ KAT07 — TENANT: başka firmanın raporu katalogda GÖRÜNMEZ.</summary>
    [Fact]
    public void KAT07_Baska_Firma_Katalogda_Gormez()
    {
        var id = Rapor();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('KAT2','KAT2',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var yabanci = new SessionContext("u3", "KAT2", new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_material", true, false, false, false),
                new ModulePermission(CustomReportDefinition.PermissionKeyOf(id), true, false, false, false),
            }));
        Assert.Empty(_custom.Catalog(yabanci));
    }

    // ══════════════════ TASARIMCI KATALOĞU (UI metadata) ══════════════════

    /// <summary>⭐ KAT08 — Tasarımcı kataloğu ile ÇALIŞTIRMA beyaz listesi AYNI kaynaktan gelir:
    /// ikinci bir liste yoktur (PK-CR-05=A).</summary>
    [Fact]
    public void KAT08_Tasarimci_Katalogu_Tek_Kaynaktan()
    {
        Assert.Same(CustomReportSources.All, CustomReportService.DesignerCatalog());
        Assert.Equal(3, CustomReportService.DesignerCatalog().Count);
    }

    /// <summary>⭐ KAT09 — Tasarımcı metadata'sı SQL/tablo/alias SIZDIRMAZ: kolon anahtarları
    /// yalnız sade tanımlayıcılardır (nokta, boşluk, parantez, yıldız, SQL kelimesi İÇERMEZ).</summary>
    [Fact]
    public void KAT09_Metadata_Sql_Sizdirmaz()
    {
        foreach (var src in CustomReportService.DesignerCatalog())
        {
            Assert.Matches("^[a-z_]+$", src.Key);
            foreach (var c in src.Columns)
            {
                Assert.Matches("^[A-Za-z][A-Za-z0-9]*$", c.Key);   // camelCase sade anahtar
                foreach (var yasak in new[] { ".", " ", "(", ")", "*", "SELECT", "FROM", "JOIN", "ORDER" })
                    Assert.DoesNotContain(yasak, c.Key, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ══════════════════ ANAHTAR SÖZLEŞMESİ ══════════════════

    /// <summary>⭐ KAT10 — Rapor anahtarı URL segmentinde GÜVENLİ taşınır: kodlama gerektiren
    /// karakter içermez (<c>/api/reports/{type}</c> yolundan geçecektir) ve sabit rapor
    /// anahtarlarıyla ÇAKIŞMAZ.</summary>
    [Fact]
    public void KAT10_Rapor_Anahtari_Url_Guvenli_ve_Cakismaz()
    {
        var id = Rapor();
        var key = CustomReportDefinition.KeyOf(id);

        Assert.Matches("^[A-Za-z0-9._-]+$", key);                      // kodlama gerekmez
        Assert.Equal(key, Uri.EscapeDataString(key));                  // URL'de DEĞİŞMEZ
        Assert.DoesNotContain(":", key, StringComparison.Ordinal);

        Assert.DoesNotContain(ReportCatalog.All, d => d.Key == key);
        Assert.Equal(id, CustomReportDefinition.IdFromKey(key));
        Assert.Null(CustomReportDefinition.IdFromKey("stock"));
    }

    /// <summary>⭐ KAT11 — Dinamik yetki anahtarı da sade ve deterministiktir (yetki ağacında
    /// serbest metin olarak saklanır — migration gerektirmez).</summary>
    [Fact]
    public void KAT11_Dinamik_Yetki_Anahtari_Sade()
    {
        var id = Rapor();
        var pk = CustomReportDefinition.PermissionKeyOf(id);
        Assert.StartsWith("report_custom_", pk, StringComparison.Ordinal);
        Assert.Matches("^[a-z0-9_]+$", pk);
        Assert.DoesNotContain(AppModules.All, m => m.Key == pk);   // statik ağaçta YOK — dinamiktir
    }

    /// <summary>⭐ KAT12 — Rapor Tasarımcısı ekranı yetki ağacına EKLENDİ ve mevcut "reports"
    /// modülüne bağlıdır (yeni yetki modülü açılmadı → migration gerekmedi).</summary>
    [Fact]
    public void KAT12_Tasarimci_Ekrani_Yetki_Agacinda()
    {
        var ekran = Assert.Single(AppScreens.All.Where(e => e.Key == "reports.designer"));
        Assert.Equal("reports", ekran.ModuleKey);
        Assert.True(ekran.OnDesktop);
        Assert.True(ekran.OnWeb);
        Assert.Equal("reports/designer", ekran.WebRoute);
        Assert.Equal("reports:designer", ekran.DesktopNavKey);
    }
}
