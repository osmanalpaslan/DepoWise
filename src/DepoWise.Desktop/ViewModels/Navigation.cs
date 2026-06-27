using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Accordion menü alt bağlantısı (örn. "Malzeme Listesi").</summary>
public sealed record NavLink(string Title, string Key);

/// <summary>Accordion menü grubu — IsExpanded iki yönlü (özel accordion, FluentTheme Expander kullanılmaz).</summary>
public sealed partial class NavGroupVm : ViewModelBase
{
    public string Icon { get; }
    public string Title { get; }
    public string ModuleKey { get; }
    public IReadOnlyList<NavLink> Children { get; }

    [ObservableProperty] private bool _isExpanded;

    public NavGroupVm(string icon, string title, string moduleKey, IReadOnlyList<NavLink> children, bool expanded = false)
    {
        Icon = icon;
        Title = title;
        ModuleKey = moduleKey;
        Children = children;
        _isExpanded = expanded;
    }
}
