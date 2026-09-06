using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SEKME ŞERİDİ NÖBETİ (kullanıcının çizdiği tasarım, 2026-09-04) ═══
///
/// Kullanıcı iki platform için sekme şeridi tasarımı çizdi ve <b>ikisinin aynı görünmesini</b> istedi.
/// Tek fark KONUMDUR: masaüstünde ALTTA, webde üst başlığın HEMEN ALTINDA.
///
/// <b>Neden test:</b> burada korunan şey "renk güzel mi" değil, tasarımın <b>yapısal sözleşmesidir</b>.
/// İki şerit ayrı dosyalarda (Avalonia XAML + Razor/CSS) yaşıyor; biri değişip diğeri unutulursa
/// kullanıcı iki farklı arayüz görür ve bunu ancak EKRANDA fark eder. Test bunu derlemede yakalar.
///
/// Avalonia bu ortamda render edilemediği için (bkz. <c>MasaustuTasarimPaketiTests</c>) görüntü değil
/// <b>görüntüyü üreten kaynağın değişmezleri</b> doğrulanır.
///
///  SEK1 — Masaüstü şeridi ALTTA ve üç parçayı da taşıyor (ikon · etiket · ✕) + sağ uçta SOHBET
///  SEK2 — Web şeridi ÜSTTE: MudMainContent'in İLK çocuğu ve artık `bottom:0` ile sabitlenmiyor
///  SEK3 — Web şeridi kaydırınca görünür kalır (sticky) ve üst barın ALTINA girmez
///  SEK4 — İki platform da AYNI üç parçayı taşır (parite: biri eklenip diğeri unutulamaz)
///  SEK5 — Sekme ikonu ekranın GRUBUNDAN gelir; her masaüstü ekranının bir grubu vardır
///  SEK6 — Renkler token'dan gelir: şeritte gömülü hex renk YOK (tema değişince şerit bozulmaz)
/// </summary>
public class SekmeSeridiTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] parcalar)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(parcalar).ToArray()));

    private static string Masaustu() => Oku("src", "DepoWise.Desktop", "Views", "MainWindow.axaml");
    private static string WebDuzen() => Oku("src", "DepoWise.Web", "Components", "Layout", "MainLayout.razor");
    private static string WebStil()  => Oku("src", "DepoWise.Web", "wwwroot", "app.css");

    /// <summary>Şeridin masaüstündeki bloğu — yalnız bu aralık incelenir ki ekranın geri kalanındaki
    /// benzer metinler teste karışmasın.</summary>
    private static string MasaustuSeritBloku()
    {
        var x = Masaustu();
        // ⚠ "DockPanel.Dock=Bottom" dosyada BAŞKA yerlerde de geçer (kenar çubuğunun kullanıcı şeridi);
        //   şerit bloğu, kendi tasarım yorumundan başlatılır ki test yanlış bölgeyi incelemesin.
        var bas = x.IndexOf("AÇIK EKRAN SEKMELERİ", StringComparison.Ordinal);
        Assert.True(bas >= 0, "Masaüstü sekme şeridi bulunamadı (tasarım yorumu yok).");
        var son = x.IndexOf("<!-- İçerik", bas, StringComparison.Ordinal);
        Assert.True(son > bas, "Sekme şeridinin bittiği yer (içerik bölümü) bulunamadı.");
        return x[bas..son];
    }

    [Fact]
    public void SEK1_Masaustu_Serit_Altta_ve_Dort_Parcayi_Tasiyor()
    {
        var blok = MasaustuSeritBloku();

        // Şerit ALTTA (kullanıcı tasarımı) — üste taşınırsa web ile konum farkı kaybolur.
        Assert.Contains("DockPanel.Dock=\"Bottom\"", blok);

        // ⭐ 2026-09-06 (kullanıcı isteği): şerit artık SOHBET düğmesini de taşıyor ve bu düğme
        // sekme olmasa da görünmelidir → görünürlük koşulu HasOpenTabs değil AltBarGorunur.
        // (AltBarGorunur = HasOpenTabs || sohbet yetkisi. Sekme de sohbet de yoksa şerit yine yer kaplamaz.)
        Assert.Contains("IsVisible=\"{Binding AltBarGorunur}\"", blok);

        Assert.Contains("Classes=\"sekme\"", blok);                     // sekme gövdesi
        Assert.Contains("Data=\"{Binding Icon}\"", blok);               // ① ikon
        Assert.Contains("Text=\"{Binding Label}\"", blok);              // ② etiket
        Assert.Contains("CloseTabCommand", blok);                       // ③ ✕ kapatma

        // ⭐ "Yeni Sekme" KALDIRILDI (kullanıcı isteği 2026-09-06: "yeni sekme alanını tamamen
        // kaldıralım"). Sessizce geri gelmemeli — kullanıcının açık kararıdır.
        Assert.DoesNotContain("YeniSekme_Click", blok);
        Assert.DoesNotContain("Yeni Sekme", blok);

        // ⭐ Sohbet düğmesi şeridin EN SAĞINDA sabit durmalı (kullanıcı tasarımı).
        Assert.Contains("DockPanel.Dock=\"Right\"", blok);
        Assert.Contains("Chat.PaneliAcKapaCommand", blok);

        // Vurgu çizgisi HER sekmede vardır, yalnız rengi değişir → aktiflik değişince yükseklik oynamaz.
        Assert.Contains("Classes=\"sekmeCizgi\"", blok);
        Assert.Contains("Classes.aktif=\"{Binding IsActive}\"", blok);
    }

    [Fact]
    public void SEK2_Web_Serit_Ustte_Alta_Sabitlenmiyor()
    {
        var r = WebDuzen();

        var anaIcerik = r.IndexOf("<MudMainContent>", StringComparison.Ordinal);
        var serit     = r.IndexOf("dw-sekme-serit", StringComparison.Ordinal);
        var kapsayici = r.IndexOf("<MudContainer", StringComparison.Ordinal);

        Assert.True(anaIcerik >= 0 && serit > anaIcerik,
            "Web sekme şeridi MudMainContent içinde değil.");
        Assert.True(serit < kapsayici,
            "Web sekme şeridi ekran içeriğinden SONRA geliyor — kullanıcı tasarımında ÜSTTE olmalı.");

        // Eski davranış (sayfanın altına sabitleme) geri gelmemeli.
        Assert.DoesNotContain("bottom:0", r);
        Assert.DoesNotContain("position:fixed;left:0;right:0;bottom:0", r);
    }

    [Fact]
    public void SEK3_Web_Serit_Kaydirinca_Gorunur_Kalir()
    {
        var css = WebStil();
        var bas = css.IndexOf(".dw-sekme-serit {", StringComparison.Ordinal);
        Assert.True(bas >= 0, "app.css'te .dw-sekme-serit kuralı yok.");
        var blok = css[bas..(bas + 400)];

        // Şerit üste taşındı; "hep görünür" davranışının karşılığı sticky'dir.
        Assert.Contains("position: sticky", blok);

        // top: 0 verilirse şerit SABİT üst barın ALTINA girip kaybolur → üst bar yüksekliği şart.
        Assert.Contains("--mud-appbar-height", blok);
        Assert.DoesNotContain("top: 0;", blok);
    }

    [Fact]
    public void SEK4_Iki_Platform_Ayni_Dort_Parcayi_Tasir()
    {
        var masaustu = MasaustuSeritBloku();
        var web = WebDuzen();

        // Kullanıcının açık isteği: "sekme bar ve sekme görüntüleri aynı gibi olsun."
        // Bir parça tek platforma eklenirse şeritler ayrışır — burada yakalanır.
        Assert.Contains("Data=\"{Binding Icon}\"", masaustu);   Assert.Contains("SekmeIkon(", web);
        Assert.Contains("Text=\"{Binding Label}\"", masaustu);  Assert.Contains("dw-sekme-yazi", web);
        Assert.Contains("CloseTabCommand", masaustu);           Assert.Contains("dw-sekme-kapat", web);

        // ⭐ "Yeni Sekme" İKİ PLATFORMDAN DA kaldırıldı (kullanıcı isteği 2026-09-06).
        // Bu satır paritenin ta kendisidir: biri kaldırılıp diğeri unutulursa test kırılır.
        // (Bu test gerçekten işe yaradı: düğme önce yalnız masaüstünden kaldırılmış, web'de kalmıştı.)
        Assert.DoesNotContain("YeniSekme_Click", masaustu);     Assert.DoesNotContain("dw-sekme-yeni", web);
    }

    /// <summary>
    /// Sekmede AYRI bir ikon seti YOKTUR: sekme, o ekranın MENÜDE göründüğü ikonu alır.
    /// Korunan ilke budur — kullanıcı menüde gördüğü simgeyi sekmede de görmelidir.
    ///
    /// <b>2026-09-05'te MEKANİZMA değişti, ilke değişmedi (MNU-IKON).</b> O tarihe kadar menüde
    /// yalnız ÜST MENÜLERİN ikonu vardı; alt menülerin hiçbirinde ikon yoktu. Dolayısıyla "menüde
    /// görünen ikon" = "grubun ikonu"ydu ve bu test onu kilitliyordu. Artık alt menüler kendi
    /// ikonlarını taşıyor; sekme de ekranın KENDİ ikonunu almalıdır — aksi hâlde aynı ekran menüde
    /// bir, sekmede başka bir simgeyle görünürdü (yani testin koruduğu ilke bozulurdu).
    /// </summary>
    [Fact]
    public void SEK5_Sekme_Ikonu_Menudekiyle_Ayni_Kaynaktan_Gelir()
    {
        // Masaüstünde açılabilen her ekranın bir grubu olmalı — grupsuz ekran menüde hiç görünmez.
        // (İkon kaynağı değişse de bu katalog şartı geçerliliğini korur.)
        var grupsuz = AppScreens.All
            .Where(s => (s.Platforms & ScreenPlatform.Desktop) != 0)
            .Where(s => string.IsNullOrWhiteSpace(s.Group))
            .Select(s => s.Key)
            .ToList();

        Assert.True(grupsuz.Count == 0,
            "Şu ekranların menü grubu yok: " + string.Join(", ", grupsuz));

        // ⭐ Her masaüstü ekranının bir simge KAVRAMI olmalı; olmayan sekmede nötr ikon alır.
        var kavramsiz = AppScreens.All
            .Where(s => (s.Platforms & ScreenPlatform.Desktop) != 0)
            .Where(s => MenuIcons.ForScreen(s.Key) == MenuIcons.Fallback)
            .Select(s => s.Key)
            .ToList();

        Assert.True(kavramsiz.Count == 0,
            "Şu ekranların simge kavramı yok: " + string.Join(", ", kavramsiz));

        // Çözücünün kendisi ÇÖKMEMELİ: bilinmeyen anahtara null döner (sekme ikonsuz çizilir).
        // Test projesi Avalonia'ya bağımlı olmadığı için (masaüstü projesine referans YOK, bilinçli:
        // testlere pencere çatısı taşımak istemiyoruz) burada kaynak sözleşmesi doğrulanır.
        var cozucu = Oku("src", "DepoWise.Desktop", "DesktopIcons.cs");
        Assert.Contains("public static Geometry? ForScreen(string? desktopNavKey)", cozucu);
        Assert.Contains("if (string.IsNullOrEmpty(desktopNavKey)) return null;", cozucu);
        // Sekme, ekranın KENDİ ikonunu alır — menüdeki alt menü satırıyla aynı kaynak.
        Assert.Contains("return ekran is null ? null : ForScreenKey(ekran.Key);", cozucu);
    }

    [Fact]
    public void SEK6_Seritte_Gomulu_Renk_Yok()
    {
        // Renkler tema token'larından gelir. Gömülü hex, temayı değiştirince şeridi bozar
        // (koyu temada okunaklı, açık temada görünmez olur).
        var masaustu = MasaustuSeritBloku();
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6}"), masaustu);

        // ⚠️ 2026-09-05 (FAZ 2) — KAPSAM DÜZELTMESİ, GEVŞETME DEĞİL.
        //
        // Bu kontrol eskiden `.dw-sekme-serit {`'ten DOSYA SONUNA kadar tarıyordu; yani sekme
        // şeridinden sonra app.css'e eklenen HER ŞEYİ de kapsıyordu. Bu, testin kendi yazılı
        // amacının ("gömülü hex, temayı değiştirince ŞERİDİ bozar") ötesine geçen bir yan etkiydi
        // ve şeridin arkasına konan meşru bir bölüm (menü renk aileleri) testi kırdı.
        //
        // Kapsam artık şeridin GERÇEK bölümüyle sınırlı: sonraki bölüm başlığında biter.
        // Şeritle ilgili garanti aynen korunur — şerit içine gömülü bir hex hâlâ yakalanır.
        var css = WebStil();
        var bas = css.IndexOf(".dw-sekme-serit {", StringComparison.Ordinal);
        Assert.True(bas > 0, "Sekme şeridi stili bulunamadı.");
        var sonrakiBolum = css.IndexOf("/* ══", bas, StringComparison.Ordinal);
        var blok = sonrakiBolum > bas ? css[bas..sonrakiBolum] : css[bas..];
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6}"), blok);
    }
}
