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
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme Giriş-Çıkış — 3 kayıt tipi (eski proje ile aynı): Yeni Kayıt / Transfer / Depo Çıkışı.
/// • Yeni Kayıt: tam malzeme formu (kod ile upsert) + stok ARTAR.
/// • Transfer: mevcut malzeme + şubeler arası taşıma.
/// • Depo Çıkışı: mevcut malzeme + stok AZALIR (negatif engeli).
/// StockService doc'lu hareket (operation_id idempotency). Altta son hareketler + iptal (ters kayıt).
/// </summary>
public sealed partial class StockEntryViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "stock", PermissionAction.Create);
    public bool CanReverse => AccessControl.Can(_session, "stock", PermissionAction.Delete);

    public ObservableCollection<string> KindOptions { get; } = new() { "Yeni Kayıt", "Transfer", "Depo Çıkışı" };
    public ObservableCollection<string> TypeOptions { get; } = new() { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };
    public ObservableCollection<MaterialRefRow> MaterialResults { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<StockMovementRow> Movements { get; } = new();

    // Yeni Kayıt lookup'ları
    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<LookupItem> SubCategories { get; } = new();
    public ObservableCollection<LookupItem> Units { get; } = new();
    public ObservableCollection<LookupItem> Brands { get; } = new();
    public ObservableCollection<LookupItem> Suppliers { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    [NotifyPropertyChangedFor(nameof(IsExit))]
    [NotifyPropertyChangedFor(nameof(ShowNewForm))]
    [NotifyPropertyChangedFor(nameof(ShowMaterialPicker))]
    [NotifyPropertyChangedFor(nameof(ShowPrice))]
    [NotifyPropertyChangedFor(nameof(ShowSingleBranch))]
    [NotifyPropertyChangedFor(nameof(QuantityLabel))]
    private string _selectedKind = "Yeni Kayıt";

    public bool IsNew => SelectedKind == "Yeni Kayıt";
    public bool IsTransfer => SelectedKind == "Transfer";
    public bool IsExit => SelectedKind == "Depo Çıkışı";

    /// <summary>Yeni Kayıt → tam malzeme formu; Transfer/Çıkış → mevcut malzeme seçici.</summary>
    public bool ShowNewForm => IsNew;
    public bool ShowMaterialPicker => IsTransfer || IsExit;
    public bool ShowPrice => IsNew;                 // birim fiyat yalnız girişte
    public bool ShowSingleBranch => !IsTransfer;
    public string QuantityLabel => IsExit ? "Çıkacak Miktar" : IsNew ? "Eklenecek Stok" : "Miktar";

    // ── Mevcut malzeme seçici (Transfer / Depo Çıkışı) ──
    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterial))]
    private MaterialRefRow? _selectedMaterial;
    public bool HasMaterial => SelectedMaterial != null;
    [ObservableProperty] private string _balanceText = "";

    // ── Yeni Kayıt: malzeme kartı alanları ──
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _newType = "Yedek Parça";
    [ObservableProperty] private LookupItem? _selectedCategory;
    [ObservableProperty] private LookupItem? _selectedSubCategory;
    [ObservableProperty] private LookupItem? _selectedUnit;
    [ObservableProperty] private LookupItem? _selectedBrand;
    [ObservableProperty] private LookupItem? _selectedSupplier;

    // ── Ortak hareket alanları ──
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _invoiceNo = "";
    [ObservableProperty] private string _orderSlipNo = "";
    [ObservableProperty] private string _creditSlipNo = "";
    [ObservableProperty] private BranchRow? _branch;
    [ObservableProperty] private BranchRow? _fromBranch;
    [ObservableProperty] private BranchRow? _toBranch;
    [ObservableProperty] private LookupItem? _personnelSel;   // Şoför / teslim alan
    [ObservableProperty] private VehicleListRow? _vehicleSel; // Teslim eden / transfer edilen araç
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
            if (Branches.Count == 0)
                try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            if (Personnel.Count == 0)
                try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
            if (Vehicles.Count == 0)
                try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
            if (Categories.Count == 0)
                try { foreach (var c in DesktopServices.Lookups.ListCategories(_session)) Categories.Add(c); } catch { }
            if (Units.Count == 0)
                try { foreach (var u in DesktopServices.Lookups.List(_session, "units")) Units.Add(u); } catch { }
            if (Brands.Count == 0)
                try { foreach (var b in DesktopServices.Lookups.ListBrands(_session, "material")) Brands.Add(b); } catch { }
            if (Suppliers.Count == 0)
                try { foreach (var sp in DesktopServices.Lookups.List(_session, "suppliers")) Suppliers.Add(sp); } catch { }
            Status = $"{Movements.Count} hareket";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnSelectedCategoryChanged(LookupItem? value)
    {
        SelectedSubCategory = null;
        SubCategories.Clear();
        if (value is not null)
            try { foreach (var sc in DesktopServices.Lookups.ListCategories(_session, value.Id)) SubCategories.Add(sc); } catch { }
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

    /// <summary>Koda göre mevcut malzemeyi bul (tam eşleşme). Yeni Kayıt'ta upsert için.</summary>
    private string? FindMaterialIdByCode(string code)
    {
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 50 }, code);
            return page.Items.FirstOrDefault(x =>
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        catch { return null; }
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }

        string? Doc(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var note = Doc(Note);
        var inv = Doc(InvoiceNo); var ord = Doc(OrderSlipNo); var crd = Doc(CreditSlipNo);
        var op = Guid.NewGuid().ToString("N");

        try
        {
            if (IsNew)
            {
                if (string.IsNullOrWhiteSpace(Code)) { FormError = "Kod zorunlu."; return; }
                if (string.IsNullOrWhiteSpace(Name)) { FormError = "Ad zorunlu."; return; }
                if (SelectedUnit is null) { FormError = "Birim seçin."; return; }
                if (Quantity < 0) { FormError = "Eklenecek stok negatif olamaz."; return; }
                if (!await ConfirmService.AskAsync("Malzeme kaydedilip stok girişi yapılsın mı? (stok ARTAR)", "Yeni Kayıt")) return;

                var code = Code.Trim();
                var categoryId = SelectedSubCategory?.Id ?? SelectedCategory?.Id;
                var materialId = FindMaterialIdByCode(code);
                if (materialId is null)
                {
                    materialId = DesktopServices.Materials.Create(_session, new NewMaterial(
                        Code: code, Name: Name.Trim(),
                        Type: string.IsNullOrWhiteSpace(NewType) ? null : NewType,
                        CategoryId: categoryId, UnitId: SelectedUnit.Id,
                        BrandId: SelectedBrand?.Id, SupplierId: SelectedSupplier?.Id,
                        UnitPrice: UnitPrice, Currency: "TRY",
                        Description: note));
                }
                if (Quantity > 0)
                    DesktopServices.Stock.ReceiveIn(_session,
                        new[] { new StockLine(materialId, Quantity, UnitPrice > 0 ? UnitPrice : null) }, op,
                        branchId: Branch?.Id, personnelId: PersonnelSel?.Id, vehicleId: VehicleSel?.Id, note: note,
                        invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd);
                Status = FindMaterialIdByCode(code) is not null && materialId is not null
                    ? "Yeni kayıt: malzeme oluşturuldu/güncellendi ve stok eklendi."
                    : "Kaydedildi.";
            }
            else
            {
                if (SelectedMaterial is null) { FormError = "Malzeme seçin."; return; }
                if (Quantity <= 0) { FormError = "Miktar sıfırdan büyük olmalı."; return; }

                if (IsExit)
                {
                    if (!await ConfirmService.AskAsync("Depo çıkışı kaydedilsin mi? (stok AZALIR)", "Depo Çıkışı")) return;
                    DesktopServices.Stock.IssueOut(_session,
                        new[] { new StockLine(SelectedMaterial.Id, Quantity) }, op,
                        branchId: Branch?.Id, personnelId: PersonnelSel?.Id, vehicleId: VehicleSel?.Id, note: note,
                        invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd);
                    Status = "Depo çıkışı kaydedildi.";
                }
                else // Transfer
                {
                    if (FromBranch is null || ToBranch is null) { FormError = "Transfer için kaynak ve hedef şube seçin."; return; }
                    if (FromBranch.Id == ToBranch.Id) { FormError = "Kaynak ve hedef şube aynı olamaz."; return; }
                    if (!await ConfirmService.AskAsync("Şubeler arası transfer kaydedilsin mi?", "Transfer")) return;
                    DesktopServices.Stock.Transfer(_session, SelectedMaterial.Id, Quantity, FromBranch.Id, ToBranch.Id, op, note,
                        personnelId: PersonnelSel?.Id, vehicleId: VehicleSel?.Id,
                        invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd);
                    Status = "Transfer kaydedildi.";
                }
            }

            ClearForm();
            Load();
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ReverseMovement(StockMovementRow? row)
    {
        if (row?.DocumentId is null) return;
        if (!CanReverse) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                "Bu stok hareketi İPTAL edilsin mi? (stok etkisi ters kayıtla geri alınır)",
                "Hareketi İptal Et", "Evet, İptal", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Stock.ReverseDocument(_session, row.DocumentId, "Kullanıcı iptali");
            Status = "Hareket iptal edildi (ters kayıt).";
            Load();
        }
        catch (Exception ex) { Status = "İptal edilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void ClearForm()
    {
        SelectedMaterial = null; MaterialSearch = ""; BalanceText = "";
        Code = ""; Name = ""; NewType = "Yedek Parça";
        SelectedCategory = null; SelectedSubCategory = null; SelectedUnit = null;
        SelectedBrand = null; SelectedSupplier = null;
        Quantity = 0; UnitPrice = 0; Note = ""; Branch = null; FromBranch = null; ToBranch = null;
        PersonnelSel = null; VehicleSel = null;
        InvoiceNo = ""; OrderSlipNo = ""; CreditSlipNo = "";
        FormError = null;
        RefreshMaterials();
    }
}
