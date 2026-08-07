using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Yakıt — sekmeli (Dağıtımlar / Depo Girişleri / Özet). Dağıtımda araç→önceki sayaç otomatik + canlı toplam.
/// Hesaplama/iş kuralları (snapshot fiyat, sayaç ileri, bakiye, negatif/yetersiz guard) serviste korunur.
/// </summary>
public sealed partial class FuelViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<FuelRow> Distributions { get; } = new();
    public ObservableCollection<FuelDepotRow> DepotEntries { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public ObservableCollection<LookupItem> Suppliers { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> VehiclePopulator => SearchPopulator.For(() => Vehicles, v => v.Display);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> PersonnelPopulator => SearchPopulator.For(() => Personnel, p => p.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> SupplierPopulator => SearchPopulator.For(() => Suppliers, s => s.Name);

    [ObservableProperty] private string? _status;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private decimal _depotBalance;
    [ObservableProperty] private decimal _currentPrice;
    [ObservableProperty] private decimal _totalReceived;
    [ObservableProperty] private decimal _totalDistributed;

    public string DepotBalanceText => $"{DepotBalance:0.##} L";
    public string CurrentPriceText => $"{CurrentPrice:0.##} ₺/L";
    public string TotalReceivedText => $"{TotalReceived:0.##} L";
    public string TotalDistributedText => $"{TotalDistributed:0.##} L";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool DistEmpty => !HasError && Distributions.Count == 0;
    public bool HasDist => Distributions.Count > 0;
    public bool DepotEmpty => DepotEntries.Count == 0;
    public bool HasDepot => DepotEntries.Count > 0;

    public bool CanWrite => AccessControl.Can(_session, "fuel", PermissionAction.Create);

    public FuelViewModel(SessionContext session, int initialTab = 0)
    {
        _session = session;
        SelectedTab = initialTab;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Distributions.Clear(); DepotEntries.Clear(); Vehicles.Clear();

            DepotBalance = DesktopServices.Fuel.GetDepotBalance(_session);
            CurrentPrice = DesktopServices.Fuel.GetCurrentFuelPrice(_session);

            foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v);
            foreach (var d in DesktopServices.Fuel.ListDistributions(_session))
                Distributions.Add(new FuelRow(d.VehicleCode ?? d.VehicleId, d.PrevMeter, d.CurrentMeter, d.Liters, d.UnitPrice, d.Currency, d.DistributionDate));
            foreach (var e in DesktopServices.Fuel.ListDepotEntries(_session))
                DepotEntries.Add(e);

            TotalDistributed = Distributions.Sum(x => x.Liters);
            TotalReceived = DepotEntries.Sum(x => x.Liters);
            Status = $"{Distributions.Count} dağıtım · {DepotEntries.Count} depo girişi";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        NotifyState();
    }

    private void NotifyState()
    {
        foreach (var n in new[] { nameof(DepotBalanceText), nameof(CurrentPriceText), nameof(TotalReceivedText),
            nameof(TotalDistributedText), nameof(DistEmpty), nameof(HasDist), nameof(DepotEmpty), nameof(HasDepot) })
            OnPropertyChanged(n);
    }

    private void EnsurePickers()
    {
        if (Personnel.Count == 0)
            try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
        if (Suppliers.Count == 0)
            try { foreach (var sp in DesktopServices.Lookups.List(_session, "suppliers")) Suppliers.Add(sp); } catch { }
    }

    // ════════════ DAĞITIM ════════════
    [ObservableProperty] private bool _showDist;
    [ObservableProperty] private VehicleListRow? _distVehicle;
    [ObservableProperty] private decimal _distPrevMeter;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistTotalText))]
    private decimal _distLiters;
    [ObservableProperty] private decimal _distMeter;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistTotalText))]
    private decimal _distUnitPrice;
    [ObservableProperty] private LookupItem? _distPersonnel;
    /// <summary>"Yakıtı Alan" (kullanıcı isteği 2026-07-19) — Yakıtı Veren'den ayrı, opsiyonel.</summary>
    [ObservableProperty] private LookupItem? _distRecipient;

    public string DistTotalText => $"{DistLiters * DistUnitPrice:0.##} ₺";

    partial void OnDistVehicleChanged(VehicleListRow? value)
    {
        DistPrevMeter = value?.CurrentMeter ?? 0;
        if (value is not null && DistMeter < value.CurrentMeter) DistMeter = value.CurrentMeter;
    }

    [RelayCommand]
    private void ToggleDist()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowDist = !ShowDist;
        if (ShowDist) { EnsurePickers(); if (DistUnitPrice == 0) DistUnitPrice = CurrentPrice; }
    }

    [RelayCommand]
    private void ClearDist()
    {
        DistVehicle = null; DistPrevMeter = 0; DistMeter = 0; DistLiters = 0; DistUnitPrice = 0; DistPersonnel = null; DistRecipient = null;
        ShowDist = false;
    }

    [RelayCommand]
    private async Task SaveDist()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Yakıt")) return;   // "Tüm Şubeler" modunda işlem yok
        if (DistVehicle is null) { Status = "Araç seçin."; return; }
        if (DistLiters <= 0) { Status = "Litre pozitif olmalı."; return; }
        if (DistPersonnel is null) { Status = "Yakıtı veren personeli seçin."; return; } // madde 8
        if (DepoWise.Application.Ui.FieldChecks.IsSuspiciouslyLarge(DistLiters)
            && !await ConfirmService.AskAsync($"Litre değeri çok büyük görünüyor ({DistLiters:0.##}). Emin misiniz?", "Litre Uyarısı", "Evet, Doğru")) return; // madde 7
        if (!await ConfirmService.AskAsync($"{DistLiters:0.##} L yakıt dağıtımı kaydedilsin mi? (Toplam {DistTotalText})", "Kaydet")) return;
        try
        {
            DesktopServices.Fuel.Distribute(_session, new NewDistribution(
                VehicleId: DistVehicle.Id, Liters: DistLiters, CurrentMeter: DistMeter,
                UnitPrice: DistUnitPrice > 0 ? DistUnitPrice : (decimal?)null,
                PersonnelId: DistPersonnel?.Id, RecipientPersonnelId: DistRecipient?.Id), Guid.NewGuid().ToString("N"));
            ClearDist(); Load();
            Status = "Dağıtım kaydedildi.";
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    // ════════════ DEPO GİRİŞİ ════════════
    [ObservableProperty] private bool _showDepot;
    [ObservableProperty] private decimal _depotLiters;
    [ObservableProperty] private decimal _depotPrice;
    [ObservableProperty] private LookupItem? _depotSupplier;
    [ObservableProperty] private string _depotInvoice = "";

    // Tedarikçi "+" ekleme (madde 2.1, kullanıcı isteği 2026-08-06): Malzemeler ekranındaki tedarikçi "+"
    // ile AYNI desen — sabit tanım alanına "+" eklenmiş bir alan başka ekranda da aynı özelliğe sahip olmalı.
    [ObservableProperty] private bool _isAddingSupplier;
    [ObservableProperty] private string _newSupplierName = "";

    [RelayCommand] private void StartAddSupplier() { IsAddingSupplier = true; NewSupplierName = ""; }
    [RelayCommand] private void CancelAddSupplier() { IsAddingSupplier = false; NewSupplierName = ""; }
    [RelayCommand]
    private void ConfirmAddSupplier()
    {
        if (string.IsNullOrWhiteSpace(NewSupplierName)) return;
        try
        {
            var id = DesktopServices.Lookups.AddSupplier(_session, NewSupplierName.Trim());
            var item = new LookupItem(id, NewSupplierName.Trim());
            Suppliers.Add(item); DepotSupplier = item;
            IsAddingSupplier = false; NewSupplierName = "";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    public string DepotTotalText => $"{DepotLiters * DepotPrice:0.##} ₺";

    partial void OnDepotLitersChanged(decimal value) => OnPropertyChanged(nameof(DepotTotalText));
    partial void OnDepotPriceChanged(decimal value) => OnPropertyChanged(nameof(DepotTotalText));

    [RelayCommand]
    private void ToggleDepot()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowDepot = !ShowDepot;
        if (ShowDepot) EnsurePickers();
    }

    [RelayCommand]
    private void ClearDepot()
    {
        DepotLiters = 0; DepotPrice = 0; DepotSupplier = null; DepotInvoice = ""; ShowDepot = false;
    }

    [RelayCommand]
    private async Task SaveDepot()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Yakıt Depo Girişi")) return;   // "Tüm Şubeler" modunda işlem yok
        if (DepotLiters <= 0 || DepotPrice <= 0) { Status = "Litre ve birim fiyat pozitif olmalı."; return; }
        if (!await ConfirmService.AskAsync($"{DepotLiters:0.##} L depo girişi kaydedilsin mi? (Toplam {DepotTotalText})", "Kaydet")) return;
        try
        {
            DesktopServices.Fuel.AddDepotEntry(_session, new NewDepotEntry(
                Liters: DepotLiters, UnitPrice: DepotPrice, SupplierId: DepotSupplier?.Id,
                InvoiceNo: string.IsNullOrWhiteSpace(DepotInvoice) ? null : DepotInvoice.Trim()),
                Guid.NewGuid().ToString("N"));
            ClearDepot(); Load();
            Status = "Depo girişi eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }
}

public sealed record FuelRow(string VehicleCode, decimal PrevMeter, decimal CurrentMeter, decimal Liters,
    decimal UnitPrice, string Currency, long DistributionDate)
{
    public string LitersText => $"{Liters:0.##}";
    public string PriceText => $"{UnitPrice:0.##} {Currency}";
    public string TotalText => $"{Liters * UnitPrice:0.##} {Currency}";
    public string MeterText => $"{PrevMeter:0.##} → {CurrentMeter:0.##}";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(DistributionDate).LocalDateTime.ToString("dd.MM.yyyy");
}
