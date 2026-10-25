using System.Text.RegularExpressions;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Settings;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-58 (AR-GE 2026-10-25, F-122) — KAYITLI RAPOR GÖRÜNÜMÜ + RAPOR PDF ═══
///  RG1 — biçim gidiş-dönüş: tüm alanlar, Türkçe ad, etiket korunur
///  RG2 — aynı ad (büyük/küçük harf, Türkçe İ/i) üzerine yazar; boş / uzun ad ve 30 sınırı reddedilir
///  RG3 — silme yalnız o görünümü kaldırır; bozuk kayıt açılışı patlatmaz
///  RG4 — 🔴 saklama KİŞİSEL: başka kullanıcı görmez; aynı kullanıcının kolon tercihini ezmez
///  RG5 — iki platform AYNI alan anahtarlarını hem kaydederken hem uygularken kullanır (nöbetçi)
///  RG6 — rapor PDF'i Excel ile aynı uç/yetki: web ?format=pdf, masaüstü aynı dışa aktarma butonu
/// </summary>
public class RaporGorunumuTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public RaporGorunumuTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rpgor_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static RaporGorunumu Ornek(string ad = "Aylık yakıt — şantiye")
    {
        var g = new RaporGorunumu { Ad = ad, Rapor = "fuel", Arama = "filtre", Siralama = "date_desc" };
        g.Ekle(RaporGorunumleri.Alan.Vehicle, new[] { "v1", "v2", "v1" });
        g.Ekle(RaporGorunumleri.Alan.Location, new[] { "", "d1" });
        g.Ekle(RaporGorunumleri.Alan.Material, new[] { "m1" });
        g.Etiket["m1"] = "YF-1\u001FYağ filtresi";
        return g;
    }

    [Fact]
    public void RG1_Bicim_Gidis_Donus()
    {
        var geri = RaporGorunumleri.Coz(RaporGorunumleri.Yaz(new[] { Ornek() })).Single();
        Assert.Equal("Aylık yakıt — şantiye", geri.Ad);
        Assert.Equal("fuel", geri.Rapor);
        Assert.Equal(new[] { "v1", "v2" }, geri.Kimlikler(RaporGorunumleri.Alan.Vehicle));   // tekrar ayıklanır
        Assert.Equal(new[] { "", "d1" }, geri.Kimlikler(RaporGorunumleri.Alan.Location));     // «Atanmamış» korunur
        Assert.Equal("YF-1\u001FYağ filtresi", geri.Etiket["m1"]);
        Assert.Equal("filtre", geri.Arama);
        Assert.Equal("date_desc", geri.Siralama);
        Assert.Empty(geri.Kimlikler(RaporGorunumleri.Alan.Branch));
        // boş seçim alanı hiç yazılmaz
        var g = new RaporGorunumu { Ad = "x", Rapor = "stock" };
        g.Ekle(RaporGorunumleri.Alan.Branch, Array.Empty<string>());
        Assert.Empty(g.Secim);
    }

    [Fact]
    public void RG2_Ayni_Ad_Uzerine_Yazar_Ve_Sinirlar()
    {
        var liste = RaporGorunumleri.Kaydet(new List<RaporGorunumu>(), Ornek("İstasyon Yakıtı"));
        var yeni = new RaporGorunumu { Ad = "  istasyon yakıtı ", Rapor = "fuel-daily" };
        liste = RaporGorunumleri.Kaydet(liste, yeni);
        var tek = Assert.Single(liste);
        Assert.Equal("fuel-daily", tek.Rapor);
        Assert.Equal("istasyon yakıtı", tek.Ad);                                     // kırpılmış yeni ad

        Assert.Throws<ArgumentException>(() => RaporGorunumleri.Kaydet(liste, new RaporGorunumu { Ad = "  ", Rapor = "fuel" }));
        Assert.Throws<ArgumentException>(() => RaporGorunumleri.Kaydet(liste, new RaporGorunumu { Ad = new string('a', 61), Rapor = "fuel" }));
        Assert.Throws<ArgumentException>(() => RaporGorunumleri.Kaydet(liste, new RaporGorunumu { Ad = "rapor yok", Rapor = "" }));

        var dolu = Enumerable.Range(1, RaporGorunumleri.EnFazla).Select(i => new RaporGorunumu { Ad = "G" + i, Rapor = "stock" }).ToList();
        var ex = Assert.Throws<ArgumentException>(() => RaporGorunumleri.Kaydet(dolu, new RaporGorunumu { Ad = "Fazla", Rapor = "stock" }));
        Assert.Contains("30", ex.Message);
        Assert.Equal(RaporGorunumleri.EnFazla, RaporGorunumleri.Kaydet(dolu, new RaporGorunumu { Ad = "g5", Rapor = "fuel" }).Count);   // üzerine yazma sınırı aşmaz
    }

    [Fact]
    public void RG3_Silme_Ve_Bozuk_Kayit()
    {
        var liste = RaporGorunumleri.Kaydet(RaporGorunumleri.Kaydet(new List<RaporGorunumu>(), Ornek("A")), Ornek("B"));
        var kalan = RaporGorunumleri.Sil(liste, "a");
        Assert.Equal("B", Assert.Single(kalan).Ad);

        var kayit = RaporGorunumleri.Yaz(kalan).ToList();
        kayit.Insert(0, "{bozuk");
        kayit.Add("");
        kayit.Add("{\"ad\":\"\",\"rapor\":\"stock\"}");
        Assert.Equal("B", Assert.Single(RaporGorunumleri.Coz(kayit)).Ad);
        Assert.Empty(RaporGorunumleri.Coz(null));
    }

    [Fact]
    public void RG4_Saklama_Kisisel_Ve_Kolon_Tercihini_Ezmez()
    {
        var prefs = new UserListPreferenceService(_factory);
        var ali = new SessionContext("u-ali", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        var veli = new SessionContext("u-veli", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        prefs.SaveColumns(ali, "reports", new[] { "Kod", "Malzeme" });   // aynı kişinin rapor tablosu kolon tercihi

        prefs.SaveColumns(ali, RaporGorunumleri.PrefKey, RaporGorunumleri.Yaz(new[] { Ornek("Ali'nin görünümü") }));

        Assert.Equal("Ali'nin görünümü", Assert.Single(RaporGorunumleri.Coz(prefs.GetColumns(ali, RaporGorunumleri.PrefKey))).Ad);
        Assert.Empty(RaporGorunumleri.Coz(prefs.GetColumns(veli, RaporGorunumleri.PrefKey)));
        Assert.Equal(new[] { "Kod", "Malzeme" }, prefs.GetColumns(ali, "reports"));
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void RG5_Iki_Platform_Ayni_Alanlar()
    {
        var masaustu = Oku("src/DepoWise.Desktop/ViewModels/ReportsViewModel.Gorunum.cs");
        var web = Oku("src/DepoWise.Web/Components/Pages/Reports.razor");
        var alanlar = typeof(RaporGorunumleri.Alan).GetFields().Select(f => f.Name).ToList();
        Assert.Equal(14, alanlar.Count);
        foreach (var a in alanlar)
        {
            var desen = new Regex(@"RaporGorunumleri\.Alan\." + a + @"\b");
            Assert.True(desen.Matches(masaustu).Count >= 2, "masaüstü kaydet+uygula eksik: " + a);
            Assert.True(desen.Matches(web).Count >= 2, "web kaydet+uygula eksik: " + a);
        }
        // web: kayıtlı kimlikler ekrandaki seçeneklerle KESİŞTİRİLEREK uygulanır (12 liste alanı) — görünüm kapsam genişletemez
        Assert.Equal(12, Regex.Matches(web, @"Kes\(g\.Kimlikler\(RaporGorunumleri\.Alan\.").Count);
        Assert.Contains("RaporGorunumleri.PrefKey", masaustu);
        Assert.Contains("RaporGorunumleri.PrefKey", web);
        // web projesi ortak modeli derler (iki platform AYNI biçim)
        Assert.Contains(@"DepoWise.Application\Reports\RaporGorunumu.cs", Oku("src/DepoWise.Web/DepoWise.Web.csproj"));
    }

    [Fact]
    public void RG6_Rapor_Pdf_Excel_Ile_Ayni_Yol()
    {
        var web = Oku("src/DepoWise.Web/Components/Pages/Reports.razor");
        Assert.Contains("OnClick=\"ExportPdf\"", web);
        Assert.Contains("(pdf ? \"?format=pdf\" : \"\")", web);
        var masaustu = Oku("src/DepoWise.Desktop/ViewModels/ReportsViewModel.Gorunum.cs");
        Assert.Contains("SpecialButtons.ExportManagerReports : SpecialButtons.ExportReports", masaustu);
        Assert.Contains("YazdirmaYardimcisi.YazdirAsync(table", masaustu);
        Assert.Contains("Command=\"{Binding ExportPdfCommand}\"", Oku("src/DepoWise.Desktop/Views/ReportsView.axaml"));
    }
}
