using System.Globalization;

namespace DepoWise.Application.Ui;

/// <summary>
/// Aranabilir çoklu seçim durum modeli (analiz §5). Kurallar:
/// - Arama (Query) seçimleri KAYBETMEZ; seçim filtreden bağımsız korunur.
/// - "Tümünü Seç" yalnız MEVCUT FİLTRE sonucunu ekler (tüm listeyi değil).
/// - "Tümünü Kaldır" yalnız filtrelenenleri seçimden çıkarır.
/// Web `MultiSelectState` ile fonksiyonel eşit.
/// </summary>
public sealed class MultiSelectState<T> where T : notnull
{
    private readonly IReadOnlyList<T> _all;
    private readonly Func<T, string> _textOf;
    private readonly HashSet<T> _selected = new();
    private static readonly CompareInfo Tr = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public string Query { get; private set; } = string.Empty;

    public MultiSelectState(IEnumerable<T> all, Func<T, string> textOf, IEnumerable<T>? initialSelected = null)
    {
        _all = all.ToList();
        _textOf = textOf;
        if (initialSelected is not null)
            foreach (var s in initialSelected) _selected.Add(s);
    }

    public void Search(string? query) => Query = query?.Trim() ?? string.Empty;

    /// <summary>Filtre sonucu (boş query → tümü). Türkçe duyarsız içerir-araması.</summary>
    public IReadOnlyList<T> Filtered()
    {
        if (Query.Length == 0) return _all;
        // Türkçe büyük/küçük harf duyarsız (İ/i, ş, ç, ğ, ü, ö) — CLAUDE.md standardı.
        return _all.Where(x => Tr.IndexOf(_textOf(x), Query, CompareOptions.IgnoreCase) >= 0).ToList();
    }

    public bool IsSelected(T item) => _selected.Contains(item);
    public IReadOnlyCollection<T> Selected => _selected;
    public int SelectedCount => _selected.Count;

    public void Toggle(T item, bool selected)
    {
        if (selected) _selected.Add(item); else _selected.Remove(item);
    }

    /// <summary>Yalnız filtrelenenleri seçime EKLER (mevcut seçimi korur).</summary>
    public void SelectAllFiltered()
    {
        foreach (var x in Filtered()) _selected.Add(x);
    }

    /// <summary>Yalnız filtrelenenleri seçimden çıkarır (filtre dışı seçimler korunur).</summary>
    public void ClearFiltered()
    {
        foreach (var x in Filtered()) _selected.Remove(x);
    }
}
