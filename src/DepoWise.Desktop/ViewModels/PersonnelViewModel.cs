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
/// Personel — personel + uygulama kullanıcısı TEK EKRANDA (Fikir A). "Uygulama erişimi ver" ile aynı formdan
/// hesap açılır (bir personele tek hesap); admin hesap bağını kaldırabilir. Koşullar: hesap yoksa/açılmıyorsa ve
/// "Saha personeli" işaretli değilse kaydederken uyarı çıkar (kutucuk işaretliyse çıkmaz). Mükerrer kişi uyarısı.
/// Unvan sabit tanım listesinden seçilir ("+" ile yeni tanım eklenir).
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
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BranchPopulator => SearchPopulator.For(() => Branches, b => b.Name);
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

    /// <summary>⭐ LST-01: liste sunucuda kesildi mi. Kesilmenin SESSİZ olması, kullanıcının kaydı
    /// "yok" sanmasına yol açıyordu — babanın yakıt kaydını kaybettiği kusurun aynı sınıfı.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KesilmeUyarisi))]
    private bool _dahaFazlaVar;
    public string KesilmeUyarisi =>
        "Bu listede gösterilenden DAHA FAZLA personel var. Hepsini görmek yerine aramayı kullanın — "
        + "aradığınız kişi listede görünmüyorsa yok demek değildir.";

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
    /// <summary>DÜZENLEME KİLİDİ: düzenlemeye başlarken okunan sürüm; kaydederken geri gönderilir.
    /// null = yeni kayıt ya da sürüm bilinmiyor → kontrol yapılmaz.</summary>
    private long? _editVersion;
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

    // ── Uygulama erişimi: MEVCUT kullanıcıyı bağla (hesap açma yok) + bağlı hesap + mükerrer ──
    [ObservableProperty] private string? _fDupWarning;   // olası aynı kişi uyarısı
    private bool _dupAck;
    [ObservableProperty] private bool _fHasAccount;      // düzenlenen personelin hesabı var mı
    [ObservableProperty] private string _fAccountInfo = "";
    private string? _fAccountUserId;
    [ObservableProperty] private bool _fGrantAccess;     // mevcut kullanıcıyı bağla anahtarı
    [ObservableProperty] private LinkableUser? _fLinkUser; // bağlanacak mevcut kullanıcı

    /// <summary>Bağlanabilir (henüz bir personele bağlı olmayan) kullanıcılar.</summary>
    public ObservableCollection<LinkableUser> LinkableUsers { get; } = new();

    /// <summary>"Mevcut kullanıcıyı bağla" anahtarı görünsün mü (admin + hesabı yok + saha personeli değil).</summary>
    public bool ShowGrantToggle => CanManageAccounts && !FHasAccount && !FFieldStaff;
    /// <summary>Kullanıcı bağlama alanları görünsün mü (anahtar açık).</summary>
    public bool ShowAccountFields => ShowGrantToggle && FGrantAccess;
    /// <summary>Bağlanabilir kullanıcı var mı (yoksa uyarı gösterilir).</summary>
    public bool HasLinkableUsers => LinkableUsers.Count > 0;

    partial void OnFGrantAccessChanged(bool value) => OnPropertyChanged(nameof(ShowAccountFields));
    partial void OnFHasAccountChanged(bool value) { OnPropertyChanged(nameof(ShowGrantToggle)); OnPropertyChanged(nameof(ShowAccountFields)); }
    /// <summary>"Saha personeli" işaretlenirse kullanıcı bağlama kapanır (kişi uygulamaya girmeyecek).</summary>
    partial void OnFFieldStaffChanged(bool value)
    {
        if (value) { FGrantAccess = false; FLinkUser = null; }
        OnPropertyChanged(nameof(ShowGrantToggle));
        OnPropertyChanged(nameof(ShowAccountFields));
    }
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
            LoadLinkableUsers();
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

    /// <summary>Bağlanabilir (henüz bağsız) kullanıcıları yükler — "mevcut kullanıcıyı bağla" listesi.
    /// Önce YEREL (anında, çevrimdışı da çalışır), sonra ÇEVRİMİÇİYSE sunucudan tazeler: başka makinede/web'de
    /// oluşturulmuş ama bu makinenin yerel users tablosunda olmayan kullanıcılar da görünsün (kullanıcı bulgusu
    /// 2026-07-19: Mustafa Alpaslan sunucuda var ama yerelde yoktu → listelenmiyordu).</summary>
    private void LoadLinkableUsers()
    {
        try
        {
            LinkableUsers.Clear();
            if (CanManageAccounts)
                foreach (var u in DesktopServices.Users.ListLinkableUsers(_session)) LinkableUsers.Add(u);
        }
        catch { }
        OnPropertyChanged(nameof(HasLinkableUsers));
        if (CanManageAccounts) _ = RefreshLinkableUsersFromServerAsync();
    }

    /// <summary>Çevrimiçiyse sunucudaki bağlanabilir kullanıcıları çekip listeyi tazeler (sunucu, kullanıcı
    /// varlığında otoritedir). Çevrimdışı/başarısız → yerel liste korunur (dokunulmaz).</summary>
    private async System.Threading.Tasks.Task RefreshLinkableUsersFromServerAsync()
    {
        var server = await ServerUserClient.GetLinkableUsersAsync();
        if (server is null) return;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            LinkableUsers.Clear();
            foreach (var u in server) LinkableUsers.Add(u);
            OnPropertyChanged(nameof(HasLinkableUsers));
        });
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
        EditId = null; _editVersion = null; // düzenleme kilidi: yeni kayıtta sürüm yok
        FFullName = ""; FTitle = null; FPhone = ""; FBranch = null; FActive = true;
        FFieldStaff = false; FormError = null;
        ResetAccountFields();
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }

    /// <summary>Hesap / mükerrer / unvan-ekleme alanlarını başlangıç durumuna al.</summary>
    private void ResetAccountFields()
    {
        FDupWarning = null; _dupAck = false;
        FHasAccount = false; FAccountInfo = ""; _fAccountUserId = null;
        FGrantAccess = false; FLinkUser = null;
        ShowAddTitle = false; NewTitle = "";
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Personel seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id; _editVersion = Selected.Record.Version; // düzenleme kilidi
        FFullName = Selected.FullName; FTitle = Selected.Title;
        FPhone = Selected.Phone ?? ""; FBranch = Branches.FirstOrDefault(b => b.Id == Selected.BranchId);
        FActive = Selected.IsActive; FFieldStaff = Selected.IsFieldStaff; FormError = null;
        ResetAccountFields();
        if (Selected.Account is { } acc)
        {
            FHasAccount = true; _fAccountUserId = acc.UserId;
            FAccountInfo = $"{acc.Username} · {(acc.IsAdmin ? "Firma Admini" : "Personel")}";
        }
        ShowAdd = true; OnPropertyChanged(nameof(FormTitle));
    }


    /// <summary>⭐ PRT-02: Excel'e aktar — O AN EKRANDAKİ SAYFA DEĞİL, filtrelenmiş TÜM sonuç
    /// (projenin liste ekranı kuralı). Yetki: mevcut "export" modülü; yeni yetki AÇILMADI.</summary>
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            // Ekrandaki filtre AYNEN uygulanır; sayfa sınırı UYGULANMAZ (tüm sonuç indirilir).
            var tumu = DesktopServices.Personnel.List(_session,
                new PageRequest { Limit = 100_000 },
                search: string.IsNullOrWhiteSpace(Search) ? null : Search).Items;
            var hedef = await FilePickerService.SaveExcelAsync("Personel.xlsx");
            if (hedef is null) return;
            await System.IO.File.WriteAllBytesAsync(hedef,
                DesktopServices.Excel.Export(DepoWise.Infrastructure.Org.PersonnelService.ToTableModel(tumu)));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }
    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FFullName)) { FormError = "Ad soyad zorunlu."; return; }
        // Yumuşak uyarı: telefon biçimi makul değilse — kullanıcı yine de geçebilir (madde 6).
        if (!DepoWise.Application.Ui.FieldChecks.PhoneLooksValid(FPhone)
            && !await ConfirmService.AskAsync("Telefon numarası eksik/hatalı görünüyor. Yine de kaydedilsin mi?", "Telefon Uyarısı", "Evet, Kaydet")) return;
        var editing = EditId is not null;

        // Mükerrer kişi uyarısı (yalnız yeni kayıt).
        if (!editing && FDupWarning is not null && !_dupAck)
        {
            if (!await ConfirmService.AskAsync(FDupWarning + "\nYine de yeni personel olarak kaydedilsin mi?", "Olası aynı kişi")) return;
            _dupAck = true;
        }

        // Bu kayıtta mevcut kullanıcı bağlanıyor mu? (admin + hesabı yok + saha değil + anahtar açık + kullanıcı seçili)
        bool wantLink = CanManageAccounts && !FHasAccount && !FFieldStaff && FGrantAccess && FLinkUser is not null;
        if (CanManageAccounts && !FHasAccount && !FFieldStaff && FGrantAccess && FLinkUser is null)
        { FormError = "Bağlanacak kullanıcıyı seçin (veya anahtarı kapatın)."; return; }

        // KOŞUL (değişmedi): hesap YOKSA ve bağlanMIYORSA ve "Saha personeli" işaretli DEĞİLSE uyar.
        // Kutucuk işaretliyse bu koşul hiç çalışmaz.
        if (!FHasAccount && !wantLink && !FFieldStaff)
        {
            if (!await ConfirmService.AskAsync(
                    "Bu personele uygulama kullanıcısı bağlanmadı. Saha personeli mi?\n\n" +
                    "• Evet ise \"Saha personeli\" olarak kaydedilir.\n" +
                    "• Hayır ise Vazgeç deyip \"Mevcut kullanıcıyı bağla\" ile bir kullanıcı bağlayın.",
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
            string personnelId;
            // DÜZENLEME KİLİDİ: düzenlemeye başlarken okunan sürüm (Selected.Record.Version) geri gönderilir;
            // kayıt arada başkası tarafından değiştiyse sessizce ezilmez.
            if (editing) { DesktopServices.Personnel.Update(_session, EditId!, dto, _editVersion); personnelId = EditId!; }
            else personnelId = DesktopServices.Personnel.Create(_session, dto);

            if (wantLink)
            {
                // Bağ (users.personnel_id) SUNUCU-OTORİTELİDİR ve users tablosu masaüstünden push EDİLMEZ →
                // seçilen kullanıcı yalnız sunucuda/başka makinede olabilir (kullanıcı bulgusu 2026-07-19:
                // Mustafa Alpaslan sunucuda var, yerelde yok). Bu yüzden: (1) personeli sunucuya gönder
                // (yeni oluşturulduysa orada olsun), (2) bağı SUNUCUDA yap → tüm makinelere ulaşır.
                await BusinessSyncPushService.PushAsync();
                var linkErr = await ServerUserClient.LinkUserAsync(personnelId, FLinkUser!.Id);
                if (linkErr is null)
                    try { DesktopServices.Users.LinkPersonnel(_session, FLinkUser!.Id, personnelId); } catch { /* yerelde kullanıcı yoksa: sunucudaki bağ geçerli */ }
                else
                {
                    // Çevrimdışı ya da sunucu reddetti → yerel dene (kullanıcı yereldeyse çalışır).
                    try { DesktopServices.Users.LinkPersonnel(_session, FLinkUser!.Id, personnelId); }
                    catch { FormError = "Personel kaydedildi ama kullanıcı bağlanamadı: " + linkErr; ShowAdd = false; EditId = null; Load(); return; }
                }
            }
            ShowAdd = false; EditId = null; Load();
            Status = editing ? "Personel güncellendi." : (wantLink ? "Personel eklendi + kullanıcı bağlandı." : "Personel eklendi.");
        }
        catch (DepoWise.Application.Security.ConcurrencyException ex)
        {
            // Kayıt biz düzenlerken değişti. Yazdıklarını KAYBETME: karar kullanıcının.
            FormError = ex.Message;
            if (await ConfirmService.AskAsync(
                    ex.Message + "\n\nKaydın güncel hâlini yüklemek ister misiniz? " +
                    "(\"Formda kal\" derseniz yazdıklarınız durur, kopyalayıp tekrar uygulayabilirsiniz.)",
                    "Kayıt değişti", okText: "Kaydı yenile", cancelText: "Formda kal"))
            {
                ShowAdd = false; EditId = null; _editVersion = null; Load();
            }
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    /// <summary>Admin: personelin hesap bağını çözer (hesap silinmez).</summary>
    [RelayCommand]
    private async Task RemoveAccount()
    {
        if (!CanManageAccounts || _fAccountUserId is null) return;
        if (!await ConfirmService.AskAsync($"'{FAccountInfo}' hesabının bu personele bağı kaldırılsın mı? (Hesap silinmez, bağ çözülür.)", "Bağı kaldır", "Evet, Kaldır", "Vazgeç", danger: true)) return;
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
        if (Selected is null) { Status = "Personel seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.FullName}' silinsin mi?", "Personel Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Personnel.SoftDelete(_session, Selected.Id); Load(); Status = "Personel silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
