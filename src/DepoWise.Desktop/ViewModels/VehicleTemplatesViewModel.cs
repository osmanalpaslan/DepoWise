using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Araç Genel Tanım (Şablonlar) — liste + yeni şablon + detay + sil. Araç formunu otomatik doldurur.</summary>
public sealed partial class VehicleTemplatesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<VehicleTemplateRow> Items { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private VehicleTemplateRow? _selected;
    public bool HasSelection => Selected != null;

    [ObservableProperty] private bool _showAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string _newName = "";
    [ObservableProperty] private string _newCode = "";
    [ObservableProperty] private int _newYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private bool _triedSave;
    public string? NameError => TriedSave && string.IsNullOrWhiteSpace(NewName) ? "Ad zorunlu." : null;
    public bool HasNameError => NameError != null;

    // Lookups
    public ObservableCollection<LookupItem> Types { get; } = new();
    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<LookupItem> Brands { get; } = new();
    public ObservableCollection<LookupItem> Models { get; } = new();
    [ObservableProperty] private LookupItem? _selType;
    [ObservableProperty] private LookupItem? _selCategory;
    [ObservableProperty] private LookupItem? _selBrand;
    [ObservableProperty] private LookupItem? _selModel;
    private bool _lookupsLoaded;

    partial void OnSelBrandChanged(LookupItem? value)
    {
        SelModel = null; Models.Clear();
        if (value is null) return;
        try { foreach (var m in DesktopServices.Lookups.ListVehicleModels(_session, value.Id)) Models.Add(m); }
        catch { }
    }

    // Inline "+"
    [ObservableProperty] private bool _isAddingType; [ObservableProperty] private string _newTypeName = "";
    [ObservableProperty] private bool _isAddingCat; [ObservableProperty] private string _newCatName = "";
    [ObservableProperty] private bool _isAddingBrand; [ObservableProperty] private string _newBrandName = "";
    [ObservableProperty] private bool _isAddingModel; [ObservableProperty] private string _newModelName = "";

    public bool CanWrite => AccessControl.Can(_session, "vehicle_templates", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "vehicle_templates", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "vehicle_templates", PermissionAction.Delete);
    public string? AddButtonText => CanWrite ? "Yeni Şablon" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private string? _editId;
    public bool IsEditMode => EditId != null;
    public string FormTitle => IsEditMode ? "ŞABLON DÜZENLE" : "YENİ ŞABLON";

    // Uyumlu malzeme (çoklu seçim + arama + tümünü seç)
    public ObservableCollection<TplMaterialPick> MaterialPicks { get; } = new();
    public ObservableCollection<TplMaterialPick> FilteredMaterials { get; } = new();
    [ObservableProperty] private string _materialSearch = "";
    public ObservableCollection<TemplateMaterialRow> DetailMaterials { get; } = new();

    partial void OnMaterialSearchChanged(string value) => RebuildFilteredMaterials();
    partial void OnSelectedChanged(VehicleTemplateRow? value)
    {
        DetailMaterials.Clear();
        if (value is null) return;
        try { foreach (var m in DesktopServices.VehicleTemplates.GetMaterialRows(_session, value.Id)) DetailMaterials.Add(m); }
        catch { }
    }

    private void RebuildFilteredMaterials()
    {
        FilteredMaterials.Clear();
        var t = MaterialSearch?.Trim();
        foreach (var p in MaterialPicks)
            if (string.IsNullOrEmpty(t) || p.Code.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(t, StringComparison.OrdinalIgnoreCase))
                FilteredMaterials.Add(p);
    }

    [RelayCommand] private void SelectAllMaterials() { foreach (var p in FilteredMaterials) p.IsSelected = true; }
    [RelayCommand] private void ClearMaterials() { foreach (var p in FilteredMaterials) p.IsSelected = false; }

    public VehicleTemplatesViewModel(SessionContext session)
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
            foreach (var t in DesktopServices.VehicleTemplates.List(_session, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()))
                Items.Add(t);
            Status = $"{Items.Count} şablon";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    private (string Name, string Code, int Year, string? Type, string? Cat, string? Brand, string? Model)? _origTpl;

    [RelayCommand]
    private async Task Add()
    {
        TriedSave = true;
        bool editing = IsEditMode;
        if (editing ? !CanEdit : !CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "Ad zorunlu."; return; }
        if (!editing && !await ConfirmService.AskAsync("Yeni şablon kaydedilsin mi?", "Kaydet")) return;
        if (editing && _origTpl is { } o)
        {
            var sum = new ChangeSummary();
            sum.Add("Ad", o.Name, NewName.Trim());
            sum.Add("İç Kod", o.Code, NewCode.Trim());
            sum.Add("Yıl", o.Year, NewYear);
            sum.Add("Makine Tipi", o.Type, SelType?.Name);
            sum.Add("Kategori", o.Cat, SelCategory?.Name);
            sum.Add("Marka", o.Brand, SelBrand?.Name);
            sum.Add("Model", o.Model, SelModel?.Name);
            if (!await ConfirmService.AskAsync(sum.Build("Şablon güncellensin mi?"), "Kaydet")) return;
        }

        var dto = new NewVehicleTemplate(
            Name: NewName.Trim(),
            InternalCode: string.IsNullOrWhiteSpace(NewCode) ? null : NewCode.Trim(),
            VehicleTypeId: SelType?.Id, CategoryId: SelCategory?.Id,
            BrandId: SelBrand?.Id, VehicleModelId: SelModel?.Id,
            ProductionYear: NewYear > 0 ? NewYear : (int?)null);
        var matIds = MaterialPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        try
        {
            if (editing)
            {
                DesktopServices.VehicleTemplates.Update(_session, EditId!, dto);
                DesktopServices.VehicleTemplates.SetMaterials(_session, EditId!, matIds);
                Clear(); Load(); Status = "Şablon güncellendi.";
            }
            else
            {
                DesktopServices.VehicleTemplates.Create(_session, dto, matIds);
                Clear(); Load(); Status = "Şablon eklendi.";
            }
        }
        catch (Exception ex) { Status = editing ? "Güncellenemedi: " + ex.Message : "Eklenemedi: " + ex.Message; }
    }

    /// <summary>Seçili şablonu düzenleme modunda forma yükler (alanlar + uyumlu malzemeler). Onay sorar.</summary>
    [RelayCommand]
    private async Task BeginEdit()
    {
        if (Selected is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu şablonu düzenlemek istiyor musunuz?", "Düzenle")) return;

        LoadLookups();
        var t = DesktopServices.VehicleTemplates.Get(_session, Selected.Id);
        if (t is null) { Status = "Şablon bulunamadı."; return; }
        EditId = t.Id;
        NewName = t.Name; NewCode = t.InternalCode ?? ""; NewYear = t.ProductionYear ?? 0;
        SelType = Types.FirstOrDefault(x => x.Id == t.VehicleTypeId);
        SelCategory = Categories.FirstOrDefault(x => x.Id == t.CategoryId);
        SelBrand = Brands.FirstOrDefault(x => x.Id == t.BrandId); // modelleri yükler
        SelModel = Models.FirstOrDefault(x => x.Id == t.VehicleModelId);
        _origTpl = (NewName, NewCode, NewYear, SelType?.Name, SelCategory?.Name, SelBrand?.Name, SelModel?.Name);
        var mat = DesktopServices.VehicleTemplates.GetMaterials(Selected.Id).ToHashSet();
        foreach (var p in MaterialPicks) p.IsSelected = mat.Contains(p.Id);
        RebuildFilteredMaterials();
        TriedSave = false; ShowAdd = true;
    }

    [RelayCommand]
    private void ToggleAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = !ShowAdd;
        if (ShowAdd) LoadLookups();
    }

    [RelayCommand]
    private void Clear()
    {
        NewName = ""; NewCode = ""; NewYear = 0;
        SelType = null; SelCategory = null; SelBrand = null; SelModel = null;
        IsAddingType = IsAddingCat = IsAddingBrand = IsAddingModel = false;
        foreach (var p in MaterialPicks) p.IsSelected = false;
        MaterialSearch = ""; EditId = null;
        TriedSave = false; ShowAdd = false;
    }

    [RelayCommand]
    private async Task RequestDelete()
    {
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (Selected is null) return;
        if (!await ConfirmService.AskAsync($"'{Selected.Name}' şablonu silinsin mi?", "Şablon Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.VehicleTemplates.Delete(_session, Selected.Id); Selected = null; Load(); Status = "Şablon silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    private void LoadLookups()
    {
        if (_lookupsLoaded) return;
        try
        {
            Types.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "vehicle_types")) Types.Add(x);
            Categories.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "vehicle_categories")) Categories.Add(x);
            Brands.Clear(); foreach (var x in DesktopServices.Lookups.ListBrands(_session, "vehicle")) Brands.Add(x);
            MaterialPicks.Clear();
            foreach (var m in DesktopServices.Materials.List(_session, new PageRequest { Limit = 200 }).Items)
                MaterialPicks.Add(new TplMaterialPick(m.Id, m.Code, m.Name));
            RebuildFilteredMaterials();
            _lookupsLoaded = true;
        }
        catch (Exception ex) { Status = "Tanımlar yüklenemedi: " + ex.Message; }
    }

    private void AddLookup(string name, Func<string> add, ObservableCollection<LookupItem> coll, Action<LookupItem> select, Action done)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try { var id = add(); var item = new LookupItem(id, name.Trim()); coll.Add(item); select(item); done(); }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddType() { IsAddingType = true; NewTypeName = ""; }
    [RelayCommand] private void CancelAddType() { IsAddingType = false; NewTypeName = ""; }
    [RelayCommand] private void ConfirmAddType() => AddLookup(NewTypeName, () => DesktopServices.Lookups.AddVehicleType(_session, NewTypeName.Trim()), Types, x => SelType = x, () => { IsAddingType = false; NewTypeName = ""; });

    [RelayCommand] private void StartAddCat() { IsAddingCat = true; NewCatName = ""; }
    [RelayCommand] private void CancelAddCat() { IsAddingCat = false; NewCatName = ""; }
    [RelayCommand] private void ConfirmAddCat() => AddLookup(NewCatName, () => DesktopServices.Lookups.AddVehicleCategory(_session, NewCatName.Trim()), Categories, x => SelCategory = x, () => { IsAddingCat = false; NewCatName = ""; });

    [RelayCommand] private void StartAddBrand() { IsAddingBrand = true; NewBrandName = ""; }
    [RelayCommand] private void CancelAddBrand() { IsAddingBrand = false; NewBrandName = ""; }
    [RelayCommand] private void ConfirmAddBrand() => AddLookup(NewBrandName, () => DesktopServices.Lookups.AddVehicleBrand(_session, NewBrandName.Trim()), Brands, x => SelBrand = x, () => { IsAddingBrand = false; NewBrandName = ""; });

    [RelayCommand] private void StartAddModel() { if (SelBrand is null) { Status = "Önce marka seçin."; return; } IsAddingModel = true; NewModelName = ""; }
    [RelayCommand] private void CancelAddModel() { IsAddingModel = false; NewModelName = ""; }
    [RelayCommand] private void ConfirmAddModel() { if (SelBrand is null) return; AddLookup(NewModelName, () => DesktopServices.Lookups.AddVehicleModel(_session, SelBrand!.Id, NewModelName.Trim()), Models, x => SelModel = x, () => { IsAddingModel = false; NewModelName = ""; }); }
}

public sealed partial class TplMaterialPick : ObservableObject
{
    public string Id { get; }
    public string Code { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
    public string Display => $"{Code} — {Name}";
    public TplMaterialPick(string id, string code, string name) { Id = id; Code = code; Name = name; }
}
