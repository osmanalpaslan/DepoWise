using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G2 (2026-08-18) — RAPORLARDA ŞUBE KAPSAMI.
///
/// <b>DEN-E2</b> — "Stok Durumu" raporu şube kapsamını HİÇ uygulamıyordu:
/// <c>NormalizeLocations(req.LocationIds)</c> istekten geleni AYNEN alıyordu. İki sonuç vardı:
/// (a) filtre boşken FİRMA GENELİ toplam dönüyor, şubeyle sınırlı kullanıcı tüm firmanın stoğunu
/// görüyordu; (b) istek gövdesine BAŞKA şubenin depo kimliği yazılırsa o deponun stoğu dönüyordu
/// (parametre manipülasyonu — fail-open). Kardeş rapor <c>StockMovements</c> aynı işi doğru yapıyordu.
///
/// <b>DEN-E1</b> — "Şube Bazlı Özet" raporu tüm şubelerin adlarını ve kayıt sayılarını gösteriyordu.
/// </summary>
public class ReportBranchScopeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ReportService _reports;
    private readonly BranchService _branches;
    private readonly SessionContext _admin;
    private readonly string _subeA, _subeB;
    private const string Co = "RPT-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public ReportBranchScopeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rptscope_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);

        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _subeA = _branches.Create(_admin, new NewBranch("ŞUBE A"));
        _subeB = _branches.Create(_admin, new NewBranch("ŞUBE B"));

        // İki malzeme, iki şubede bakiye.
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Çimento',NULL,'0',1,1,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M2','{Co}','K2','Demir',NULL,'0',1,1,1,0);");
        Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('{Co}','M1','{_subeA}','100',1);");
        Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('{Co}','M2','{_subeB}','250',1);");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Yalnız ŞUBE A'ya yetkili personel (admin bypass YOK).</summary>
    private SessionContext SadeceA() => new("kul", Co, new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>()))
    { ScopeBranchIds = new[] { _subeA } };

    private static decimal Toplam(TableModel t, int kolon)
    {
        decimal s = 0;
        foreach (var r in t.Rows) s += Money.Parse(Convert.ToString(r[kolon]));
        return s;
    }

    // ── DEN-E2 ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>Filtre BOŞKEN bile kapsam uygulanmalı — eskiden firma geneli toplam dönüyordu.</summary>
    [Fact]
    public void StokDurumu_FiltresizKen_Yalniz_Izinli_Subeyi_Toplar()
    {
        var t = _reports.StockStatus(SadeceA(), new ReportRequest(Executed: true));

        // M1 (ŞUBE A) = 100 görünmeli, M2 (ŞUBE B) = 250 GÖRÜNMEMELİ → toplam 100.
        Assert.Equal(100m, Toplam(t, 2));
    }

    /// <summary>Admin/sınırsız kullanıcıda eski davranış BOZULMAMALI (firma geneli).</summary>
    [Fact]
    public void StokDurumu_Sinirsiz_Kullanicida_Firma_Geneli_Kalir()
    {
        var t = _reports.StockStatus(_admin, new ReportRequest(Executed: true));
        Assert.Equal(350m, Toplam(t, 2));   // 100 + 250
    }

    /// <summary>⭐ Parametre manipülasyonu: kapsam dışı depo istenirse veri SIZDIRILMAMALI.</summary>
    [Fact]
    public void StokDurumu_Kapsam_Disi_Depo_Istenirse_BOS_Doner()
    {
        var t = _reports.StockStatus(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeB }));

        Assert.Empty(t.Rows);
    }

    /// <summary>Kendi şubesini açıkça seçmek ÇALIŞMAYA devam etmeli.</summary>
    [Fact]
    public void StokDurumu_Kendi_Subesini_Secmek_Calisir()
    {
        var t = _reports.StockStatus(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeA }));

        Assert.Single(t.Rows);
        Assert.Equal("K1", t.Rows[0][0]);
    }

    /// <summary>Karışık istek: izinli olan gelir, olmayan düşer (sessiz genişleme YOK).</summary>
    [Fact]
    public void StokDurumu_Karisik_Istekte_Yalniz_Izinli_Gelir()
    {
        var t = _reports.StockStatus(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeA, _subeB }));

        Assert.Single(t.Rows);
        Assert.Equal("K1", t.Rows[0][0]);
    }

    // ── DEN-E1 ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SubeBazliOzet_Yalniz_Izinli_Subeleri_Listeler()
    {
        var t = _reports.StatusReport(SadeceA(), new ReportRequest(Executed: true));

        var subeAdlari = t.Rows.Select(r => Convert.ToString(r[0]) ?? "").Distinct().ToList();
        Assert.Contains("ŞUBE A", subeAdlari);
        Assert.DoesNotContain("ŞUBE B", subeAdlari);   // ⭐ kapsam dışı şubenin ADI bile görünmemeli
    }

    [Fact]
    public void SubeBazliOzet_Sinirsiz_Kullanicida_Tum_Subeler_Gorunur()
    {
        var t = _reports.StatusReport(_admin, new ReportRequest(Executed: true));

        var subeAdlari = t.Rows.Select(r => Convert.ToString(r[0]) ?? "").Distinct().ToList();
        Assert.Contains("ŞUBE A", subeAdlari);
        Assert.Contains("ŞUBE B", subeAdlari);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  DEN-2026-08-25 — AYNI AÇIĞIN KALAN ÜÇ RAPORU
    //
    //  DEN-E1/E2 turunda Stok Durumu ve Şube Bazlı Özet düzeltilmişti; uçtan uca denetimde AYNI
    //  eksiğin üç raporda daha durduğu görüldü:
    //   • RPR-01 "Araç — Şablonlu"      → şube kolonu GÖSTERİYOR ama kapsam UYGULAMIYORDU
    //   • RPR-02 "Araç — Şablon Dışı"   → aynısı
    //   • RPR-03 "Stok Sayım"           → req.LocationIds AYNEN kullanılıyordu (fail-open)
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    private string Arac(string kod, string? subeId)
        => AracEkle(kod, subeId);

    private string AracEkle(string kod, string? subeId)
    {
        var id = "V-" + kod;
        Sql($"INSERT INTO vehicles(id,company_id,internal_code,plate,branch_id,status,template_id," +
            $"created_at,updated_at,version,is_deleted) VALUES('{id}','{Co}','{kod}','{kod}-PLK'," +
            (subeId is null ? "NULL" : $"'{subeId}'") + ",'active',NULL,1,1,1,0);");
        return id;
    }

    /// <summary>⭐ RPR-01 — şablon dışı araç raporu yalnız izinli şubenin araçlarını göstermeli.</summary>
    [Fact]
    public void AracSablonDisi_Yalniz_Izinli_Subenin_Araclari()
    {
        AracEkle("AA", _subeA);
        AracEkle("BB", _subeB);

        var t = _reports.VehiclesNonTemplate(SadeceA(), new ReportRequest(Executed: true));

        var kodlar = t.Rows.Select(r => Convert.ToString(r[0]) ?? "").ToList();
        Assert.Contains("AA", kodlar);
        Assert.DoesNotContain("BB", kodlar);      // ⭐ kapsam dışı aracın PLAKASI bile sızmamalı
    }

    /// <summary>RPR-01b — sınırsız kullanıcıda eski davranış korunur (tüm araçlar).</summary>
    [Fact]
    public void AracSablonDisi_Sinirsiz_Kullanicida_Tum_Araclar()
    {
        AracEkle("AA", _subeA);
        AracEkle("BB", _subeB);

        var t = _reports.VehiclesNonTemplate(_admin, new ReportRequest(Executed: true));

        Assert.Equal(2, t.Rows.Count);
    }

    /// <summary>
    /// ⭐ RPR-01d — <b>SÖZLEŞME KORUMASI:</b> araç raporları YÖNETİCİ raporudur; oturumun ÇALIŞMA şubesi
    /// (giriş ekranında seçilen şube) bu raporu DARALTMAZ — "Şube 2 ile giriş yapılsa bile tüm şubeler
    /// görünür" (ürün kararı, BranchScopeTests ile de kilitli).
    ///
    /// Bu test denetim sırasında GERÇEKTEN kırıldı: ilk düzeltme yanlışlıkla <c>ReportScope.BranchSql</c>
    /// kullanmış (izinli ∩ OTURUM) ve çalışan bir davranışı bozmuştu. Doğrusu <c>BranchAccess.AllowedSql</c>
    /// — yani YETKİ uygulanır, görünüm tercihi uygulanmaz. İki yön de ayrı testle kilitlidir.
    /// </summary>
    [Fact]
    public void AracRaporu_Calisma_Subesi_Daraltmaz()
    {
        AracEkle("AA", _subeA);
        AracEkle("BB", _subeB);

        // Yetkisi sınırsız (admin) ama ŞUBE B ile çalışıyor → yine de İKİ araç da görünmeli.
        var subeIleCalisan = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _subeB };

        var t = _reports.VehiclesNonTemplate(subeIleCalisan, new ReportRequest(Executed: true));

        Assert.Equal(2, t.Rows.Count);
    }

    /// <summary>RPR-01c — ŞUBESİZ (firma geneli) araç kapsam filtresinde GİZLENMEZ.</summary>
    [Fact]
    public void AracSablonDisi_Subesiz_Arac_Gizlenmez()
    {
        AracEkle("CC", null);

        var t = _reports.VehiclesNonTemplate(SadeceA(), new ReportRequest(Executed: true));

        Assert.Contains("CC", t.Rows.Select(r => Convert.ToString(r[0]) ?? ""));
    }

    /// <summary>⭐ RPR-02 — şablonlu araç raporu da aynı kapsamı uygulamalı.</summary>
    [Fact]
    public void AracSablonlu_Yalniz_Izinli_Subenin_Araclari()
    {
        Sql($"INSERT INTO vehicle_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
            $"VALUES('TPL','{Co}','Kamyon',1,1,1,0);");
        AracEkle("AA", _subeA);
        AracEkle("BB", _subeB);
        Sql("UPDATE vehicles SET template_id='TPL';");

        var t = _reports.VehiclesByTemplate(SadeceA(), new ReportRequest(Executed: true));

        var kodlar = t.Rows.Select(r => Convert.ToString(r[1]) ?? "").ToList();
        Assert.Contains("AA", kodlar);
        Assert.DoesNotContain("BB", kodlar);
    }

    // ── RPR-03 · Stok Sayım ─────────────────────────────────────────────────────────────────────
    private void SayimEkle(string docId, string subeId, string materialId)
    {
        Sql($"INSERT INTO stock_documents(id,company_id,doc_type,doc_no,doc_date,to_branch_id,status," +
            $"created_at,updated_at,version,is_deleted) VALUES('{docId}','{Co}','count','{docId}',1,'{subeId}'," +
            $"'posted',1,1,1,0);");
        Sql($"INSERT INTO stock_count_lines(id,document_id,material_id,system_qty,counted_qty,diff_qty) " +
            $"VALUES('{docId}-L','{docId}','{materialId}','10','12','2');");
    }

    /// <summary>⭐ RPR-03 — sayım raporu filtresizken bile kapsam uygulamalı.</summary>
    [Fact]
    public void StokSayim_Filtresizken_Yalniz_Izinli_Sube()
    {
        SayimEkle("D1", _subeA, "M1");
        SayimEkle("D2", _subeB, "M2");

        var t = _reports.StockCount(SadeceA(), new ReportRequest(Executed: true));

        // Kolon sırası: Tarih · Sayılan Depo · Kod · Malzeme … → malzeme kodu 2. sıradadır.
        var kodlar = t.Rows.Select(r => Convert.ToString(r[2]) ?? "").ToList();
        Assert.Contains("K1", kodlar);
        Assert.DoesNotContain("K2", kodlar);
    }

    /// <summary>⭐ RPR-03b — parametre manipülasyonu: kapsam dışı depo istenirse veri SIZDIRILMAMALI.</summary>
    [Fact]
    public void StokSayim_Kapsam_Disi_Depo_Istenirse_BOS_Doner()
    {
        SayimEkle("D1", _subeA, "M1");
        SayimEkle("D2", _subeB, "M2");

        var t = _reports.StockCount(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeB }));

        Assert.Empty(t.Rows);
    }

    /// <summary>RPR-03c — sınırsız kullanıcıda eski davranış korunur.</summary>
    [Fact]
    public void StokSayim_Sinirsiz_Kullanicida_Tum_Subeler()
    {
        SayimEkle("D1", _subeA, "M1");
        SayimEkle("D2", _subeB, "M2");

        var t = _reports.StockCount(_admin, new ReportRequest(Executed: true));

        Assert.Equal(2, t.Rows.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  RPR-04 (2026-08-25) — RAPOR FİLTRESİ SEÇENEKLERİ DE KAPSAMLI OLMALI
    //
    //  Rapor SONUÇLARI kapsamlıydı ama FİLTRE açılır listeleri değildi: tek şubeye yetkili kullanıcı
    //  firmanın bütün araç PLAKALARINI ve personel ADLARINI görüyordu. Kural artık servistedir →
    //  web ve masaüstü AYNI metodu çağırır (parite testle kilitli).
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ RPR-04a — kapsamlı kullanıcı yalnız izinli şubenin aracını görür.</summary>
    [Fact]
    public void RPR04a_Arac_Filtresi_Kapsamli()
    {
        AracEkle("AA", _subeA);
        AracEkle("BB", _subeB);

        var veh = new DepoWise.Infrastructure.Vehicles.VehicleService(_factory, _clock);
        var kodlar = veh.ListForReportFilter(SadeceA()).Select(v => v.InternalCode).ToList();

        Assert.Contains("AA", kodlar);
        Assert.DoesNotContain("BB", kodlar);
    }

    /// <summary>RPR-04b — ŞUBESİZ araç gizlenmez (sistem geneli ilke: şubesiz kayıt herkese görünür).</summary>
    [Fact]
    public void RPR04b_Subesiz_Arac_Gizlenmez()
    {
        AracEkle("CC", null);

        var veh = new DepoWise.Infrastructure.Vehicles.VehicleService(_factory, _clock);
        Assert.Contains("CC", veh.ListForReportFilter(SadeceA()).Select(v => v.InternalCode));
    }

    /// <summary>RPR-04c — kapsamsız kullanıcıda (admin) davranış DEĞİŞMEZ: hepsini görür.</summary>
    [Fact]
    public void RPR04c_Admin_Tum_Araclari_Gorur()
    {
        AracEkle("AA", _subeA);
        AracEkle("BB", _subeB);

        var veh = new DepoWise.Infrastructure.Vehicles.VehicleService(_factory, _clock);
        Assert.Equal(2, veh.ListForReportFilter(_admin).Count);
    }

    /// <summary>RPR-04d — PERSONEL filtresi de kapsamlı; şubesiz personel gizlenmez.</summary>
    [Fact]
    public void RPR04d_Personel_Filtresi_Kapsamli()
    {
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_factory, new DepoWise.Infrastructure.Org.ScopeResolver(_factory), _clock);
        per.Create(_admin, new DepoWise.Infrastructure.Org.NewPersonnel("Ali Bir", null, null, _subeA));
        per.Create(_admin, new DepoWise.Infrastructure.Org.NewPersonnel("Veli İki", null, null, _subeB));
        per.Create(_admin, new DepoWise.Infrastructure.Org.NewPersonnel("Şubesiz Kişi", null, null, null));

        var lk = new DepoWise.Infrastructure.Materials.LookupService(_factory, _clock);
        var adlar = lk.ListPersonnelForReportFilter(SadeceA()).Select(p => p.Name).ToList();

        Assert.Contains("Ali Bir", adlar);
        Assert.Contains("Şubesiz Kişi", adlar);
        Assert.DoesNotContain("Veli İki", adlar);
    }

    /// <summary>
    /// ⭐ RPR-04e — PARİTE: masaüstü rapor ekranı da ORTAK metotları çağırmalı. Aksi hâlde web'de
    /// kırpılan liste masaüstünde açık kalırdı (bu turda bulunan ayrışmanın kendisi).
    /// </summary>
    [Fact]
    public void RPR04e_Masaustu_Ortak_Filtre_Metotlarini_Kullanir()
    {
        var dir = AppContext.BaseDirectory;
        for (int k = 0; k < 8 && dir is not null; k++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) break;
            dir = Directory.GetParent(dir)?.FullName;
        }
        var src = File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs"));

        Assert.Contains("Vehicles.ListForReportFilter(_session)", src);
        Assert.Contains("Lookups.ListPersonnelForReportFilter(_session)", src);
        // Rapor filtresinde firma-geneli listeler KALMAMALI.
        Assert.DoesNotContain("Vehicles.List(_session)", src);
        Assert.DoesNotContain("Lookups.ListPersonnel(_session)", src);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
