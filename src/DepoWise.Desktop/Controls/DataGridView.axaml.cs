using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static GridColumnVm? ColumnOf(object? source)
        => (source as Control)?.DataContext as GridColumnVm;

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
