using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Stok Sayım — malzeme seç (sistem stoğu gösterilir) + sayılan miktar + gerekçe → fark kadar 'adjustment'
/// stok hareketi (StockService.Count). Altta son sayım/düzeltme hareketleri.
/// </summary>
public sealed partial class StockCountViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "stock", PermissionAction.Create);

    public ObservableCollection<MaterialRefRow> MaterialResults { get; } = new();
    public ObservableCollection<StockMovementRow> Adjustments { get; } = new();

    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterial))]
    private MaterialRefRow? _selectedMaterial;
    public bool HasMaterial => SelectedMaterial != null;
    [ObservableProperty] private decimal _systemBalance;
    [ObservableProperty] private decimal _countedQty;
    [ObservableProperty] private string _reason = "Sayım";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private string? _status;

    public bool HasRows => Adjustments.Count > 0;
    public bool IsEmpty => Adjustments.Count == 0;
    public string DiffText => HasMaterial ? $"Fark: {(CountedQty - SystemBalance):0.##}" : "";

    public StockCountViewModel(SessionContext session)
    {
        _session = session;
        Load();
        RefreshMaterials();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            Adjustments.Clear();
            foreach (var m in DesktopServices.Stock.RecentMovements(_session).Where(x => x.MovementType == "adjustment"))
                Adjustments.Add(m);
            Status = $"{Adjustments.Count} sayım düzeltmesi";
        }
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnMaterialSearchChanged(string value) => RefreshMaterials();
    partial void OnCountedQtyChanged(decimal value) => OnPropertyChanged(nameof(DiffText));

    private void RefreshMaterials()
    {
        MaterialResults.Clear();
        var term = MaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 30 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items) MaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
        }
        catch { }
    }

    [RelayCommand]
    private void PickMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        SelectedMaterial = m;
        MaterialSearch = $"{m.Code} - {m.Name}";
        MaterialResults.Clear();
        try { SystemBalance = DesktopServices.Stock.GetBalance(m.Id); } catch { SystemBalance = 0; }
        CountedQty = SystemBalance;
        OnPropertyChanged(nameof(DiffText));
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (SelectedMaterial is null) { FormError = "Malzeme seçin."; return; }
        if (string.IsNullOrWhiteSpace(Reason)) { FormError = "Gerekçe zorunlu."; return; }
        var diff = CountedQty - SystemBalance;
        if (diff == 0) { FormError = "Sayılan miktar sistemle aynı; fark yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"Sayım kaydedilsin mi?\nSistem: {SystemBalance:0.##}  Sayılan: {CountedQty:0.##}  Fark: {diff:0.##}", "Stok Sayım")) return;
        try
        {
            DesktopServices.Stock.Count(_session, new[] { new CountLine(SelectedMaterial.Id, CountedQty) },
                Reason.Trim(), Guid.NewGuid().ToString("N"));
            Status = "Sayım kaydedildi (fark stoğa yansıdı).";
            SelectedMaterial = null; MaterialSearch = ""; SystemBalance = 0; CountedQty = 0; Reason = "Sayım";
            OnPropertyChanged(nameof(DiffText));
            Load(); RefreshMaterials();
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }
}
