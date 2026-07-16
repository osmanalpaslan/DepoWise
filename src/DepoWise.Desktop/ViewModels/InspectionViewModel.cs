using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Muayene / Sigorta — araç belge kayıtları (Muayene/Sigorta/Kasko/Kalibrasyon) + son/sonraki tarih + durum
/// (Yaklaşıyor/Süresi geçti). Yeni kayıt formu; liste tarih durumuna göre renklenir.
/// </summary>
public sealed partial class InspectionViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "inspection", PermissionAction.Create);

    public ObservableCollection<InspectionRow> Items { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<string> DocTypeOptions { get; } = new() { "Muayene", "Sigorta", "Kasko", "Kalibrasyon" };
    public ObservableCollection<string> ResultOptions { get; } = new() { "Geçti", "Kaldı", "Ertelendi" };

    [ObservableProperty] private string? _status;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private VehicleListRow? _fVehicle;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectionType))]
    [NotifyPropertyChangedFor(nameof(IsPostponed))]
    private string _fDocType = "Muayene";
    /// <summary>Sonuç alanı yalnız Muayene belgesinde görünür (sigorta/kasko/kalibrasyonda sonuç yok).</summary>
    public bool IsInspectionType => FDocType == "Muayene";
    [ObservableProperty] private DateTimeOffset? _fLastDate;
    [ObservableProperty] private DateTimeOffset? _fNextDate;
    [ObservableProperty] private string _fPlace = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPostponed))]
    private string _fResult = "Geçti";
    public bool IsPostponed => IsInspectionType && FResult == "Ertelendi";
    [ObservableProperty] private DateTimeOffset? _fPostponeDate;
    [ObservableProperty] private string _fNote = "";
    [ObservableProperty] private string? _formError;

    public InspectionViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    private static string Code(string display) => display switch
    {
        "Sigorta" => "insurance", "Kasko" => "kasko", "Kalibrasyon" => "calibration", _ => "inspection"
    };

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var i in DesktopServices.Inspection.List(_session)) Items.Add(i);
            if (Vehicles.Count == 0)
                try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
            Status = $"{Items.Count} belge";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void NewRecord()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (Vehicles.Count == 0) try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
        FVehicle = null; FDocType = "Muayene"; FLastDate = null; FNextDate = null;
        FPlace = ""; FResult = "Geçti"; FPostponeDate = null; FNote = ""; FormError = null;
        ShowAdd = true;
    }

    [RelayCommand]
    private void CancelAdd() => ShowAdd = false;

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Muayene / Sigorta")) return;   // "Tüm Şubeler" modunda işlem yok
        if (FVehicle is null) { FormError = "Araç seçin."; return; }
        if (IsPostponed && FPostponeDate is null) { FormError = "Ertelendi seçildi: erteleme tarihi zorunlu."; return; }
        // Tarih mantığı uyarıları (madde 5+9) — kullanıcı onaylarsa engellenmez.
        var nextForCheck = IsPostponed ? FPostponeDate : FNextDate;
        if (FLastDate is not null && nextForCheck is not null && nextForCheck < FLastDate
            && !await ConfirmService.AskAsync("Sonraki tarih, son tarihten ÖNCE görünüyor (mantıksız olabilir). Yine de kaydedilsin mi?", "Tarih Uyarısı", "Evet, Kaydet")) return;
        if (nextForCheck is not null && nextForCheck.Value.Date < DateTimeOffset.Now.Date
            && !await ConfirmService.AskAsync("Sonraki tarih geçmişte kalıyor. Yine de kaydedilsin mi?", "Tarih Uyarısı", "Evet, Kaydet")) return;
        if (!await ConfirmService.AskAsync($"{FDocType} belgesi kaydedilsin mi?", "Muayene / Sigorta")) return;
        try
        {
            // Ertelendi ise sonraki tarih = erteleme tarihi → uyarılar bu tarihe göre çalışır
            var nextMs = (IsPostponed ? FPostponeDate : FNextDate)?.ToUnixTimeMilliseconds();
            DesktopServices.Inspection.Save(_session, new NewInspection(
                VehicleId: FVehicle.Id, DocType: Code(FDocType),
                LastDate: FLastDate?.ToUnixTimeMilliseconds(),
                NextDate: nextMs,
                Result: IsInspectionType ? FResult : null,
                Place: string.IsNullOrWhiteSpace(FPlace) ? null : FPlace.Trim(),
                Note: string.IsNullOrWhiteSpace(FNote) ? null : FNote.Trim()));
            ShowAdd = false; Load();
            Status = "Belge kaydedildi.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }
}
