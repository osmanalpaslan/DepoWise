using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Hakkında — şimdilik içerik boş (sonra doldurulacak).</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    public AboutViewModel(SessionContext session) => _session = session;
}
