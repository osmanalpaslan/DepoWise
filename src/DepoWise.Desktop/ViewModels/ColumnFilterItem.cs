using CommunityToolkit.Mvvm.ComponentModel;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme/Araç Listesi — görünür bir kolon için filtre kutusu (kullanıcı isteği 2026-07-17). Metin
/// kolonlarında "içerir" araması yapar; SAYISAL kolonlarda (kullanıcı isteği 2026-07-18) tam-sayı/
/// karşılaştırma (&gt;5, &lt;5, &gt;=5, &lt;=5) / aralık (5-10) söz dizimi kabul eder. Filtrele'ye basılınca
/// sırayla (kataloğa göre) SearchGrid'e gönderilir, böylece birden çok filtre aktifken "başlangıca göre"
/// önceliği DETERMİNİSTİK sırayla uygulanır (bkz. GridQuery).
/// </summary>
public sealed partial class ColumnFilterItem : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public bool IsNumeric { get; }
    public string Hint => IsNumeric ? "Tam sayı: 5 · Karşılaştırma: >5  <5  >=5  <=5 · Aralık: 5-10" : "İçerir araması";
    [ObservableProperty] private string _value = "";

    /// <summary>M7 — kutuda deger var mi. YALNIZ gorsel vurgu icin (TextBox.CellFilter.filled);
    /// filtre MANTIGI degismedi, bu turetilmis bir alandir.</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(HasValue));

    public ColumnFilterItem(string key, string label, bool isNumeric = false) { Key = key; Label = label; IsNumeric = isNumeric; }
}
