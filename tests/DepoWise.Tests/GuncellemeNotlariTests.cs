using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — GÜNCELLEME NOTLARI (kullanıcı isteği: teknik olmayan, son kullanıcının anlayacağı notlar; masaüstünde
/// güncelleme öncesi + sonrası, web'de yayından sonra bir kez; iki ortamda Güncelleme Notları ekranı).
/// Kilitler: katalog sözleşmesi (en yeni üstte, tekil kimlik, boş madde yok, geçerli platform, TEKNİK TERİM YOK) ·
/// platform süzmesi · masaüstü sürüm eşleşmesi · iki platformun ekran/pencere/pop-up bağlantıları.
/// </summary>
public class GuncellemeNotlariTests
{
    private static string Kok()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Depo kökü bulunamadı.");
    }
    private static string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { Kok() }.Concat(p).ToArray()));

    [Fact]
    public void GN1_Katalog_sozlesmesi()
    {
        var t = GuncellemeNotlari.Tumu;
        Assert.NotEmpty(t);
        Assert.Equal(t.Count, t.Select(n => n.Id).Distinct().Count());                       // tekil kimlik
        Assert.Equal(t.OrderByDescending(n => n.Tarih).Select(n => n.Id), t.Select(n => n.Id)); // en yeni üstte
        foreach (var n in t)
        {
            Assert.False(string.IsNullOrWhiteSpace(n.Baslik));
            Assert.NotEmpty(n.Maddeler);
            Assert.All(n.Maddeler, m => Assert.False(string.IsNullOrWhiteSpace(m.Metin)));
            Assert.All(n.Maddeler, m => Assert.Contains(m.Platform, new[] { GuncellemeNotlari.Ikisi, GuncellemeNotlari.Masaustu, GuncellemeNotlari.Web }));
            if (n.MasaustuSurumu is not null) Assert.Matches(@"^\d+\.\d+\.\d+$", n.MasaustuSurumu);
        }
        var surumler = t.Where(n => n.MasaustuSurumu is not null).Select(n => n.MasaustuSurumu).ToList();
        Assert.Equal(surumler.Count, surumler.Distinct().Count());                            // bir sürüm = bir not
    }

    /// <summary>Kullanıcı şartı: notlar TEKNİK OLMAYACAK. Yazılımcı jargonu kataloğa sızarsa test kırılır.</summary>
    [Fact]
    public void GN2_Notlarda_teknik_terim_yok()
    {
        var yasak = new Regex(@"\b(API|SQL|migration|endpoint|deploy|commit|refactor|null|exception|Blazor|Avalonia|SQLite|PostgreSQL|JSON|HTTP|backend|frontend|bug|fix|cache|index|query|transaction)\b",
            RegexOptions.IgnoreCase);
        foreach (var n in GuncellemeNotlari.Tumu)
        {
            Assert.DoesNotMatch(yasak, n.Baslik);
            foreach (var m in n.Maddeler) Assert.DoesNotMatch(yasak, m.Metin);
        }
    }

    [Fact]
    public void GN3_Platform_suzmesi_ve_masaustu_metni()
    {
        var n = new GuncellemeNotu("t", new DateOnly(2026, 1, 1), "9.9.9", "Deneme", new[]
        {
            new GuncellemeMaddesi("ortak"), new GuncellemeMaddesi("yalnız masaüstü", GuncellemeNotlari.Masaustu),
            new GuncellemeMaddesi("yalnız web", GuncellemeNotlari.Web),
        });
        Assert.Equal(new[] { "ortak", "yalnız web" }, n.MaddelerIcin(web: true).Select(m => m.Metin));
        Assert.Equal(new[] { "ortak", "yalnız masaüstü" }, n.MaddelerIcin(web: false).Select(m => m.Metin));
        Assert.Equal("• ortak\n• yalnız masaüstü", GuncellemeNotlari.MasaustuMetni(n));
        Assert.Equal("01.01.2026", n.TarihMetni);
    }

    [Fact]
    public void GN4_Masaustu_surum_eslesmesi_ve_son_web_notu()
    {
        var ilk = GuncellemeNotlari.Tumu.First(n => n.MasaustuSurumu is not null);
        Assert.Same(ilk, GuncellemeNotlari.MasaustuSurumuIcin(ilk.MasaustuSurumu));
        Assert.Null(GuncellemeNotlari.MasaustuSurumuIcin("0.0.1"));
        Assert.Null(GuncellemeNotlari.MasaustuSurumuIcin(null));
        Assert.NotNull(GuncellemeNotlari.SonWeb);
        Assert.True(GuncellemeNotlari.SonWeb!.MaddelerIcin(web: true).Any());
    }

    /// <summary>Masaüstü güncelleme ÖNCESİ (sunucu notu pencerelerde) + SONRASI (Yenilikler bir kez) + ekran bağlantıları.</summary>
    [Fact]
    public void GN5_Masaustu_baglantilari()
    {
        var app = Oku("src", "DepoWise.Desktop", "App.axaml.cs");
        var shell = Oku("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs");
        var oto = Oku("src", "DepoWise.Desktop", "AutoUpdateService.cs");
        Assert.Contains("AutoUpdateService.NotBolumu(AutoUpdateService.PendingNotes)", app);       // eşitleme ekranı "Kur / Ertele"
        Assert.Contains("GuncellemeNotuGosterici.GosterGerekirseAsync(main)", app);                 // güncelleme SONRASI
        Assert.Contains("AutoUpdateService.NotBolumu(latest.ReleaseNotes)", shell);                // "Güncelleme Mevcut"
        Assert.Contains("AutoUpdateService.NotBolumu(AutoUpdateService.PendingNotes)", shell);      // "Yeniden Başlat"
        Assert.Contains("case \"about:notes\":", shell);
        Assert.Contains("latest.ReleaseNotes", oto);                                                // bekleyen paketin notu saklanır
    }

    [Fact]
    public void GN6_Web_baglantilari()
    {
        var layout = Oku("src", "DepoWise.Web", "Components", "Layout", "MainLayout.razor");
        var sayfa = Oku("src", "DepoWise.Web", "Components", "Pages", "UpdateNotes.razor");
        Assert.Contains("await GuncellemeNotuGosterAsync();", layout);
        Assert.Contains("\"dw_not_gorulen\"", layout);
        Assert.Contains("GuncellemeNotlari.SonWeb", layout);
        Assert.Contains("@page \"/update-notes\"", sayfa);
        Assert.Contains("GuncellemeNotlari.PlatformIcin(web: true)", sayfa);
        var ekran = AppScreens.All.Single(s => s.Key == "about.notes");
        Assert.Equal("update-notes", ekran.WebRoute);
        Assert.Equal("about:notes", ekran.DesktopNavKey);
        Assert.Equal("", ekran.WebPermOverride);   // herkese açık
    }

    [Fact]
    public void GN7_Notu_tekrar_gosterme_karari()
    {
        // Masaüstü yardımcısı Desktop projesinde; karar mantığı katalog üstünden aynı kuralla sınanır.
        var surum = GuncellemeNotlari.Tumu.First(n => n.MasaustuSurumu is not null).MasaustuSurumu!;
        GuncellemeNotu? Karar(string? mevcut, string? gorulen)
            => string.IsNullOrWhiteSpace(mevcut) || mevcut == gorulen ? null : GuncellemeNotlari.MasaustuSurumuIcin(mevcut);
        Assert.NotNull(Karar(surum, null));        // ilk kez → göster
        Assert.NotNull(Karar(surum, "1.0.100"));   // eski sürüm görülmüştü → göster
        Assert.Null(Karar(surum, surum));          // aynı sürüm görüldü → gösterme
        Assert.Null(Karar("0.0.1", null));         // notu olmayan sürüm → gösterme
        var gosterici = Oku("src", "DepoWise.Desktop", "GuncellemeNotuGosterici.cs");
        Assert.Contains("string.Equals(mevcutSurum, gorulenSurum, StringComparison.Ordinal)", gosterici);
        Assert.Contains("GuncellemeNotlari.MasaustuSurumuIcin(mevcutSurum)", gosterici);
    }
}
