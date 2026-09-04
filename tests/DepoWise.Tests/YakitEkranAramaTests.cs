using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 6 — YAKIT EKRANI: ARAYÜZ SÖZLEŞMESİ + ÖLÜ ARAMA KUTUSU (2026-09-04) ═══
///
/// Servis davranışı <see cref="YakitListeSayfalamaTests"/>'te kanıtlanır. Buradaki testler
/// <b>arayüz sözleşmesini</b> kilitler — iki ekran (Avalonia XAML + Razor) bu ortamda render
/// edilemediği için görüntü değil, görüntüyü üreten kaynağın değişmezleri korunur.
///
/// <b>Kullanıcının üç şikayeti ve ortamlara göre gerçek durumu:</b>
/// <list type="bullet">
///   <item>Eski kayıtlar görünmüyor → <b>her iki ortamda da</b> vardı (sabit 200 satır tavanı).</item>
///   <item>Liste sayfalanmıyor → <b>her iki ortamda da</b> vardı.</item>
///   <item>"Arama düğmesi çalışmıyor" → masaüstünde kutu <b>ölüydü</b>; webde kutu <b>hiç yoktu</b>.
///         Belirti farklı, sonuç aynı: kullanıcı filtreleyemiyordu.</item>
/// </list>
///
///  YKE1 — Masaüstü sabit 200 tavanını KULLANMAZ; sayfalanmış sorguya bağlıdır
///  YKE2 — Web sabit 200 ucunu KULLANMAZ; /api/fuel/grid ucuna bağlıdır
///  YKE3 — Arama YALNIZ düğme ve Enter ile çalışır (yazarken tetiklenmez) — kullanıcının açık isteği
///  YKE4 — İki ortamda da tarih aralığı + araç + serbest metin alanları VAR
///  YKE5 — ⭐ Ölü arama kutusu sınıf düzeltmesi: Toolbar varsayılanı KAPALI
///  YKE6 — Aramayı gerçekten kullanan ekranlar ShowSearch="True" ile açıkça bildirir
///  YKE7 — Filtre değişince sayfa 1'e döner (yoksa boş ekran "kayıt silinmiş" gibi görünür)
/// </summary>
public class YakitEkranAramaTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string MasaVm() => Oku("src", "DepoWise.Desktop", "ViewModels", "FuelViewModel.cs");

    /// <summary>Yorumları ayıklar. "Şu çağrı ARTIK YOK" testleri kaynağın tamamına bakarsa,
    /// kusuru ANLATAN yorum satırının kendisi eşleşir ve test yanlışlıkla kırmızı olur
    /// (ilk denemede tam olarak bu oldu). Kontrol edilmesi gereken KOD, açıklama değil.</summary>
    private static string KodSadece(string kaynak)
    {
        var b = System.Text.RegularExpressions.Regex.Replace(kaynak, @"/\*.*?\*/", "",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return string.Join("\n", b.Split('\n').Where(s => !s.TrimStart().StartsWith("//")));
    }
    private static string MasaView() => Oku("src", "DepoWise.Desktop", "Views", "FuelView.axaml");
    private static string Web() => Oku("src", "DepoWise.Web", "Components", "Pages", "Fuel.razor");

    [Fact]
    public void YKE1_Masaustu_Sabit_200_Tavanini_Kullanmaz()
    {
        var vm = MasaVm();

        // Kusurun kendisi: `ListDistributions(_session, 200, …)` — en yeni 200 dışındaki kayıtlar
        // sessizce düşüyordu. Liste artık sayfalanmış sorgudan beslenir.
        Assert.Contains("SearchDistributions(_session, Page, PageSize", vm);
        Assert.DoesNotContain("ListDistributions(_session, 200", KodSadece(vm));

        // Toplam sayı ve sayfa bilgisi kullanıcıya YAZILMALI — kesilmenin sessiz olmaması
        // asıl şikayetin özüydü.
        Assert.Contains("TotalCount = grid.TotalCount", vm);
        Assert.Contains("sayfa {Page} / {TotalPages}", vm);
    }

    [Fact]
    public void YKE2_Web_Sabit_200_Ucunu_Kullanmaz()
    {
        var w = Web();

        Assert.Contains("/api/fuel/grid?page=", w);
        // Eski uç dağıtım listesi için ARTIK kullanılmamalı (depo sekmesi kendi ucunu kullanır).
        Assert.DoesNotContain("\"/api/fuel?includeCancelled=", w);

        Assert.Contains("totalCount", w);
        Assert.Contains("sayfa {_distPage} / {_distTotalPages}", w);
    }

    [Fact]
    public void YKE3_Arama_Yalniz_Dugme_ve_Enter_ile_Calisir()
    {
        // ⭐ Kullanıcının AÇIK isteği: "sadece bu botun ve enter tuşu ile sorgu alanı aktif olup
        // arama yapsın". Yani yazarken anlık arama YOK — her tuşta sunucuya gitmek hem yavaş
        // hem de kullanıcının istemediği bir davranış.
        var vm = MasaVm();
        var view = MasaView();
        var w = Web();

        // Masaüstü: arama alanları için OnXChanged tetikleyicisi OLMAMALI.
        Assert.DoesNotContain("partial void OnAramaMetniChanged", vm);
        Assert.DoesNotContain("partial void OnAramaAracChanged", vm);
        Assert.DoesNotContain("partial void OnAramaBaslangicChanged", vm);
        Assert.DoesNotContain("partial void OnAramaBitisChanged", vm);

        // Masaüstü: Enter kısayolu ve Sorgula düğmesi AYNI komuta bağlı.
        Assert.Contains("KeyBinding Gesture=\"Enter\" Command=\"{Binding SorgulaCommand}\"", view);
        Assert.Contains("Content=\"Sorgula\"", view);

        // Web: Enter + Sorgula düğmesi.
        Assert.Contains("e.Key == \"Enter\") await Sorgula()", w);
        Assert.Contains("OnClick=\"Sorgula\"", w);
    }

    [Fact]
    public void YKE4_Iki_Ortamda_da_Tarih_Arac_ve_Metin_Alanlari_Var()
    {
        var view = MasaView();
        var w = Web();

        // Kullanıcı bunları bugüne kadar yalnız RAPORDA yapabiliyordu; ama raporda DÜZENLEME yok,
        // kaydı bulup düzeltmesi gerekiyordu. Bu yüzden alanlar bu ekranda olmalı.
        Assert.Contains("AramaArac", view);
        Assert.Contains("AramaBaslangic", view);
        Assert.Contains("AramaBitis", view);
        Assert.Contains("AramaMetni", view);

        Assert.Contains("_qVehicle", w);
        Assert.Contains("_qFrom", w);
        Assert.Contains("_qTo", w);
        Assert.Contains("_qText", w);
    }

    [Fact]
    public void YKE5_Toolbar_Arama_Kutusu_Varsayilani_KAPALI()
    {
        // ⭐ SINIF DÜZELTMESİ. Varsayılan `true` iken şablon HER ekranda arama kutusu çiziyordu,
        // ama Toolbar kullanan 50 ekranın yalnız 4'ü onu bağlamıştı → 46 ekranda kutu görünüyor,
        // kullanıcı yazıyor, hiçbir şey olmuyordu. Kullanıcının şikayeti bunun tekil örneğiydi.
        // Çalışmayan bir kutuyu göstermek, hiç göstermemekten daha kötüdür.
        var c = Oku("src", "DepoWise.Desktop", "Controls", "Components.cs");
        Assert.Contains("AvaloniaProperty.Register<Toolbar, bool>(nameof(ShowSearch), false)", c);
    }

    [Fact]
    public void YKE6_Aramayi_Kullanan_Ekranlar_Acikca_Bildirir()
    {
        // Varsayılan kapandığı için, aramayı GERÇEKTEN kullanan ekranlar bunu açıkça istemeli;
        // aksi hâlde çalışan bir arama sessizce kaybolurdu.
        var kok = Path.Combine(RepoKok(), "src", "DepoWise.Desktop", "Views");
        var eksik = new List<string>();

        foreach (var yol in Directory.EnumerateFiles(kok, "*.axaml"))
        {
            var m = File.ReadAllText(yol);
            if (!m.Contains("ctrl:Toolbar")) continue;
            if (!m.Contains("SearchText=")) continue;              // aramayı kullanmıyor
            if (!m.Contains("ShowSearch=\"True\"")) eksik.Add(Path.GetFileName(yol));
        }

        Assert.True(eksik.Count == 0,
            "Bu ekranlar arama kutusunu BAĞLAMIŞ ama ShowSearch=\"True\" dememiş; varsayılan kapalı "
            + "olduğu için aramaları görünmez oldu: " + string.Join(", ", eksik));
    }

    [Fact]
    public void YKE7_Filtre_Degisince_Sayfa_Bire_Doner()
    {
        // 7. sayfadayken filtre daraltılırsa sonuç 2 sayfaya düşer → kullanıcı BOŞ ekran görür ve
        // "kayıtlar silinmiş" sanır. Bu, çözmeye çalıştığımız "kayıt görünmüyor" şikayetinin
        // yeni bir biçimde geri gelmesi olurdu.
        // Not: sayfa sıfırlaması HER İKİ sekme için de yapılır (dağıtımlar + depo girişleri) —
        // ikisi de aynı filtre çubuğundan besleniyor.
        Assert.Contains("private void Sorgula() { Page = 1; DepoPage = 1; Load(); }", MasaVm());
        Assert.Contains("_distPage = 1; _depotPage = 1;", Web());
    }
}
