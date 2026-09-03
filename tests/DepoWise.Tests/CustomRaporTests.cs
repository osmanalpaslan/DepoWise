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
/// ═══ ARA İŞ 4 (ADR-186) — CUSTOM RAPOR SÖZLEŞME KİLİTLERİ ═══
///
/// Kararlar: PK-CR-01=A (ham SQL/JOIN YOK) · 02=A (tanım tablosu + senkron) · 03=A (mevcut motor
/// genişletilir) · 04=A (dinamik yetki anahtarı) · 05=A (merkezî beyaz liste) · 06/10=A (tarih
/// KAYNAK BAZLI zorunlu + SQL satır tavanı) · 09=A (v1 = yalnız 3 kaynak).
///
/// Bu dosya güvenlik kapılarını, beyaz listeyi, tarih/limit sözleşmesini ve tenant izolasyonunu
/// kilitler. Hiçbir kontrol gevşetilmez; negatif senaryolar açıkça test edilir.
/// </summary>
public class CustomRaporTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly ReportService _reports;
    private readonly CustomReportService _custom;
    private readonly SessionContext _adminA, _adminB;

    public CustomRaporTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_crapor_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        var mats = new MaterialService(_f);
        var veh = new VehicleService(_f);
        var maint = new MaintenanceService(_f);
        var daily = new DailyActivityService(_f, maint);
        _custom = new CustomReportService(_f, mats, veh, daily);
        _reports = new ReportService(_f) { Custom = _custom };

        _adminA = Kur("CR-A", "admina");
        _adminB = Kur("CR-B", "adminb");
    }

    private SessionContext Kur(string co, string user)
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

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static ReportRequest Istek(long? from = null, long? to = null) => new(true, from, to);

    /// <summary>Belirli modüllere yalnız GÖRÜNTÜLEME yetkisi olan personel oturumu.</summary>
    private static SessionContext Personel(string company, params string[] moduller)
        => new("u-" + company, company, new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, false, false, false)).ToArray()));

    private string MalzemeRaporu(SessionContext s, params string[] kolonlar)
        => _custom.Create(s, "Malzeme Raporu", CustomReportSources.Materials,
            kolonlar.Length > 0 ? kolonlar : new[] { MaterialListColumns.Code, MaterialListColumns.Name },
            new[] { new CustomReportFilter(MaterialListColumns.Name, "Ç") }, null, false);

    // ══════════════════════ MIGRATION 083 ══════════════════════

    /// <summary>⭐ CR01 — Migration083 tabloyu kurar; katalog azamisi 83 olur.</summary>
    [Fact]
    public void CR01_Migration083_Tabloyu_Kurar_Katalog_83()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='custom_report_defs';";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));

        // ⚠️ Bu test eskiden azami sürümü SABİT 83'e bağlıyordu; katalog büyüyünce (Migration084,
        // ARA İŞ 5) kırılıyordu. GEVŞETİLMEDİ — GÜÇLENDİRİLDİ: (a) 083'ün gerçekten uygulanmış
        // olduğu AÇIKÇA doğrulanır, (b) azami sürüm kataloğun azamisiyle karşılaştırılır → runner
        // bir migration'ı atlarsa test yine kırılır.
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version=83;";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));

        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var katalogAzami = DepoWise.Infrastructure.Database.Migrations.MigrationCatalog.All().Max(m => m.Version);
        Assert.Equal((long)katalogAzami, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>⭐ CR02 — GERÇEK 82 → 83 yükseltme provası: veri dolu şema-82 DB'de migration
    /// uygulanır, mevcut satırlar KORUNUR ve yeni tablo kurulur.</summary>
    [Fact]
    public void CR02_Yukseltme_82den83e_Mevcut_Veri_Korunur()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_cr82_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f82 = new SqliteConnectionFactory(yol);
            new MigrationRunner(f82, MigrationCatalog.All().Where(m => m.Version <= 82)).Run();
            using (var conn = f82.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('UP','UP',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }
            Assert.Equal(82L, Sema(f82));
            Assert.Equal(0L, TabloVar(f82, "custom_report_defs"));

            var uygulanan = new MigrationRunner(f82).Run();

            // ⚠️ Bu test 82 → 83 YÜKSELTMESİNİ kanıtlar; runner ise kataloğun TAMAMINI uygular.
            // Eskiden azami sürüm SABİT 83'e bağlıydı ve katalog büyüyünce (Migration084, ARA İŞ 5)
            // kırılıyordu. GEVŞETİLMEDİ — GÜÇLENDİRİLDİ: 083'ün gerçekten uygulandığı AÇIKÇA
            // doğrulanır, azami sürüm ise kataloğun azamisiyle karşılaştırılır → runner bir
            // migration'ı atlarsa test yine kırılır.
            Assert.Contains(83, uygulanan);
            Assert.Equal(1L, Say(f82, "SELECT COUNT(*) FROM schema_migrations WHERE version=83;"));
            Assert.Equal((long)MigrationCatalog.All().Max(m => m.Version), Sema(f82));
            Assert.Equal(1L, TabloVar(f82, "custom_report_defs"));
            using (var conn = f82.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM companies WHERE id='UP';";
                Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // mevcut veri KORUNDU
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    /// <summary>⭐ CR03 — ROLLBACK: migration başarısız olursa şema 82'de kalır, tablo oluşmaz.</summary>
    [Fact]
    public void CR03_Migration_Basarisiz_Olursa_Sema_82de_Kalir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_crrb_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 82)).Run();
            Assert.Equal(82L, Sema(f));

            Assert.ThrowsAny<Exception>(() =>
                new MigrationRunner(f, new IMigration[] { new BozukMigration83() }).Run());

            Assert.Equal(82L, Sema(f));
            Assert.Equal(0L, TabloVar(f, "custom_report_defs"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    private sealed class BozukMigration83 : IMigration
    {
        public int Version => 83;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE custom_report_defs(id TEXT); CREATE INDEX x ON olmayan_tablo(id);";
            cmd.ExecuteNonQuery();
        }
    }

    private static long Say(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long Sema(SqliteConnectionFactory f)
        => Say(f, "SELECT MAX(version) FROM schema_migrations;");

    private static long TabloVar(SqliteConnectionFactory f, string tablo)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tablo}';";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ══════════════════════ KAYNAK KATALOĞU (PK-CR-09=A) ══════════════════════

    /// <summary>⭐ CR04 — v1 kaynakları TAM OLARAK ÜÇTÜR; başka kaynak eklenmemiştir.</summary>
    [Fact]
    public void CR04_V1_Kaynaklari_Tam_Olarak_Uc()
    {
        Assert.Equal(3, CustomReportSources.All.Count);
        Assert.Equal(
            new[] { CustomReportSources.Materials, CustomReportSources.Vehicles, CustomReportSources.DailyActivity },
            CustomReportSources.All.Select(x => x.Key).ToArray());
    }

    /// <summary>⭐ CR05 — Her kaynak geçerli bir yetki modülüne ve kategori anahtarına çözülür
    /// (kapılar meta veriden gelir, tanımdan değil).</summary>
    [Fact]
    public void CR05_Her_Kaynak_Gecerli_Modul_ve_Kategoriye_Cozulur()
    {
        var agac = AppModules.All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var src in CustomReportSources.All)
        {
            Assert.Contains(src.DataModule, agac);
            var kategoriModulu = ReportCatalog.CategoryModule(src.Category);
            Assert.StartsWith("report_", kategoriModulu, StringComparison.Ordinal);
            Assert.Contains(kategoriModulu, agac);
            Assert.NotEmpty(src.Columns);
        }
    }

    /// <summary>⭐ CR06 — TARİH SÖZLEŞMESİ (PK-CR-10=A): olay verisinde tarih zorunlu; ana veride
    /// tarih YOK ama en az bir filtre zorunlu.</summary>
    [Fact]
    public void CR06_Tarih_Sozlesmesi_Kaynak_Bazli()
    {
        var malzeme = CustomReportSources.ByKey(CustomReportSources.Materials)!;
        var arac = CustomReportSources.ByKey(CustomReportSources.Vehicles)!;
        var faaliyet = CustomReportSources.ByKey(CustomReportSources.DailyActivity)!;

        Assert.False(malzeme.RequiresDate); Assert.True(malzeme.RequiresFilter);
        Assert.False(arac.RequiresDate); Assert.True(arac.RequiresFilter);
        Assert.True(faaliyet.RequiresDate); Assert.False(faaliyet.RequiresFilter);
    }

    // ══════════════════════ BEYAZ LİSTE / SQL GÜVENLİĞİ (PK-CR-01/05=A) ══════════════════════

    /// <summary>⭐ CR07 — Bilinmeyen KAYNAK reddedilir.</summary>
    [Fact]
    public void CR07_Bilinmeyen_Kaynak_Reddedilir()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _custom.Create(_adminA, "X", "olmayan_kaynak", new[] { "code" }, null, null, false));
        Assert.Contains("Bilinmeyen rapor kaynağı", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ CR08 — Beyaz liste DIŞI kolon reddedilir (SQL'e asla ulaşmaz).</summary>
    [Fact]
    public void CR08_Beyaz_Liste_Disi_Kolon_Reddedilir()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _custom.Create(_adminA, "X", CustomReportSources.Materials,
                new[] { MaterialListColumns.Code, "gizli_kolon" }, null, null, false));
        Assert.Contains("geçersiz kolon", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ CR09 — SQL ENJEKSİYON denemeleri kolon/sıralama alanlarında REDDEDİLİR.
    /// Kullanıcı tablo adı, kolon adı, SQL ifadesi veya ORDER BY parçası GÖNDEREMEZ.</summary>
    [Theory]
    [InlineData("code; DROP TABLE materials;--")]
    [InlineData("1=1 OR code")]
    [InlineData("code) UNION SELECT password_hash FROM users--")]
    [InlineData("m.code")]
    [InlineData("(SELECT 1)")]
    [InlineData("code DESC, name")]
    public void CR09_Sql_Enjeksiyon_Denemeleri_Reddedilir(string kotu)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            _custom.Create(_adminA, "X", CustomReportSources.Materials, new[] { kotu }, null, null, false));

        Assert.ThrowsAny<ArgumentException>(() =>
            _custom.Create(_adminA, "X", CustomReportSources.Materials,
                new[] { MaterialListColumns.Code }, null, sortColumn: kotu, sortDesc: false));

        Assert.ThrowsAny<ArgumentException>(() =>
            _custom.Create(_adminA, "X", CustomReportSources.Materials,
                new[] { MaterialListColumns.Code },
                new[] { new CustomReportFilter(kotu, "değer") }, null, false));
    }

    /// <summary>⭐ CR10 — Filtre DEĞERİ serbesttir ama PARAMETRE olarak geçer: enjeksiyon metni
    /// içeren bir arama terimi tabloyu düşürmez, yalnız sonuç döndürmez.</summary>
    [Fact]
    public void CR10_Filtre_Degeri_Parametre_Olarak_Gecer()
    {
        var mats = new MaterialService(_f);
        mats.Create(_adminA, new NewMaterial("M-1", "Çimento"));

        var id = _custom.Create(_adminA, "Enjeksiyon", CustomReportSources.Materials,
            new[] { MaterialListColumns.Code, MaterialListColumns.Name },
            new[] { new CustomReportFilter(MaterialListColumns.Name, "'; DROP TABLE materials;--") },
            null, false);

        var tablo = _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek());
        Assert.Empty(tablo.Rows);                       // eşleşme yok — ama patlama da yok
        Assert.Equal(1L, TabloVar(_f, "materials"));    // tablo DURUYOR
    }

    /// <summary>⭐ CR11 — Aynı kolon iki kez seçilemez (bozuk tanım).</summary>
    [Fact]
    public void CR11_Tekrarli_Kolon_Reddedilir()
        => Assert.ThrowsAny<ArgumentException>(() =>
            _custom.Create(_adminA, "X", CustomReportSources.Materials,
                new[] { MaterialListColumns.Code, MaterialListColumns.Code }, null, null, false));

    /// <summary>⭐ CR12 — Kolonsuz tanım reddedilir.</summary>
    [Fact]
    public void CR12_Kolonsuz_Tanim_Reddedilir()
        => Assert.ThrowsAny<ArgumentException>(() =>
            _custom.Create(_adminA, "X", CustomReportSources.Materials, Array.Empty<string>(), null, null, false));

    // ══════════════════════ TARİH / FİLTRE / LİMİT (PK-CR-06/10=A) ══════════════════════

    /// <summary>⭐ CR13 — OLAY VERİSİ: tarih aralığı OLMADAN çalıştırma REDDEDİLİR.</summary>
    [Fact]
    public void CR13_Gunluk_Faaliyet_Tarihsiz_Calistirilamaz()
    {
        var id = _custom.Create(_adminA, "Faaliyet", CustomReportSources.DailyActivity,
            new[] { DailyActivityListColumns.Date, DailyActivityListColumns.Type }, null, null, false);

        var ex = Assert.Throws<ArgumentException>(() =>
            _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), new ReportRequest(true, null, null)));
        Assert.Contains("tarih aralığı zorunludur", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ CR14 — OLAY VERİSİ: ters tarih aralığı reddedilir.</summary>
    [Fact]
    public void CR14_Ters_Tarih_Araligi_Reddedilir()
    {
        var id = _custom.Create(_adminA, "Faaliyet", CustomReportSources.DailyActivity,
            new[] { DailyActivityListColumns.Date }, null, null, false);
        var ex = Assert.Throws<ArgumentException>(() =>
            _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek(from: 5000, to: 1000)));
        Assert.Contains("sonra olamaz", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ CR15 — ANA VERİ: filtre OLMADAN çalıştırma REDDEDİLİR (sınırsız sorgu engeli).</summary>
    [Fact]
    public void CR15_Malzeme_Filtresiz_Calistirilamaz()
    {
        var id = _custom.Create(_adminA, "Malzeme", CustomReportSources.Materials,
            new[] { MaterialListColumns.Code }, filters: null, null, false);

        var ex = Assert.Throws<ArgumentException>(() =>
            _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek()));
        Assert.Contains("en az bir filtre", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ CR16 — ANA VERİ: Araç kaynağı da filtresiz çalışmaz.</summary>
    [Fact]
    public void CR16_Arac_Filtresiz_Calistirilamaz()
    {
        var id = _custom.Create(_adminA, "Araç", CustomReportSources.Vehicles,
            new[] { VehicleListColumns.InternalCode }, filters: null, null, false);
        Assert.ThrowsAny<ArgumentException>(() =>
            _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek()));
    }

    /// <summary>⭐ CR17 — ANA VERİDE created_at İŞ GÜNÜ OLARAK KULLANILMAZ (PK-CR-10=A):
    /// Malzeme/Araç kaynaklarının kolon beyaz listesinde tarih alanı YOKTUR ve tarih filtresi istemezler.</summary>
    [Fact]
    public void CR17_Ana_Veride_CreatedAt_Is_Gunu_Olarak_Kullanilmaz()
    {
        foreach (var key in new[] { CustomReportSources.Materials, CustomReportSources.Vehicles })
        {
            var src = CustomReportSources.ByKey(key)!;
            Assert.False(src.RequiresDate);
            Assert.DoesNotContain(src.Columns, c =>
                c.Key.Contains("created", StringComparison.OrdinalIgnoreCase) ||
                c.Key.Contains("updated", StringComparison.OrdinalIgnoreCase) ||
                c.Key.Equals("date", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>⭐ CR18 — SATIR TAVANI: sonuç <see cref="CustomReportRules.MaxRows"/> üstüne çıkamaz
    /// ve istenen tavan aşılmaz (tavan SQL sorgusundaki LIMIT ile uygulanır).</summary>
    [Fact]
    public void CR18_Satir_Tavani_Uygulanir()
    {
        var mats = new MaterialService(_f);
        for (int i = 1; i <= 12; i++) mats.Create(_adminA, new NewMaterial($"T-{i:00}", $"Test {i}"));

        var id = _custom.Create(_adminA, "Tavan", CustomReportSources.Materials,
            new[] { MaterialListColumns.Code },
            new[] { new CustomReportFilter(MaterialListColumns.Code, "T-") }, null, false);

        var tablo = _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek(), maxRows: 5);
        Assert.Equal(5, tablo.Rows.Count);
        Assert.True(CustomReportRules.MaxRows > 0);
    }

    // ══════════════════════ GÜVENLİK KAPILARI (PK-CR-01/04=A) ══════════════════════

    /// <summary>⭐ CR19 — "reports" ÜST KAPISI olmayan kullanıcı custom raporu ÇALIŞTIRAMAZ.</summary>
    [Fact]
    public void CR19_Reports_Yetkisi_Yoksa_Calismaz()
    {
        var id = MalzemeRaporu(_adminA);
        var yetkisiz = Personel("CR-A", "report_material", "materials",
            CustomReportDefinition.PermissionKeyOf(id));
        Assert.ThrowsAny<ForbiddenException>(() =>
            _reports.Run(yetkisiz, CustomReportDefinition.KeyOf(id), Istek()));
    }

    /// <summary>⭐ CR20 — KATEGORİ yetkisi (ADR-181) olmayan kullanıcı custom raporu çalıştıramaz.</summary>
    [Fact]
    public void CR20_Kategori_Yetkisi_Yoksa_Calismaz()
    {
        var id = MalzemeRaporu(_adminA);
        var yetkisiz = Personel("CR-A", "reports", "materials",
            CustomReportDefinition.PermissionKeyOf(id));   // report_material YOK
        Assert.ThrowsAny<ForbiddenException>(() =>
            _reports.Run(yetkisiz, CustomReportDefinition.KeyOf(id), Istek()));
    }

    /// <summary>⭐ CR21 — DİNAMİK RAPOR ANAHTARI (PK-CR-04=A) olmayan kullanıcı çalıştıramaz;
    /// anahtar verilince çalışır. Deny-by-default korunur.</summary>
    [Fact]
    public void CR21_Dinamik_Rapor_Anahtari_Zorunlu()
    {
        var mats = new MaterialService(_f);
        mats.Create(_adminA, new NewMaterial("M-9", "Çimento"));
        var id = MalzemeRaporu(_adminA);

        var anahtarsiz = Personel("CR-A", "reports", "report_material", "materials");
        Assert.ThrowsAny<ForbiddenException>(() =>
            _reports.Run(anahtarsiz, CustomReportDefinition.KeyOf(id), Istek()));

        var anahtarli = Personel("CR-A", "reports", "report_material", "materials",
            CustomReportDefinition.PermissionKeyOf(id));
        var tablo = _reports.Run(anahtarli, CustomReportDefinition.KeyOf(id), Istek());
        Assert.Single(tablo.Rows);
    }

    /// <summary>⭐ CR22 — VERİ MODÜLÜ kapısı (RPR-15): kaynağın ekranı role KAPATILMIŞSA custom rapor
    /// da çalışmaz (rapor yolu kapatmayı DELMEZ).</summary>
    [Fact]
    public void CR22_Kapatilmis_Veri_Modulu_Custom_Raporu_Da_Kapatir()
    {
        var id = MalzemeRaporu(_adminA);
        var kapali = new SessionContext("u-blok", "CR-A", new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_material", true, false, false, false),
                new ModulePermission("materials", true, false, false, false),
                new ModulePermission(CustomReportDefinition.PermissionKeyOf(id), true, false, false, false),
            }))
        {
            // "Rol Yetki Kontrol" ile Malzemeler ekranı bu role KAPATILMIŞ.
            BlockedModules = new HashSet<string>(StringComparer.Ordinal) { "materials" },
        };

        var ex = Assert.ThrowsAny<ForbiddenException>(() =>
            _reports.Run(kapali, CustomReportDefinition.KeyOf(id), Istek()));
        Assert.Contains("kapatılmıştır", ex.Message, StringComparison.Ordinal);
    }

    // ══════════════════════ TENANT / GEÇERSİZ TANIM ══════════════════════

    /// <summary>⭐ CR23 — TENANT İZOLASYONU: B firması, A firmasının rapor tanımını ÇALIŞTIRAMAZ
    /// ve listede GÖRMEZ.</summary>
    [Fact]
    public void CR23_Baska_Firmanin_Tanimi_Calistirilamaz()
    {
        var id = MalzemeRaporu(_adminA);

        Assert.Null(_custom.ById(_adminB, id));
        Assert.Empty(_custom.List(_adminB));

        var ex = Assert.Throws<ArgumentException>(() =>
            _reports.Run(_adminB, CustomReportDefinition.KeyOf(id), Istek()));
        Assert.Contains("Bilinmeyen rapor tipi", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ CR24 — Bilinmeyen custom anahtar, mevcut güvenlik davranışını korur
    /// (istisna ile kapı atlatılamaz).</summary>
    [Fact]
    public void CR24_Bilinmeyen_Custom_Anahtar_Reddedilir()
        => Assert.Throws<ArgumentException>(() =>
            _reports.Run(_adminA, CustomReportDefinition.KeyOf("olmayan-id"), Istek()));

    /// <summary>⭐ CR25 — PASİF tanım çalıştırılamaz.</summary>
    [Fact]
    public void CR25_Pasif_Tanim_Calistirilamaz()
    {
        var id = MalzemeRaporu(_adminA);
        _custom.Update(_adminA, id, "Malzeme Raporu", CustomReportSources.Materials,
            new[] { MaterialListColumns.Code }, new[] { new CustomReportFilter(MaterialListColumns.Code, "M") },
            null, false, isActive: false);

        Assert.Throws<ArgumentException>(() => _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek()));
    }

    /// <summary>⭐ CR26 — BOZUK JSON (elle düzenlenmiş tanım) istisna ile kapı atlatamaz:
    /// tanım kolonsuz kalır ve çalıştırma REDDEDİLİR.</summary>
    [Fact]
    public void CR26_Bozuk_Tanim_Guvenli_Reddedilir()
    {
        var id = MalzemeRaporu(_adminA);
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE custom_report_defs SET columns_json='{bozuk json' WHERE id=@i;";
            cmd.AddWithValue("@i", id);
            cmd.ExecuteNonQuery();
        }
        var ex = Assert.Throws<ArgumentException>(() => _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek()));
        Assert.Contains("En az bir kolon", ex.Message, StringComparison.Ordinal);
    }

    // ══════════════════════ ÇALIŞMA + REGRESYON ══════════════════════

    /// <summary>⭐ CR27 — Custom rapor mevcut <see cref="TableModel"/> döndürür: başlıklar seçilen
    /// kolonların Türkçe etiketleridir, satırlar yalnız o kolonları içerir (ikinci tablo modeli YOK).</summary>
    [Fact]
    public void CR27_TableModel_Projeksiyonu_Dogru()
    {
        var mats = new MaterialService(_f);
        mats.Create(_adminA, new NewMaterial("M-100", "Çimento"));

        var id = _custom.Create(_adminA, "Kolon Testi", CustomReportSources.Materials,
            new[] { MaterialListColumns.Name, MaterialListColumns.Code },   // SIRA kullanıcının seçtiği gibi
            new[] { new CustomReportFilter(MaterialListColumns.Code, "M-100") }, null, false);

        var tablo = _reports.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek());

        Assert.Equal("Kolon Testi", tablo.Title);
        Assert.Equal(new[] { "Ad", "Kod" }, tablo.Headers.ToArray());
        var satir = Assert.Single(tablo.Rows);
        Assert.Equal("Çimento", satir[0]);
        Assert.Equal("M-100", satir[1]);
    }

    /// <summary>⭐ CR28 — MEVCUT SABİT RAPORLAR BOZULMADI (25 → 26: 2026-09-03 daily-activity-summary,
    /// bilinçli ekleme): katalog sayısı ve anahtarları aynı,
    /// custom anahtar öneki sabit raporlarla ÇAKIŞMIYOR.</summary>
    [Fact]
    public void CR28_Mevcut_Sabit_Raporlar_Bozulmadi()
    {
        Assert.Equal(26, ReportCatalog.All.Count);
        Assert.DoesNotContain(ReportCatalog.All, d => d.Key.StartsWith(CustomReportDefinition.KeyPrefix, StringComparison.Ordinal));
        foreach (var d in ReportCatalog.All)
            Assert.Null(CustomReportDefinition.IdFromKey(d.Key));

        // Sabit rapor hâlâ normal çalışıyor (dispatch bozulmadı).
        var tablo = _reports.Run(_adminA, "stock", Istek());
        Assert.NotNull(tablo);
    }

    /// <summary>⭐ CR29 — Bağlayıcı YOKSA (eski davranış) custom anahtar "bilinmeyen rapor"dur;
    /// sabit raporlar etkilenmez → geriye uyumluluk.</summary>
    [Fact]
    public void CR29_Baglayici_Yoksa_Eski_Davranis()
    {
        var baglayicisiz = new ReportService(_f);   // Custom = null
        var id = MalzemeRaporu(_adminA);
        Assert.Throws<ArgumentException>(() => baglayicisiz.Run(_adminA, CustomReportDefinition.KeyOf(id), Istek()));
        Assert.NotNull(baglayicisiz.Run(_adminA, "stock", Istek()));
    }

    /// <summary>⭐ CR30 — CRUD: oluştur → listele → güncelle → yumuşak sil (fiziksel silme YOK).</summary>
    [Fact]
    public void CR30_Crud_Yumusak_Silme()
    {
        var id = MalzemeRaporu(_adminA);
        Assert.Single(_custom.List(_adminA));

        _custom.Update(_adminA, id, "Yeni Ad", CustomReportSources.Materials,
            new[] { MaterialListColumns.Code }, new[] { new CustomReportFilter(MaterialListColumns.Code, "M") },
            null, false, isActive: true);
        Assert.Equal("Yeni Ad", _custom.ById(_adminA, id)!.Name);

        _custom.Delete(_adminA, id);
        Assert.Empty(_custom.List(_adminA));
        Assert.Null(_custom.ById(_adminA, id));

        // Satır FİZİKSEL olarak duruyor (yumuşak silme)
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_deleted FROM custom_report_defs WHERE id=@i;";
        cmd.AddWithValue("@i", id);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ══════════════════════ SENKRON (PK-CR-02=A) ══════════════════════

    /// <summary>⭐ CR31 — Tanım tablosu senkron listesinde ve push yetki haritasında KAYITLI
    /// (masaüstü çevrimdışı çalışabilsin diye).</summary>
    [Fact]
    public void CR31_Senkron_Listesinde_ve_Yetki_Haritasinda_Kayitli()
    {
        Assert.Contains("custom_report_defs", DepoWise.Infrastructure.Sync.BusinessSyncService.Tables);
        Assert.Equal("reports", DepoWise.Infrastructure.Sync.BusinessSyncService.ModuleOf("custom_report_defs"));
    }

    /// <summary>⭐ CR32 — SENKRON UÇTAN UCA: masaüstünde (kaynak DB) tanımlanan custom rapor,
    /// snapshot ile sunucuya taşınır → çevrimdışı makinede oluşturulan tanım kaybolmaz.</summary>
    [Fact]
    public void CR32_Tanim_Senkronla_Tasinir()
    {
        var hedefYol = Path.Combine(Path.GetTempPath(), "dw_crsync_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var hedef = new SqliteConnectionFactory(hedefYol);
            new MigrationRunner(hedef).Run();
            using (var conn = hedef.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('CR-A','CR-A',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }

            var id = MalzemeRaporu(_adminA);

            var snapshot = new DepoWise.Infrastructure.Sync.BusinessSyncService(_f).BuildSnapshot("CR-A");
            using var doc = System.Text.Json.JsonDocument.Parse(snapshot);
            new DepoWise.Infrastructure.Sync.BusinessSyncService(hedef).ApplyPull("CR-A", doc.RootElement);

            using var conn2 = hedef.Create();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT name, source_key FROM custom_report_defs WHERE id=@i;";
            cmd2.AddWithValue("@i", id);
            using var r = cmd2.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("Malzeme Raporu", r.GetString(0));
            Assert.Equal(CustomReportSources.Materials, r.GetString(1));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(hedefYol); } catch { }
        }
    }

    /// <summary>⭐ CR33 — ESKİ İSTEMCİ REGRESYONU: yeni tablo senkron listesine eklendi diye,
    /// tabloyu tanımayan (şema 82) bir alıcının senkronu BOZULMAZ; diğer tablolar uygulanır.</summary>
    [Fact]
    public void CR33_Eski_Istemci_Yeni_Tabloyu_Bilmese_de_Senkron_Bozulmaz()
    {
        var eskiYol = Path.Combine(Path.GetTempPath(), "dw_cr82c_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var eski = new SqliteConnectionFactory(eskiYol);
            new MigrationRunner(eski, MigrationCatalog.All().Where(m => m.Version <= 82)).Run();
            using (var conn = eski.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('CR-A','CR-A',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }
            Assert.Equal(0L, TabloVar(eski, "custom_report_defs"));   // eski istemci tabloyu BİLMİYOR

            MalzemeRaporu(_adminA);
            new MaterialService(_f).Create(_adminA, new NewMaterial("SNK-1", "Çimento"));

            var snapshot = new DepoWise.Infrastructure.Sync.BusinessSyncService(_f).BuildSnapshot("CR-A");
            using var doc = System.Text.Json.JsonDocument.Parse(snapshot);

            // İSTİSNA YOK + bilinen tablolar uygulanır + bilinmeyen tablo yerelde OLUŞTURULMAZ
            var sonuc = new DepoWise.Infrastructure.Sync.BusinessSyncService(eski).ApplyPull("CR-A", doc.RootElement);
            Assert.DoesNotContain(sonuc.Errors, e => e.Contains("custom_report_defs", StringComparison.Ordinal));
            Assert.Equal(0L, TabloVar(eski, "custom_report_defs"));

            using var conn2 = eski.Create();
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM materials WHERE code='SNK-1';";
            Assert.Equal(1L, Convert.ToInt64(cmd2.ExecuteScalar()));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(eskiYol); } catch { }
        }
    }
}
