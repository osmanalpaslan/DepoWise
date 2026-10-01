using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using DepoWise.Application.Reports;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// Filtre satırı hücresi (kullanıcı isteği 2026-09-30) — DataContext bir <see cref="ColumnFilterItem"/>'dır.
/// Metin/sayı kolonunda <c>TextBox.CellFilter</c> (Enter = filtrele); seçim kolonunda açılır liste
/// (<c>ComboBox.CellFilter</c>, sağda aşağı ok) — seçim yapılınca hemen filtreler. İki durumda da değer
/// aynı <see cref="ColumnFilterItem.Value"/>'ya yazılır: filtre mantığı ve VM'ler DEĞİŞMEZ.
///
/// Kod ile kurulur (yeni ControlTheme yok) — <see cref="SortHeader"/> ile aynı gerekçe: kanıtlanmış
/// bileşenler (TextBox/ComboBox + mevcut CellFilter stili) kullanılır.
/// </summary>
public sealed class HucreFiltre : Panel
{
    public const string Tumu = "Tümü";

    public static readonly StyledProperty<ICommand?> ApplyCommandProperty =
        AvaloniaProperty.Register<HucreFiltre, ICommand?>(nameof(ApplyCommand));

    public ICommand? ApplyCommand { get => GetValue(ApplyCommandProperty); set => SetValue(ApplyCommandProperty, value); }

    private ColumnFilterItem? _item;
    private ComboBox? _combo;
    private bool _yaziliyor;   // ComboBox'ı koddan güncellerken SelectionChanged'i yok say

    public HucreFiltre() => DataContextChanged += (_, _) => Kur();

    private void Kur()
    {
        if (_item is not null) { _item.PropertyChanged -= ItemDegisti; _item.Options.CollectionChanged -= SecenekDegisti; }
        Children.Clear(); _combo = null;
        _item = DataContext as ColumnFilterItem;
        if (_item is null) return;

        if (!_item.IsSelect)
        {
            var tb = new TextBox { PlaceholderText = _item.Placeholder };
            tb.Classes.Add("CellFilter");
            ToolTip.SetTip(tb, _item.Hint);
            tb.Bind(TextBox.TextProperty, new Binding(nameof(ColumnFilterItem.Value)) { Mode = BindingMode.TwoWay });
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Uygula(); e.Handled = true; } };
            Children.Add(tb);
        }
        else
        {
            _combo = new ComboBox { PlaceholderText = _item.Placeholder, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            _combo.Classes.Add("CellFilter");
            _combo.SelectionChanged += (_, _) =>
            {
                if (_yaziliyor || _item is null) return;
                var sec = _combo.SelectedItem as string;
                _item.Value = sec is null || sec == Tumu ? "" : sec;
                Uygula();
            };
            Children.Add(_combo);
            SecenekleriYaz();
            _item.Options.CollectionChanged += SecenekDegisti;
        }
        _item.PropertyChanged += ItemDegisti;
        Isaretle();
    }

    private void SecenekDegisti(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => SecenekleriYaz();

    private void SecenekleriYaz()
    {
        if (_combo is null || _item is null) return;
        _yaziliyor = true;
        try
        {
            var liste = new List<string> { Tumu };
            liste.AddRange(_item.Options);
            // Seçili değer listede yoksa (ör. elle yazılmış eski filtre) kaybolmasın diye eklenir.
            if (_item.HasValue && !liste.Contains(_item.Value)) liste.Add(_item.Value);
            _combo.ItemsSource = liste;
            _combo.SelectedItem = _item.HasValue ? _item.Value : null;
        }
        finally { _yaziliyor = false; }
    }

    private void ItemDegisti(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ColumnFilterItem.Value)) return;
        Isaretle();
        // "Filtreleri Temizle" gibi dışarıdan sıfırlama → açılır liste de boşa döner.
        if (_combo is not null && _item is not null && (_combo.SelectedItem as string ?? "") != _item.Value)
        {
            _yaziliyor = true;
            try { _combo.SelectedItem = _item.HasValue ? _item.Value : null; }
            finally { _yaziliyor = false; }
        }
    }

    /// <summary>Dolu filtre vurgusu (CellFilter.filled — mevcut stil).</summary>
    private void Isaretle()
    {
        if (Children.FirstOrDefault() is Control c) c.Classes.Set("filled", _item?.HasValue == true);
    }

    private void Uygula()
    {
        if (ApplyCommand?.CanExecute(null) == true) ApplyCommand.Execute(null);
    }

    /// <summary>
    /// Seçim kolonlarının seçeneklerini doldurur: ekranın "Excel'e Aktar" altyapısındaki filtresiz TÜM satırlar
    /// (<paramref name="getir"/>) ARKA PLANDA okunur (UI thread bloklanmaz), sütun BAŞLIĞIYLA eşlenir
    /// (gizli kolon sırayı kaydırmaz), boş/"—" değerler atlanır, Türkçe sıralanır.
    /// Hata olursa açılır liste yalnız "Tümü" ile kalır — liste ekranı asla düşmez.
    /// </summary>
    public static async void SecenekleriDoldur(IEnumerable<ColumnFilterItem> kutular, Func<TableModel> getir)
    {
        var secimler = kutular.Where(k => k.IsSelect).ToList();
        if (secimler.Count == 0) return;
        try
        {
            var tm = await Task.Run(getir);
            var tr = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), ignoreCase: true);
            foreach (var k in secimler)
            {
                var i = tm.Headers.ToList().IndexOf(k.Label);
                if (i < 0) continue;
                var degerler = tm.Rows.Select(r => i < r.Count ? Convert.ToString(r[i], new System.Globalization.CultureInfo("tr-TR"))?.Trim() : null)
                    .Where(v => !string.IsNullOrEmpty(v) && v != "—")
                    .Distinct(tr).OrderBy(v => v, tr).Take(500).ToList();
                k.Options.Clear();
                foreach (var v in degerler) k.Options.Add(v!);
            }
        }
        catch { /* seçenek yüklenemedi: liste "Tümü" ile kalır, elle filtre yine çalışır */ }
    }
}
