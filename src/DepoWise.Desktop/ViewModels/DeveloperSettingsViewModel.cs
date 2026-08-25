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

    /// <summary>
    /// ⭐ SEC-03 (2026-08-25): karar <see cref="DeveloperMode.TryActivate"/>'dedir — ekran kendi kuralını
    /// YAZMAZ. Eskiden burada yalnız kod karşılaştırılıp bayrak doğrudan set ediliyordu; yetkisiz bir
    /// kullanıcı kodu bilerek süper admin yetkilerine geçebiliyordu.
    /// </summary>
    [RelayCommand]
    private void ActivateDeveloper()
    {
        if (!DeveloperMode.CanActivate(_session))
        {
            Status = "Bu ekran yalnız Süper Admin içindir.";
            DeveloperCode = "";
            return;
        }
        if (!DeveloperMode.TryActivate(_session, DeveloperCode)) { Status = "Geçersiz geliştirici kodu."; return; }
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
