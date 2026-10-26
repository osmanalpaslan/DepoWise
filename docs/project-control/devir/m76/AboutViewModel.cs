using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Hakkında — ⭐ M-76 (AR-GE 2026-10-26, F-062): eskiden boş yer tutucuydu. Web ile aynı bilgiler: uygulama adı,
/// bu bilgisayardaki program sürümü, firma, son güncelleme notu + Güncelleme Notları'na geçiş.</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public AboutViewModel(SessionContext session)
    {
        _session = session;
        try { AppName = DesktopServices.Branding.AppName; } catch { AppName = "Alpnex"; }
        try { SurumMetni = "Sürüm " + DesktopServices.Update.CurrentVersion(); } catch { SurumMetni = "—"; }
        try { FirmaAdi = DesktopServices.Companies.GetName(session.CompanyId); } catch { FirmaAdi = session.CompanyId; }
        if (string.IsNullOrWhiteSpace(FirmaAdi)) FirmaAdi = session.CompanyId;
        var son = GuncellemeNotlari.Tumu.FirstOrDefault(n => n.MaddelerIcin(web: false).Any());
        SonGuncelleme = son is null ? null : $"{son.TarihMetni} — {son.Baslik}";
    }

    public string AppName { get; }
    public string SurumMetni { get; }
    public string FirmaAdi { get; }
    public string? SonGuncelleme { get; }
    public bool SonGuncellemeVar => SonGuncelleme is not null;

    [RelayCommand]
    private void GuncellemeNotlariniAc() => ShellViewModel.Current?.NavigateCommand.Execute("about:notes");
}
