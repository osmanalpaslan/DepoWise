using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MOB-W — MOBİL TARAYICI UYUMLULUĞU NÖBETİ (kullanıcı kararı 2026-09-04) ═══
///
/// Ayrı bir mobil UYGULAMA yapılmayacak; kullanıcı telefonun tarayıcısından girip işi oradan
/// yönetecek. Bu yüzden web'in dar ekran davranışı artık bir ÖZELLİKTİR ve korunması gerekir.
///
/// <b>Neden test:</b> mobil davranış 62 sayfaya tek tek yazılmadı — <c>app.css</c> içinde TEK bir
/// katmanda toplandı. Bu katmanın gücü de zayıflığı da aynı yerden gelir: tek dosyadaki bir hata
/// BÜTÜN ekranları etkiler. Testler bu katmanın değişmezlerini kilitler.
///
///  MOB1 — Mobil katman var ve MudBlazor ile aynı kırılım noktalarını kullanıyor
///  MOB2 — Menü dar ekranda içeriği İTMEZ (Responsive; Persistent'e dönüş yasak)
///  MOB3 — Mobil kurallar YALNIZ medya sorgusu içinde; geniş ekran etkilenmez
///  MOB4 — Global arama iki kopya hâlinde var ve görünürlüğü CSS ile seçiliyor (MudHidden DEĞİL)
///  MOB5 — Viewport etiketi doğru (yoksa tarayıcı sayfayı küçültür, tüm katman boşa gider)
///  MOB6 — Tablolar dar ekranda yatay kaydırılabilir
/// </summary>
public class MobilWebTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string Stil() => Oku("src", "DepoWise.Web", "wwwroot", "app.css");
    private static string Duzen() => Oku("src", "DepoWise.Web", "Components", "Layout", "MainLayout.razor");

    /// <summary>Mobil katmanın metni — §18 başlığından dosya sonuna kadar.</summary>
    private static string MobilKatman()
    {
        var css = Stil();
        var bas = css.IndexOf("18) MOB-W", StringComparison.Ordinal);
        Assert.True(bas >= 0,
            "app.css'te MOB-W mobil katmanı bulunamadı. Mobil uyumluluk bu katmana bağlıdır; " +
            "kaldırılırsa web telefonda kullanılamaz hâle gelir.");
        return css[bas..];
    }

    [Fact]
    public void MOB1_Mobil_Katman_Var_ve_Dogru_Kirilim_Noktalarini_Kullanir()
    {
        var k = MobilKatman();

        // MudBlazor'un kırılım noktalarıyla aynı olmalı: aksi hâlde MudBlazor bir düzene,
        // bizim kurallarımız başka bir düzene geçer ve arada bozuk bir aralık kalır.
        Assert.Contains("@media (max-width: 960px)", k);   // tablet
        Assert.Contains("@media (max-width: 600px)", k);   // telefon

        // ÜST BAR sınırı 1100 px'tir ve TAHMİNLE DEĞİL ÖLÇÜMLE seçilmiştir: tam masaüstü barının
        // öğe toplamı ~1060 px'tir, altında her hâlde taşar (1000 px'lik pencerede doğrulandı;
        // bu taşma MOB-W'den ÖNCE de vardı). 960'a çekilirse 961–1060 arası yeniden bozulur.
        Assert.Contains("@media (max-width: 1100px)", k);

        // Dokunma hedefleri EKRAN GENİŞLİĞİNE değil GİRDİ TÜRÜNE bakmalı: dokunmatik dizüstü de
        // parmakla kullanılır, geniş ekranlı diye küçük hedef bırakılamaz.
        Assert.Contains("@media (pointer: coarse)", k);
    }

    [Fact]
    public void MOB2_Menu_Dar_Ekranda_Icerigi_Itmez()
    {
        var d = Duzen();

        // Persistent menü dar ekranda içeriği YANA İTER: 390 px ekranda 240 px menü açıkken
        // içeriğe ~150 px kalır ve ekran kullanılamaz. Responsive, dar ekranda üste binen
        // bir katmana döner ve seçim yapılınca kapanır.
        Assert.Contains("DrawerVariant.Responsive", d);
        Assert.DoesNotContain("DrawerVariant.Persistent", d);
        Assert.Contains("Breakpoint=\"Breakpoint.Md\"", d);
    }

    [Fact]
    public void MOB3_Mobil_Kurallar_Yalniz_Medya_Sorgusu_Icinde()
    {
        // ⭐ EN KRİTİK KURAL. Katmandaki bir kural yanlışlıkla medya sorgusunun DIŞINDA kalırsa
        // masaüstü tarayıcıyı da etkiler — yani "telefonu düzeltirken bilgisayarı bozma" riski.
        // Burada süslü parantez derinliği sayılır ve 0. seviyedeki seçiciler toplanır.
        var k = MobilKatman();
        var temiz = Regex.Replace(k, @"/\*.*?\*/", "", RegexOptions.Singleline);   // yorumları at

        var derinlik = 0;
        var kokSeciciler = new List<string>();
        var tampon = new System.Text.StringBuilder();

        foreach (var c in temiz)
        {
            if (c == '{')
            {
                if (derinlik == 0)
                {
                    var s = tampon.ToString().Trim();
                    if (s.Length > 0 && !s.StartsWith("@media") && !s.StartsWith("@supports"))
                        kokSeciciler.Add(s);
                }
                derinlik++; tampon.Clear();
            }
            else if (c == '}') { derinlik = Math.Max(0, derinlik - 1); tampon.Clear(); }
            else if (derinlik == 0) tampon.Append(c);
        }

        // Tek bilinçli istisna: iki arama kopyasından MENÜDEKİ varsayılan olarak gizlidir;
        // 600 px altında görünür olur. Bu kuralın kendisi medya sorgusunda OLAMAZ (varsayılan hâl).
        var beklenmeyen = kokSeciciler.Where(s => !s.Contains(".dw-mobil-arama")).ToList();

        Assert.True(beklenmeyen.Count == 0,
            "Mobil katmanda medya sorgusu DIŞINDA kural var — bunlar geniş ekranı da etkiler: "
            + string.Join(" · ", beklenmeyen));
    }

    [Fact]
    public void MOB4_Global_Arama_Iki_Kopya_ve_Gorunurluk_CSS_ile()
    {
        var d = Duzen();
        var k = MobilKatman();

        // Arama telefonda üst bara SIĞMIYOR (180 px sabit kutu diğer düğmeleri dışarı itiyordu).
        // Çözüm: iki kopya — üst barda ve menüde — aynı alana bağlı, biri gizli.
        Assert.Contains("dw-appbar-search", d);
        Assert.Contains("dw-mobil-arama", d);

        // ⚠ Görünürlük CSS ile seçilmeli. MudHidden denendi ve GENİŞ EKRANDA arama kutusunu
        // tamamen kaybettirdi (kırılım bilgisini JavaScript'ten alıyor). Geri dönüş yasak.
        Assert.DoesNotContain("<MudHidden", d);
        Assert.Contains(".dw-appbar-search { display: none !important; }", k);
        Assert.Contains(".dw-mobil-arama { display: none; }", k);
    }

    [Fact]
    public void MOB5_Viewport_Etiketi_Dogru()
    {
        // Bu etiket olmazsa tarayıcı sayfayı masaüstü genişliğinde çizip küçültür;
        // yazılar okunmaz olur ve mobil katmanın TAMAMI devre dışı kalır.
        var app = Oku("src", "DepoWise.Web", "Components", "App.razor");
        Assert.Matches(new Regex(@"<meta\s+name=""viewport""\s+content=""[^""]*width=device-width"), app);
    }

    [Fact]
    public void MOB6_Tablolar_Dar_Ekranda_Yatay_Kaydirilir()
    {
        var k = MobilKatman();

        // Web'de 40 dosyada 102 tablo var ve hiçbirinin yatay kaydırma sarmalayıcısı yoktu.
        // Tablo kendi içinde kaymazsa ya kolonlar okunmaz hâle sıkışır ya SAYFA yana kayar.
        Assert.Contains(".mud-table-container", k);
        Assert.Contains("div:has(> table.dw-grid)", k);
        Assert.Contains("overflow-x: auto", k);

        // Kaydırılabilirlik GÖRÜNÜR olmalı: telefonda kaydırma çubuğu gizlidir, kullanıcı
        // tablonun sağa devam ettiğini anlayamaz ve kolonların yarısını hiç görmez.
        Assert.Contains("::-webkit-scrollbar", k);
    }
}
