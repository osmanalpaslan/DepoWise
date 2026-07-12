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
    public bool CanDelete => _session.IsSuperAdmin;

    public ObservableCollection<CompanyRow> Items { get; } = new();
    /// <summary>Pasife alınmış (silinmiş) firmalar — sözleşme yenileme / yeniden aktifleştirme.</summary>
    public ObservableCollection<CompanyRow> DeletedItems { get; } = new();
    public bool HasDeleted => DeletedItems.Count > 0;

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
    [ObservableProperty] private int _formMaxUsers;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ FİRMA" : "FİRMA DÜZENLE";

    public CompaniesViewModel(SessionContext session)
    {
        _session = session;
        Load();                 // önce yereli göster (anında açılsın)
        _ = Refresh();          // sonra sunucudan aynala (çevrimiçiyse) — web'deki değişiklikler yansır
    }

    /// <summary>Sunucudaki firma listesini yerele aynalar (çevrimiçiyse), sonra listeyi tazeler.
    /// FİRMALAR WEB-OTORİTELİ: web'de eklenen/silinen firma böylece masaüstünde de doğru görünür.</summary>
    [RelayCommand]
    private async Task Refresh()
    {
        // SIRA ÖNEMLİ: önce çevrimdışı kuyruk sunucuya işlenir, SONRA sunucunun listesi yerele aynalanır.
        // (Kuyruk boşalmadan aynalanırsa, henüz gönderilmemiş yerel firma "sunucuda yok" sanılıp silinirdi.)
        await CompanySyncService.FlushThenSyncAsync();
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
            DeletedItems.Clear();
            if (_session.IsSuperAdmin)
                foreach (var c in DesktopServices.Companies.ListDeleted(_session)) DeletedItems.Add(c);
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasDeleted));
    }

    /// <summary>Pasife alınmış firmayı yeniden aktifleştirir (sözleşme yenileme). Kullanıcılar tekrar aktif olur.</summary>
    [RelayCommand]
    private async Task Reactivate(CompanyRow? row)
    {
        if (row is null) { Status = "Firma seçin."; return; }
        if (!_session.IsSuperAdmin) { Status = "Yetki yok (yalnız Süper Admin)."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{row.Name}' firması yeniden aktifleştirilsin mi?\nFirma geri gelir ve silme sırasında pasife alınan kullanıcılar tekrar giriş yapabilir.",
                "Aktife Al", "Evet, Aktife Al", "Vazgeç")) return;
        try
        {
            await CompanySyncService.ReactivateAsync(row.Id);   // yerel + kuyruk
            await Refresh();
            Status = $"'{row.Name}' aktifleştirildi." + PendingNote();
        }
        catch (Exception ex) { Status = "Aktifleştirilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void NewCompany()
    {
        if (!CanWrite) { Status = "Yetki yok (yalnız Süper Admin)."; return; }
        EditId = null;
        FormName = ""; FormTaxNo = ""; FormTaxOffice = ""; FormAddress = "";
        FormPhone = ""; FormEmail = ""; FormAuthorized = ""; FormMaxUsers = 0; FormError = null;
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
        FormMaxUsers = Selected.MaxUsers;
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; }

    [RelayCommand]
    private async Task DeleteCompany()
    {
        if (Selected is null) { Status = "Firma seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok (yalnız Süper Admin)."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.Name}' firması silinsin mi?\nBağlı kullanıcılar SİLİNMEZ, pasife alınır; firma aşağıdaki 'Pasif Firmalar' bölümünden geri getirilebilir.",
                "Firma Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            await CompanySyncService.DeleteAsync(Selected.Id);   // yerel + kuyruk (çevrimdışı çalışır)
            await Refresh();
            Status = "Firma silindi." + PendingNote();
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName)) { FormError = "Firma adı zorunlu."; return; }
        var dto = new NewCompany(FormName.Trim(), FormTaxNo, FormTaxOffice, FormAddress, FormPhone, FormEmail, FormAuthorized, FormMaxUsers);
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Firma güncellensin mi?" : "Firma oluşturulsun mu?", "Kaydet")) return;
        try
        {
            // Yerele yazar + kuyruğa alır; çevrimiçiyse hemen sunucuya işlenir (çevrimdışıysa bekler).
            if (editing) await CompanySyncService.UpdateAsync(EditId!, dto);
            else await CompanySyncService.CreateAsync(dto);
            ShowAdd = false; EditId = null;
            await Refresh();
            Status = (editing ? "Firma güncellendi." : "Firma oluşturuldu.") + PendingNote();
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    /// <summary>Kuyrukta bekleyen işlem varsa kullanıcıya çevrimdışı olduğunu söyle.</summary>
    private static string PendingNote()
    {
        var n = CompanySyncService.PendingCount();
        return n > 0 ? $" ({n} işlem çevrimdışı kuyrukta — internet gelince eşitlenecek.)" : "";
    }
}
