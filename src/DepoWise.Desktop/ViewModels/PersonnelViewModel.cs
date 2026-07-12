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

/// <summary>Liste satırı: personel + (varsa) bağlı kullanıcı hesabı rozeti.</summary>
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
    public bool IsFieldStaff => Record.IsFieldStaff;
    public string StatusText => Record.IsActive ? "Aktif" : "Pasif";
    public bool HasAccount => Account is not null;
    /// <summary>ERİŞİM sütunu: hesap varsa Kullanıcı/Admin, yoksa "Saha personeli" ya da uyarı ("Kullanıcı yok").</summary>
    public string AccessBadge => Account is not null
        ? (Account.IsAdmin ? "Admin · " + Account.Username : "Kullanıcı · " + Account.Username)
        : (Record.IsFieldStaff ? "Saha personeli" : "Kullanıcı yok");
}

/// <summary>
/// Personel (Fikir B) — yalnız personel kayıtları. Uygulama hesabı AÇILMAZ; hesap "Kullanıcılar" ekranından
/// açılıp "Personel seç" ile bu kayda bağlanır. Kullanıcı bağlı değilse ve "Saha personeli" işaretli değilse
/// kaydederken uyarı çıkar. Unvan sabit tanım listesinden seçilir ("+" ile yeni tanım eklenir).
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
    [ObservableProperty] private string? _fTitle;        // unvan: sabit tanım listesinden seçilir
    [ObservableProperty] private string _fPhone = "";
    [ObservableProperty] private BranchRow? _fBranch;
    [ObservableProperty] private bool _fActive = true;
    [ObservableProperty] private bool _fFieldStaff;      // "Saha personeli" — işaretliyse kullanıcı uyarısı çıkmaz
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ PERSONEL" : "PERSONEL DÜZENLE";

    // ── Unvan sabit tanımları + "+" ile ekleme ──
    public ObservableCollection<string> Titles { get; } = new();
    [ObservableProperty] private bool _showAddTitle;
    [ObservableProperty] private string _newTitle = "";

    // ── Bağlı hesap (yalnız bilgi; hesap işlemleri Kullanıcılar ekranında) + mükerrer ──
    [ObservableProperty] private string? _fDupWarning;   // olası aynı kişi uyarısı
    private bool _dupAck;
    [ObservableProperty] private bool _fHasAccount;      // düzenlenen personelin hesabı var mı
    [ObservableProperty] private string _fAccountInfo = "";
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
            LoadTitles();
            ApplyFilter();
            Status = $"{_all.Count} personel";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>Unvan sabit tanımlarını (firma bazlı) yükler.</summary>
    private void LoadTitles()
    {
        try
        {
            Titles.Clear();
            foreach (var t in DesktopServices.PersonnelTitles.List(_session)) Titles.Add(t.Name);
        }
        catch { }
    }

    /// <summary>"+" — yeni unvan tanımı ekler ve seçili yapar.</summary>
    [RelayCommand]
    private void AddTitle()
    {
        var name = (NewTitle ?? "").Trim();
        if (name.Length == 0) { FormError = "Unvan adı boş olamaz."; return; }
        try
        {
            var t = DesktopServices.PersonnelTitles.Create(_session, name);
            LoadTitles();
            FTitle = t.Name;                 // yeni unvanı doğrudan seç
            ShowAddTitle = false; NewTitle = ""; FormError = null;
            Status = "Unvan eklendi.";
        }
        catch (Exception ex) { FormError = "Unvan eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void ToggleAddTitle() { ShowAddTitle = !ShowAddTitle; NewTitle = ""; }

    [RelayCommand]
    private void NewPersonnel()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; FFullName = ""; FTitle = null; FPhone = ""; FBranch = null; FActive = true;
        FFieldStaff = false; FormError = null;
        ResetAccountFields();
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }

    /// <summary>Hesap bilgisi / mükerrer / unvan-ekleme alanlarını başlangıç durumuna al.</summary>
    private void ResetAccountFields()
    {
        FDupWarning = null; _dupAck = false;
        FHasAccount = false; FAccountInfo = "";
        ShowAddTitle = false; NewTitle = "";
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Personel seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id; FFullName = Selected.FullName; FTitle = Selected.Title;
        FPhone = Selected.Phone ?? ""; FBranch = Branches.FirstOrDefault(b => b.Id == Selected.BranchId);
        FActive = Selected.IsActive; FFieldStaff = Selected.IsFieldStaff; FormError = null;
        ResetAccountFields();
        if (Selected.Account is { } acc)
        {
            FHasAccount = true;
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

        // Mükerrer kişi uyarısı (yalnız yeni kayıt).
        if (!editing && FDupWarning is not null && !_dupAck)
        {
            if (!await ConfirmService.AskAsync(FDupWarning + "\nYine de yeni personel olarak kaydedilsin mi?", "Olası aynı kişi")) return;
            _dupAck = true;
        }

        // Fikir B: kullanıcı bağlı DEĞİLSE ve "Saha personeli" işaretli DEĞİLSE uyar.
        // Kutucuk işaretliyse bu koşul hiç çalışmaz.
        if (!FHasAccount && !FFieldStaff)
        {
            if (!await ConfirmService.AskAsync(
                    "Bu personele uygulama kullanıcısı bağlanmadı. Saha personeli mi?\n\n" +
                    "• Evet ise \"Saha personeli\" olarak kaydedilir.\n" +
                    "• Hayır ise Vazgeç deyin; hesabı \"Kullanıcılar\" ekranından açıp bu personele bağlayın.",
                    "Saha personeli", "Evet, saha personeli")) return;
            FFieldStaff = true;   // onayladı → kutucuk işaretlenir
        }
        else if (!await ConfirmService.AskAsync(editing ? "Personel güncellensin mi?" : "Personel eklensin mi?", "Kaydet")) return;

        var dto = new NewPersonnel(FFullName.Trim(),
            string.IsNullOrWhiteSpace(FTitle) ? null : FTitle!.Trim(),
            string.IsNullOrWhiteSpace(FPhone) ? null : FPhone.Trim(),
            FBranch?.Id, FActive, FFieldStaff);
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
