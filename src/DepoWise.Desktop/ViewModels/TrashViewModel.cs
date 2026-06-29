using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Files;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Çöp Kutusu — silinen (is_deleted=1) kayıtları listeler; "Geri Yükle" ile kurtarır (RestoreTrash yetkisi).</summary>
public sealed partial class TrashViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanRestore => AccessControl.CanUseButton(_session, SpecialButtons.RestoreTrash);

    public ObservableCollection<TrashRowVm> Items { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    public TrashViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var t in DesktopServices.Trash.List(_session, reauthenticated: true)) Items.Add(new TrashRowVm(t));
            Status = $"{Items.Count} silinmiş kayıt";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private async Task Restore(TrashRowVm? row)
    {
        if (row is null) return;
        if (!CanRestore) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{row.Label}' geri yüklensin mi?", "Geri Yükle")) return;
        try
        {
            DesktopServices.Trash.Restore(_session, row.Table, row.Id, reauthenticated: true);
            Load();
            Status = "Kayıt geri yüklendi.";
        }
        catch (Exception ex) { Status = "Geri yüklenemedi: " + ex.Message; }
    }
}

public sealed class TrashRowVm
{
    public string Table { get; }
    public string Id { get; }
    public string Label { get; }
    public string DateText { get; }
    public string TableText { get; }

    public TrashRowVm(TrashItem t)
    {
        Table = t.Table; Id = t.Id; Label = string.IsNullOrWhiteSpace(t.Label) ? "(adsız)" : t.Label;
        DateText = DateTimeOffset.FromUnixTimeMilliseconds(t.UpdatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        TableText = t.Table switch
        {
            "materials" => "Malzeme", "vehicles" => "Araç", "personnel" => "Personel", "branches" => "Şube",
            "suppliers" => "Tedarikçi", "brands" => "Marka", "units" => "Birim",
            "material_categories" => "Kategori", "vehicle_templates" => "Araç Şablonu",
            "vehicle_types" => "Makine Tipi", "vehicle_categories" => "Araç Kategorisi",
            "vehicle_models" => "Model", "maintenance_definitions" => "Bakım Tanımı", _ => t.Table
        };
    }
}
