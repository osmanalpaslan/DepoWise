using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Firma Tanım — YALNIZ Süper Admin (CompanyService + AccessControl süper-admin-only). Liste + yeni/düzenle
/// (ad, vergi no/dairesi, adres, telefon, e-posta, yetkili). Firma Admini bu ekranı GÖREMEZ/ATAYAMAZ.
/// </summary>
public sealed partial class CompaniesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "companies", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "companies", PermissionAction.Edit);

    public ObservableCollection<CompanyRow> Items { get; } = new();

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
    private CompanyRow? _selected;
    public bool HasSelection => Selected != null;

    // Form
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formTaxNo = "";
    [ObservableProperty] private string _formTaxOffice = "";
    [ObservableProperty] private string _formAddress = "";
    [ObservableProperty] private string _formPhone = "";
    [ObservableProperty] private string _formEmail = "";
    [ObservableProperty] private string _formAuthorized = "";
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ FİRMA" : "FİRMA DÜZENLE";

    public CompaniesViewModel(SessionContext session)
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
            foreach (var c in DesktopServices.Companies.List(_session)) Items.Add(c);
            Status = $"{Items.Count} firma";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private void NewCompany()
    {
        if (!CanWrite) { Status = "Yetki yok (yalnız Süper Admin)."; return; }
        EditId = null;
        FormName = ""; FormTaxNo = ""; FormTaxOffice = ""; FormAddress = "";
        FormPhone = ""; FormEmail = ""; FormAuthorized = ""; FormError = null;
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Firma seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok (yalnız Süper Admin)."; return; }
        EditId = Selected.Id;
        FormName = Selected.Name;
        FormTaxNo = Selected.TaxNo ?? "";
        FormTaxOffice = Selected.TaxOffice ?? "";
        FormAddress = Selected.Address ?? "";
        FormPhone = Selected.Phone ?? "";
        FormEmail = Selected.Email ?? "";
        FormAuthorized = Selected.AuthorizedPerson ?? "";
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName)) { FormError = "Firma adı zorunlu."; return; }
        var dto = new NewCompany(FormName.Trim(), FormTaxNo, FormTaxOffice, FormAddress, FormPhone, FormEmail, FormAuthorized);
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Firma güncellensin mi?" : "Firma oluşturulsun mu?", "Kaydet")) return;
        try
        {
            if (editing) DesktopServices.Companies.Update(_session, EditId!, dto);
            else DesktopServices.Companies.Create(_session, dto);
            ShowAdd = false; EditId = null;
            Load();
            Status = editing ? "Firma güncellendi." : "Firma oluşturuldu.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }
}
