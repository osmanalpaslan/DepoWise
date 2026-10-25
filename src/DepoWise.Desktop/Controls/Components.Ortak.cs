using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using CommunityToolkit.Mvvm.Input;

namespace DepoWise.Desktop.Controls;

// ═══ M-60 (AR-GE 2026-10-25, F-126 · tasarım belgesi 08 §3.2) — ORTAK EKRAN PARÇALARI ═══
// Opt-in bileşenler: mevcut ekranlar DEĞİŞMEZ; ekran dönüşümleri (M-63/M-64) bunları kullanır.
// Components.cs ile aynı kural: yalnız sunum (gösterim hesabı dahil), iş mantığı yok. Görseller Themes/ComponentThemes.axaml.

/// <summary>
/// TEK SAYFALAMA BİÇİMİ (U4: bugün 3 ayrı biçim vardı). Tablonun altında: "51–100 / 1.240 · ‹ Sayfa 2 / 25 › · Sayfa boyutu [50]".
/// <see cref="Page"/> ve <see cref="PageSize"/> iki yönlü bağlanır; ekran yalnız bu değerlere göre yükler.
/// Sayfa boyutu değişince sayfa 1'e döner; toplam azalınca sayfa son sayfaya kırpılır.
/// </summary>
public class Pager : TemplatedControl
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly StyledProperty<int> PageProperty =
        AvaloniaProperty.Register<Pager, int>(nameof(Page), 1, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<Pager, int>(nameof(PageSize), 50, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> TotalCountProperty =
        AvaloniaProperty.Register<Pager, int>(nameof(TotalCount));
    /// <summary>Sayfa boyutu seçicisi görünsün mü (sabit sayfa boyutlu ekranlarda kapatılır).</summary>
    public static readonly StyledProperty<bool> ShowPageSizeProperty =
        AvaloniaProperty.Register<Pager, bool>(nameof(ShowPageSize), true);
    /// <summary>⭐ M-64: numaralı sayfa düğmeleri ("‹ 1 2 [3] 4 5 ›") — kullanıcının 2026-07-18 isteğiyle numaralı sayfalaması olan
    /// ekranlar (Malzemeler, Araçlar) için; kapalıyken "Sayfa x / y" metni görünür.</summary>
    public static readonly StyledProperty<bool> ShowPageNumbersProperty =
        AvaloniaProperty.Register<Pager, bool>(nameof(ShowPageNumbers));
    public static readonly StyledProperty<IReadOnlyList<int>> PageSizesProperty =
        AvaloniaProperty.Register<Pager, IReadOnlyList<int>>(nameof(PageSizes), new[] { 25, 50, 100, 200 });

    public static readonly DirectProperty<Pager, string> SummaryProperty =
        AvaloniaProperty.RegisterDirect<Pager, string>(nameof(Summary), o => o.Summary);
    public static readonly DirectProperty<Pager, string> PageTextProperty =
        AvaloniaProperty.RegisterDirect<Pager, string>(nameof(PageText), o => o.PageText);
    public static readonly DirectProperty<Pager, bool> CanPreviousProperty =
        AvaloniaProperty.RegisterDirect<Pager, bool>(nameof(CanPrevious), o => o.CanPrevious);
    public static readonly DirectProperty<Pager, bool> CanNextProperty =
        AvaloniaProperty.RegisterDirect<Pager, bool>(nameof(CanNext), o => o.CanNext);
    public static readonly DirectProperty<Pager, IReadOnlyList<SayfaNo>> PageNumbersProperty =
        AvaloniaProperty.RegisterDirect<Pager, IReadOnlyList<SayfaNo>>(nameof(PageNumbers), o => o.PageNumbers);

    public int Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }
    public int PageSize { get => GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }
    public int TotalCount { get => GetValue(TotalCountProperty); set => SetValue(TotalCountProperty, value); }
    public IReadOnlyList<int> PageSizes { get => GetValue(PageSizesProperty); set => SetValue(PageSizesProperty, value); }
    public bool ShowPageSize { get => GetValue(ShowPageSizeProperty); set => SetValue(ShowPageSizeProperty, value); }
    public bool ShowPageNumbers { get => GetValue(ShowPageNumbersProperty); set => SetValue(ShowPageNumbersProperty, value); }

    /// <summary>Numaralı düğmedeki tek sayfa; <see cref="Aktif"/> = bulunulan sayfa (vurgulu).</summary>
    public sealed record SayfaNo(int No, bool Aktif);
    private IReadOnlyList<SayfaNo> _pageNumbers = Array.Empty<SayfaNo>();
    public IReadOnlyList<SayfaNo> PageNumbers { get => _pageNumbers; private set => SetAndRaise(PageNumbersProperty, ref _pageNumbers, value); }
    public ICommand GoToCommand { get; }

    private string _summary = "0 kayıt", _pageText = "Sayfa 1 / 1";
    private bool _canPrevious, _canNext;
    public string Summary { get => _summary; private set => SetAndRaise(SummaryProperty, ref _summary, value); }
    public string PageText { get => _pageText; private set => SetAndRaise(PageTextProperty, ref _pageText, value); }
    public bool CanPrevious { get => _canPrevious; private set => SetAndRaise(CanPreviousProperty, ref _canPrevious, value); }
    public bool CanNext { get => _canNext; private set => SetAndRaise(CanNextProperty, ref _canNext, value); }

    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }

    public Pager()
    {
        PreviousCommand = new RelayCommand(() => { if (Page > 1) Page--; });
        NextCommand = new RelayCommand(() => { if (Page < SayfaSayisi(TotalCount, PageSize)) Page++; });
        GoToCommand = new RelayCommand<int>(p => { if (p >= 1 && p <= SayfaSayisi(TotalCount, PageSize)) Page = p; });
        Yenile();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == PageSizeProperty) { if (Page != 1) Page = 1; }
        if (e.Property == PageProperty || e.Property == PageSizeProperty || e.Property == TotalCountProperty)
        {
            var son = SayfaSayisi(TotalCount, PageSize);
            if (Page > son) { Page = son; return; }   // toplam azaldı → son sayfaya kırp (yeniden tetiklenir)
            if (Page < 1) { Page = 1; return; }
            Yenile();
        }
    }

    private void Yenile()
    {
        var son = SayfaSayisi(TotalCount, PageSize);
        Summary = Ozet(Page, PageSize, TotalCount);
        PageText = $"Sayfa {Page.ToString("N0", Tr)} / {son.ToString("N0", Tr)}";
        CanPrevious = Page > 1;
        CanNext = Page < son;
        PageNumbers = Numaralar(Page, son).Select(n => new SayfaNo(n, n == Page)).ToList();
    }

    /// <summary>Gösterilecek sayfa numaraları: bulunulan sayfanın ±3 komşusu (eski Malzemeler/Araçlar davranışı).</summary>
    public static IReadOnlyList<int> Numaralar(int sayfa, int son)
    {
        var bas = Math.Max(1, sayfa - 3);
        var bit = Math.Min(son, sayfa + 3);
        var l = new List<int>();
        for (var p = bas; p <= bit; p++) l.Add(p);
        return l;
    }

    /// <summary>Toplam sayfa (en az 1).</summary>
    public static int SayfaSayisi(int toplam, int boyut) => boyut <= 0 ? 1 : Math.Max(1, (toplam + boyut - 1) / boyut);

    /// <summary>"51–100 / 1.240" — boşsa "0 kayıt".</summary>
    public static string Ozet(int sayfa, int boyut, int toplam)
    {
        if (toplam <= 0 || boyut <= 0) return "0 kayıt";
        var bas = (Math.Max(1, sayfa) - 1) * boyut + 1;
        var son = Math.Min(toplam, Math.Max(1, sayfa) * boyut);
        return $"{bas.ToString("N0", Tr)}–{son.ToString("N0", Tr)} / {toplam.ToString("N0", Tr)}";
    }
}

/// <summary>
/// SEÇİM ÇUBUĞU (bağlamsal komut çubuğu, U1/U3): tablonun üstünde; seçim YOKKEN görünmez, seçim varken
/// "N kayıt seçili · [eylemler] · Seçimi kaldır". Eylemler <c>Content</c> olarak verilir (yıkıcı eylem en sağda, Danger).
/// </summary>
public class SelectionBar : ContentControl
{
    public static readonly StyledProperty<int> SelectedCountProperty =
        AvaloniaProperty.Register<SelectionBar, int>(nameof(SelectedCount));
    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<SelectionBar, ICommand?>(nameof(ClearCommand));
    public static readonly DirectProperty<SelectionBar, string> TextProperty =
        AvaloniaProperty.RegisterDirect<SelectionBar, string>(nameof(Text), o => o.Text);

    public int SelectedCount { get => GetValue(SelectedCountProperty); set => SetValue(SelectedCountProperty, value); }
    public ICommand? ClearCommand { get => GetValue(ClearCommandProperty); set => SetValue(ClearCommandProperty, value); }

    private string _text = "";
    public string Text { get => _text; private set => SetAndRaise(TextProperty, ref _text, value); }

    public SelectionBar() => Guncelle();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SelectedCountProperty) Guncelle();
    }

    private void Guncelle()
    {
        Text = SelectedCount == 1 ? "1 kayıt seçili" : $"{SelectedCount.ToString("N0", CultureInfo.GetCultureInfo("tr-TR"))} kayıt seçili";
        PseudoClasses.Set(":has-selection", SelectedCount > 0);
    }
}

/// <summary>
/// SAYFA İÇİ UYARI BANDI (InfoBar): bilgi / uyarı / hata / başarı; isteğe bağlı eylem düğmesi ve kapatma.
/// Bugün elle yazılan "Tüm Şubeler modundasınız…" bandının ortak hâli. Renk tek başına anlam taşımaz: başlık/metin de yazılır.
/// </summary>
public class InfoBar : TemplatedControl
{
    public static readonly StyledProperty<BadgeKind> KindProperty =
        AvaloniaProperty.Register<InfoBar, BadgeKind>(nameof(Kind), BadgeKind.Info);
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<InfoBar, string?>(nameof(Title));
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfoBar, string?>(nameof(Message));
    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<InfoBar, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<InfoBar, ICommand?>(nameof(ActionCommand));
    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<InfoBar, bool>(nameof(IsClosable));

    public BadgeKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string? ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public bool IsClosable { get => GetValue(IsClosableProperty); set => SetValue(IsClosableProperty, value); }

    /// <summary>Kapat (✕) — yalnız bu bandı gizler; kalıcı bir ayar yazmaz.</summary>
    public ICommand CloseCommand { get; }

    public InfoBar()
    {
        CloseCommand = new RelayCommand(() => IsVisible = false);
        UpdatePseudo();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == KindProperty) UpdatePseudo();
    }

    private void UpdatePseudo()
    {
        PseudoClasses.Set(":success", Kind == BadgeKind.Success);
        PseudoClasses.Set(":warning", Kind == BadgeKind.Warning);
        PseudoClasses.Set(":danger", Kind == BadgeKind.Danger);
        PseudoClasses.Set(":info", Kind is BadgeKind.Info or BadgeKind.Neutral);
    }
}
