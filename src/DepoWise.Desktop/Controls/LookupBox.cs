using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using DepoWise.Application.Ui;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// "Sabit tanım" (lookup) seçim alanı (kullanıcı isteği 2026-08-08 / Prompt 1). Sol tık → açılır liste
/// (ilk 25 kayıt); tekrar tık → kapanır (aç-kapa döngüsü, yalnız alan içinde). Arama başlayınca da 25'lik
/// sayfalama; altta ‹ Önceki / Sonraki › + "Sayfa X/Y". Çekirdek (filtre+sayfalama) ortak <see cref="LookupPaging"/>
/// (test edilebilir). Kontrol TAMAMEN KODDA kurulur (SortHeader deseni) — yeni ControlTheme dosyası gerekmez,
/// mevcut "Field/Search/Ghost/Caption" stilleri kullanılır (görsel test edilemeyen ortamda "sessiz boş" riski en az).
///
/// Bağlanabilir: ItemsSource (tüm kayıtlar) · SelectedItem (TwoWay) · DisplayMember (varsayılan "Name") · PlaceholderText.
/// </summary>
public class LookupBox : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<LookupBox, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<LookupBox, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string?> DisplayMemberProperty =
        AvaloniaProperty.Register<LookupBox, string?>(nameof(DisplayMember), "Name");
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<LookupBox, string?>(nameof(PlaceholderText));

    /// <summary>
    /// Seçim değişti (İş #9, 2026-08-09). MVVM ekranları <c>SelectedItem</c> bağlayıp
    /// <c>partial void On...Changed</c> kullanır; KOD-ARKASI ekranlar (hızlı düzenleme pencereleri)
    /// için ComboBox'taki <c>SelectionChanged</c> ile aynı işi gören olay budur.
    /// </summary>
    public event EventHandler? SelectionChanged;

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public string? DisplayMember { get => GetValue(DisplayMemberProperty); set => SetValue(DisplayMemberProperty, value); }
    public string? PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

    private const int PageSize = 25;
    private readonly Border _field;
    private readonly TextBlock _display;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly TextBlock _pageText;
    private readonly Button _prev, _next;
    private readonly Border _popupBorder;
    private readonly Flyout _flyout;
    private int _page = 1;
    private bool _suppress;
    private DateTime _closedAt = DateTime.MinValue;

    private sealed record Row(string Display, object Item);

    public LookupBox()
    {
        _display = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        var chevron = new TextBlock { Text = "▾", Opacity = 0.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 1);
        var fieldContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        fieldContent.Children.Add(_display);
        fieldContent.Children.Add(chevron);

        _field = new Border { Child = fieldContent, HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.Hand) };
        _field.Classes.Add("LookupField");
        _field.PointerReleased += OnFieldClick;   // release'te (press'teki light-dismiss'ten SONRA) → toggle-guard çalışır
        Content = _field;

        _search = new TextBox { };
        _search.Classes.Add("Search");
        _search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { _page = 1; Refresh(); } };

        _list = new ListBox { MaxHeight = 240, SelectionMode = SelectionMode.Single };
        _list.ItemTemplate = new FuncDataTemplate<Row>((r, _) => new TextBlock { Text = r?.Display ?? "", Padding = new Thickness(2) }, true);
        _list.SelectionChanged += OnSelect;

        _prev = new Button { Content = "‹", Padding = new Thickness(10, 2) }; _prev.Classes.Add("Ghost");
        _prev.Click += (_, _) => { if (_page > 1) { _page--; Refresh(); } };
        _next = new Button { Content = "›", Padding = new Thickness(10, 2) }; _next.Classes.Add("Ghost");
        _next.Click += (_, _) => { _page++; Refresh(); };
        _pageText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _pageText.Classes.Add("Caption");
        Grid.SetColumn(_pageText, 1); Grid.SetColumn(_next, 2);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
        footer.Children.Add(_prev); footer.Children.Add(_pageText); footer.Children.Add(_next);

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(_search); panel.Children.Add(_list); panel.Children.Add(footer);
        _popupBorder = new Border { Padding = new Thickness(6), Child = panel };
        _popupBorder.Classes.Add("dw-lookup-popup");   // arka plan/kenar/köşe (presenter'ın kendi çerçevesi kapatıldı)

        _flyout = new Flyout { Content = _popupBorder, Placement = PlacementMode.BottomEdgeAlignedLeft };
        // FlyoutPresenter'ın VARSAYILAN MaxWidth'i (geniş alanlarda açılır listeyi daraltan sebep) + padding kaldırılır
        // → açılır liste TAM alan genişliğine sabitlenebilir (Border.Width OnFieldClick'te set edilir).
        _flyout.FlyoutPresenterClasses.Add("dw-lookup-presenter");
        _flyout.Closed += (_, _) => _closedAt = DateTime.UtcNow;

        UpdateDisplay();
    }

    private void OnFieldClick(object? sender, PointerReleasedEventArgs e)
    {
        // Aç-kapa döngüsü: açıkken alana tık → light-dismiss KAPATIR; hemen ardından gelen bu Click yeniden
        // açmasın (double-toggle önleme). Closed olayı <250 ms önce olduysa "bu tık kapattı" say → aç.
        if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < 250) return;
        _page = 1;
        _suppress = true; _search.Text = ""; _suppress = false;
        Refresh();
        _popupBorder.Width = System.Math.Max(Bounds.Width, 200);   // açılır liste TAM alan genişliğinde (kullanıcı isteği)
        _flyout.ShowAt(_field);
        Dispatcher.UIThread.Post(() => { try { _search.Focus(); } catch { } });
    }

    private void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (_list.SelectedItem is Row row)
        {
            SelectedItem = row.Item;
            _flyout.Hide();
        }
    }

    private void Refresh()
    {
        var all = (ItemsSource?.Cast<object>() ?? Enumerable.Empty<object>()).ToList();
        var res = LookupPaging.Apply(all, DisplayOf, _search.Text, _page, PageSize);
        _page = res.Page;
        _suppress = true;
        _list.ItemsSource = res.Items.Select(x => new Row(DisplayOf(x), x)).ToList();
        _list.SelectedItem = null;
        _suppress = false;
        _pageText.Text = $"Sayfa {res.Page}/{res.TotalPages}";
        _prev.IsEnabled = res.Page > 1;
        _next.IsEnabled = res.Page < res.TotalPages;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SelectedItemProperty || e.Property == PlaceholderTextProperty || e.Property == DisplayMemberProperty)
            UpdateDisplay();
        // Programatik atama da (ör. formu doldururken) olayı tetikler — ComboBox.SelectionChanged ile aynı davranış.
        if (e.Property == SelectedItemProperty) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisplay()
    {
        if (SelectedItem is { } sel) { _display.Text = DisplayOf(sel); _display.Opacity = 1; }
        else { _display.Text = PlaceholderText ?? ""; _display.Opacity = 0.5; }
    }

    // Reflection ile görünen metin (DisplayMember; varsayılan "Name"). Tip başına PropertyInfo önbelleklenir.
    private static readonly Dictionary<(Type, string), PropertyInfo?> _propCache = new();
    private string DisplayOf(object? item)
    {
        if (item is null) return "";
        var member = DisplayMember;
        if (!string.IsNullOrEmpty(member))
        {
            var key = (item.GetType(), member!);
            if (!_propCache.TryGetValue(key, out var pi)) { pi = item.GetType().GetProperty(member!); _propCache[key] = pi; }
            if (pi != null) return pi.GetValue(item)?.ToString() ?? "";
        }
        return item.ToString() ?? "";
    }
}
