using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MAS-04 — LİSTE TABLOLARINDA KOLON HİZASI ═══ (kullanıcı bildirimi 2026-08-26)
///
/// <b>KULLANICININ GÖRDÜĞÜ.</b> "Kolon adları ve aynı kolondaki filtre hücreleri, tablo başlıkları ile
/// aynı hizada olmalı; diğer verilerin kısımlarına taşmamalı. Bir Excel tablosu gibi olmalı."
///
/// <b>KÖK NEDEN (iki ayrı kusur).</b>
/// <list type="number">
///   <item><b>Biriken kayma.</b> Başlık, filtre ve veri satırları AYNI genişliği okuyordu
///     (<c>ColWidths</c>, Min=Max), ama filtre hücresi ayrıca <c>Margin="4,0"</c> taşıyordu.
///     <b>Margin genişliğin DIŞINA</b> eklenir → filtre kolonu <c>W+8</c>, diğerleri <c>W</c> oluyordu.
///     Fark her kolonda birikiyordu (4. kolonda ~30 px). Çözüm: <c>Margin</c> → <c>Padding</c>
///     (iç boşluk; <c>MinWidth=MaxWidth</c> sınırının İÇİNDE kalır, kolonu büyütmez).</item>
///   <item><b>Üst sınırsız kolonlar.</b> Stok Hareketleri / Stok Değişiklik Kaydı / Denetim Kaydı
///     ekranlarında hücrelerde yalnız <c>MinWidth</c> vardı ve kolonların bir kısmı <c>*</c> idi.
///     Uzun bir değer veri kolonunu genişletiyor, başlık sabit kalıyordu; ayrıca dikey kaydırma
///     çubuğu çıkınca <c>*</c> kolonlar yalnız gövdede daralıyordu. Çözüm: tüm kolonlar
///     <c>Auto</c> + her hücrede <c>MinWidth = MaxWidth</c>.</item>
/// </list>
///
/// <b>SINIR — DÜRÜST BEYAN.</b> Avalonia arayüzü bu projede otomatize edilemiyor; "piksel ölçtüm"
/// diyen sahte bir GUI testi üretilmedi. Bunun yerine hizayı ÜRETEN kuralların kaynakta gerçekten
/// durduğu doğrulanır. Aşağıdaki her testin karşılığı bir mutasyon denemesiyle sınanmıştır.
/// </summary>
public class MasaustuTabloKolonHizasiTests
{
    /// <summary>Başlık + filtre + veri satırı olan ekranlar (ortak <c>ColWidths</c> mimarisi).</summary>
    public static readonly string[] FiltreliEkranlar = { "MaterialsView", "VehiclesView", "DailyActivityView" };

    /// <summary>Başlık + veri satırı olan ekranlar (sabit piksel genişlikli kolonlar).</summary>
    public static readonly string[] BasitEkranlar = { "StockMovementsView", "StockChangeLogView", "AuditLogView" };

    public static TheoryData<string> Filtreli() { var d = new TheoryData<string>(); foreach (var e in FiltreliEkranlar) d.Add(e); return d; }
    public static TheoryData<string> Basit() { var d = new TheoryData<string>(); foreach (var e in BasitEkranlar) d.Add(e); return d; }

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Gorunum(string ad)
        => File.ReadAllText(Path.Combine(RepoKok(), "src", "DepoWise.Desktop", "Views", ad + ".axaml"));

    private static string Kaynak(params string[] parcalar)
        => File.ReadAllText(Path.Combine(new[] { RepoKok(), "src", "DepoWise.Desktop" }.Concat(parcalar).ToArray()));

    // ══════════════ A) BİRİKEN KAYMA — filtre hücresinde DIŞ boşluk olamaz ══════════════

    /// <summary>⭐ ASIL HATA: filtre hücresi <c>Margin</c> taşıyamaz. <c>Margin</c>, sabitlenmiş
    /// genişliğin DIŞINA eklenir ve kolonu büyütür; kayma kolon kolon birikir.</summary>
    [Theory]
    [MemberData(nameof(Filtreli))]
    public void HZA1_Filtre_Hucresinde_Dis_Bosluk_Yok(string ekran)
    {
        var x = Gorunum(ekran);

        var dis = Regex.Matches(x, @"<ContentControl[^>]*\sMargin=""[^""]*""");
        Assert.True(dis.Count == 0,
            $"{ekran}: filtre hücresinde Margin var ({dis.Count} yer) — kolonu genişletir, hiza birikerek bozulur. " +
            "Boşluk gerekiyorsa Padding kullanılmalı (genişliğin İÇİNDE kalır).");
    }

    /// <summary>Görsel boşluk kaybolmadı: filtre hücresi iç boşlukla (Padding) nefes alıyor.</summary>
    [Theory]
    [MemberData(nameof(Filtreli))]
    public void HZA2_Filtre_Hucresi_Ic_Bosluk_Kullanir(string ekran)
    {
        var x = Gorunum(ekran);
        var ic = Regex.Matches(x, @"<ContentControl[^>]*\sPadding=""4,0""").Count;
        Assert.True(ic > 0, $"{ekran}: filtre hücrelerinde Padding=\"4,0\" bulunamadı (boşluk büsbütün kaybolmuş olabilir).");
    }

    /// <summary>⭐ Üç satır da AYNI genişlik kaynağını okumalı: başlık ve filtre ile veri hücreleri
    /// <c>ColWidths</c> sözlüğüne bağlıdır. Biri başka bir kaynağa bağlanırsa hiza sessizce kayar.</summary>
    [Theory]
    [MemberData(nameof(Filtreli))]
    public void HZA3_Genislik_Tek_Kaynaktan_Okunur(string ekran)
    {
        var x = Gorunum(ekran);

        // NOT: bağlama metninin İÇİNDE de "}" geçer ({x:Static ...}) → sınır olarak tırnak kullanılır.
        var min = Regex.Matches(x, @"MinWidth=""\{Binding[^""]*Conv\.ColWidth[^""]*""").Count;
        var max = Regex.Matches(x, @"MaxWidth=""\{Binding[^""]*Conv\.ColWidth[^""]*""").Count;

        Assert.True(min > 0, $"{ekran}: ColWidths'e bağlı MinWidth bulunamadı.");
        Assert.Equal(min, max);   // her alt sınırın bir üst sınırı var → uzun değer kolonu genişletemez
    }

    // ══════════════ B) ÜST SINIRSIZ KOLONLAR — sabit genişlik zorunlu ══════════════

    /// <summary>⭐ Tablo kolonlarında <c>*</c> (kalan alanı paylaş) olamaz: dikey kaydırma çubuğu
    /// çıkınca yalnız GÖVDEDE daralır, başlıkta daralmaz → hiza bozulur.</summary>
    [Theory]
    [MemberData(nameof(Basit))]
    public void HZB1_Tablo_Kolonlarinda_Esneyen_Kolon_Yok(string ekran)
    {
        foreach (var def in TabloKolonTanimlari(Gorunum(ekran)))
            Assert.DoesNotContain("*", def);
    }

    /// <summary>⭐ Başlık ve veri satırı BİREBİR aynı kolon düzenini kullanmalı.</summary>
    [Theory]
    [MemberData(nameof(Basit))]
    public void HZB2_Baslik_Ve_Veri_Ayni_Kolon_Duzeni(string ekran)
    {
        var defler = TabloKolonTanimlari(Gorunum(ekran));

        Assert.Equal(2, defler.Count);            // başlık + veri satırı
        Assert.Equal(defler[0], defler[1]);
    }

    /// <summary>⭐ Her hücrenin alt sınırı kadar ÜST sınırı da olmalı — uzun değer kolonu genişletemesin
    /// ve "…" ile kesme gerçekten devreye girsin.</summary>
    [Theory]
    [MemberData(nameof(Basit))]
    public void HZB3_Her_Hucrenin_Ust_Siniri_Var(string ekran)
    {
        var x = Gorunum(ekran);
        var eksik = new List<string>();

        foreach (Match m in Regex.Matches(x, @"<SelectableTextBlock[^>]*Grid\.Column=""\d+""[^>]*/>"))
        {
            var h = m.Value;
            var min = Regex.Match(h, @"MinWidth=""(\d+)""");
            if (!min.Success) continue;
            var max = Regex.Match(h, @"MaxWidth=""(\d+)""");
            if (!max.Success || max.Groups[1].Value != min.Groups[1].Value)
                eksik.Add(h.Length > 90 ? h[..90] + "…" : h);
        }

        Assert.True(eksik.Count == 0,
            $"{ekran}: {eksik.Count} hücrede üst sınır yok/uyumsuz — uzun değer kolonu genişletir:{Environment.NewLine}"
            + string.Join(Environment.NewLine, eksik.Take(3)));
    }

    // ══════════════ C) BAŞLIK VE GÖVDE BİRLİKTE KAYAR ══════════════

    /// <summary>⭐ Yatay kaydırma tablonun TAMAMINA uygulanmalı. Gövde kendi başına kayarsa
    /// başlık yerinde kalır ve kolonlar tamamen birbirinden ayrılır.</summary>
    [Theory]
    [MemberData(nameof(Filtreli))]
    [MemberData(nameof(Basit))]
    public void HZC1_Baslik_Ve_Govde_Birlikte_Kayar(string ekran)
    {
        var x = Gorunum(ekran);
        var i = x.IndexOf("Grid.IsSharedSizeScope", StringComparison.Ordinal);
        Assert.True(i > 0, $"{ekran}: ana tablo bulunamadı.");

        var pencere = x.Substring(i, Math.Min(400, x.Length - i));
        Assert.Contains("<ScrollViewer HorizontalScrollBarVisibility=\"Auto\"", pencere);
        Assert.Contains("VerticalScrollBarVisibility=\"Disabled\"", pencere);
    }

    // ══════════════ D) SÜTUN AYIRICI ÇİZGİLERİ ══════════════

    /// <summary>⭐ Kullanıcının seçtiği görünüm: başlık, filtre ve veri satırlarının HEPSİNDE
    /// sütun ayırıcı çizgisi olmalı. Biri eksik kalırsa tablo yarım çizgili görünür.</summary>
    [Theory]
    [MemberData(nameof(Filtreli))]
    public void HZD1_Filtreli_Ekranda_Uc_Satirda_Da_Ayirici_Var(string ekran)
    {
        Assert.Equal(3, Regex.Matches(Gorunum(ekran), @"ctrl:ColumnRules\.Enabled=""True""").Count);
    }

    /// <summary>Başlık + veri satırı olan ekranlarda ikisinde de ayırıcı bulunur.</summary>
    [Theory]
    [MemberData(nameof(Basit))]
    public void HZD2_Basit_Ekranda_Iki_Satirda_Da_Ayirici_Var(string ekran)
    {
        Assert.Equal(2, Regex.Matches(Gorunum(ekran), @"ctrl:ColumnRules\.Enabled=""True""").Count);
    }

    /// <summary>Ayırıcı çizgi tıklamayı YUTMAMALI — yoksa satır seçimi ve metin kopyalama bozulur.</summary>
    [Fact]
    public void HZD3_Ayirici_Cizgi_Tiklamayi_Yutmaz()
    {
        var kaynak = Kaynak("Controls", "ColumnRules.cs");
        Assert.Contains("IsHitTestVisible = false", kaynak);
    }

    /// <summary>Ayırıcı, kolon SINIRINI hesaplamaz; kolonun içine sağa hizalanır → kolon genişliği
    /// sürüklenince ya da tablo kaydırılınca kendiliğinden doğru yerde kalır (ikinci konum kaynağı yok).</summary>
    [Fact]
    public void HZD4_Ayirici_Konumu_Hesaplanmaz()
    {
        var kaynak = Kaynak("Controls", "ColumnRules.cs");
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Right", kaynak);
        Assert.DoesNotContain("Bounds.Width", kaynak);   // ölçüm yapan ikinci bir kaynak OLMAMALI
    }

    /// <summary>Gizli kolonun ayırıcısı da gizlenmeli; aksi hâlde 0 px'lik kolon 1 px kalıntı çizgi
    /// gösterir ve hizayı bozar.</summary>
    [Fact]
    public void HZD5_Gizli_Kolonun_Ayiricisi_Gizlenir()
    {
        var kaynak = Kaynak("Controls", "ColumnRules.cs");
        Assert.Contains("IsVisibleProperty", kaynak);
        Assert.Contains("cizgi.IsVisible", kaynak);
    }

    // ══════════════ E) BAŞLIK YAZISI, VERİ HÜCRESİYLE AYNI NOKTADAN BAŞLAR ══════════════

    /// <summary>⭐ Başlık düğmesinin kenarlığı ve sol iç boşluğu, yazıyı veriden sağa kaydırıyordu.
    /// İkisi de sıfırlandı → başlık ve veri aynı x'te başlar.</summary>
    [Fact]
    public void HZE1_Baslik_Yazisi_Veriyle_Ayni_Noktadan_Baslar()
    {
        var kaynak = Kaynak("SortHeader.cs");
        Assert.Contains("BorderThickness = new Thickness(0)", kaynak);
        Assert.Contains("Padding = new Thickness(0, 4)", kaynak);
    }

    /// <summary>Genişliğin tek kaynağı hâlâ ViewModel: başlık kendi genişliğini ayrıca hesaplamaz.</summary>
    [Fact]
    public void HZE2_Baslik_Genisligi_Tek_Kaynaktan_Gelir()
    {
        var kaynak = Kaynak("SortHeader.cs");
        Assert.Contains("MinWidth = w;", kaynak);
        Assert.Contains("MaxWidth = w;", kaynak);
    }

    // ══════════════ F) RAPORLAR EKRANININ ORTAK TABLOSU (DataGridView) ══════════════

    /// <summary>⭐ Aynı <c>Margin</c> hatası ortak rapor tablosunda da vardı (kolon başına +4 px).</summary>
    [Fact]
    public void HZF1_Rapor_Tablosunda_Filtre_Dis_Bosluk_Kullanmaz()
    {
        var x = Kaynak("Controls", "DataGridView.axaml");
        var filtreBolumu = Bolum(x, "Başlık-altı filtre satırı", "Pinned TOPLAM");

        Assert.DoesNotContain("Margin=\"2,0\"", filtreBolumu);
        Assert.Contains("Padding=\"2,0\"", filtreBolumu);
    }

    /// <summary>Rapor tablosunda başlık · filtre · gövde · toplam satırlarının HEPSİ aynı kolon
    /// genişliğini okur; hiçbiri kendi ölçüsünü üretmez.</summary>
    [Fact]
    public void HZF2_Rapor_Tablosunda_Tum_Satirlar_Ayni_Genisligi_Okur()
    {
        var x = Kaynak("Controls", "DataGridView.axaml");

        Assert.Equal(2, Regex.Matches(x, @"Width=""\{Binding Width\}""").Count);          // başlık + filtre
        Assert.Equal(2, Regex.Matches(x, @"Width=""\{Binding Column\.Width\}""").Count);  // gövde + toplam
    }

    /// <summary>Rapor tablosunda da sütun ayırıcıları var (başlık + filtre + gövde + toplam).</summary>
    [Fact]
    public void HZF3_Rapor_Tablosunda_Ayirici_Cizgiler_Var()
    {
        var x = Kaynak("Controls", "DataGridView.axaml");
        Assert.Equal(3, Regex.Matches(x, @"BorderThickness=""0,0,1,0""").Count);   // filtre + gövde + toplam
        Assert.Contains("Width=\"1\" HorizontalAlignment=\"Right\"", x);           // başlık
    }

    // ══════════════ G) DEPO GENELİ — TÜM LİSTE EKRANLARI ══════════════

    /// <summary>
    /// ⭐ <b>KAPSAM TESTİ.</b> Kullanıcı "bunun gibi kayıtları listeleyen BÜTÜN tablo ve ekranlarda var"
    /// dedi. Tek bir ekranı düzeltip diğerlerini bırakmak bu isteği karşılamaz. Bu test, tablo içeren
    /// HER masaüstü ekranında sütun ayırıcılarının bulunduğunu doğrular; yeni bir liste ekranı
    /// eklenip ayırıcısı unutulursa kırılır.
    /// </summary>
    [Fact]
    public void HZG1_Tablo_Iceren_Tum_Ekranlarda_Ayirici_Var()
    {
        var eksik = new List<string>();
        foreach (var (ad, x) in TabloluEkranlar())
        {
            // Raporlar ekranının ortak tablosu ayırıcıyı kenarlıkla çizer (kolonları ItemsControl üretir).
            if (ad == "DataGridView") { Assert.Contains("BorderThickness=\"0,0,1,0\"", x); continue; }

            // ⚠️ "Dosyada bir yerde geçiyor mu" YETMEZ: bir ekranda başlık satırında ayırıcı olup
            // veri satırında olmayabilir. Her tablo satırı AYRI AYRI kontrol edilir.
            // (Mutasyon turunda tam bu zayıflıktan bir kusur kaçmıştı — test bu yüzden güçlendirildi.)
            foreach (var (bas, _) in TabloSatirAraliklari(x))
            {
                var acilisEtiketi = x[bas..(x.IndexOf('>', bas) + 1)];
                if (!acilisEtiketi.Contains("ColumnRules.Enabled=\"True\""))
                    eksik.Add($"{ad} (konum {bas})");
            }
        }

        Assert.True(eksik.Count == 0,
            $"{eksik.Count} tablo satırında sütun ayırıcısı yok:{Environment.NewLine}"
            + string.Join(Environment.NewLine, eksik.Take(6)));
    }

    /// <summary>
    /// ⭐ Tablo hücrelerindeki DÜZ YAZI alanlarının hepsinde üst sınır olmalı; yoksa uzun bir değer
    /// gövdedeki kolonu genişletir, başlıkta genişlemez ve sonraki tüm kolonlar kayar.
    ///
    /// <b>Bilinçli istisnalar:</b> (a) <c>*</c> (esnek) kolonlar — orada yer varken yazıyı kesmek
    /// yanlış olurdu; (b) yazı olmayan hücreler (buton · sayı kutusu · durum rozeti · yığın) — onlara
    /// sabit genişlik vermek etiketi kırpardı. Bu istisnalar <see cref="MetinDisiOgeler"/> ile adlandırılır.
    /// </summary>
    [Fact]
    public void HZG2_Tum_Ekranlarda_Duz_Yazi_Hucreleri_Sinirli()
    {
        var eksik = new List<string>();
        foreach (var (ad, x) in TabloluEkranlar())
        {
            var araliklar = TabloSatirAraliklari(x);
            foreach (Match m in Regex.Matches(x, @"<SelectableTextBlock[^>]*Grid\.Column=""\d+""[^>]*?/>", RegexOptions.Singleline))
            {
                // Yalnız GERÇEK tablo satırları: pencere içi form Grid'leri bu testin konusu değil.
                if (!araliklar.Any(a => m.Index >= a.Bas && m.Index < a.Son)) continue;

                var h = m.Value;
                if (!Regex.IsMatch(h, @"MinWidth=""\d+""")) continue;
                if (Regex.IsMatch(h, @"MaxWidth=")) continue;
                if (EsnekKolondaMi(x, m.Index, h)) continue;    // "*" kolon → bilinçli istisna
                eksik.Add($"{ad}: {h[..Math.Min(80, h.Length)]}…");
            }
        }

        Assert.True(eksik.Count == 0,
            $"{eksik.Count} düz yazı hücresinde üst sınır yok:{Environment.NewLine}"
            + string.Join(Environment.NewLine, eksik.Take(5)));
    }

    /// <summary>Yazı olmayan hücre türleri — bunlara sabit genişlik verilmez (etiket kırpılırdı).</summary>
    public static readonly string[] MetinDisiOgeler = { "Button", "StackPanel", "NumericUpDown", "StatusBadge" };

    /// <summary>⭐ Ayırıcı çizgiyi ekleyen davranış TEK bir yerde durmalı; ekran ekran kopyalanırsa
    /// biri güncellenir diğeri unutulur.</summary>
    [Fact]
    public void HZG3_Ayirici_Davranisi_Tek_Yerde()
    {
        var kok = RepoKok();
        var kopya = Directory
            .EnumerateFiles(Path.Combine(kok, "src", "DepoWise.Desktop"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("IsRuleProperty"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Single(kopya);
        Assert.Equal("ColumnRules.cs", kopya[0]);
    }

    // ══════════════ Yardımcılar ══════════════

    /// <summary>
    /// Bir görünümdeki GERÇEK tablo satırlarının metin aralıkları: başlık Border'ı içindeki Grid ve
    /// liste satır şablonundaki Grid. Pencere/form yerleşim Grid'leri kapsam DIŞIDIR — orada kolon
    /// sınırı diye bir kavram yoktur.
    /// </summary>
    private static List<(int Bas, int Son)> TabloSatirAraliklari(string x)
    {
        var sonuc = new List<(int, int)>();

        void Ekle(int arananBaslangic, int pencere)
        {
            var g = x.IndexOf("<Grid", arananBaslangic, StringComparison.Ordinal);
            if (g < 0 || g - arananBaslangic > pencere) return;
            sonuc.Add((g, GridSonu(x, g)));
        }

        foreach (Match m in Regex.Matches(x, @"Classes=""TableHeader"""))
            Ekle(m.Index, 400);

        foreach (Match m in Regex.Matches(x, @"<ListBox[^>]*Classes=""Table"""))
        {
            var dt = x.IndexOf("<DataTemplate", m.Index, StringComparison.Ordinal);
            if (dt > 0) Ekle(dt, 400);
        }
        return sonuc;
    }

    /// <summary>İç içe Grid'leri sayarak kapanış <c>&lt;/Grid&gt;</c> konumunu bulur.</summary>
    private static int GridSonu(string x, int gridBas)
    {
        int derinlik = 0;
        foreach (Match m in Regex.Matches(x[gridBas..], @"<Grid[ >]|</Grid>"))
        {
            if (m.Value == "</Grid>") { if (--derinlik == 0) return gridBas + m.Index; }
            else derinlik++;
        }
        return x.Length;
    }

    /// <summary>Tablo içeren tüm masaüstü görünümleri (ad, içerik).</summary>
    private static IEnumerable<(string Ad, string Icerik)> TabloluEkranlar()
    {
        var kok = Path.Combine(RepoKok(), "src", "DepoWise.Desktop");
        foreach (var klasor in new[] { "Views", "Controls" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(kok, klasor), "*.axaml"))
            {
                var x = File.ReadAllText(f);
                if (x.Contains("Classes=\"TableHeader\"")) yield return (Path.GetFileNameWithoutExtension(f), x);
            }
    }

    /// <summary>
    /// Hücrenin ait olduğu kolon <c>*</c> (esnek) mi?
    ///
    /// ⚠️ Hücrenin kolon tanımı, "dosyada ondan önce geçen son ColumnDefinitions" DEĞİLDİR — o,
    /// bambaşka bir Grid'e ait olabilir. Doğru cevap için hücreyi KAPSAYAN Grid bulunur: geriye
    /// doğru gidilirken kapanan her <c>&lt;/Grid&gt;</c> bir seviye atlatır. Grid'in kolonları hem
    /// satır içi (<c>ColumnDefinitions="..."</c>) hem blok (<c>&lt;Grid.ColumnDefinitions&gt;</c>)
    /// sözdiziminde okunur.
    /// </summary>
    private static bool EsnekKolondaMi(string x, int hucreKonumu, string hucre)
    {
        var kolonNo = int.Parse(Regex.Match(hucre, @"Grid\.Column=""(\d+)""").Groups[1].Value);
        var oncesi = x[..hucreKonumu];

        // Kapsayan <Grid ...> etiketini bul (iç içe Grid'leri atlayarak).
        int derinlik = 0, gridBas = -1;
        foreach (var m in Regex.Matches(oncesi, @"<Grid[ >]|</Grid>").Reverse())
        {
            if (m.Value == "</Grid>") { derinlik++; continue; }
            if (derinlik > 0) { derinlik--; continue; }
            gridBas = m.Index; break;
        }
        if (gridBas < 0) return true;   // kapsayan Grid yok → bu testin konusu değil

        var gridMetni = x[gridBas..hucreKonumu];
        // Blok sözdiziminde SharedSizeGroup varsa kolon genişliğini ÇERÇEVE eşitler (Avalonia'nın
        // yerleşik mekanizması: aynı IsSharedSizeScope içindeki aynı gruptaki kolonlar hep aynı olur)
        // → orada ayrıca üst sınıra gerek yoktur, koymak yazıyı bosuna keserdi.
        var blok = Regex.Matches(gridMetni, @"<ColumnDefinition\s[^>]*>").Select(m => m.Value).ToArray();
        if (blok.Length > kolonNo && blok[kolonNo].Contains("SharedSizeGroup")) return true;

        var satirIci = Regex.Match(gridMetni, @"^<Grid[^>]*ColumnDefinitions=""([^""]+)""");
        var kolonlar = satirIci.Success
            ? satirIci.Groups[1].Value.Split(',').Select(c => c.Trim()).ToArray()
            : Regex.Matches(gridMetni, @"<ColumnDefinition\s[^>]*Width=""([^""]+)""")
                   .Select(m => m.Groups[1].Value.Trim()).ToArray();

        if (kolonlar.Length == 0) return true;                       // kolon tanımı okunamadı → sayma
        return kolonNo >= kolonlar.Length || kolonlar[kolonNo].Contains('*');
    }

    /// <summary>Ekranın ANA tablosundaki (Grid.IsSharedSizeScope'tan sonraki) kolon tanımı dizeleri.</summary>
    private static List<string> TabloKolonTanimlari(string x)
    {
        var i = x.IndexOf("Grid.IsSharedSizeScope", StringComparison.Ordinal);
        Assert.True(i > 0, "ana tablo bulunamadı.");
        return Regex.Matches(x[i..], @"ColumnDefinitions=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToList();
    }

    private static string Bolum(string x, string basla, string bitir)
    {
        var a = x.IndexOf(basla, StringComparison.Ordinal);
        var b = x.IndexOf(bitir, StringComparison.Ordinal);
        Assert.True(a > 0 && b > a, $"bölüm bulunamadı: {basla} → {bitir}");
        return x[a..b];
    }
}
