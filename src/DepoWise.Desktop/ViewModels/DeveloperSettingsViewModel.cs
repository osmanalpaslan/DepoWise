using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Ayarlar › Geliştirici Modu — kod ile aç (süper admin yetkileri), butonla veya çıkışta kapan.
/// Oturum içi, kalıcı değil.
/// </summary>
public sealed partial class DeveloperSettingsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    [ObservableProperty] private string _developerCode = "";
    [ObservableProperty] private string? _status;

    public bool DeveloperActive => DeveloperMode.IsActive;
    public string DeveloperStatusText => DeveloperMode.IsActive
        ? "Geliştirici modu AÇIK (süper admin yetkileri)" : "Geliştirici modu kapalı";

    public DeveloperSettingsViewModel(SessionContext session) => _session = session;

    private void NotifyDev()
    {
        OnPropertyChanged(nameof(DeveloperActive));
        OnPropertyChanged(nameof(DeveloperStatusText));
    }

    [RelayCommand]
    private void ActivateDeveloper()
    {
        if (DeveloperCode?.Trim() != DeveloperMode.Code) { Status = "Geçersiz geliştirici kodu."; return; }
        DeveloperMode.IsActive = true;
        DeveloperCode = "";
        NotifyDev();
        Status = "Geliştirici modu açıldı (çıkışta otomatik kapanır).";
    }

    [RelayCommand]
    private void DeactivateDeveloper()
    {
        DeveloperMode.IsActive = false;
        NotifyDev();
        Status = "Geliştirici modu kapatıldı.";
    }
}
