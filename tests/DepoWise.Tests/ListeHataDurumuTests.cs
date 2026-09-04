using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ K §13 (2026-09-05) — "YÜKLENEMEDİ" İLE "KAYIT YOK" KARIŞTIRILMAZ ═══
///
/// <b>Bulgu (uçtan uca denetim, protokol §13 "yanlış 'başarılı' mesajı vermeme"):</b> web'deki liste
/// ekranları isteği <c>catch { _rows = boş; }</c> ile yutuyordu. Sunucuya ulaşılamadığında ekran
/// <b>"Hareket yok." / "Henüz personel yok." / "Henüz muayene/sigorta kaydı yok."</b> yazıyordu —
/// yani kullanıcıya <b>SESSİZCE YANLIŞ BİLGİ</b>. Kullanıcının babası başka bir şehirde tek başına
/// çalışıyor: "kayıt yok" yazan bir ekranı gördüğünde kaydın silindiğini sanabilir, aynı kaydı
/// yeniden girebilir (mükerrer veri) ya da muayene tarihini kaçırabilir.
///
/// <b>Düzeltme:</b> hata ile boşluk AYRI durumlardır. Hata hâlinde sebep gösterilir ve
/// <b>"Tekrar dene"</b> sunulur; boş liste yalnız GERÇEKTEN boşken yazılır.
///
/// <b>Masaüstünde aynı kusur YOK</b> (ölçüldü): <c>StockMovementsViewModel</c>,
/// <c>PersonnelViewModel</c>, <c>InspectionViewModel</c> hatayı <c>Status</c>/<c>LoadError</c> ile
/// zaten kullanıcıya söylüyordu. Bu yüzden düzeltme web'e özgüdür — masaüstü tarafında dokunulmadı.
///
///  LHD1 — Değişen üç liste ekranı hata durumunu AYRI gösteriyor
///  LHD2 — Hata yutulmuyor: `catch (Exception` ile sebep yakalanıyor
///  LHD3 — Kullanıcıya tekrar deneme yolu sunuluyor
///  LHD4 — Masaüstü tarafı hatayı zaten bildiriyor (parite kaydı)
/// </summary>
public class ListeHataDurumuTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string Sayfa(string ad) => Oku("src", "DepoWise.Web", "Components", "Pages", ad);

    /// <summary>Bu turda dokunulan üç web listesi. Her biri kendi hata bayrağını taşır.</summary>
    public static TheoryData<string, string> Sayfalar => new()
    {
        { "Stock.razor", "_hrkError" },
        { "Maintenance.razor", "_bkmError" },
        { "Personnel.razor", "_listeHatasi" },
        { "Inspection.razor", "_listeHatasi" },
    };

    [Theory]
    [MemberData(nameof(Sayfalar))]
    public void LHD1_Hata_Durumu_Bos_Listeden_Ayri_Gosteriliyor(string dosya, string bayrak)
    {
        var kod = Sayfa(dosya);
        Assert.Contains(bayrak, kod);
        Assert.Contains($"else if ({bayrak} is not null)", kod);
        Assert.Contains("yüklenemedi", kod);
        // ⭐ Kullanıcıya boşluğun ANLAMI söyleniyor: "kayıt yok" demek değildir.
        Assert.Contains("anlamına GELMEZ", kod);
    }

    [Theory]
    [MemberData(nameof(Sayfalar))]
    public void LHD2_Hata_Yutulmuyor(string dosya, string bayrak)
    {
        var kod = Sayfa(dosya);
        Assert.Contains("catch (Exception ex)", kod);
        Assert.Contains($"{bayrak} = ex is HttpRequestException", kod);
    }

    [Theory]
    [MemberData(nameof(Sayfalar))]
    public void LHD3_Tekrar_Deneme_Sunuluyor(string dosya, string _)
    {
        Assert.Contains("Tekrar dene", Sayfa(dosya));
    }

    /// <summary>
    /// Parite kaydı: masaüstünde aynı kusur olmadığı için orada değişiklik YAPILMADI. Bu test o
    /// gerekçeyi kilitler — biri masaüstündeki bildirimi kaldırırsa, "web'de var, masaüstünde yok"
    /// dengesizliği sessizce oluşmasın.
    /// </summary>
    [Fact]
    public void LHD4_Masaustu_Hatayi_Zaten_Bildiriyor()
    {
        foreach (var vm in new[] { "StockMovementsViewModel.cs", "PersonnelViewModel.cs", "InspectionViewModel.cs" })
        {
            var kod = Oku("src", "DepoWise.Desktop", "ViewModels", vm);
            Assert.Contains("catch (Exception ex)", kod);
            Assert.True(kod.Contains("Status = \"Hata: \" + ex.Message") || kod.Contains("LoadError = ex.Message"),
                $"{vm}: liste hatası kullanıcıya bildirilmiyor.");
        }
    }
}
