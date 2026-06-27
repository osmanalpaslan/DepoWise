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

/// <summary>Accordion menü grubu — IsExpanded iki yönlü; IsActive = ikon rayı/grup vurgusu.</summary>
public sealed partial class NavGroupVm : ViewModelBase
{
    public string Icon { get; }
    public string Title { get; }
    public string ModuleKey { get; }
    public IReadOnlyList<NavLinkVm> Children { get; }

    /// <summary>İkon rayından tıklanınca gidilecek birincil hedef (ilk alt öğe ya da modül).</summary>
    public string PrimaryKey => Children.FirstOrDefault()?.Key ?? ModuleKey;

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
