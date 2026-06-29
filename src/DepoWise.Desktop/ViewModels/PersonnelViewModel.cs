using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Personel — liste + yeni/düzenle (ad soyad, unvan, telefon, şube, aktif) + sil. PersonnelService (tenant +
/// şube kapsamı + soft delete). Diğer ekranlardaki personel seçicileri bu kayıtları kullanır.
/// </summary>
public sealed partial class PersonnelViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "personnel", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "personnel", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "personnel", PermissionAction.Delete);

    public ObservableCollection<PersonnelRecord> Items { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PersonnelRecord? _selected;
    public bool HasSelection => Selected != null;

    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _fFullName = "";
    [ObservableProperty] private string _fTitle = "";
    [ObservableProperty] private string _fPhone = "";
    [ObservableProperty] private BranchRow? _fBranch;
    [ObservableProperty] private bool _fActive = true;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ PERSONEL" : "PERSONEL DÜZENLE";

    public PersonnelViewModel(SessionContext session)
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
            foreach (var p in DesktopServices.Personnel.List(_session, new PageRequest { Limit = 500 }).Items) Items.Add(p);
            if (Branches.Count == 0)
                try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            Status = $"{Items.Count} personel";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void NewPersonnel()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; FFullName = ""; FTitle = ""; FPhone = ""; FBranch = null; FActive = true; FormError = null;
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Personel seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id; FFullName = Selected.FullName; FTitle = Selected.Title ?? "";
        FPhone = Selected.Phone ?? ""; FBranch = Branches.FirstOrDefault(b => b.Id == Selected.BranchId);
        FActive = Selected.IsActive; FormError = null;
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FFullName)) { FormError = "Ad soyad zorunlu."; return; }
        var dto = new NewPersonnel(FFullName.Trim(),
            string.IsNullOrWhiteSpace(FTitle) ? null : FTitle.Trim(),
            string.IsNullOrWhiteSpace(FPhone) ? null : FPhone.Trim(),
            FBranch?.Id, FActive);
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Personel güncellensin mi?" : "Personel eklensin mi?", "Kaydet")) return;
        try
        {
            if (editing) DesktopServices.Personnel.Update(_session, EditId!, dto);
            else DesktopServices.Personnel.Create(_session, dto);
            ShowAdd = false; EditId = null; Load();
            Status = editing ? "Personel güncellendi." : "Personel eklendi.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Personel seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.FullName}' silinsin mi?", "Personel Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Personnel.SoftDelete(_session, Selected.Id); Load(); Status = "Personel silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
