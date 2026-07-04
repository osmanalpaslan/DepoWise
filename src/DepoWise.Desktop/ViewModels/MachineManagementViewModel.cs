using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Makine Yönetimi (yalnız Süper Admin) — kayıtlı makineleri listeler; onaylar/aktif eder, pasife alır (istediği an),
/// makine kotası belirler. Kota aşımında yeni makine login'i web/sync katmanında engellenir (bu ekran kotayı saklar).
/// </summary>
public sealed partial class MachineManagementViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<DeviceRow> Devices { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverQuota))]
    [NotifyPropertyChangedFor(nameof(QuotaText))]
    private int _quota;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverQuota))]
    [NotifyPropertyChangedFor(nameof(QuotaText))]
    private int _activeCount;

    public bool HasError => LoadError != null;
    public bool HasRows => Devices.Count > 0;
    public bool IsEmpty => !HasError && Devices.Count == 0;
    public bool OverQuota => Quota > 0 && ActiveCount > Quota;
    public string QuotaText => Quota > 0
        ? $"Aktif makine: {ActiveCount} / Kota: {Quota}" + (OverQuota ? "  — KOTA AŞILDI" : "")
        : $"Aktif makine: {ActiveCount} (kota sınırsız)";

    public MachineManagementViewModel(SessionContext session)
    {
        _session = session;
        var q = DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.MachineQuota);
        Quota = int.TryParse(q, out var qv) ? qv : 0;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Devices.Clear();
            foreach (var d in DesktopServices.Enrollment.ListDevices(_session)) Devices.Add(d);
            ActiveCount = DesktopServices.Enrollment.ActiveDeviceCount(_session);
            Status = $"{Devices.Count} makine";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void SaveQuota()
    {
        try
        {
            DesktopServices.Settings.Set(_session.CompanyId, SettingKeys.MachineQuota,
                Quota > 0 ? Quota.ToString() : null, _session.UserId);
            Status = Quota > 0 ? $"Kota kaydedildi: {Quota} makine." : "Kota kaldırıldı (sınırsız).";
            OnPropertyChanged(nameof(QuotaText));
            OnPropertyChanged(nameof(OverQuota));
        }
        catch (Exception ex) { Status = "Kota kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Activate(DeviceRow? d)
    {
        if (d is null) return;
        if (Quota > 0 && ActiveCount >= Quota)
        {
            if (!await ConfirmService.AskAsync(
                    $"Kota dolu ({ActiveCount}/{Quota}). Yine de bu makineyi aktifleştirmek istiyor musunuz?",
                    "Kota Uyarısı", "Evet, Aktifleştir", "Vazgeç", danger: true)) return;
        }
        try
        {
            if (d.Status == "pending") DesktopServices.Enrollment.ApproveDevice(_session, d.Id);
            else DesktopServices.Enrollment.Reactivate(_session, d.Id);
            Status = $"'{d.Name}' aktifleştirildi.";
            Load();
        }
        catch (Exception ex) { Status = "Aktifleştirilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Deactivate(DeviceRow? d)
    {
        if (d is null) return;
        if (!await ConfirmService.AskAsync(
                $"'{d.Name}' makinesi PASİFE alınsın mı? Bu makine bağlanamaz.",
                "Makineyi Pasife Al", "Evet, Pasife Al", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Enrollment.RevokeDevice(_session, d.Id);
            Status = $"'{d.Name}' pasife alındı.";
            Load();
        }
        catch (Exception ex) { Status = "Pasife alınamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete(DeviceRow? d)
    {
        if (d is null) return;
        if (!await ConfirmService.AskAsync(
                $"'{d.Name}' makine kaydı KALICI olarak silinsin mi?",
                "Makineyi Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Enrollment.DeleteDevice(_session, d.Id);
            Status = $"'{d.Name}' silindi.";
            Load();
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
