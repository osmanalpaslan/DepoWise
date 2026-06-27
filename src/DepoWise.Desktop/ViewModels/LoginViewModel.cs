using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;

    public string AppName => DesktopServices.Branding.AppName;

    /// <summary>Başarılı girişte oturumla çağrılır (App pencereyi değiştirir).</summary>
    public Action<SessionContext>? OnLoggedIn { get; set; }

    [RelayCommand]
    private void Login()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Kullanıcı adı ve parola gerekli.";
            return;
        }
        IsBusy = true;
        try
        {
            var companyId = DesktopServices.ResolveCompanyId();
            var result = DesktopServices.Auth.Login(companyId, Username.Trim(), Password);
            if (result.Locked)
            {
                Error = $"Çok fazla hatalı deneme. {result.SecondsRemaining} sn sonra tekrar deneyin.";
                return;
            }
            if (!result.Success || result.Session is null)
            {
                Error = result.Error ?? "Giriş başarısız.";
                return;
            }
            DesktopServices.Session = result.Session;
            OnLoggedIn?.Invoke(result.Session);
        }
        catch (Exception ex)
        {
            Error = "Giriş hatası: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
