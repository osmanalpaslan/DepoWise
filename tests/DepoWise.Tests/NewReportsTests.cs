using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RPR-10 / RPR-11 · EKSİK İKİ RAPOR (denetim 2026-08-26) ═══
///
/// Kataloğun 19 raporu vardı ama iki ekranın rapor karşılığı YOKTU: <b>Muayene/Sigorta</b> ve
/// <b>Personel</b>. İkisinin de veri modeli, servisi ve ekranı zaten mevcuttu — yani iş kuralı
/// UYDURULMADI, kolonlar ve durum eşiği mevcut ekranlardan alındı.
///
/// Bu testler raporların yalnız "çalıştığını" değil, <b>doğru veriyi doğru kapsamda</b> verdiğini
/// sınar: firma izolasyonu · şube kapsamı · çalışma şubesi · tarih filtresi · boş sonuç · durum kuralı.
/// </summary>
public class NewReportsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ReportService _reports;
    private readonly BranchService _branches;
    private readonly SessionContext _admin;
    private readonly string _subeA, _subeB;
    private const string Co = "NR-CO";
    private const string Yabanci = "NR-YABANCI";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private long Now => _clock.UtcNow.ToUnixTimeMilliseconds();
    private const long Gun = 86_400_000L;

    public NewReportsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_newrep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);

        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','Bizim',1,1,1,0);");
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Yabanci}','Yabancı',1,1,1,0);");
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _subeA = _branches.Create(_admin, new NewBranch("ŞUBE A"));
        _subeB = _branches.Create(_admin, new NewBranch("ŞUBE B"));

        // ── Araçlar: A'da 34ABC, B'de 06XYZ; ayrıca BAŞKA FİRMADA bir araç.
        Arac("V1", Co, _subeA, "AR-001", "34ABC");
        Arac("V2", Co, _subeB, "AR-002", "06XYZ");
        Arac("VX", Yabanci, null, "YB-001", "99ZZZ");

        // ── Muayene/sigorta belgeleri: süresi geçmiş · yaklaşan · normal · tarihi olmayan.
        Belge("I1", Co, "V1", "inspection", Now - 400 * Gun, Now - 10 * Gun, "TÜVTÜRK", "Geçti");
        Belge("I2", Co, "V1", "insurance", Now - 350 * Gun, Now + 10 * Gun, "Acente", "");
        Belge("I3", Co, "V2", "kasko", Now - 300 * Gun, Now + 200 * Gun, "Acente", "");
        Belge("I4", Co, "V2", "calibration", null, null, "", "");
        Belge("IX", Yabanci, "VX", "inspection", Now, Now + 5 * Gun, "Yabancı", "");
        // İPTAL edilmiş belge (is_deleted=1) → hiçbir kapsamda görünmemeli.
        Belge("I5", Co, "V1", "inspection", Now, Now + 3 * Gun, "İptal", "", silindi: true);

        // ── Personel: A'da hesaplı admin, B'de saha personeli, A'da hesapsız; silinmiş bir kayıt.
        Personel("P1", Co, _subeA, "Ali Yılmaz", "Depo Sorumlusu", "0555");
        Personel("P2", Co, _subeB, "Ayşe Demir", "Şantiye Şefi", "0666", saha: true);
        Personel("P3", Co, _subeA, "Mehmet Kaya", "Operatör", "0777");
        Personel("P4", Co, _subeA, "Silinmiş Kişi", "", "", silindi: true);
        Personel("PX", Yabanci, null, "Yabancı Kişi", "", "");
        // P1'e bağlı FİRMA ADMİNİ hesabı.
        KullaniciAdmin("U1", Co, "aliy", "P1");
    }

    // ── kurulum yardımcıları ────────────────────────────────────────────────────────────────────
    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Arac(string id, string co, string? sube, string kod, string plaka)
        => Sql($"INSERT INTO vehicles(id,company_id,branch_id,internal_code,plate,status,created_at,updated_at,version,is_deleted) " +
               $"VALUES('{id}','{co}',{(sube is null ? "NULL" : $"'{sube}'")},'{kod}','{plaka}','active',1,1,1,0);");

    private void Belge(string id, string co, string arac, string tur, long? son, long? sonraki, string yer, string sonuc, bool silindi = false)
        => Sql($"INSERT INTO vehicle_inspections(id,company_id,vehicle_id,doc_type,last_date,next_date,result,place,created_at,updated_at,version,is_deleted) " +
               $"VALUES('{id}','{co}','{arac}','{tur}',{son?.ToString() ?? "NULL"},{sonraki?.ToString() ?? "NULL"},'{sonuc}','{yer}',1,1,1,{(silindi ? 1 : 0)});");

    private void Personel(string id, string co, string? sube, string ad, string unvan, string tel, bool saha = false, bool silindi = false)
        => Sql($"INSERT INTO personnel(id,company_id,branch_id,full_name,title,phone,is_active,is_field_staff,created_at,updated_at,version,is_deleted) " +
               $"VALUES('{id}','{co}',{(sube is null ? "NULL" : $"'{sube}'")},'{ad}','{unvan}','{tel}',1,{(saha ? 1 : 0)},1,1,1,{(silindi ? 1 : 0)});");

    private void KullaniciAdmin(string id, string co, string kadi, string personel)
    {
        Sql($"INSERT INTO users(id,company_id,username,password_hash,full_name,is_active,personnel_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('{id}','{co}','{kadi}','x','X',1,'{personel}',1,1,1,0);");
        Sql($"INSERT INTO roles(id,company_id,role_key,name,created_at,updated_at,version,is_deleted) " +
            $"VALUES('R-{id}','{co}','{RoleKeys.CompanyAdmin}','Firma Admini',1,1,1,0);");
        Sql($"INSERT INTO user_roles(user_id,role_id) VALUES('{id}','R-{id}');");
    }

    /// <summary>Yalnız ŞUBE A'ya yetkili operasyon kullanıcısı (admin bypass YOK).
    /// RPR-12: iki yeni rapor kendi EKRAN iznini de istediği için o izinler de verilir.</summary>
    private SessionContext SadeceA(string? girisSube = null) => new("kul", Co, new[] { RoleKeys.Staff },
        new PermissionSet(new[]
        {
            new ModulePermission("reports", true, false, false, false),
            new ModulePermission("inspection", true, false, false, false),
            new ModulePermission("personnel", true, false, false, false),
        }, Array.Empty<string>()))
    { ScopeBranchIds = new[] { _subeA }, OperatingBranchId = girisSube };

    /// <summary>YALNIZ "reports" izni olan kullanıcı — RPR-12 kapısını ölçmek için.</summary>
    private SessionContext YalnizRapor() => new("kul3", Co, new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>()))
    { ScopeBranchIds = new[] { _subeA } };

    private static List<string> Kolon(TableModel t, int i)
        => t.Rows.Select(r => Convert.ToString(r[i]) ?? "").ToList();

    // ═══════════════ RPR-10 · MUAYENE / SİGORTA ═══════════════════════════════════════════════

    [Fact]
    public void RPR10_Kolonlar_Ekranla_Ayni()
    {
        var t = _reports.Inspections(_admin, new ReportRequest(Executed: true));
        Assert.Equal(new[] { "Şube", "Araç", "Belge", "Son Tarih", "Sonraki Tarih", "Kalan Gün", "Yer", "Sonuç", "Durum" },
                     t.Headers);
    }

    [Fact]
    public void RPR10_Durum_Kurali_Ekranla_Ayni()
    {
        var t = _reports.Inspections(_admin, new ReportRequest(Executed: true));
        var arac = Kolon(t, 1);
        var belge = Kolon(t, 2);
        var durum = Kolon(t, 8);

        // I1: sonraki tarih 10 gün ÖNCE → süresi geçti
        var i1 = belge.IndexOf("Muayene");
        Assert.True(i1 >= 0);
        Assert.Equal("Süresi geçti", durum[i1]);

        // I2: 10 gün SONRA (30 günden az) → yaklaşıyor
        var i2 = belge.IndexOf("Sigorta");
        Assert.Equal("Yaklaşıyor", durum[i2]);

        // I3: 200 gün sonra → normal
        var i3 = belge.IndexOf("Kasko");
        Assert.Equal("Normal", durum[i3]);

        // I4: tarih yok → normal + kalan gün boş
        var i4 = belge.IndexOf("Kalibrasyon");
        Assert.Equal("Normal", durum[i4]);
        Assert.Equal("", Kolon(t, 5)[i4]);

        // Araç metni ekranla aynı: "kod - plaka"
        Assert.Contains("AR-001 - 34ABC", arac);
    }

    [Fact]
    public void RPR10_Iptal_Edilen_Belge_Listelenmez()
    {
        var t = _reports.Inspections(_admin, new ReportRequest(Executed: true));
        Assert.DoesNotContain("İptal", Kolon(t, 6));
    }

    [Fact]
    public void RPR10_Baska_Firmanin_Belgesi_Gelmez()
    {
        var t = _reports.Inspections(_admin, new ReportRequest(Executed: true));
        Assert.DoesNotContain("YB-001 - 99ZZZ", Kolon(t, 1));
        Assert.DoesNotContain("Yabancı", Kolon(t, 6));
    }

    /// <summary>⭐ Süper admin BAŞKA firmayı seçse bile YALNIZ o firmayı görür (karışma yok).</summary>
    [Fact]
    public void RPR10_Super_Admin_Firma_Secimi_Karistirmaz()
    {
        var super = new SessionContext("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var t = _reports.Inspections(super, new ReportRequest(Executed: true, CompanyId: Yabanci));

        Assert.Single(t.Rows);
        Assert.Equal("YB-001 - 99ZZZ", t.Rows[0][1]);
    }

    [Fact]
    public void RPR10_Sube_Kapsami_Uygulanir()
    {
        var t = _reports.Inspections(SadeceA(), new ReportRequest(Executed: true));
        var araclar = Kolon(t, 1);

        Assert.Contains("AR-001 - 34ABC", araclar);          // ŞUBE A aracı
        Assert.DoesNotContain("AR-002 - 06XYZ", araclar);    // ŞUBE B aracı GELMEMELİ
    }

    /// <summary>Yetkisiz şube elle istenirse kapsam GENİŞLEMEZ (fail-closed).</summary>
    [Fact]
    public void RPR10_Yetkisiz_Sube_Istenirse_Genislemez()
    {
        var t = _reports.Inspections(SadeceA(), new ReportRequest(Executed: true, BranchIds: new[] { _subeB }));
        Assert.DoesNotContain("AR-002 - 06XYZ", Kolon(t, 1));
    }

    [Fact]
    public void RPR10_Tarih_Filtresi_Sonraki_Tarihe_Uygulanir()
    {
        // Yalnız önümüzdeki 30 gün: I2 (10 gün sonra) gelmeli, I3 (200 gün sonra) gelmemeli.
        var t = _reports.Inspections(_admin, new ReportRequest(Executed: true, FromDate: Now, ToDate: Now + 30 * Gun));
        var belge = Kolon(t, 2);

        Assert.Contains("Sigorta", belge);
        Assert.DoesNotContain("Kasko", belge);
        Assert.DoesNotContain("Muayene", belge);   // süresi geçmiş → aralık dışında
    }

    [Fact]
    public void RPR10_Arac_Filtresi_Calisir()
    {
        var t = _reports.Inspections(_admin, new ReportRequest(Executed: true, VehicleIds: new[] { "V2" }));
        Assert.All(Kolon(t, 1), x => Assert.Equal("AR-002 - 06XYZ", x));
    }

    [Fact]
    public void RPR10_Bos_Sonuc_Patlamaz()
    {
        var t = _reports.Inspections(_admin,
            new ReportRequest(Executed: true, FromDate: Now + 5000 * Gun, ToDate: Now + 5001 * Gun));
        Assert.Empty(t.Rows);
        Assert.Equal(9, t.Headers.Count);   // boş sonuçta da kolonlar durur (ekran çökmez)
    }

    [Fact]
    public void RPR10_Yetkisiz_Kullanici_Calistiramaz()
    {
        var yetkisiz = new SessionContext("y", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _reports.Inspections(yetkisiz, new ReportRequest(Executed: true)));
    }

    /// <summary>Ağır rapor kapısı: Sorgula'ya basılmadan çalışmaz.</summary>
    [Fact]
    public void RPR10_Sorgulanmadan_Calismaz()
        => Assert.ThrowsAny<Exception>(() => _reports.Inspections(_admin, new ReportRequest(Executed: false)));

    // ═══════════════ RPR-11 · PERSONEL ════════════════════════════════════════════════════════

    [Fact]
    public void RPR11_Kolonlar_Ekranla_Ayni()
    {
        var t = _reports.Personnel(_admin, new ReportRequest(Executed: true));
        Assert.Equal(new[] { "Şube", "Ad Soyad", "Unvan", "Telefon", "Erişim", "Durum" }, t.Headers);
    }

    [Fact]
    public void RPR11_Erisim_Rozeti_Ekranla_Ayni()
    {
        var t = _reports.Personnel(_admin, new ReportRequest(Executed: true));
        var ad = Kolon(t, 1);
        var erisim = Kolon(t, 4);

        Assert.Equal("Admin · aliy", erisim[ad.IndexOf("Ali Yılmaz")]);      // bağlı admin hesabı
        Assert.Equal("Saha personeli", erisim[ad.IndexOf("Ayşe Demir")]);    // hesap yok, saha işaretli
        Assert.Equal("Kullanıcı yok", erisim[ad.IndexOf("Mehmet Kaya")]);    // ikisi de yok
    }

    [Fact]
    public void RPR11_Silinen_Personel_Listelenmez()
    {
        var t = _reports.Personnel(_admin, new ReportRequest(Executed: true));
        Assert.DoesNotContain("Silinmiş Kişi", Kolon(t, 1));
    }

    [Fact]
    public void RPR11_Baska_Firmanin_Personeli_Gelmez()
    {
        var t = _reports.Personnel(_admin, new ReportRequest(Executed: true));
        Assert.DoesNotContain("Yabancı Kişi", Kolon(t, 1));
    }

    [Fact]
    public void RPR11_Sube_Kapsami_Uygulanir()
    {
        var t = _reports.Personnel(SadeceA(), new ReportRequest(Executed: true));
        var ad = Kolon(t, 1);

        Assert.Contains("Ali Yılmaz", ad);
        Assert.Contains("Mehmet Kaya", ad);
        Assert.DoesNotContain("Ayşe Demir", ad);   // ŞUBE B personeli GELMEMELİ
    }

    /// <summary>⭐ Personel ŞUBEYE bağlı bir kayıttır → giriş (çalışma) şubesi kapsamı daraltır.
    /// (Stok raporlarındaki depo/şantiye filtresiyle KARIŞTIRILMAMALI: orası fiziksel yerdir.)</summary>
    [Fact]
    public void RPR11_Calisma_Subesi_Daraltir()
    {
        var ikiSube = new SessionContext("kul2", Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("personnel", true, false, false, false),
            }, Array.Empty<string>()))
        { ScopeBranchIds = new[] { _subeA, _subeB }, OperatingBranchId = _subeB };

        var ad = Kolon(_reports.Personnel(ikiSube, new ReportRequest(Executed: true)), 1);
        Assert.Contains("Ayşe Demir", ad);
        Assert.DoesNotContain("Ali Yılmaz", ad);
    }

    [Fact]
    public void RPR11_Yetkisiz_Kullanici_Calistiramaz()
    {
        var yetkisiz = new SessionContext("y", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _reports.Personnel(yetkisiz, new ReportRequest(Executed: true)));
    }

    // ═══════════════ RPR-12 · RAPORUN DAYANDIĞI EKRAN İZNİ ════════════════════════════════════

    /// <summary>
    /// ⭐ RPR-12 — "reports" izni tek başına YETMEZ: rapor başka bir ekranın verisini gösteriyorsa
    /// o ekranın izni de gerekir. Personel raporu KİŞİSEL VERİ (ad, telefon, kullanıcı adı) gösterir;
    /// yalnız rapor izni verilmiş biri bunu okuyamamalı.
    /// </summary>
    [Fact]
    public void RPR12_Personel_Raporu_Personel_Iznini_Ister()
        => Assert.Throws<ForbiddenException>(() => _reports.Personnel(YalnizRapor(), new ReportRequest(Executed: true)));

    [Fact]
    public void RPR12_Muayene_Raporu_Muayene_Iznini_Ister()
        => Assert.Throws<ForbiddenException>(() => _reports.Inspections(YalnizRapor(), new ReportRequest(Executed: true)));

    /// <summary>KİLİT: izni OLAN kullanıcı raporu çalıştırabilmeye devam eder (yanlış pozitif yok).</summary>
    [Fact]
    public void RPR12_Izinli_Kullanici_Calistirabilir()
    {
        Assert.NotEmpty(_reports.Personnel(SadeceA(), new ReportRequest(Executed: true)).Headers);
        Assert.NotEmpty(_reports.Inspections(SadeceA(), new ReportRequest(Executed: true)).Headers);
    }

    /// <summary>
    /// Katalogdaki her RequiredModule GERÇEK bir modül anahtarı olmalı — yazım hatası, raporu
    /// sessizce herkese kapatır ya da açar.
    /// </summary>
    [Fact]
    public void RPR12_Katalogdaki_Modul_Anahtarlari_Gercek()
    {
        foreach (var d in ReportCatalog.All)
        {
            if (d.RequiredModule is null) continue;
            Assert.True(AppModules.All.Any(m => m.Key == d.RequiredModule),
                $"{d.Key} → bilinmeyen modül anahtarı: {d.RequiredModule}");
        }
    }

    /// <summary>
    /// Katalogdaki RequiredModule, servisin GERÇEKTEN istediği izinle aynı olmalı. Aksi halde liste
    /// bir raporu gösterir ama çalıştırınca 403 gelir (ya da tersi). Bu test ikisini karşılaştırır:
    /// izni OLMAYAN kullanıcı için rapor MUTLAKA hata vermeli.
    /// </summary>
    [Fact]
    public void RPR12_Katalog_Ile_Servis_Kapisi_Ayni()
    {
        foreach (var d in ReportCatalog.All)
        {
            if (d.RequiredModule is null) continue;

            // "reports" + o modül DIŞINDAKİ her şey verilir; hedef modül BİLEREK verilmez.
            var izinler = new List<ModulePermission> { new("reports", true, false, false, false) };
            foreach (var (key, _) in AppModules.All)
                if (key != d.RequiredModule && key != "reports") izinler.Add(new ModulePermission(key, true, false, false, false));

            var kul = new SessionContext("k-" + d.Key, Co, new[] { RoleKeys.Staff },
                new PermissionSet(izinler, Array.Empty<string>()));

            Assert.Throws<ForbiddenException>(() =>
                _reports.Run(kul, d.Key, new ReportRequest(Executed: true)));
        }
    }

    // ═══════════════ KATALOG / DISPATCH KİLİDİ ════════════════════════════════════════════════

    [Fact]
    public void Yeni_Raporlar_Katalogda_ve_Run_Uzerinden_Calisir()
    {
        foreach (var key in new[] { "inspection", "personnel" })
        {
            var d = ReportCatalog.ByKey(key);
            Assert.NotNull(d);
            Assert.False(d!.IsManager);                       // ikisi de OPERASYON raporudur
            Assert.False(string.IsNullOrWhiteSpace(d.InfoNote));
            Assert.NotEmpty(_reports.Run(_admin, key, new ReportRequest(Executed: true)).Headers);
        }
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
