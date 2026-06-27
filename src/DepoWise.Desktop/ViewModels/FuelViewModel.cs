using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Yakıt — depo bakiyesi + güncel fiyat KPI, dağıtım listesi, depo girişi + dağıtım formları.
/// FuelService üzerine; hesaplama/iş kuralları (snapshot fiyat, sayaç ileri, bakiye) serviste korunur.
/// </summary>
public sealed partial class FuelViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<FuelRow> Items { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private decimal _depotBalance;
    [ObservableProperty] private decimal _currentPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    public string DepotBalanceText => $"{DepotBalance:0.##} L";
    public string CurrentPriceText => $"{CurrentPrice:0.##} ₺/L";

    // Depo girişi formu
    [ObservableProperty] private bool _showDepot;
    [ObservableProperty] private decimal _depotLiters;
    [ObservableProperty] private decimal _depotPrice;

    // Dağıtım formu
    [ObservableProperty] private bool _showDist;
    [ObservableProperty] private VehicleListRow? _distVehicle;
    [ObservableProperty] private decimal _distLiters;
    [ObservableProperty] private decimal _distMeter;

    public bool CanWrite => AccessControl.Can(_session, "fuel", PermissionAction.Create);

    public FuelViewModel(SessionContext session)
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
            Vehicles.Clear();

            DepotBalance = DesktopServices.Fuel.GetDepotBalance(_session);
            CurrentPrice = DesktopServices.Fuel.GetCurrentFuelPrice(_session);
            OnPropertyChanged(nameof(DepotBalanceText));
            OnPropertyChanged(nameof(CurrentPriceText));

            foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v);

            foreach (var d in DesktopServices.Fuel.ListDistributions(_session))
                Items.Add(new FuelRow(d.VehicleCode ?? d.VehicleId, d.PrevMeter, d.CurrentMeter, d.Liters, d.UnitPrice, d.Currency, d.DistributionDate));
            Status = $"{Items.Count} dağıtım";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand] private void ToggleDepot() { if (Guard()) ShowDepot = !ShowDepot; }
    [RelayCommand] private void ToggleDist() { if (Guard()) ShowDist = !ShowDist; }

    private bool Guard()
    {
        if (!CanWrite) { Status = "Yetki yok."; return false; }
        return true;
    }

    [RelayCommand]
    private void SaveDepot()
    {
        if (!Guard()) return;
        if (DepotLiters <= 0 || DepotPrice <= 0) { Status = "Litre ve birim fiyat pozitif olmalı."; return; }
        try
        {
            DesktopServices.Fuel.AddDepotEntry(_session,
                new NewDepotEntry(Liters: DepotLiters, UnitPrice: DepotPrice), Guid.NewGuid().ToString("N"));
            DepotLiters = 0; DepotPrice = 0; ShowDepot = false;
            Load();
            Status = "Depo girişi eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void SaveDist()
    {
        if (!Guard()) return;
        if (DistVehicle is null) { Status = "Araç seçin."; return; }
        if (DistLiters <= 0) { Status = "Litre pozitif olmalı."; return; }
        try
        {
            DesktopServices.Fuel.Distribute(_session,
                new NewDistribution(VehicleId: DistVehicle.Id, Liters: DistLiters, CurrentMeter: DistMeter),
                Guid.NewGuid().ToString("N"));
            DistVehicle = null; DistLiters = 0; DistMeter = 0; ShowDist = false;
            Load();
            Status = "Dağıtım kaydedildi.";
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }
}

public sealed record FuelRow(string VehicleCode, decimal PrevMeter, decimal CurrentMeter, decimal Liters,
    decimal UnitPrice, string Currency, long DistributionDate)
{
    public string LitersText => $"{Liters:0.##}";
    public string PriceText => $"{UnitPrice:0.##} {Currency}";
    public string MeterText => $"{PrevMeter:0.##} → {CurrentMeter:0.##}";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(DistributionDate).LocalDateTime.ToString("dd.MM.yyyy");
}
