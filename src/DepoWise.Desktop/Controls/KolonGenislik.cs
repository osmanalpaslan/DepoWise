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
        baslik.ContextMenu ??= MenuKur(baslik);

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

        Kaydol(anahtar, grid);
        Tutamaklar(grid, anahtar);
        Uygula(grid, anahtar);
        foreach (var g in liste.GetVisualDescendants().OfType<Grid>().Where(ColumnRules.GetEnabled))
        { Kaydol(anahtar, g); Uygula(g, anahtar); }
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

    /// <summary>Oturum deposundaki genişlikleri grid'e uygular. Hiç sürüklenmemiş tabloya dokunmaz.</summary>
    private static void Uygula(Grid grid, string anahtar)
    {
        if (!_oturum.TryGetValue(anahtar, out var harita) || harita.Count == 0) return;
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
                if (c is TextBlock tb && tb.TextTrimming == TextTrimming.None) tb.TextTrimming = TextTrimming.CharacterEllipsis;
            }
        }
    }

    // ───────────────────────── sağ tık menüsü ─────────────────────────

    private static ContextMenu MenuKur(Border baslik)
    {
        var menu = new ContextMenu();
        var ayarla = new MenuItem { Header = "Kolonları Ayarla" };
        var kaydet = new MenuItem { Header = "Kolon Ayarlarını Kaydet" };

        ICommand? SeciciKomutu()
        {
            var dc = baslik.FindAncestorOfType<UserControl>()?.DataContext;
            return dc?.GetType().GetProperty("OpenColumnPickerCommand")?.GetValue(dc) as ICommand;
        }

        ayarla.Click += (_, _) => { var k = SeciciKomutu(); if (k?.CanExecute(null) == true) k.Execute(null); };
        kaydet.Click += async (_, _) =>
        {
            try
            {
                TabloyuKaydet(baslik);
                await ConfirmService.InfoAsync("Kolon ayarları bu bilgisayara kaydedildi. Uygulama yeniden açıldığında da bu genişlikler kullanılır.",
                    "Kolon Ayarları");
            }
            catch (Exception ex) { await ConfirmService.InfoAsync("Kolon ayarları kaydedilemedi: " + ex.Message, "Kolon Ayarları", danger: true); }
        };
        // "Kolonları Ayarla" yalnız kolon seçicisi olan ekranlarda görünür.
        menu.Opening += (_, _) => ayarla.IsVisible = SeciciKomutu() is not null;

        menu.Items.Add(ayarla);
        menu.Items.Add(kaydet);
        return menu;
    }

    private static void TabloyuKaydet(Border baslik)
    {
        var dc = baslik.FindAncestorOfType<UserControl>()?.DataContext;
        if (dc is IListGridViewModel vm)
            Kalici["vm:" + dc.GetType().Name] = new Dictionary<string, double>(vm.ColWidths);
        else if (baslik.GetValue(TabloProperty) is { } anahtar)
        {
            if (_oturum.TryGetValue(anahtar, out var h) && h.Count > 0) Kalici[anahtar] = new Dictionary<string, double>(h);
            else Kalici.Remove(anahtar);
        }
        DosyayaYaz();
    }
}
