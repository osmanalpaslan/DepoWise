using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Desktop.Theming;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Ayarlar → Tema: Koyu / Açık / Sistem seçimi. Seçim anında uygulanır ve saklanır (ThemeService).</summary>
public sealed partial class ThemeSettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string _status = "";

    public bool IsDark => ThemeService.CurrentMode == ThemeService.Dark;
    public bool IsLight => ThemeService.CurrentMode == ThemeService.Light;
    public bool IsSystem => ThemeService.CurrentMode == ThemeService.System;

    /// <summary>Renk teması kartları (accent).</summary>
    public System.Collections.Generic.IReadOnlyList<AccentOption> Accents { get; } =
        System.Array.ConvertAll(ThemeService.Accents, a => new AccentOption(a.Key, a.Name, a.Hex));

    /// <summary>Görünüm (stil kütüphanesi) kartları.</summary>
    public System.Collections.Generic.IReadOnlyList<StyleOption> StyleOptions { get; } =
        System.Array.ConvertAll(ThemeService.Styles, s => new StyleOption(s.Key, s.Name, s.Desc));

    [RelayCommand]
    private void ChooseStyle(string? styleKey)
    {
        if (string.IsNullOrEmpty(styleKey)) return;
        ThemeService.ApplyStyle(styleKey);
        ThemeService.ApplyAccent(ThemeService.CurrentAccent, persist: false); // accent override'ı yeni base üstüne yeniden uygula
        foreach (var s in StyleOptions) s.Refresh();
        Status = "Görünüm uygulandı.";
    }

    [RelayCommand]
    private void Choose(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        ThemeService.Apply(mode);
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsSystem));
        Status = "Tema uygulandı: " + mode switch { "Dark" => "Koyu", "Light" => "Açık", _ => "Sistem (OS)" };
    }

    [RelayCommand]
    private void ChooseColor(string? accentKey)
    {
        if (string.IsNullOrEmpty(accentKey)) return;
        ThemeService.ApplyAccent(accentKey);
        foreach (var a in Accents) a.Refresh();
        Status = "Renk teması uygulandı.";
    }
}

/// <summary>Tema ekranı renk kartı (seçili durumu canlı yansıtır).</summary>
public sealed partial class AccentOption : ViewModelBase
{
    public string Key { get; }
    public string Name { get; }
    public Avalonia.Media.IBrush Swatch { get; }

    public AccentOption(string key, string name, string hex)
    {
        Key = key; Name = name;
        Swatch = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
    }

    public bool IsSelected => ThemeService.CurrentAccent == Key;
    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>Tema ekranı görünüm (stil) kartı.</summary>
public sealed partial class StyleOption : ViewModelBase
{
    public string Key { get; }
    public string Name { get; }
    public string Desc { get; }
    public StyleOption(string key, string name, string desc) { Key = key; Name = name; Desc = desc; }
    public bool IsSelected => ThemeService.CurrentStyle == Key;
    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
