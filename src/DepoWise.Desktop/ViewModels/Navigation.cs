using System.Collections.Generic;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Accordion menü alt bağlantısı (örn. "Malzeme Listesi").</summary>
public sealed record NavLink(string Title, string Key);

/// <summary>Accordion menü grubu (ikon + başlık + alt bağlantılar). Tasarım şemasına göre.</summary>
public sealed record NavGroup(string Icon, string Title, string ModuleKey, IReadOnlyList<NavLink> Children, bool IsExpanded = false);
