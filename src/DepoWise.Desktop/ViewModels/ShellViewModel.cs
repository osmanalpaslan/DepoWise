using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uygulama kabuğu: koyu accordion menü (yetkiye göre) + içerik navigasyonu + üst karşılama.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public string AppName { get; }
    public string CompanyName { get; }
    public string DisplayName { get; }
    public string Welcome { get; }
    public IReadOnlyList<NavGroupVm> Groups { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _activeKey = "dashboard";

    public ShellViewModel(SessionContext session)
    {
        _session = session;
        AppName = DesktopServices.Branding.AppName;
        CompanyName = DesktopServices.Branding.CompanyName;
        DisplayName = DesktopServices.DisplayName(session.UserId);
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
                new NavLink("Malzeme Listesi", "materials"),
                new NavLink("Yeni Kayıt", "materials:new"),
                new NavLink("Kategoriler", "definitions"),
            }, expanded: true),
            new NavGroupVm("🚚", "Araçlar", "vehicles", new[]
            {
                new NavLink("Araç Listesi", "vehicles"),
                new NavLink("Şablonlar", "vehicles:templates"),
                new NavLink("Yeni Araç Ekle", "vehicles:new"),
            }),
            new NavGroupVm("🔧", "Bakım Takibi", "maintenance", new[] { new NavLink("Bakım Listesi", "maintenance") }),
            new NavGroupVm("⛽", "Yakıt", "fuel", new[] { new NavLink("Yakıt İşlemleri", "fuel") }),
            new NavGroupVm("📄", "Talepler", "requests", new[] { new NavLink("Talep Listesi", "requests") }),
            new NavGroupVm("📊", "Raporlar", "reports", new[] { new NavLink("Raporlar", "reports") }),
            new NavGroupVm("⚙️", "Tanımlar / Ayarlar", "definitions", new[] { new NavLink("Tanımlar", "definitions") }),
        };

        var visible = new List<NavGroupVm>();
        foreach (var g in all)
            if (AccessControl.CanSeeMenu(s, g.ModuleKey))
                visible.Add(g);
        return visible;
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
                break;
            case "materials":
            case "materials:new":
                CurrentPage = new MaterialsViewModel(_session);
                CurrentTitle = "Malzemeler";
                break;
            default:
                var label = FindLabel(key);
                CurrentPage = new PlaceholderViewModel(label);
                CurrentTitle = label;
                break;
        }
    }

    [RelayCommand]
    private void GoDashboard() => Navigate("dashboard");

    private string FindLabel(string key)
    {
        foreach (var g in Groups)
            foreach (var c in g.Children)
                if (c.Key == key) return c.Title;
        return key;
    }
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
