using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ G / PRT-02 — KALAN PARİTE: LİSTE EKRANI DIŞA AKTARIM KURALI (2026-09-04) ═══
///
/// Projenin kendi kuralı (<c>.claude/rules/list-screens.md</c> Kural 2):
/// <i>"Filtre/sıralama/sayfalama olan HER liste ekranında bir 'Excel'e Aktar' butonu bulunur.
/// Buton, o an ekrandaki SAYFA değil, FİLTRELENMİŞ TÜM SONUÇ KÜMESİNİ indirir."</i>
///
/// <b>Personel</b> ve <b>Muayene/Sigorta</b> ekranları bu kuralın dışında kalmıştı — iki platformda da.
/// Kullanıcı listeyi görüyor ama dışarı alamıyordu; aynı bilgi için elle kopyalamak zorundaydı.
///
/// <b>P-1 ölçüldü ve zaten yapılmıştı:</b> masaüstünde "Bağı Kaldır" komutu (<c>RemoveAccount</c>)
/// mevcut ve düğmeye bağlı. Yol haritası satırı eskimişti; kod yazılmadı.
///
///  PRT1 — Sunucu uçları var ve "export" yetkisini ZORUNLU kılıyor (yeni yetki modülü açılmadı)
///  PRT2 — Uçlar SAYFA DEĞİL tüm sonucu döndürür (sayfa sınırı uygulanmaz)
///  PRT3 — Masaüstü: iki ekranda da düğme + komut var ve yetkiye bağlı
///  PRT4 — Web: iki ekranda da düğme + çağrı var
///  PRT5 — Excel tablo modelleri tanımlı ve kolonları boş değil
/// </summary>
public class ParitePRT02Tests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string Api() => Oku("src", "DepoWise.Api", "Program.cs");

    /// <summary>Yorum satırlarını atar — açıklama metni testi yanlışlıkla doğrulamasın.</summary>
    private static string KodSadece(string s)
    {
        var blok = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return string.Join("\n", blok.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
    }

    [Fact]
    public void PRT1_Uclar_Var_Ve_Export_Yetkisi_Zorunlu()
    {
        var api = KodSadece(Api());
        Assert.Contains("\"/api/personnel/export\"", api);
        Assert.Contains("\"/api/inspection/export\"", api);

        // Her iki uç da mevcut "export" modülünü ister — yeni yetki modülü AÇILMADI
        // (yeni modül, yetki ağacını sessizce büyütür ve mevcut atamaları geçersizleştirirdi).
        var personelBlok = api[api.IndexOf("\"/api/personnel/export\"", StringComparison.Ordinal)..];
        Assert.Contains("Require(s, \"export\"", personelBlok[..600]);
        var muayeneBlok = api[api.IndexOf("\"/api/inspection/export\"", StringComparison.Ordinal)..];
        Assert.Contains("Require(s, \"export\"", muayeneBlok[..600]);
    }

    /// <summary>⭐ Kuralın ÖZÜ: "o anki sayfa" değil TÜM sonuç. Sayfa sınırı uygulanırsa kullanıcı
    /// eksik dosya indirir ve bunu fark etmez — sessiz veri eksikliği.</summary>
    [Fact]
    public void PRT2_Uclar_Sayfa_Degil_Tum_Sonucu_Dondurur()
    {
        var api = KodSadece(Api());
        var blok = api[api.IndexOf("\"/api/personnel/export\"", StringComparison.Ordinal)..];
        Assert.Contains("Limit = 100_000", blok[..800]);   // ekran sayfa boyutu DEĞİL
    }

    [Fact]
    public void PRT3_Masaustu_Iki_Ekranda_Da_Dugme_Ve_Komut_Var()
    {
        foreach (var (vm, view) in new[]
                 {
                     ("PersonnelViewModel.cs", "PersonnelView.axaml"),
                     ("InspectionViewModel.cs", "InspectionView.axaml"),
                 })
        {
            var vmKod = KodSadece(Oku("src", "DepoWise.Desktop", "ViewModels", vm));
            Assert.Contains("private async Task ExportExcel()", vmKod.Replace("System.Threading.Tasks.Task", "Task"));
            Assert.Contains("AccessControl.Can(_session, \"export\"", vmKod);

            var xaml = Oku("src", "DepoWise.Desktop", "Views", view);
            Assert.Contains("ExportExcelCommand", xaml);
            // Yetkisi olmayana düğme GÖSTERİLMEZ (asıl kapı sunucuda; bu yalnız görünürlük).
            Assert.Contains("IsVisible=\"{Binding CanExport}\"", xaml);
        }
    }

    [Fact]
    public void PRT4_Web_Iki_Ekranda_Da_Dugme_Ve_Cagri_Var()
    {
        foreach (var (sayfa, uc) in new[]
                 {
                     ("Personnel.razor", "/api/personnel/export"),
                     ("Inspection.razor", "/api/inspection/export"),
                 })
        {
            var kod = Oku("src", "DepoWise.Web", "Components", "Pages", sayfa);
            Assert.Contains("Excel'e Aktar", kod);
            Assert.Contains(uc, kod);
            Assert.Contains("dwDownload", kod);   // tarayıcıya indirme aynı ortak yolu kullanır
        }
    }

    [Fact]
    public void PRT5_Excel_Tablo_Modelleri_Tanimli()
    {
        var personel = Oku("src", "DepoWise.Infrastructure", "Org", "PersonnelService.cs");
        Assert.Contains("public static Application.Reports.TableModel ToTableModel", personel);
        Assert.Contains("\"Ad Soyad\"", personel);

        var muayene = Oku("src", "DepoWise.Infrastructure", "Maintenance", "InspectionService.cs");
        Assert.Contains("public static Application.Reports.TableModel ToTableModel", muayene);
        Assert.Contains("\"Belge Türü\"", muayene);
    }
}
