using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Açıklamalı menü alt bağlantısı (örn. "Malzeme Listesi"). IsActive = seçili satır.</summary>
public sealed partial class NavLinkVm : ViewModelBase
{
    public string Title { get; }
    public string Key { get; }

    [ObservableProperty] private bool _isActive;

    public NavLinkVm(string title, string key)
    {
        Title = title;
        Key = key;
    }
}

/// <summary>
/// SEC (2026-08-19) — menünün EN ÜST seviyesindeki düğüm: ya bir ÜST GRUP (altında üst menüler),
/// ya da doğrudan bir üst menü.
///
/// <b>Geri uyumluluk:</b> üst grup tanımlanmadığında her üst menü kendi düğümü olur
/// (<c>IsSection=false</c>, tek elemanlı <see cref="Groups"/>) ve menü bugünkü hâliyle çizilir.
/// Bu yüzden mevcut grup şablonu (XAML) ve ikon rayı DEĞİŞTİRİLMEDİ — yalnız bir seviye sarmalandı.
/// </summary>
public sealed partial class NavSectionVm : ViewModelBase
{
    public string Title { get; }
    public bool IsSection { get; }
    public IReadOnlyList<NavGroupVm> Groups { get; }

    /// <summary>M6: üst grup ikonu (Themes/Icons.axaml). Yoksa üst grup ikonsuz görünür.</summary>
    public Avalonia.Media.Geometry? IconGeometry { get; init; }
    public bool HasIcon => IconGeometry is not null;

    [ObservableProperty] private bool _isExpanded;

    /// <summary>Alt liste görünür mü? Üst grup değilse DAİMA görünür (bugünkü davranış).</summary>
    public bool ChildrenVisible => !IsSection || IsExpanded;

    /// <summary>Üst grubun içindeki üst menüler bir tık içeriden başlar.</summary>
    public Avalonia.Thickness ChildIndent => IsSection ? new Avalonia.Thickness(8, 0, 0, 0) : default;

    public NavSectionVm(string title, bool isSection, IReadOnlyList<NavGroupVm> groups, bool expanded = false)
    {
        Title = title;
        IsSection = isSection;
        Groups = groups;
        _isExpanded = expanded;
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ChildrenVisible));
}

/// <summary>Accordion menü grubu — IsExpanded iki yönlü; IsActive = ikon rayı/grup vurgusu.</summary>
public sealed partial class NavGroupVm : ViewModelBase
{
    public string Icon { get; }
    public string Title { get; }
    public string ModuleKey { get; }
    public IReadOnlyList<NavLinkVm> Children { get; }

    /// <summary>M6 — menude cizilecek vektor ikon (Themes/Icons.axaml). Yoksa grup ikonsuz gorunur.
    /// Emoji tasiyan <see cref="Icon"/> alani DURUR: geri donus yolu ve web/MenuLayout ile ortak katalog.</summary>
    public Avalonia.Media.Geometry? IconGeometry { get; init; }
    public bool HasIcon => IconGeometry is not null;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isActive;

    public NavGroupVm(string icon, string title, string moduleKey, IReadOnlyList<NavLinkVm> children, bool expanded = false)
    {
        Icon = icon;
        Title = title;
        ModuleKey = moduleKey;
        Children = children;
        _isExpanded = expanded;
    }
}
