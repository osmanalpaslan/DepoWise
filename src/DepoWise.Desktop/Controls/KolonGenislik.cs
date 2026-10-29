using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ TÜM TABLOLARDA SÜTUN GENİŞLİĞİ + SAĞ TIK MENÜSÜ ═══ (kullanıcı isteği 2026-09-30)
///
/// <b>Sütun genişliği.</b> <see cref="ColumnRules"/> kullanan her tablonun BAŞLIĞINA, kolon sınırlarında
/// sürükleme tutamağı eklenir. Sürüklenen genişlik başlık + o tablonun TÜM satır grid'lerine aynı anda
/// uygulanır (tek kaynak: <see cref="_oturum"/>) → hiza bozulmaz. Başlığın altında eşleşen bir
/// <c>ListBox.Table</c> bulunamazsa tutamak HİÇ eklenmez (yarım uygulama → hizasızlık riski yok).
/// Hiç sürüklenmemiş tabloya DOKUNULMAZ — bugünkü düzen aynen kalır.
///
/// <b>Kalıcılık yalnız "Kolon Ayarlarını Kaydet" ile</b> (sağ tık). 2026-08-08'de otomatik kayıt 15 sn'lik
/// eşitlemeyle sorun çıkardığı için kaldırılmıştı; bu yüzden kayıt <b>yerel bir dosyaya</b> yazılır
/// (<c>%LOCALAPPDATA%\DepoWise\kolon-genislikleri.json</c>) — veritabanına/sunucuya GİTMEZ, eşitleme ile
/// çakışamaz. Eşitleme ekranı yenileyip satırları yeniden kursa bile genişlik oturum deposundan tekrar uygulanır.
///
/// <b>Filtre satırlı listeler</b> (Malzemeler/Araçlar/Günlük Faaliyet, <see cref="IListGridViewModel"/>) kendi
/// sürükleme mekanizmasına (SortHeader) sahiptir: genel tutamak onlara EKLENMEZ; "Kaydet" onların VM
/// genişliklerini yazar, açılışta <see cref="VmGenislikleri"/> geri yükler.
///
/// <b>Sağ tık menüsü</b> her tablonun başlığında: "Kolonları Ayarla" (yalnız kolon seçicisi olan ekranlarda) ve
/// "Kolon Ayarlarını Kaydet". Yeni maddeler <see cref="MenuKur"/>'a eklenir.
///
/// Tamamen GÖRSELDİR: her adım try/catch içinde — çalışan bir ekran asla düşmez.
/// </summary>
public static class KolonGenislik
{
    private static readonly string Dosya = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "kolon-genislikleri.json");

    /// <summary>Kaydedilmiş genişlikler (dosyadaki). Anahtar: tablo ("Ekran#n") ya da "vm:EkranVM".</summary>
    private static Dictionary<string, Dictionary<string, double>>? _kalici;
    private static Dictionary<string, Dictionary<string, double>> Kalici => _kalici ??= Yukle();

    /// <summary>Oturumdaki (sürüklenmiş, henüz kaydedilmemiş olabilir) genişlikler — açılışta kayıtlılarla başlar.</summary>
    private static Dictionary<string, Dictionary<string, double>>? _oturumDepo;
    private static Dictionary<string, Dictionary<string, double>> _oturum
        => _oturumDepo ??= Kalici.ToDictionary(x => x.Key, x => new Dictionary<string, double>(x.Value));

    private static readonly Dictionary<string, List<WeakReference<Grid>>> _kayit = new();
    /// <summary>Esnek sütunların görünür genişlikten hesaplanan OTOMATİK genişlikleri (kalıcı kayda yazılmaz; kullanıcının ayarı önce gelir).</summary>
    private static readonly Dictionary<string, Dictionary<string, double>> _otomatik = new();
    private static readonly ConditionalWeakTable<ListBox, string> _listeAnahtari = new();
    /// <summary>Tablo başlığının kolon sayısı — yalnız aynı düzendeki satır grid'lerine uygulanır.</summary>
    private static readonly Dictionary<string, int> _kolonSayisi = new();

    private static readonly AttachedProperty<bool> BagliProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>("KgBagli", typeof(KolonGenislik));
    private static readonly AttachedProperty<string?> TabloProperty =
        AvaloniaProperty.RegisterAttached<Border, string?>("KgTablo", typeof(KolonGenislik));

    // ───────────────────────── kalıcı depo ─────────────────────────

    private static Dictionary<string, Dictionary<string, double>> Yukle()
    {
        try
        {
            if (File.Exists(Dosya))
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(Dosya)) ?? new();
        }
        catch { /* bozuk dosya: varsayılan genişliklerle devam */ }
        return new();
    }

    private static void DosyayaYaz()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dosya)!);
        var gecici = Dosya + ".tmp";
        File.WriteAllText(gecici, JsonSerializer.Serialize(Kalici));
        File.Move(gecici, Dosya, overwrite: true);   // yarım yazılmış dosya kalmaz
    }

    /// <summary>Filtre satırlı liste VM'leri için açılış genişlikleri: kayıtlı varsa onun üstüne, yoksa varsayılan.</summary>
    public static Dictionary<string, double> VmGenislikleri(string vmAd, IReadOnlyDictionary<string, double> varsayilan)
    {
        var sonuc = new Dictionary<string, double>(varsayilan);
        try
        {
            if (Kalici.TryGetValue("vm:" + vmAd, out var kayit))
                foreach (var (k, w) in kayit) if (w >= 30) sonuc[k] = w;
        }
        catch { }
        return sonuc;
    }

    // ───────────────────────── bağlama ─────────────────────────

    /// <summary><see cref="ColumnRules"/> her tablo grid'i için çağırır (görsel ağaca bağlandıktan sonra).</summary>
    public static void Bagla(Grid grid)
    {
        try
        {
            if (grid.GetValue(BagliProperty)) return;
            grid.SetValue(BagliProperty, true);

            var baslik = grid.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("TableHeader"));
            if (baslik is not null) { BaslikBagla(grid, baslik); return; }

            // Satır grid'i: kendi listesinin tablo anahtarı biliniyorsa genişlikleri uygula.
            var liste = grid.GetVisualAncestors().OfType<ListBox>().FirstOrDefault();
            if (liste is not null && _listeAnahtari.TryGetValue(liste, out var anahtar))
            { Kaydol(anahtar, grid); Uygula(grid, anahtar); }
        }
        catch { /* yalnız görsel: ekranı asla düşürme */ }
    }

    private static void BaslikBagla(Grid grid, Border baslik)
    {
        var ekran = baslik.FindAncestorOfType<UserControl>();
        if (ekran is null) return;
        BasliklariDuzenle(grid);   // "İÇ KOD" → "İç Kod" (tüm tablolar)
        var vmEkrani = ekran.DataContext as IListGridViewModel;
        EkranaBagla(ekran, vmEkrani is not null
                ? () => Kalici["vm:" + ekran.DataContext!.GetType().Name] = new Dictionary<string, double>(vmEkrani.ColWidths)
                : () => GenelTabloyuKaydet(baslik),
            () => SeciciEylemi(ekran.DataContext));

        // Filtre satırlı listeler kendi sürükleme mekanizmasını kullanır (SortHeader → VM.ColWidths).
        if (ekran.DataContext is IListGridViewModel) return;

        var basliklar = ekran.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("TableHeader")).ToList();
        var anahtar = $"{ekran.GetType().Name}#{basliklar.IndexOf(baslik)}";
        baslik.SetValue(TabloProperty, anahtar);

        // Gövde, başlıkla AYNI kapsayıcının DOĞRUDAN çocuğu olmalı (standart desen: DockPanel{başlık, ListBox}).
        // Daha derinde aranırsa başka bir tablonun listesi yakalanıp genişlik yanlış tabloya uygulanabilirdi.
        var liste = (baslik.Parent as Panel)?.Children.OfType<ListBox>().FirstOrDefault(l => l.Classes.Contains("Table"));
        if (liste is null) return;   // eşleşen gövde yok → genişlik ayarı açılmaz
        _listeAnahtari.AddOrUpdate(liste, anahtar);
        _kolonSayisi[anahtar] = grid.ColumnDefinitions.Count;

        // Uygula hücrelerin MinWidth/MaxWidth'ini ezer → tasarımdaki asgari genişlik ve "üst sınırsız" bilgisi ÖNCE alınır.
        var ilkTanim = grid.ColumnDefinitions.Select(c => c.Width).ToArray();
        var asgariler = Enumerable.Range(0, grid.ColumnDefinitions.Count).Select(k => grid.Children
            .Where(c => Grid.GetColumn(c) == k && !ColumnRules.KuralMi(c))
            .Select(c => c.MinWidth + c.Margin.Left + c.Margin.Right).DefaultIfEmpty(0).Max()).ToArray();
        var sinirsiz = Enumerable.Range(0, grid.ColumnDefinitions.Count).Select(k => grid.Children
            .Any(c => Grid.GetColumn(c) == k && !ColumnRules.KuralMi(c) && double.IsInfinity(c.MaxWidth))).ToArray();
        Kaydol(anahtar, grid);
        Tutamaklar(grid, anahtar);
        Uygula(grid, anahtar);
        foreach (var g in liste.GetVisualDescendants().OfType<Grid>().Where(ColumnRules.GetEnabled))
        { Kaydol(anahtar, g); Uygula(g, anahtar); }

        // ═══ 2026-10-29 KALICI KURAL (kullanıcı isteği; .claude/rules/list-screens.md Kural 3): bir sütunun verisi diğer sütuna
        // TAŞMAZ, sütun konumları listelenen veriye göre DEĞİŞMEZ. Neden kayıyordu: ListBox.Table yatayda kaydırılabilir → satır
        // grid'leri SINIRSIZ genişlikte ölçülür; esnek (*) / üst sınırsız sütunlar HER SATIRDA kendi içeriğine göre genişler
        // (ölçüldü: aynı tabloda "Malzeme" satıra göre 200–430 px; işlem düğmesi olmayan satırda kalan alan da esnek sütuna geçer).
        // Çözüm: bu sütunların genişliği TABLONUN GÖRÜNÜR GENİŞLİĞİNDEN hesaplanıp piksel olarak sabitlenir → başlık, filtre satırı
        // ve TÜM satırlar aynı genişliği kullanır; uzun yazı "…" ile kesilir. Yalnız taşmada değil HER ZAMAN uygulanır (2026-10-29
        // ilk sürüm yalnız taşmada çalışıyordu → taşmayan tabloda satırlar yine kaydı). Pencere/çerçeve genişliği değişince
        // yeniden hesaplanır (ilk açılıştaki geçici dar ölçüm de böylece düzelir). Kullanıcının sürüklediği sütun (oturum
        // deposu) hesaba girmez, onun değeri geçerlidir. Otomatik değerler ayrı depoda: "Kaydet" yalnız kullanıcının ayarını yazar.
        foreach (var fr in ((Panel)baslik.Parent!).Children.OfType<Border>().Where(b => b.Classes.Contains("TableFilterRow")))
            foreach (var g in fr.GetVisualDescendants().OfType<Grid>()
                         .Where(g => ColumnRules.GetEnabled(g) && g.ColumnDefinitions.Count == grid.ColumnDefinitions.Count))
            { Kaydol(anahtar, g); Uygula(g, anahtar); }
        var sonGenislik = -1.0;
        void Tohumla(object? s, EventArgs e)
        {
            try
            {
                // görünür genişlik = tablonun ÇERÇEVESİ (Border.Table); liste kutusunun kendisi uzun satırlarla genişleyebilir
                var cerceve = baslik.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("Table"));
                var gorunurGenislik = cerceve?.Bounds.Width ?? liste.Bounds.Width;
                if (gorunurGenislik <= 0 || !baslik.IsEffectivelyVisible) return;
                if (Math.Abs(gorunurGenislik - sonGenislik) < 1) return;   // çerçeve değişmedi → hesap aynı
                sonGenislik = gorunurGenislik;
                _oturum.TryGetValue(anahtar, out var kullanici);
                // Esneklik ÖZGÜN tanımdan: TableHeaderSync satırdan kopyalamadan önce sakladığı tanım (başlık sekme değişiminde
                // yeniden bağlanırsa bağlanma anındaki tanım zaten piksele çevrilmiş olabilir — ölçüldü).
                var oz = TableHeaderSync.OzgunTanim(baslik);
                if (oz is not null && oz.Length == ilkTanim.Length) ilkTanim = oz;
                bool Elle(int k) => kullanici is not null && kullanici.ContainsKey(k.ToString());
                // TableHeaderSync başlığa satırın PİKSEL genişliğini kopyalar → esneklik ÖZGÜN tanımdan okunur.
                var gorunur = gorunurGenislik - baslik.Padding.Left - baslik.Padding.Right - 6;   // kenar + yuvarlama payı
                var sabitToplam = 0.0; var yildizToplam = 0.0;
                for (int k = 0; k < grid.ColumnDefinitions.Count; k++)
                {
                    var cd = grid.ColumnDefinitions[k];
                    if (Elle(k)) sabitToplam += kullanici![k.ToString()];
                    else if (ilkTanim[k].IsStar) { if (Gorunur(k)) yildizToplam += ilkTanim[k].Value; }
                    else if (Degisken(k)) sabitToplam += DegiskenGenislik(k);
                    // sabit kolon: tasarımdaki genişlik (ActualWidth ilk turlarda satırdan kopyalanmış/şişmiş olabilir)
                    else if (Gorunur(k)) sabitToplam += asgariler[k] > 0 ? asgariler[k] : cd.ActualWidth;
                }
                var kalan = Math.Max(0, gorunur - sabitToplam);
                var oto = new Dictionary<string, double>();
                for (int k = 0; k < grid.ColumnDefinitions.Count; k++)
                {
                    if (Elle(k) || !Gorunur(k)) continue;                             // kullanıcının ayarı / gizli kolon
                    if (ilkTanim[k].IsStar)
                        oto[k.ToString()] = Math.Floor(Math.Max(Math.Max(asgariler[k], 40),
                            yildizToplam > 0 ? kalan * ilkTanim[k].Value / yildizToplam : 0));
                    else if (Degisken(k)) oto[k.ToString()] = Math.Round(DegiskenGenislik(k));
                }
                _otomatik[anahtar] = oto;
                // Bağlanma anında henüz çizilmemiş satırlar da kayda girsin (sanallaştırma / şablon sırası).
                foreach (var g in liste.GetVisualDescendants().OfType<Grid>()
                             .Where(g => ColumnRules.GetEnabled(g) && g.ColumnDefinitions.Count == grid.ColumnDefinitions.Count))
                    Kaydol(anahtar, g);
                TumunuUygula(anahtar);
            }
            catch { /* yalnız görsel */ }

            // Gizli kolon (ör. fiyat yetkisi kapalı) dondurulmaz: hücresi görünmüyor ve asgari genişliği yok.
            bool Gorunur(int k) => grid.Children.Any(c => Grid.GetColumn(c) == k && !ColumnRules.KuralMi(c) && c.IsVisible);
            // Auto ama üst sınırsız: içerikle büyürdü → tasarımdaki asgari genişlikte (en az 120 px) sabitlenir.
            bool Degisken(int k) => !ilkTanim[k].IsStar && sinirsiz[k];
            double DegiskenGenislik(int k) => Math.Max(asgariler[k], 120);
        }
        grid.LayoutUpdated += Tohumla;
    }


    private static void Kaydol(string anahtar, Grid grid)
    {
        if (!_kayit.TryGetValue(anahtar, out var l)) _kayit[anahtar] = l = new();
        l.RemoveAll(w => !w.TryGetTarget(out _));
        if (!l.Any(w => w.TryGetTarget(out var g) && ReferenceEquals(g, grid))) l.Add(new WeakReference<Grid>(grid));
    }

    // ───────────────────────── sürükleme ─────────────────────────

    private static void Tutamaklar(Grid grid, string anahtar)
    {
        for (int i = 0; i < grid.ColumnDefinitions.Count - 1; i++)
        {
            var kolon = i;
            var tutamak = new Border
            {
                Width = 8,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, -4, 0),
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                ZIndex = 10,
            };
            ToolTip.SetTip(tutamak, "Sürükleyerek sütunu genişletin/daraltın");
            ColumnRules.KuralIsaretle(tutamak);   // hücre sayılmasın (ayırıcı çizgi mantığı bozulmasın)
            Grid.SetColumn(tutamak, kolon);

            double basX = 0, basW = 0; bool suruk = false;
            tutamak.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(tutamak).Properties.IsLeftButtonPressed) return;
                suruk = true; basX = e.GetPosition(null).X;   // pencere çerçevesi: sabit (SortHeader ile aynı gerekçe)
                basW = grid.ColumnDefinitions[kolon].ActualWidth;
                e.Pointer.Capture(tutamak); e.Handled = true;
            };
            tutamak.PointerMoved += (_, e) =>
            {
                if (!suruk) return;
                var w = Math.Clamp(basW + (e.GetPosition(null).X - basX), 30, 1000);
                if (!_oturum.TryGetValue(anahtar, out var harita)) _oturum[anahtar] = harita = new();
                harita[kolon.ToString()] = Math.Round(w);
                TumunuUygula(anahtar);
            };
            tutamak.PointerReleased += (_, e) => { if (suruk) { suruk = false; e.Pointer.Capture(null); } };
            grid.Children.Add(tutamak);
        }
    }

    private static void TumunuUygula(string anahtar)
    {
        if (!_kayit.TryGetValue(anahtar, out var l)) return;
        foreach (var w in l.ToList()) if (w.TryGetTarget(out var g)) Uygula(g, anahtar);
    }

    /// <summary>Otomatik + oturum (kullanıcı) genişliklerini grid'e uygular; kullanıcının değeri otomatiği ezer.</summary>
    private static void Uygula(Grid grid, string anahtar)
    {
        var harita = new Dictionary<string, double>();
        if (_otomatik.TryGetValue(anahtar, out var oto)) foreach (var (k, w) in oto) harita[k] = w;
        if (_oturum.TryGetValue(anahtar, out var elle)) foreach (var (k, w) in elle) harita[k] = w;
        if (harita.Count == 0) return;
        // Başlıkla AYNI kolon düzenine sahip olmayan grid'e uygulanmaz (yanlış kolona genişlik → hiza bozulurdu).
        if (!_kolonSayisi.TryGetValue(anahtar, out var adet) || grid.ColumnDefinitions.Count != adet) return;
        foreach (var (kStr, w) in harita)
        {
            if (!int.TryParse(kStr, out var k) || k < 0 || k >= grid.ColumnDefinitions.Count) continue;
            var cd = grid.ColumnDefinitions[k];
            cd.SharedSizeGroup = null;            // ortak grup en büyük ölçüyü dayatırdı → daraltma çalışmazdı
            cd.Width = new GridLength(w);
            foreach (var c in grid.Children.Where(c => Grid.GetColumn(c) == k && !ColumnRules.KuralMi(c)))
            {
                c.MinWidth = 0;
                c.MaxWidth = w;
                if (c is TextBlock tb)
                {
                    if (tb.TextTrimming == TextTrimming.None) tb.TextTrimming = TextTrimming.CharacterEllipsis;
                    // kesilen yazının tamamı ipucunda (Kural 3): satır yeniden kullanılınca da güncel kalsın diye bağlama
                    if (ToolTip.GetTip(tb) is null) tb[!ToolTip.TipProperty] = tb[!TextBlock.TextProperty];
                }
            }
        }
    }

    /// <summary>Genel tablonun (liste başlığı) oturum genişliklerini kalıcı depoya alır.</summary>
    private static void GenelTabloyuKaydet(Border baslik)
    {
        if (baslik.GetValue(TabloProperty) is not { } anahtar) return;
        if (_oturum.TryGetValue(anahtar, out var h) && h.Count > 0) Kalici[anahtar] = new Dictionary<string, double>(h);
        else Kalici.Remove(anahtar);
    }

    /// <summary>
    /// 2026-10-01 (kullanıcı isteği: tablo güncellemeleri her ekranda) — başlık yazıları TAMAMEN BÜYÜK harften
    /// "İç Kod" biçimine (<see cref="SortHeader.BaslikMetni"/> ile AYNI kural). Yalnız SABİT yazılar çevrilir;
    /// veriye bağlı (binding) başlığa dokunulmaz — değeri dondurup bağı koparmamak için.
    /// </summary>
    private static void BasliklariDuzenle(Grid baslikGrid)
    {
        foreach (var tb in baslikGrid.GetVisualDescendants().OfType<TextBlock>().ToList())
        {
            if (string.IsNullOrEmpty(tb.Text)) continue;
            if (Avalonia.Data.BindingOperations.GetBindingExpressionBase(tb, TextBlock.TextProperty) is not null) continue;
            var yeni = SortHeader.BaslikMetni(tb.Text);
            if (yeni != tb.Text) tb.Text = yeni;
        }
    }

    // ───────────────────────── ekran geneli sağ tık menüsü ─────────────────────────
    // 2026-10-01 (kullanıcı isteği): menü yalnız başlıkta değil, tablo bulunan ekranın HER YERİNDE açılır.
    // Bir ekrandaki tüm tablolar (liste başlıkları + rapor tablosu) aynı kayda bağlanır: "Kaydet" hepsini yazar.
    // Metin kutuları kendi menüsünü (kes/kopyala/yapıştır) korur; hücrede yazı SEÇİLİYKEN kopyalama menüsü çıkar.

    private sealed class EkranKaydi
    {
        public readonly List<Action> Kaydediciler = new();
        public readonly List<Func<Action?>> Ayarlayicilar = new();
    }
    private static readonly ConditionalWeakTable<UserControl, EkranKaydi> _ekranlar = new();

    /// <summary>Bir tabloyu, bulunduğu ekranın sağ tık menüsüne bağlar.</summary>
    /// <param name="ekran">Tablonun bulunduğu ekran (görünüm).</param>
    /// <param name="kaydet">"Kolon Ayarlarını Kaydet" anında bu tablonun genişliklerini <see cref="Kalici"/>'ya yazar.</param>
    /// <param name="ayarla">"Kolonları Ayarla" eylemi (yoksa null döner → madde gizlenir).</param>
    public static void EkranaBagla(UserControl ekran, Action kaydet, Func<Action?>? ayarla)
    {
        var kayit = _ekranlar.GetValue(ekran, e =>
        {
            var k = new EkranKaydi();
            e.AddHandler(Control.ContextRequestedEvent, (_, a) => MenuAc(e, k, a), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            return k;
        });
        kayit.Kaydediciler.Add(kaydet);
        if (ayarla is not null) kayit.Ayarlayicilar.Add(ayarla);
    }

    private static void MenuAc(UserControl ekran, EkranKaydi kayit, ContextRequestedEventArgs e)
    {
        try
        {
            var kaynak = e.Source as Visual;
            // Metin kutusu kendi menüsünü açar; seçili yazı varsa kopyalama menüsü çıksın.
            if (kaynak is TextBox || kaynak?.GetVisualAncestors().Any(a => a is TextBox) == true) return;
            var stb = kaynak as SelectableTextBlock ?? kaynak?.GetVisualAncestors().OfType<SelectableTextBlock>().FirstOrDefault();
            if (!string.IsNullOrEmpty(stb?.SelectedText)) return;

            var menu = new ContextMenu();
            var ayarlaEylemi = kayit.Ayarlayicilar.Select(f => f()).FirstOrDefault(a => a is not null);
            if (ayarlaEylemi is not null)
            {
                var ayarla = new MenuItem { Header = "Kolonları Ayarla" };
                ayarla.Click += (_, _) => ayarlaEylemi();
                menu.Items.Add(ayarla);
            }
            var kaydet = new MenuItem { Header = "Kolon Ayarlarını Kaydet" };
            kaydet.Click += async (_, _) =>
            {
                try
                {
                    foreach (var k in kayit.Kaydediciler.ToList()) k();
                    DosyayaYaz();
                    await ConfirmService.InfoAsync("Kolon ayarları bu bilgisayara kaydedildi. Uygulama yeniden açıldığında da bu genişlikler kullanılır.",
                        "Kolon Ayarları");
                }
                catch (Exception ex) { await ConfirmService.InfoAsync("Kolon ayarları kaydedilemedi: " + ex.Message, "Kolon Ayarları", danger: true); }
            };
            menu.Items.Add(kaydet);
            menu.Open(ekran);
            e.Handled = true;
        }
        catch { /* menü açılamazsa ekran etkilenmez */ }
    }

    /// <summary>Filtre satırlı liste VM'inin kolon seçicisi (OpenColumnPickerCommand) — yoksa null.</summary>
    internal static Action? SeciciEylemi(object? dc)
        => dc?.GetType().GetProperty("OpenColumnPickerCommand")?.GetValue(dc) is ICommand k ? () => { if (k.CanExecute(null)) k.Execute(null); } : null;

    // ───────────────────────── rapor tablosu (DataGridView) ─────────────────────────

    /// <summary>Rapor tablosunun kayıtlı genişlikleri (kolon anahtarı → px); kayıt yoksa null.</summary>
    public static IReadOnlyDictionary<string, double>? RaporGenislikleri(string? raporAnahtari)
    {
        try { return raporAnahtari is not null && Kalici.TryGetValue("rapor:" + raporAnahtari, out var h) ? h : null; }
        catch { return null; }
    }

    /// <summary>Rapor tablosunun ŞU ANKİ genişliklerini kalıcı depoya alır (dosyaya <see cref="DosyayaYaz"/> yazar).</summary>
    public static void RaporKaydet(string? raporAnahtari, IEnumerable<(string Key, double Width)> kolonlar)
    {
        if (string.IsNullOrEmpty(raporAnahtari)) return;
        Kalici["rapor:" + raporAnahtari] = kolonlar.ToDictionary(k => k.Key, k => Math.Round(k.Width));
    }
}
