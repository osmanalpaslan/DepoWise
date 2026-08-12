using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// G3 — TABLO SATIRI: HÜCREDEKİ YAZIYA TIKLAMAK DA SATIRI SEÇER (kullanıcı isteği 2026-08-12).
///
/// <b>SORUN:</b> tablolar <c>ListBox.Table</c> deseniyle kuruluyor (DataGrid paketi Avalonia 12.0.4 ile
/// uyumsuz) ve satır içeriği <see cref="SelectableTextBlock"/> ile yazılıyor (40+ ekranda ~793 kullanım).
/// <see cref="SelectableTextBlock"/>, metin seçimini başlatmak için <c>PointerPressed</c> olayını İŞLER ve
/// TÜKETİR (<c>Handled = true</c>). Olay <see cref="ListBoxItem"/>'a hiç ULAŞMAZ → <b>satır seçilmez</b>.
/// Kullanıcı satırın BOŞ alanına tıklamak zorunda kalıyordu (olay orada doğrudan ContentPresenter'a gider).
///
/// <b>NEDEN "IsHitTestVisible=False" DEĞİL:</b> en kısa çözüm satır içindeki metni tıklanamaz yapmaktı,
/// ama bu iki şeyi birden bozardı:
///   • hücre metninin fare ile seçilip KOPYALANMASI (kullanıcı bunu kaybetmemeli),
///   • satır içindeki <c>ToolTip</c>'ler (ör. <c>MaintenanceView.axaml:462</c> — kısaltılmış metnin tam hâli
///     tooltip'te gösteriliyor; tooltip hit-test gerektirir).
///
/// <b>ÇÖZÜM:</b> olayı <see cref="RoutingStrategies.Tunnel"/> (önizleme) aşamasında dinleriz. Tünelleme,
/// olay çocuklara İNMEDEN ÖNCE çalışır → <see cref="SelectableTextBlock"/> onu tüketse bile biz satırı
/// zaten seçmiş oluruz. Olayı <b>işaretlemeyiz</b> (<c>Handled</c>'a dokunmayız) → metin seçimi, kopyalama,
/// tooltip ve çift tık AYNEN çalışmaya devam eder.
///
/// <b>GERÇEK KONTROLLER KORUNUR:</b> tıklanan öğe bir düğme/onay kutusu/açılır liste/metin kutusu/sayı
/// kutusu içindeyse satır seçimi TETİKLENMEZ — o kontrolün kendi davranışı bozulmasın (ör. satır
/// içindeki "Tümü" düğmesi ya da miktar alanı). Klavye ile seçim <see cref="ListBox"/>'ın kendi işidir,
/// buraya hiç dokunulmaz.
///
/// <b>KULLANIM:</b> tek satır stil — <c>Themes/Components.axaml</c> içinde <c>ListBox.Table</c>'a
/// bağlanır. Ekran ekran yama YOKTUR; tabloyla SINIRLIDIR (tablo dışındaki metinler etkilenmez).
/// </summary>
public static class TableRowSelect
{
    /// <summary>Bu <see cref="ListBox"/> için "yazıya tıklayınca da satır seçilsin" davranışı.</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("Enabled", typeof(TableRowSelect));

    public static bool GetEnabled(ListBox element) => element.GetValue(EnabledProperty);
    public static void SetEnabled(ListBox element, bool value) => element.SetValue(EnabledProperty, value);

    static TableRowSelect()
    {
        EnabledProperty.Changed.AddClassHandler<ListBox>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(ListBox list, AvaloniaPropertyChangedEventArgs e)
    {
        // Tünelleme (önizleme): olay çocuklara inmeden ÖNCE çalışır → metin onu tüketse bile satır seçilir.
        if (e.NewValue is true)
            list.AddHandler(InputElement.PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        else
            list.RemoveHandler(InputElement.PointerPressedEvent, OnPreviewPointerPressed);
    }

    private static void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list) return;
        // Yalnız SOL tuş satır seçer. Sağ tuş bağlam menüsü davranışına karışmayız.
        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;

        // Gerçek etkileşimli kontrol içindeysek dokunma — o kontrolün kendi davranışı bozulmasın.
        if (IsInteractive(source, list)) return;

        var item = FindItem(source, list);
        if (item is null) return;

        // Yalnız SEÇİMİ ayarlarız; olayı Handled YAPMAYIZ → metin seçimi/kopyalama/tooltip/çift tık sürer.
        // Çoklu seçim kipinde (Ctrl/Shift) ListBox'ın kendi mantığına karışmamak için dokunulmaz.
        if (list.SelectionMode.HasFlag(SelectionMode.Multiple)) return;
        if (e.KeyModifiers is not KeyModifiers.None) return;
        if (!item.IsSelected) item.IsSelected = true;
    }

    /// <summary>Tıklanan öğe, kendi tıklama davranışı olan bir kontrolün içinde mi?</summary>
    private static bool IsInteractive(Visual source, ListBox stopAt)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, stopAt); v = v.GetVisualParent())
        {
            if (v is Button or CheckBox or RadioButton or ComboBox or TextBox or NumericUpDown
                or ToggleSwitch or Slider or CalendarDatePicker or AutoCompleteBox)
                return true;
        }
        return false;
    }

    /// <summary>Tıklanan öğenin bağlı olduğu satır (<see cref="ListBoxItem"/>); yoksa null.</summary>
    private static ListBoxItem? FindItem(Visual source, ListBox stopAt)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, stopAt); v = v.GetVisualParent())
            if (v is ListBoxItem item) return item;
        return null;
    }
}
