using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme Giriş-Çıkış — 3 kayıt tipi: Giriş (stok +), Çıkış (stok −, negatif engeli), Transfer (şubeler arası).
/// Mevcut malzemeyi ara-seç → miktar (+ giriş için birim fiyat) → şube/not → kaydet. StockService doc'lu hareket
/// (operation_id idempotency). Altta son hareketler. Yeni malzeme oluşturma 'Malzemeler' ekranındadır.
/// </summary>
public sealed partial class StockEntryViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "stock", PermissionAction.Create);

    public ObservableCollection<string> KindOptions { get; } = new() { "Giriş", "Çıkış", "Transfer" };
    public ObservableCollection<MaterialRefRow> MaterialResults { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();
    public ObservableCollection<StockMovementRow> Movements { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    [NotifyPropertyChangedFor(nameof(IsExit))]
    [NotifyPropertyChangedFor(nameof(ShowPrice))]
    [NotifyPropertyChangedFor(nameof(ShowSingleBranch))]
    [NotifyPropertyChangedFor(nameof(QuantityLabel))]
    private string _selectedKind = "Giriş";

    public bool IsTransfer => SelectedKind == "Transfer";
    public bool IsExit => SelectedKind == "Çıkış";
    public bool ShowPrice => SelectedKind == "Giriş";
    public bool ShowSingleBranch => !IsTransfer;
    public string QuantityLabel => IsExit ? "Çıkacak Miktar" : "Miktar";

    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterial))]
    private MaterialRefRow? _selectedMaterial;
    public bool HasMaterial => SelectedMaterial != null;
    [ObservableProperty] private string _balanceText = "";

    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private BranchRow? _branch;
    [ObservableProperty] private BranchRow? _fromBranch;
    [ObservableProperty] private BranchRow? _toBranch;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Movements.Count > 0;
    public bool IsEmpty => !HasError && Movements.Count == 0;

    public StockEntryViewModel(SessionContext session)
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
            LoadError = null;
            Movements.Clear();
            foreach (var m in DesktopServices.Stock.RecentMovements(_session)) Movements.Add(m);
            Branches.Clear();
            try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            Status = $"{Movements.Count} hareket";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnMaterialSearchChanged(string value) => RefreshMaterials();

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
        try { BalanceText = $"Mevcut stok: {DesktopServices.Stock.GetBalance(m.Id):0.##}"; }
        catch { BalanceText = ""; }
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (SelectedMaterial is null) { FormError = "Malzeme seçin."; return; }
        if (Quantity <= 0) { FormError = "Miktar sıfırdan büyük olmalı."; return; }
        if (IsTransfer)
        {
            if (FromBranch is null || ToBranch is null) { FormError = "Transfer için kaynak ve hedef şube seçin."; return; }
            if (FromBranch.Id == ToBranch.Id) { FormError = "Kaynak ve hedef şube aynı olamaz."; return; }
        }

        var confirm = IsExit ? "Depo çıkışı kaydedilsin mi? (stok AZALIR)"
            : IsTransfer ? "Şubeler arası transfer kaydedilsin mi?"
            : "Malzeme girişi kaydedilsin mi? (stok ARTAR)";
        if (!await ConfirmService.AskAsync(confirm, "Stok Hareketi")) return;

        var op = Guid.NewGuid().ToString("N");
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        try
        {
            if (IsExit)
                DesktopServices.Stock.IssueOut(_session,
                    new[] { new StockLine(SelectedMaterial.Id, Quantity) }, op, branchId: Branch?.Id, note: note);
            else if (IsTransfer)
                DesktopServices.Stock.Transfer(_session, SelectedMaterial.Id, Quantity, FromBranch!.Id, ToBranch!.Id, op, note);
            else
                DesktopServices.Stock.ReceiveIn(_session,
                    new[] { new StockLine(SelectedMaterial.Id, Quantity, UnitPrice > 0 ? UnitPrice : null) }, op,
                    branchId: Branch?.Id, note: note);

            Status = "Stok hareketi kaydedildi.";
            ClearForm();
            Load();
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void ClearForm()
    {
        SelectedMaterial = null; MaterialSearch = ""; BalanceText = "";
        Quantity = 0; UnitPrice = 0; Note = ""; Branch = null; FromBranch = null; ToBranch = null;
        FormError = null;
        RefreshMaterials();
    }
}
