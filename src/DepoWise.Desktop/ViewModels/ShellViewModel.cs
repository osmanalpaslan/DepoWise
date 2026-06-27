using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uygulama kabuğu: yetkiye göre menü (MenuBuilder) + içerik navigasyonu. Branding ayarlardan gelir.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public string AppName { get; }
    public string CompanyName { get; }
    public string UserName { get; }
    public IReadOnlyList<MenuItem> MenuItems { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _currentTitle = "";

    public ShellViewModel(SessionContext session)
    {
        _session = session;
        AppName = DesktopServices.Branding.AppName;
        CompanyName = DesktopServices.Branding.CompanyName;
        UserName = session.UserId;
        MenuItems = MenuBuilder.Build(session);

        // İlk ekran: yetki varsa Malzemeler, yoksa Ana Ekran placeholder.
        if (AccessControl.Can(session, "materials", PermissionAction.View))
            Navigate("materials");
        else
            Navigate("dashboard");
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        switch (key)
        {
            case "materials":
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

    private string FindLabel(string key)
    {
        foreach (var (k, lbl) in AppModules.All)
            if (k == key) return lbl;
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
        Message = $"\"{title}\" ekranı yakında. İş mantığı ve servis katmanı hazır ve testli; " +
                  "ekran bağlama sırada.";
    }
}
