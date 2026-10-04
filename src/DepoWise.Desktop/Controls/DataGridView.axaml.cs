using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using System.Linq;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// Ortak tablo görünümü (Birim 4). DataContext bir <see cref="GridController"/>'dır. Görsel + bağlama burada;
/// iş/durum controller'da (MVVM). Kod-arkasının TEK işi: başlık sağ kenarındaki Thumb sürüklemesini kolon
/// genişliğine çevirmek (routed event ile — dinamik item'lar için güvenli; her instance'a ayrı olay bağlamaz).
/// </summary>
public partial class DataGridView : UserControl
{
    public DataGridView()
    {
        InitializeComponent();
        AddHandler(Thumb.DragDeltaEvent, OnThumbDragDelta, RoutingStrategies.Bubble);
        AddHandler(Thumb.DragCompletedEvent, OnThumbDragCompleted, RoutingStrategies.Bubble);
        // 2026-10-04: başlık tutamağı (Border.ColGrip) — işaretçi yakalamalı sürükleme (liste tablolarıyla aynı yöntem).
        AddHandler(PointerPressedEvent, OnGripPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnGripMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnGripReleased, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => SagTikBagla();
    }

    private bool _sagTikBagli;

    /// <summary>
    /// 2026-10-01 (kullanıcı isteği): rapor ekranının HER YERİNDE sağ tık → "Kolonları Ayarla" (araç çubuğundaki
    /// Kolonlar menüsünü açar) + "Kolon Ayarlarını Kaydet" (genişlikler yerel dosyaya; rapor açılınca uygulanır).
    /// </summary>
    private void SagTikBagla()
    {
        if (_sagTikBagli) return;
        var ekran = (this.GetVisualParent() as Avalonia.Visual)?.FindAncestorOfType<UserControl>(includeSelf: true) ?? this;
        _sagTikBagli = true;
        KolonGenislik.EkranaBagla(ekran,
            () => { if (DataContext is GridController g) KolonGenislik.RaporKaydet(g.KayitAnahtari, g.Columns.Select(c => (c.Key, c.Width))); },
            () => this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Flyout is not null) is { } b
                ? () => b.Flyout!.ShowAt(b) : null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static GridColumnVm? ColumnOf(object? source)
        => (source as Control)?.DataContext as GridColumnVm;

    private GridColumnVm? _surukKolon;
    private Border? _surukTutamak;
    private double _surukBasX, _surukBasW;

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Border b || !b.Classes.Contains("ColGrip") || b.DataContext is not GridColumnVm col) return;
        if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
        _surukKolon = col; _surukTutamak = b;
        _surukBasX = e.GetPosition(null).X; _surukBasW = col.Width;   // pencere çerçevesi: sabit referans
        e.Pointer.Capture(b);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_surukKolon is null) return;
        _surukKolon.Width = Math.Max(50, Math.Min(600, _surukBasW + (e.GetPosition(null).X - _surukBasX)));
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_surukKolon is null) return;
        if (DataContext is GridController g) g.CommitWidth(_surukKolon);
        e.Pointer.Capture(null);
        _surukKolon = null; _surukTutamak = null;
        e.Handled = true;
    }

    private void OnThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (ColumnOf(e.Source) is { } col)
        {
            col.Width = Math.Max(50, Math.Min(600, col.Width + e.Vector.X));
            e.Handled = true;
        }
    }

    private void OnThumbDragCompleted(object? sender, VectorEventArgs e)
    {
        if (ColumnOf(e.Source) is { } col && DataContext is GridController g)
        {
            g.CommitWidth(col);
            e.Handled = true;
        }
    }
}
