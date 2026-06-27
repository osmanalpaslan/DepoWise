using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uygulama kabuğu: koyu accordion menü (yetkiye göre) + içerik navigasyonu + üst karşılama.
/// Menü grupları tasarım şemasına göre; yalnız okuma yetkisi olan modüller görünür.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public string AppName { get; }
    public string CompanyName { get; }
    public string UserName { get; }
    public string Welcome { get; }
    public IReadOnlyList<NavGroup> Groups { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _currentTitle = "";

    public ShellViewModel(SessionContext session)
    {
        _session = session;
        AppName = DesktopServices.Branding.AppName;
        CompanyName = DesktopServices.Branding.CompanyName;
        UserName = session.UserId;
        Welcome = "Hoş geldiniz, " + session.UserId;
        Groups = BuildGroups(session);

        Navigate("dashboard");
    }

    private static IReadOnlyList<NavGroup> BuildGroups(SessionContext s)
    {
        var all = new[]
        {
            new NavGroup("📦", "Malzemeler", "materials", new[]
            {
                new NavLink("Malzeme Listesi", "materials"),
                new NavLink("Yeni Kayıt", "materials:new"),
            }, IsExpanded: true),
            new NavGroup("🚚", "Araçlar", "vehicles", new[]
            {
                new NavLink("Araç Listesi", "vehicles"),
                new NavLink("Yeni Araç Ekle", "vehicles:new"),
            }),
            new NavGroup("🔧", "Bakım Takibi", "maintenance", new[] { new NavLink("Bakım Listesi", "maintenance") }),
            new NavGroup("⛽", "Yakıt Sarfiyatı", "fuel", new[] { new NavLink("Yakıt İşlemleri", "fuel") }),
            new NavGroup("📄", "Malzeme Talepleri", "requests", new[] { new NavLink("Talep Listesi", "requests") }),
            new NavGroup("📊", "Raporlar", "reports", new[] { new NavLink("Raporlar", "reports") }),
            new NavGroup("⚙️", "Tanımlar / Ayarlar", "definitions", new[] { new NavLink("Tanımlar", "definitions") }),
        };

        var visible = new List<NavGroup>();
        foreach (var g in all)
            if (AccessControl.CanSeeMenu(s, g.ModuleKey))
                visible.Add(g);
        return visible;
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        switch (key)
        {
            case "dashboard":
                CurrentPage = new DashboardViewModel(_session);
                CurrentTitle = "Ana Ekran";
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
