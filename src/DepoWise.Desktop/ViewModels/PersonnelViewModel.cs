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
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Liste satırı: personel + bağlı kullanıcı hesabı rozeti (#6 Çalışan Yönetimi).</summary>
public sealed class PersonnelRowVm
{
    public PersonnelRecord Record { get; }
    public PersonnelAccount? Account { get; }
    public PersonnelRowVm(PersonnelRecord r, PersonnelAccount? a) { Record = r; Account = a; }
    public string Id => Record.Id;
    public string FullName => Record.FullName;
    public string? Title => Record.Title;
    public string? Phone => Record.Phone;
    public string? BranchId => Record.BranchId;
    public bool IsActive => Record.IsActive;
    public string StatusText => Record.IsActive ? "Aktif" : "Pasif";
    public bool HasAccount => Account is not null;
    public string AccessBadge => Account is null ? "Saha" : (Account.IsAdmin ? "Admin · " + Account.Username : "Kullanıcı · " + Account.Username);
}

/// <summary>
/// Çalışan Yönetimi (#6) — personel + uygulama kullanıcısı tek ekranda. Liste + yeni/düzenle + sil; "Uygulama
/// erişimi ver" ile hesap açılır (bir personele tek hesap). Mükerrer kişi uyarısı + saha-personeli onayı.
/// </summary>
public sealed partial class PersonnelViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "personnel", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "personnel", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "personnel", PermissionAction.Delete);
    public bool CanManageAccounts => AccessControl.IsAdmin(_session);

    public ObservableCollection<PersonnelRowVm> Items { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();
    private readonly System.Collections.Generic.List<PersonnelRowVm> _all = new();

    [ObservableProperty] private string _search = "";
    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Items.Clear();
        var q = (Search ?? "").Trim();
        System.Collections.Generic.IEnumerable<PersonnelRowVm> rows = _all;
        if (q.Length > 0)
        {
            var ci = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            bool Has(string? s) => s != null && ci.CompareInfo.IndexOf(s, q, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
            rows = _all.Where(p => Has(p.FullName) || Has(p.Title) || Has(p.Phone));
        }
        foreach (var p in rows) Items.Add(p);
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [ObservableProperty] private string? _status;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PersonnelRowVm? _selected;
    public bool HasSelection => Selected != null;

    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _fFullName = "";
    [ObservableProperty] private string _fTitle = "";
    [ObservableProperty] private string _fPhone = "";
    [ObservableProperty] private BranchRow? _fBranch;
    [ObservableProperty] private bool _fActive = true;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ ÇALIŞAN" : "ÇALIŞAN DÜZENLE";

    // ── #6 Hesap / mükerrer alanları ──
    [ObservableProperty] private string? _fDupWarning;   // olası aynı kişi uyarısı
    private bool _dupAck;
    [ObservableProperty] private bool _fHasAccount;      // düzenlenen çalışanın hesabı var mı
    [ObservableProperty] private string _fAccountInfo = "";
    private string? _fAccountUserId;
    [ObservableProperty] private bool _fGrantAccess;     // yeni hesap aç
    [ObservableProperty] private string _fUsername = "";
    [ObservableProperty] private string _fPassword = "";
    [ObservableProperty] private bool _fRoleAdmin;       // false=Personel, true=Firma Admini
    /// <summary>Hesap açma alanları görünsün mü (admin + hesabı yok + toggle açık).</summary>
    public bool ShowAccountFields => CanManageAccounts && !FHasAccount && FGrantAccess;
    /// <summary>"Uygulama erişimi ver" toggle'ı görünsün mü (admin + hesabı yok).</summary>
    public bool ShowGrantToggle => CanManageAccounts && !FHasAccount;
    partial void OnFGrantAccessChanged(bool value) => OnPropertyChanged(nameof(ShowAccountFields));
    partial void OnFHasAccountChanged(bool value) { OnPropertyChanged(nameof(ShowAccountFields)); OnPropertyChanged(nameof(ShowGrantToggle)); }
    partial void OnFFullNameChanged(string value) => UpdateDupWarning();
    partial void OnFPhoneChanged(string value) => UpdateDupWarning();

    private void UpdateDupWarning()
    {
        _dupAck = false;
        if (EditId is not null || string.IsNullOrWhiteSpace(FFullName)) { FDupWarning = null; return; }
        try
        {
            var dups = DesktopServices.Personnel.FindDuplicates(_session, FFullName, FPhone, null);
            FDupWarning = dups.Count == 0 ? null
                : "Olası aynı kişi: " + string.Join(" · ", dups.Select(d => string.IsNullOrEmpty(d.Phone) ? d.FullName : $"{d.FullName} ({d.Phone})"));
        }
        catch { FDupWarning = null; }
    }

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
            _all.Clear();
            System.Collections.Generic.IReadOnlyDictionary<string, PersonnelAccount> accounts;
            try { accounts = DesktopServices.Users.AccountsByPersonnel(_session.CompanyId); }
            catch { accounts = new System.Collections.Generic.Dictionary<string, PersonnelAccount>(); }
            foreach (var p in DesktopServices.Personnel.List(_session, new PageRequest { Limit = 500 }).Items)
                _all.Add(new PersonnelRowVm(p, accounts.TryGetValue(p.Id, out var a) ? a : null));
            if (Branches.Count == 0)
                try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            ApplyFilter();
            Status = $"{_all.Count} çalışan";
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
        ResetAccountFields();
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }

    /// <summary>Hesap/mükerrer alanlarını başlangıç durumuna al.</summary>
    private void ResetAccountFields()
    {
        FDupWarning = null; _dupAck = false;
        FHasAccount = false; FAccountInfo = ""; _fAccountUserId = null;
        FGrantAccess = false; FUsername = ""; FPassword = ""; FRoleAdmin = false;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Çalışan seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id; FFullName = Selected.FullName; FTitle = Selected.Title ?? "";
        FPhone = Selected.Phone ?? ""; FBranch = Branches.FirstOrDefault(b => b.Id == Selected.BranchId);
        FActive = Selected.IsActive; FormError = null;
        ResetAccountFields();
        if (Selected.Account is { } acc)
        {
            FHasAccount = true; _fAccountUserId = acc.UserId;
            FAccountInfo = $"{acc.Username} · {(acc.IsAdmin ? "Firma Admini" : "Personel")}";
        }
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FFullName)) { FormError = "Ad soyad zorunlu."; return; }
        var editing = EditId is not null;
        bool wantAccount = CanManageAccounts && !FHasAccount && FGrantAccess;
        if (wantAccount)
        {
            if (string.IsNullOrWhiteSpace(FUsername)) { FormError = "Kullanıcı adı zorunlu."; return; }
            if (string.IsNullOrWhiteSpace(FPassword)) { FormError = "Şifre zorunlu."; return; }
        }

        // Mükerrer kişi uyarısı: kullanıcı yine de kaydetmek isterse onaylatılır (yalnız yeni kayıt).
        if (!editing && FDupWarning is not null && !_dupAck)
        {
            if (!await ConfirmService.AskAsync(FDupWarning + "\nYine de yeni çalışan olarak kaydedilsin mi?", "Olası aynı kişi")) return;
            _dupAck = true;
        }
        // Yeni kayıt + hesap verilmiyorsa: "yalnız saha personeli mi?" onayı.
        if (!editing && !wantAccount)
        {
            if (!await ConfirmService.AskAsync("Bu çalışana uygulama erişimi (kullanıcı hesabı) verilmedi. Yalnız saha personeli olarak mı kaydedilsin?", "Saha personeli", "Evet, saha personeli")) return;
        }
        else if (!await ConfirmService.AskAsync(editing ? "Çalışan güncellensin mi?" : "Çalışan eklensin mi?", "Kaydet")) return;

        var dto = new NewPersonnel(FFullName.Trim(),
            string.IsNullOrWhiteSpace(FTitle) ? null : FTitle.Trim(),
            string.IsNullOrWhiteSpace(FPhone) ? null : FPhone.Trim(),
            FBranch?.Id, FActive);
        try
        {
            string personnelId;
            if (editing) { DesktopServices.Personnel.Update(_session, EditId!, dto); personnelId = EditId!; }
            else personnelId = DesktopServices.Personnel.Create(_session, dto);

            if (wantAccount)
            {
                var roleKey = FRoleAdmin ? RoleKeys.CompanyAdmin : RoleKeys.Staff;
                DesktopServices.Users.CreateUser(_session, new NewUser(
                    FUsername.Trim(), FPassword, FFullName.Trim(),
                    new[] { roleKey }, _session.CompanyId, null, FBranch?.Id, false, personnelId));
            }
            ShowAdd = false; EditId = null; Load();
            Status = editing ? "Çalışan güncellendi." : (wantAccount ? "Çalışan + hesap eklendi." : "Çalışan eklendi.");
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    /// <summary>Admin: çalışanın kullanıcı hesabı bağını çözer (hesap silinmez).</summary>
    [RelayCommand]
    private async Task RemoveAccount()
    {
        if (!CanManageAccounts || _fAccountUserId is null) return;
        if (!await ConfirmService.AskAsync($"'{FAccountInfo}' hesabının bu çalışana bağı kaldırılsın mı? (Hesap silinmez, bağ çözülür.)", "Bağı kaldır", "Evet, Kaldır", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Users.LinkPersonnel(_session, _fAccountUserId, null);
            FHasAccount = false; FAccountInfo = ""; _fAccountUserId = null; Load();
            Status = "Hesap bağı kaldırıldı.";
        }
        catch (Exception ex) { FormError = "Kaldırılamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Çalışan seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.FullName}' silinsin mi?", "Çalışan Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Personnel.SoftDelete(_session, Selected.Id); Load(); Status = "Çalışan silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
