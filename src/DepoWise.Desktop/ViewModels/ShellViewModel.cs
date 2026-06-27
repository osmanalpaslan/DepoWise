using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uygulama kabuğu: ikon rayı + açıklamalı accordion menü + üst bar + içerik navigasyonu.
/// Yetkiye göre menü; "Eşitle"/marka korunur. Navigasyon binding'leri (NavigateCommand/GoDashboardCommand) korunur.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public string AppName { get; }
    public string CompanyName { get; }
    public string DisplayName { get; }
    public string Initial { get; }
    public string Welcome { get; }
    public IReadOnlyList<NavGroupVm> Groups { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _currentContext = "";
    [ObservableProperty] private string _activeKey = "dashboard";
    [ObservableProperty] private bool _isNavPanelOpen = true;

    public ShellViewModel(SessionContext session)
    {
        _session = session;
        AppName = DesktopServices.Branding.AppName;
        CompanyName = DesktopServices.Branding.CompanyName;
        DisplayName = DesktopServices.DisplayName(session.UserId);
        Initial = string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant();
        Welcome = $"Hoş geldiniz, {DisplayName} — {DateTime.Now:dd MMMM yyyy dddd}";
        Groups = BuildGroups(session);

        Navigate("dashboard");
    }

    private static IReadOnlyList<NavGroupVm> BuildGroups(SessionContext s)
    {
        var all = new[]
        {
            new NavGroupVm("📦", "Malzemeler", "materials", new[]
            {
                new NavLinkVm("Malzeme Listesi", "materials"),
                new NavLinkVm("Yeni Kayıt", "materials:new"),
                new NavLinkVm("Kategoriler", "definitions"),
            }, expanded: true),
            new NavGroupVm("🚚", "Araçlar", "vehicles", new[]
            {
                new NavLinkVm("Araç Listesi", "vehicles"),
                new NavLinkVm("Şablonlar", "vehicles:templates"),
                new NavLinkVm("Yeni Araç Ekle", "vehicles:new"),
            }),
            new NavGroupVm("🔧", "Bakım Takibi", "maintenance", new[] { new NavLinkVm("Bakım Listesi", "maintenance") }),
            new NavGroupVm("⛽", "Yakıt", "fuel", new[] { new NavLinkVm("Yakıt İşlemleri", "fuel") }),
            new NavGroupVm("📄", "Talepler", "requests", new[] { new NavLinkVm("Talep Listesi", "requests") }),
            new NavGroupVm("📊", "Raporlar", "reports", new[] { new NavLinkVm("Raporlar", "reports") }),
            new NavGroupVm("⚙️", "Tanımlar / Ayarlar", "definitions", new[] { new NavLinkVm("Tanımlar", "definitions") }),
        };

        return all.Where(g => AccessControl.CanSeeMenu(s, g.ModuleKey)).ToList();
    }

    /// <summary>İkon rayından grup seçimi: grubu aç + birincil hedefe git.</summary>
    [RelayCommand]
    private void SelectGroup(NavGroupVm? group)
    {
        if (group is null) return;
        group.IsExpanded = true;
        Navigate(group.PrimaryKey);
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        ActiveKey = key;
        switch (key)
        {
            case "dashboard":
                CurrentPage = new DashboardViewModel(_session);
                CurrentTitle = "Genel Özet";
                CurrentContext = "Özet istatistikler ve kritik uyarılar";
                break;
            case "materials":
            case "materials:new":
                CurrentPage = new MaterialsViewModel(_session);
                CurrentTitle = "Malzemeler";
                CurrentContext = "Malzeme kartları ve stok";
                break;
            default:
                var label = FindLabel(key);
                CurrentPage = new PlaceholderViewModel(label);
                CurrentTitle = label;
                CurrentContext = "";
                break;
        }
        UpdateActiveStates(key);
    }

    [RelayCommand]
    private void GoDashboard() => Navigate("dashboard");

    [RelayCommand]
    private void ToggleNavPanel() => IsNavPanelOpen = !IsNavPanelOpen;

    /// <summary>Seçili modül/satır vurgularını günceller (mavi vurgu + koyu seçili satır).</summary>
    private void UpdateActiveStates(string key)
    {
        foreach (var g in Groups)
        {
            bool groupActive = false;
            foreach (var c in g.Children)
            {
                c.IsActive = c.Key == key;
                if (c.IsActive) groupActive = true;
            }
            g.IsActive = groupActive || g.ModuleKey == key;
        }
    }

    private string FindLabel(string key)
        => Groups.SelectMany(g => g.Children).FirstOrDefault(c => c.Key == key)?.Title ?? key;
}

/// <summary>Henüz UI bağlanmamış modüller için bilgilendirici yer tutucu (iş mantığı + testler hazır).</summary>
public sealed partial class PlaceholderViewModel : ViewModelBase
{
    public string Title { get; }
    public string Message { get; }

    public PlaceholderViewModel(string title)
    {
        Title = title;
        Message = $"\"{title}\" ekranı yakında. İş mantığı ve servis katmanı hazır ve testli; ekran bağlama sırada.";
    }
}
