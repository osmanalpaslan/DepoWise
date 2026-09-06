using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M6 (İKON SETİ) + M7 (TABLO BAŞLIĞI 1a/2a) ═══ (kullanıcının tasarım paketi, 2026-08-27)
///
/// <b>NE YAPILDI.</b> İki görsel katman eklendi — YALNIZ masaüstü (Avalonia); web'de tek satır değişmedi.
/// <list type="bullet">
///   <item>M6 — 31 vektör ikon (<c>Themes/Icons.axaml</c>) ve menü gruplarına bağlanması.
///         Emoji alanı (<c>AppScreens.AppScreenGroup.DesktopIcon</c>, <c>NavGroupVm.Icon</c>) SİLİNMEDİ:
///         web ve <c>MenuLayout</c> onu okumaya devam eder, aynı zamanda geri dönüş yoludur.</item>
///   <item>M7 — başlık bandı marka (kehribar) rengine döndü, filtre satırı kendi sınıfına
///         (<c>Border.TableFilterRow</c>) ayrıldı ve uygulama zeminine indi; kolon-başı filtre kutuları
///         hap köşeden 8 px dikdörtgene (<c>TextBox.CellFilter</c>) geçti.</item>
/// </list>
///
/// <b>NEDEN TEST.</b> Avalonia bu ortamda render edilemez; bu yüzden testler görüntüyü değil,
/// <b>görüntüyü üreten kaynağın değişmezlerini</b> korur:
/// <list type="number">
///   <item>Filtre kutusu ile SERBEST arama kutusu birbirine karışmamalı (35 + 1 filtre / 19 arama).</item>
///   <item>Altı yeni renk anahtarı HER İKİ temada da tanımlı olmalı — yalnız birinde olursa diğer temada
///         <c>DynamicResource</c> çözülmez ve başlık bandı varsayılan renkte çıkar.</item>
///   <item>Filtre satırı yatay boşluğunu stilden almalı (bkz. <c>MasaustuTabloKolonHizasiTests.HZA0</c>).</item>
///   <item>İkonlar renk taşımamalı ve <c>F1</c> öneki almamalı; ikon sözlüğü <c>Application.Styles</c>'a
///         DEĞİL <c>Application.Resources</c>'a kaydedilmeli.</item>
/// </list>
/// </summary>
public class MasaustuTasarimPaketiTests
{
    private const string T = "\"";

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string MasaustuKok() => Path.Combine(RepoKok(), "src", "DepoWise.Desktop");

    private static string Kaynak(params string[] parcalar)
        => File.ReadAllText(Path.Combine(new[] { MasaustuKok() }.Concat(parcalar).ToArray()));

    private static IEnumerable<string> TumAxaml()
        => Directory.EnumerateFiles(MasaustuKok(), "*.axaml", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                             && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static int Say(string metin)
        => TumAxaml().Sum(f => Regex.Matches(File.ReadAllText(f), Regex.Escape(metin)).Count);

    // ══════════════ M7 — FİLTRE SATIRI KAPSAMI ══════════════

    /// <summary>⭐ Filtre satırı ile başlık bandı ayrı sınıflar olmalı. Filtre satırı sayısı DÖRT'tür:
    /// üç liste ekranı + ortak rapor tablosu. (Tasarım paketi dördüncüsünü — <c>DataGridView</c> —
    /// atlamıştı; atlansaydı rapor ekranlarında filtre bandı başlık rengine bürünürdü.)</summary>
    [Fact]
    public void TSR1_Dort_Filtre_Satiri_Kendi_Sinifini_Kullanir()
    {
        var beklenen = new[]
        {
            Path.Combine("Views", "VehiclesView.axaml"),
            Path.Combine("Views", "MaterialsView.axaml"),
            Path.Combine("Views", "DailyActivityView.axaml"),
            Path.Combine("Controls", "DataGridView.axaml"),
            // 2026-09-04 (ARA İŞ 6): Yakıt Dağıtımları'na tarih/araç/metin arama çubuğu eklendi.
            // Bu GERÇEK bir filtre satırıdır → aynı sınıfı kullanır ve sayıya dahildir.
            Path.Combine("Views", "FuelView.axaml"),
        };

        foreach (var göreli in beklenen)
        {
            var x = File.ReadAllText(Path.Combine(MasaustuKok(), göreli));
            Assert.Contains($"<Border Classes={T}TableFilterRow{T} DockPanel.Dock={T}Top{T}>", x);
        }

        // Sayı BİLİNÇLİ olarak 4 → 5 güncellendi. İlk denemede 7 çıkmıştı: aynı ekrana eklenen İKİ
        // SAYFALAMA satırı da bu sınıfı kullanıyordu. Nöbetçi test bunu yakaladı ve doğru olan
        // yapıldı — sayıyı 7'ye çıkarmak değil, sayfalama satırlarını bu sınıftan ÇIKARMAK:
        // `TableFilterRow` başlık altındaki FİLTRE bandının sınıfıdır; projedeki mevcut sayfalama
        // satırları (DailyActivityView, VehiclesView) düz StackPanel'dir. Sınıfı anlamı dışında
        // kullanmak, ileride "filtre satırı" araması yapan herkesi yanıltırdı.
        Assert.Equal(beklenen.Length, Say($"Classes={T}TableFilterRow{T}"));
    }

    /// <summary>⭐ Kolon-başı filtre kutusu ile SERBEST arama kutusu karışmamalı. Serbest arama
    /// (menü araması, malzeme/araç/cari araması…) hap köşeli kalır; yalnız kolon filtreleri
    /// dikdörtgene döner. Bir serbest arama kutusu yanlışlıkla dönüştürülürse bu sayı düşer.</summary>
    [Fact]
    public void TSR2_Filtre_Kutulari_Serbest_Aramadan_Ayri()
    {
        // 2026-09-04 (ADR-201): 36 → 37. Günlük Faaliyet listesine kullanıcı isteğiyle
        // "Malzeme Miktarı" kolonu eklendi; her kolon gibi kendi filtre kutusunu de getirdi.
        // Sayı BİLİNÇLİ olarak güncellendi — nöbetçi testin amacı zaten bu onayı zorlamaktır.
        Assert.Equal(37, Say($"Classes={T}CellFilter{T}"));   // 36 kolon filtresi + ortak rapor tablosu
        // 2026-09-06 (FAZ 4.8): 19 → 20. Kullanıcı isteği: "Araç bakımlarında tarih / araç kodu /
        // plaka sorgulama alanı ve butonları yok." Bakım listesine serbest arama kutusu eklendi.
        // Sayı BİLİNÇLİ olarak güncellendi — nöbetçi testin amacı zaten bu onayı zorlamaktır.
        Assert.Equal(20, Say($"Classes={T}Search{T}"));       // serbest arama kutuları — hap köşeli KALIR
    }

    /// <summary>Filtre kutusunun üçlü imzası (Value + Label + Hint) korunmalı: filtre MANTIĞI
    /// değişmedi, yalnız sınıf adı değişti. İmza bozulursa kutu veriye bağlanmaz.</summary>
    [Theory]
    [InlineData("VehiclesView", 14)]
    [InlineData("MaterialsView", 15)]
    [InlineData("DailyActivityView", 7)]   // 2026-09-04 (ADR-201): +1 = "Malzeme Miktarı" kolonu
    public void TSR3_Filtre_Kutusu_Baglari_Korundu(string ekran, int adet)
    {
        var x = Kaynak("Views", ekran + ".axaml");
        var imza = $"Text={T}{{Binding Value, Mode=TwoWay}}{T} PlaceholderText={T}{{Binding Label}}{T} ToolTip.Tip={T}{{Binding Hint}}{T}";

        Assert.Equal(adet, Regex.Matches(x, Regex.Escape(imza)).Count);
        Assert.Equal(adet, Regex.Matches(x, Regex.Escape($"<TextBox Classes={T}CellFilter{T}")).Count);
        // Enter → filtrele kısayolu her kutuda duruyor.
        Assert.Equal(adet, Regex.Matches(x, "<TextBox.KeyBindings>").Count);
    }

    /// <summary>Dolu filtre vurgusu yalnız GÖRSELDİR: <c>HasValue</c> türetilmiş bir alandır ve
    /// değer değiştiğinde bildirilir. Bildirim olmazsa kutu dolduğunda çerçeve renk değiştirmez.</summary>
    [Fact]
    public void TSR4_Dolu_Filtre_Vurgusu_Bildirilir()
    {
        var cs = File.ReadAllText(Path.Combine(MasaustuKok(), "ViewModels", "ColumnFilterItem.cs"));

        Assert.Contains("public bool HasValue => !string.IsNullOrWhiteSpace(Value);", cs);
        Assert.Contains("OnPropertyChanged(nameof(HasValue))", cs);
        Assert.Matches(@"partial void OnValueChanged\(string \w+\)", cs);
    }

    // ══════════════ M7 — RENK ANAHTARLARI ══════════════

    /// <summary>⭐ Altı yeni fırçanın HEPSİ hem <c>Dark</c> hem <c>Light</c> sözlüğünde olmalı.
    /// Yalnız birinde tanımlanırsa diğer temada <c>DynamicResource</c> çözülmez → başlık bandı
    /// varsayılan/şeffaf çıkar (sessiz görsel bozulma).</summary>
    [Theory]
    [InlineData("TableHeaderBrush")]
    [InlineData("TableHeaderEdgeBrush")]
    [InlineData("TableHeaderTextBrush")]
    [InlineData("TableFilterRowBrush")]
    [InlineData("CellFilterBackgroundBrush")]
    [InlineData("CellFilterBorderBrush")]
    public void TSR5_Yeni_Renkler_Iki_Temada_Da_Tanimli(string anahtar)
    {
        var p = Kaynak("Themes", "Palette.axaml");

        var dark = p.IndexOf($"<ResourceDictionary x:Key={T}Dark{T}>", StringComparison.Ordinal);
        var light = p.IndexOf($"<ResourceDictionary x:Key={T}Light{T}>", StringComparison.Ordinal);
        Assert.True(dark >= 0 && light > dark, "Palette.axaml'de Dark/Light sözlükleri bulunamadı.");

        var koyuBolum = p[dark..light];
        var acikBolum = p[light..];
        var arama = $"x:Key={T}{anahtar}{T}";

        Assert.Contains(arama, koyuBolum);
        Assert.Contains(arama, acikBolum);
    }

    /// <summary>Başlık bandı ve filtre satırı stilleri gerçekten yeni anahtarları kullanmalı —
    /// aksi hâlde Palette'e token eklenir ama ekranda hiçbir şey değişmez.</summary>
    [Fact]
    public void TSR6_Stiller_Yeni_Renkleri_Kullanir()
    {
        var c = Kaynak("Themes", "Components.axaml");

        Assert.Contains($"Value={T}{{DynamicResource TableHeaderBrush}}{T}", c);
        Assert.Contains($"Value={T}{{DynamicResource TableHeaderEdgeBrush}}{T}", c);
        Assert.Contains($"Value={T}{{DynamicResource TableHeaderTextBrush}}{T}", c);
        Assert.Contains($"Value={T}{{DynamicResource TableFilterRowBrush}}{T}", c);
        Assert.Contains($"<Style Selector={T}TextBox.CellFilter{T}>", c);
        // Serbest arama stili SİLİNMEDİ — 19 kutu ona bağlı.
        Assert.Contains($"<Style Selector={T}TextBox.Search{T}>", c);
    }

    // ══════════════ M7 — SIRALANAN KOLON İŞARETİ ══════════════

    /// <summary>Sıralanan kolonun altındaki 2 px vurgu, sıralama/sürükleme MANTIĞINA dokunmadan
    /// eklendi: genişliğin tek kaynağı hâlâ ViewModel'dir ve tutamak davranışı değişmedi.</summary>
    [Fact]
    public void TSR7_Siralanan_Kolon_Vurgusu_Mantiga_Dokunmadi()
    {
        var cs = File.ReadAllText(Path.Combine(MasaustuKok(), "SortHeader.cs"));

        Assert.Contains("RowDefinitions = new RowDefinitions(\"*,2\");", cs);
        Assert.Contains("IsHitTestVisible = false", cs);            // vurgu fareyi engellemez
        Assert.Contains("_vurgu.IsVisible = aktif;", cs);
        Assert.Contains("_label.ClearValue(TextBlock.ForegroundProperty);", cs);   // pasifken stile döner

        // DEĞİŞMEYENLER — genişlik tek kaynaktan, sürükleme pencere koordinatından.
        Assert.Contains("var w = _vm.GetColumnWidth(ColumnKey);", cs);
        Assert.Contains("_vm?.PreviewColumnWidth(ColumnKey, newWidth);", cs);
        Assert.Contains("_vm?.CommitColumnWidth();", cs);
    }

    // ══════════════ M6 — İKON SETİ ══════════════

    /// <summary>⭐ İkon sözlüğü bir <c>ResourceDictionary</c>'dir: <c>Application.Resources</c>'a
    /// <c>ResourceInclude</c> ile girer. <c>Application.Styles</c>'a <c>StyleInclude</c> olarak
    /// eklenirse çalışma zamanında anahtarlar bulunamaz ve tüm menü ikonsuz kalır.</summary>
    [Fact]
    public void TSR8_Ikon_Sozlugu_Dogru_Yere_Kayitli()
    {
        var app = Kaynak("App.axaml");

        Assert.Contains($"<ResourceInclude Source={T}/Themes/Icons.axaml{T}/>", app);
        Assert.DoesNotContain($"<StyleInclude Source={T}/Themes/Icons.axaml{T}/>", app);

        var kaynaklar = app.IndexOf("<Application.Resources>", StringComparison.Ordinal);
        var stiller = app.IndexOf("<Application.Styles>", StringComparison.Ordinal);
        var ikon = app.IndexOf("/Themes/Icons.axaml", StringComparison.Ordinal);
        Assert.True(kaynaklar >= 0 && stiller > kaynaklar, "App.axaml yapısı beklenenden farklı.");
        Assert.InRange(ikon, kaynaklar, stiller);
    }

    /// <summary>⭐ İkonlar RENK TAŞIMAZ (kullanan <c>PathIcon</c>'un Foreground'unu miras alır) ve
    /// <c>F1</c> öneki ALMAZ. <c>F1</c>, Avalonia'nın varsayılan EvenOdd dolgu kuralını NonZero'ya
    /// çevirir; iç detaylarını delikle çizen 10 ikon içi dolu lekeye döner.</summary>
    [Fact]
    public void TSR9_Ikonlar_Renksiz_Ve_EvenOdd()
    {
        var ikonlar = Kaynak("Themes", "Icons.axaml");
        var anahtarlar = Regex.Matches(ikonlar, @"<StreamGeometry x:Key=""(\w+)"">")
                              .Select(m => m.Groups[1].Value).ToList();

        // Sayı SABİTLENMEZ (yeni ikon eklemek meşrudur) ama TABAN korunur: mevcut bir ikonun
        // sessizce silinmesi, onu kullanan ekranı ikonsuz bırakır.
        Assert.True(anahtarlar.Count >= 31, $"İkon sayısı 31'in altına düştü ({anahtarlar.Count}) — silinmiş ikon var.");
        Assert.Equal(anahtarlar.Count, Regex.Matches(ikonlar, "<StreamGeometry ").Count);   // hepsi x:Key taşımalı
        Assert.Equal(anahtarlar.Count, anahtarlar.Distinct().Count());                      // çift anahtar yok

        Assert.DoesNotContain("Fill=", ikonlar);
        Assert.DoesNotContain("Brush", ikonlar);
        Assert.DoesNotContain(">F1 ", ikonlar);
        Assert.DoesNotContain(">F1", ikonlar);
    }

    /// <summary>⭐ Kodda/XAML'de adı geçen HER ikon anahtarı sözlükte gerçekten olmalı. Yazım hatası
    /// veya silinmiş bir ikon derlemeyi bozmaz — çalışma zamanında sessizce boş çizilir.</summary>
    [Fact]
    public void TSR13_Kullanilan_Ikon_Anahtarlarinin_Hepsi_Tanimli()
    {
        var ikonlar = Kaynak("Themes", "Icons.axaml");
        var tanimli = Regex.Matches(ikonlar, @"<StreamGeometry x:Key=""(\w+)"">")
                           .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        // ⚠️ XAML'de yalnız KAYNAK başvurusu sayılır. `Classes="IconChip"` bir stil sınıfı adıdır,
        // ikon anahtarı DEĞİLDİR — ayırt edilmezse test yanlış alarm verir.
        var kullanilan = new HashSet<string>(StringComparer.Ordinal);
        bool Atla(string f) => f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

        foreach (var f in Directory.EnumerateFiles(MasaustuKok(), "*.axaml", SearchOption.AllDirectories)
                                   .Where(f => !Atla(f) && !f.EndsWith("Icons.axaml", StringComparison.Ordinal)))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(?:Static|Dynamic)Resource\s+(Icon\w+)\s*\}"))
                kullanilan.Add(m.Groups[1].Value);

        foreach (var f in Directory.EnumerateFiles(MasaustuKok(), "*.cs", SearchOption.AllDirectories).Where(f => !Atla(f)))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"""(Icon[A-Z]\w+)"""))
                kullanilan.Add(m.Groups[1].Value);

        Assert.NotEmpty(kullanilan);   // tarama boşa düşerse test hiçbir şeyi korumaz

        var eksik = kullanilan.Except(tanimli).OrderBy(x => x).ToList();
        Assert.True(eksik.Count == 0, "Tanımsız ikon anahtarı kullanılıyor: " + string.Join(", ", eksik));
    }

    /// <summary>
    /// ⭐ Menü ikonu grup BAŞLIĞINA bağlanır, <c>ModuleKey</c>'e değil: "Operasyon Raporları" ve
    /// "Yönetici Raporları" aynı modülü ("reports") paylaşır, başlıkları farklıdır. Eşlemedeki her
    /// başlık katalogda GERÇEKTEN var olmalı ve karşılığındaki geometri GERÇEKTEN çizilmiş olmalı;
    /// yoksa o grup sessizce ikonsuz kalır.
    ///
    /// <b>2026-09-05 (MNU-IKON) — korunan garanti aynı, baktığı yer değişti.</b> Eşleme eskiden
    /// <c>DesktopIcons</c> içindeki iki sözlükteydi (<c>ByGroup</c> / <c>BySection</c>) ve web'de
    /// AYRI bir kopyası vardı; iki tablo bağımsız eskiyebiliyordu (web'de beş anahtar tam olarak
    /// böyle ölmüştü). Artık zincir iki adımlı:
    /// <c>MenuIcons</c> (başlık → KAVRAM, iki platformun ortağı) → <c>DesktopIcons</c>
    /// (kavram → geometri anahtarı) → <c>Icons.axaml</c> (anahtar → çizim).
    /// Bu test zincirin masaüstü ucunu uçtan uca doğrular; kavram kapsamının tamamı ve web ucu
    /// <see cref="MenuIkonTests"/> içindedir.
    /// </summary>
    [Fact]
    public void TSR10_Ikon_Eslemesi_Katalogla_Ortusuyor()
    {
        var kavramlar = File.ReadAllText(Path.Combine(RepoKok(), "src", "DepoWise.Application", "Security", "MenuIcons.cs"));
        var eşleme = File.ReadAllText(Path.Combine(MasaustuKok(), "DesktopIcons.cs"));
        var katalog = File.ReadAllText(Path.Combine(RepoKok(), "src", "DepoWise.Application", "Security", "AppScreens.cs"));
        var ikonlar = Kaynak("Themes", "Icons.axaml");

        // İki ayrı sözlük var: üst menüler (ByGroup) ve menünün ÜST grupları (BySection).
        // Karıştırılmamalı — üst grup başlığı AppScreens.Sections'ta, üst menü Groups'ta tanımlıdır.
        string Bolum(string ad)
        {
            var b = kavramlar.IndexOf(ad + " = new(", StringComparison.Ordinal);
            Assert.True(b >= 0, ad + " sözlüğü bulunamadı.");
            var son = kavramlar.IndexOf("};", b, StringComparison.Ordinal);
            return kavramlar[b..son];
        }

        (string Ad, string Kavram)[] Ciftler(string bolum)
            => Regex.Matches(bolum, @"\[""([^""]+)""\]\s*=\s*""([a-z0-9\-]+)""")
                    .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToArray();

        var ustMenuler = Ciftler(Bolum("ByGroup"));
        var ustGruplar = Ciftler(Bolum("BySection"));

        // Katalogdaki her üst menünün kavramı olmalı (eskiden 17'si vardı, 7'si eksikti).
        var katalogMenuSayisi = Regex.Matches(katalog, @"new AppScreenGroup\(""([^""]+)""")
                                     .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(katalogMenuSayisi, ustMenuler.Length);
        Assert.Equal(6, ustGruplar.Length);

        // Kavram → geometri anahtarı → gerçekten çizilmiş geometri.
        void ZinciriDogrula(string ad, string kavram, bool ustGrupMu)
        {
            if (ustGrupMu) Assert.Contains($"\"{ad}\")", katalog);                  // AppScreenSection başlığı
            else Assert.Contains($"new AppScreenGroup(\"{ad}\"", katalog);           // katalogda var mı

            var m = Regex.Match(eşleme, @"\[""" + Regex.Escape(kavram) + @"""\]\s*=\s*""(Icon\w+)""");
            Assert.True(m.Success, $"'{ad}' kavramı ({kavram}) DesktopIcons'ta karşılıksız.");
            Assert.Contains($"x:Key=\"{m.Groups[1].Value}\"", ikonlar);              // geometri çizilmiş mi
        }

        foreach (var (grup, kavram) in ustMenuler) ZinciriDogrula(grup, kavram, ustGrupMu: false);
        foreach (var (bolum, kavram) in ustGruplar) ZinciriDogrula(bolum, kavram, ustGrupMu: true);

        // Katalogdaki HER üst grubun ikonu olmalı — biri eklenip eşleme unutulursa menüde tek başına ikonsuz kalır.
        var katalogUst = Regex.Matches(katalog, @"new AppScreenSection\(""[^""]+"",\s*""([^""]+)""\)")
                              .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(katalogUst.Count, ustGruplar.Length);
        foreach (var b in katalogUst)
            Assert.Contains(ustGruplar, x => x.Ad == b);
    }

    /// <summary>Emoji alanı SİLİNMEDİ: web ve <c>MenuLayout</c> onu okur, aynı zamanda geri dönüş yoludur.
    /// İkon bulunamazsa grup ikonsuz çizilir — çökmez.</summary>
    [Fact]
    public void TSR11_Emoji_Alani_Duruyor_Ve_Cokme_Yok()
    {
        var nav = File.ReadAllText(Path.Combine(MasaustuKok(), "ViewModels", "Navigation.cs"));
        var eşleme = File.ReadAllText(Path.Combine(MasaustuKok(), "DesktopIcons.cs"));
        var pencere = Kaynak("Views", "MainWindow.axaml");

        Assert.Contains("public string Icon { get; }", nav);              // emoji alanı duruyor
        Assert.Contains("public bool HasIcon => IconGeometry is not null;", nav);
        Assert.Contains("return null;", eşleme);                          // kaynak yoksa null
        Assert.Contains($"IsVisible={T}{{Binding HasIcon}}{T}", pencere); // null ise gizlenir
    }

    /// <summary>Katalogdaki emoji alanı (web ile ortak) DEĞİŞTİRİLMEDİ — TÜM gruplar emojisini korur.
    /// BAR-01 turunda bulunan ESKİME düzeltildi (kök neden: sabit "17", FAZ 1-4'ün 7 yeni menü grubuyla
    /// —Projeler/Evrak/Ekipman/Zimmet/Satın Alma/İş Emirleri/Takvim vb.— çoktan eskimişti; tam paket o
    /// turlarda hedefli koşulduğundan görülmemişti). Sabit yerine sayı KATALOĞUN KENDİSİNE bağlandı:
    /// regex artık her yeni grupta da TÜM grupları yakalamak zorunda (sessizce 0 eşleşme imkânsız) ve
    /// emoji-boşaltma denetimi her grup için sürüyor — kilit gevşemedi, eskimeyen hâle geldi.</summary>
    [Fact]
    public void TSR12_Katalog_Emojileri_Degismedi()
    {
        var katalog = File.ReadAllText(Path.Combine(RepoKok(), "src", "DepoWise.Application", "Security", "AppScreens.cs"));

        Assert.Equal(DepoWise.Application.Security.AppScreens.Groups.Count,
            Regex.Matches(katalog, @"new AppScreenGroup\(").Count);
        foreach (Match m in Regex.Matches(katalog, @"new AppScreenGroup\(""[^""]+"",\s*""([^""]+)"""))
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[1].Value), "Grup emojisi boşaltılmış.");
    }
}
