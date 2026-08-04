using DepoWise.Application.Common;

namespace DepoWise.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Alpnex — Çözüm İskeleti (Faz 01)";

    public string HealthSummary { get; }

    public MainWindowViewModel() : this(null) { }

    public MainWindowViewModel(HealthResult? health)
    {
        HealthSummary = health is null
            ? "Health: çalıştırılmadı"
            : $"Host: {health.Host} | DB: {health.DatabasePath} | " +
              $"journal={health.JournalMode} | FK={(health.ForeignKeysOn ? "on" : "off")} | " +
              $"write/read={(health.WriteReadOk ? "ok" : "fail")} | " +
              (health.Ok ? "DURUM: SAĞLIKLI" : $"DURUM: HATA {health.Error}");
    }
}
