using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Settings;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Ortak tablo bileşeninin (DataGrid kontrolü) tek bir kolonu — genişlik/görünürlük/sıralama/filtre
/// canlı bağlanır; header, filtre kutusu ve gövde hücreleri AYNI nesneyi paylaşır (hizalama otomatik).</summary>
public sealed partial class GridColumnVm : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public bool Numeric { get; }

    public GridColumnVm(string key, string label, bool numeric)
    {
        Key = key; Label = label; Numeric = numeric;
    }

    [ObservableProperty] private double _width = 150;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _sortGlyph = "";   // "" / "▲" / "▼"
    public string Hint => Numeric ? "5, >5, 5-10…" : "içerir…";

    // Komutlar KOLON üzerindedir (controller SetData'da bağlar) → item template'lerde ve popup'larda
    // parent-traversal binding'i GEREKMEZ (Avalonia popup binding tuzağından kaçınılır).
    internal Action<GridColumnVm>? SortAction;
    internal Action<GridColumnVm, int>? MoveAction;
    internal Action<GridColumnVm>? ToggleVisibleAction;

    [RelayCommand] private void Sort() => SortAction?.Invoke(this);
    [RelayCommand] private void MoveLeft() => MoveAction?.Invoke(this, -1);
    [RelayCommand] private void MoveRight() => MoveAction?.Invoke(this, 1);
    [RelayCommand] private void ToggleVisible() => ToggleVisibleAction?.Invoke(this);
}

/// <summary>Bir tablo hücresi — kendi kolonuna referans tutar (genişlik/görünürlük onun üzerinden bağlanır).</summary>
public sealed class GridCellVm
{
    public string Text { get; }
    public GridColumnVm Column { get; }
    public GridCellVm(string text, GridColumnVm column) { Text = text; Column = column; }
}

/// <summary>Bir görüntülenen satır — hücreleri KOLON SIRASINA göre dizilidir (yeniden sıralamada yeniden kurulur).</summary>
public sealed class GridRowVm
{
    public IReadOnlyList<GridCellVm> Cells { get; }
    public GridRowVm(IReadOnlyList<GridCellVm> cells) { Cells = cells; }
}

/// <summary>
/// GENEL AMAÇLI ortak tablo beyni (Birim 4, kullanıcı isteği 2026-08-07). Hiçbir modüle bağımlı DEĞİL:
/// kolon tanımı + in-memory satırlar verilir; kolon-altı filtre / başlık-tık sıralama / genişlik / gizleme /
/// yeniden sıralama + KİŞİSEL tercih (ekran açılışında TEK yükleme; performans kuralı) sağlar.
/// Filtre/sıralama İSTEMCİ tarafında (sunucuya tekrar sorulmaz) — rapor gibi hazır sonuç için ideal; çekirdek
/// mantık ortak <see cref="GridDataView"/>'dedir (test edilebilir, web ile aynı davranış).
/// Kalıcılık dışarıdan verilen kancalarla yapılır (test edilebilir; Infrastructure'a doğrudan bağımlı değil).
/// </summary>
public sealed partial class GridController : ObservableObject
{
    public ObservableCollection<GridColumnVm> Columns { get; } = new();   // GÖRÜNÜM SIRASI (gizli de dahil, IsVisible=false)
    public ObservableCollection<GridRowVm> View { get; } = new();         // filtreli + sıralı satırlar

    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private string _summary = "";

    private IReadOnlyList<IReadOnlyList<string>> _rows = Array.Empty<IReadOnlyList<string>>();
    private readonly Dictionary<string, int> _colIndex = new(StringComparer.Ordinal);
    private string? _sortKey; private bool _sortDesc;
    private bool _suspend;   // toplu değişiklikte tekrar tekrar hesaplamayı önler

    // ── Kalıcılık kancaları (Reports VM bağlar; testte null bırakılabilir) ──
    public Func<ListPreferences?>? LoadPreferences { get; set; }
    public Action<IReadOnlyList<string>>? PersistColumns { get; set; }
    public Action<IReadOnlyDictionary<string, int>>? PersistWidths { get; set; }
    public Action<string, bool>? PersistSort { get; set; }

    /// <summary>Yeni veri seti yükler (yeni rapor). Kolon şeması değişince tercih TEK sefer yüklenir.</summary>
    public void SetData(IReadOnlyList<ListColumn> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        _suspend = true;
        foreach (var c in Columns) c.PropertyChanged -= OnColumnChanged;
        Columns.Clear();
        _colIndex.Clear();
        _sortKey = null; _sortDesc = false;

        for (int i = 0; i < columns.Count; i++)
        {
            var def = columns[i];
            _colIndex[def.Key] = i;
            var vm = new GridColumnVm(def.Key, def.Label, def.IsNumeric)
            {
                SortAction = ToggleSort,
                MoveAction = MoveColumn,
                ToggleVisibleAction = c => SetVisible(c, !c.IsVisible),
            };
            vm.PropertyChanged += OnColumnChanged;
            Columns.Add(vm);
        }
        _rows = rows;
        ApplyPreferences();     // sıra + seçim + genişlik + sıralama (varsa)
        _suspend = false;
        Recompute();
    }

    /// <summary>Grid'i boşaltır (rapor değişince eski sonuç görünmesin). HasRows=false → görünüm gizlenir.</summary>
    public void Clear()
    {
        _suspend = true;
        foreach (var c in Columns) c.PropertyChanged -= OnColumnChanged;
        Columns.Clear();
        View.Clear();
        _colIndex.Clear();
        _rows = Array.Empty<IReadOnlyList<string>>();
        _sortKey = null; _sortDesc = false;
        HasRows = false; Summary = "";
        _suspend = false;
    }

    private void ApplyPreferences()
    {
        var p = LoadPreferences?.Invoke();
        if (p is null) return;

        // Kolon sırası + seçim: kayıttaki sıra; kayıtta olmayan mevcut kolon gizli kalır (IsVisible=false),
        // kayıttaki geçersiz (silinmiş) kolon atlanır.
        if (p.Columns is { Count: > 0 })
        {
            var byKey = Columns.ToDictionary(c => c.Key, StringComparer.Ordinal);
            var ordered = new List<GridColumnVm>();
            foreach (var key in p.Columns)
                if (byKey.TryGetValue(key, out var vm)) { vm.IsVisible = true; ordered.Add(vm); }
            var chosen = new HashSet<string>(ordered.Select(c => c.Key), StringComparer.Ordinal);
            foreach (var c in Columns) if (!chosen.Contains(c.Key)) { c.IsVisible = false; ordered.Add(c); }
            if (ordered.Count == Columns.Count)
            {
                Columns.Clear();
                foreach (var c in ordered) Columns.Add(c);
            }
        }
        // Kolon GENİŞLİĞİ tercihi UYGULANMAZ (kullanıcı isteği 2026-08-08): her açılışta standart genişlik;
        // oturum içinde Thumb ile serbestçe resize (kaydedilmez). p.Widths bilinçli olarak yok sayılır.
        if (p.Sort is not null && _colIndex.ContainsKey(p.Sort.Key)) { _sortKey = p.Sort.Key; _sortDesc = p.Sort.Desc; }
        UpdateSortGlyphs();
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspend) return;
        if (e.PropertyName == nameof(GridColumnVm.Filter)) Recompute();
        else if (e.PropertyName == nameof(GridColumnVm.IsVisible)) Recompute();
    }

    /// <summary>Filtre + sıralamayı istemci tarafında uygular ve View'i yeniden kurar (in-memory, tekrar sorgu yok).
    /// Çekirdek mantık ortak <see cref="GridDataView"/>'dedir (test edilebilir; web ile aynı davranış).</summary>
    public void Recompute()
    {
        var cols = Columns.Select(c => new ListColumn(c.Key, c.Label, c.Numeric)).ToList();
        var filters = Columns.Where(c => !string.IsNullOrWhiteSpace(c.Filter))
                             .ToDictionary(c => c.Key, c => c.Filter, StringComparer.Ordinal);
        var list = GridDataView.Compute(cols, _rows, filters, _sortKey, _sortDesc);

        // View'i kolon SIRASINA göre kur (yeniden sıralama/gizleme burada yansır).
        View.Clear();
        foreach (var r in list)
        {
            var cells = new List<GridCellVm>(Columns.Count);
            foreach (var col in Columns) cells.Add(new GridCellVm(Cell(r, col.Key), col));
            View.Add(new GridRowVm(cells));
        }
        HasRows = _rows.Count > 0;
        Summary = _rows.Count == 0 ? "" : $"{View.Count} / {_rows.Count} satır" + (list.Count != _rows.Count ? " (filtreli)" : "");
    }

    public void ToggleSort(GridColumnVm col)
    {
        if (_sortKey != col.Key) { _sortKey = col.Key; _sortDesc = false; }
        else if (!_sortDesc) { _sortDesc = true; }
        else { _sortKey = null; _sortDesc = false; }
        UpdateSortGlyphs();
        Recompute();
        if (_sortKey is not null) PersistSort?.Invoke(_sortKey, _sortDesc);
    }

    private void UpdateSortGlyphs()
    {
        foreach (var c in Columns) c.SortGlyph = c.Key == _sortKey ? (_sortDesc ? "▼" : "▲") : "";
    }

    public void MoveColumn(GridColumnVm col, int dir)
    {
        int i = Columns.IndexOf(col);
        int j = i + dir;
        if (i < 0 || j < 0 || j >= Columns.Count) return;
        Columns.Move(i, j);
        Recompute();
        PersistColumns?.Invoke(Columns.Where(c => c.IsVisible).Select(c => c.Key).ToList());
    }

    public void SetVisible(GridColumnVm col, bool visible)
    {
        // En az bir kolon görünür kalmalı.
        if (!visible && Columns.Count(c => c.IsVisible) <= 1) return;
        col.IsVisible = visible;   // OnColumnChanged → Recompute
        PersistColumns?.Invoke(Columns.Where(c => c.IsVisible).Select(c => c.Key).ToList());
    }

    public void CommitWidth(GridColumnVm col)
        => PersistWidths?.Invoke(Columns.ToDictionary(c => c.Key, c => (int)Math.Round(c.Width)));

    private string Cell(IReadOnlyList<string> row, string key)
        => _colIndex.TryGetValue(key, out var i) && i < row.Count ? row[i] : "";
}
