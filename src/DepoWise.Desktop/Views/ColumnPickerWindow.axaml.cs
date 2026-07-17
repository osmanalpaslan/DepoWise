using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Views;

/// <summary>
/// "Kolonları Ayarla" penceresi (kullanıcı isteği 2026-07-17) — Malzeme/Araç Listesi'nde hangi alanların
/// gösterileceğini seçme. Seçim KİŞİSELDİR (UserListPreferenceService ile kaydedilir, çağıran ekran yapar).
/// Kaydet → seçili anahtarların listesi (kataloğun sırasına göre); Vazgeç → null.
/// </summary>
public partial class ColumnPickerWindow : Window
{
    public ColumnPickerWindow() => InitializeComponent();

    public ColumnPickerWindow(IReadOnlyList<(string Key, string Label)> available, IReadOnlyList<string> selected)
    {
        InitializeComponent();
        var selectedSet = new HashSet<string>(selected);
        var items = available.Select(c => new ColumnPickItem(c.Key, c.Label, selectedSet.Contains(c.Key))).ToList();
        this.FindControl<ItemsControl>("Items")!.ItemsSource = items;

        var ok = this.FindControl<Button>("OkBtn")!;
        var cancel = this.FindControl<Button>("CancelBtn")!;
        ok.Click += (_, _) =>
        {
            var chosen = items.Where(i => i.Checked).Select(i => i.Key).ToList();
            // En az bir kolon kalmalı — hepsi kaldırılırsa ilk kolonu (genelde "Kod") işaretli tut.
            if (chosen.Count == 0 && available.Count > 0) chosen.Add(available[0].Key);
            Close(chosen);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
