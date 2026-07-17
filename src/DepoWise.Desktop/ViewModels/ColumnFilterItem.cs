using CommunityToolkit.Mvvm.ComponentModel;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme/Araç Listesi — görünür bir kolon için filtre kutusu (kullanıcı isteği 2026-07-17). "İçerir"
/// araması yapar; Filtrele'ye basılınca sırayla (kataloğa göre) SearchGrid'e gönderilir, böylece birden
/// çok filtre aktifken "başlangıca göre" önceliği DETERMİNİSTİK sırayla uygulanır (bkz. GridQuery).
/// </summary>
public sealed partial class ColumnFilterItem : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private string _value = "";

    public ColumnFilterItem(string key, string label) { Key = key; Label = label; }
}
