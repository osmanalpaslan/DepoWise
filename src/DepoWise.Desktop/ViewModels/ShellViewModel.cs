using System.Collections.Generic;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Application.Ui;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uygulama kabuğu: yetkiye göre menü + branding metinleri + yüklenme göstergesi.
/// Menü `MenuBuilder` (ortak mantık) ile üretilir; başlık/marka ayarlardan gelir (sabit değil).
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    public string AppName { get; }
    public string CompanyName { get; }
    public IReadOnlyList<MenuItem> MenuItems { get; }
    public bool IsLoading { get; }
    public string HealthSummary { get; }

    public ShellViewModel(SessionContext session, BrandingSettings branding, string healthSummary, bool isLoading = false)
    {
        AppName = branding.AppName;
        CompanyName = branding.CompanyName;
        MenuItems = MenuBuilder.Build(session);
        HealthSummary = healthSummary;
        IsLoading = isLoading;
    }
}
