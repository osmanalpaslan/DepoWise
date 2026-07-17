using CommunityToolkit.Mvvm.ComponentModel;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Kolonları Ayarla penceresindeki tek bir onay kutusu satırı (kullanıcı isteği 2026-07-17).</summary>
public sealed partial class ColumnPickItem : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _checked;

    public ColumnPickItem(string key, string label, bool @checked) { Key = key; Label = label; _checked = @checked; }
}
