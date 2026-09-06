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

/// <summary>
/// Kullanıcılar — liste + yeni kullanıcı (rol atama). Firma OTOMATİK (Firma Admini kendi firmasına kilitli;
/// servis RoleAssignmentGuard ile çözer). Atanabilir roller aktöre göre kısıtlı (Süper Admin/Firma Admini
/// koruması). Yeni kullanıcıya yetki VERİLMEZSE hiçbir ekran görmez (deny-by-default). Modül yetkileri ayrı
/// "Yetkiler" ekranından düzenlenir.
/// </summary>
public sealed partial class UsersViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "users", PermissionAction.Create);
    public bool CanManage => AccessControl.Can(_session, "users", PermissionAction.Edit);
    /// <summary>Sil + şifre değiştir yalnız Admin / Süper Admin.</summary>
    public bool CanManageUsers => AccessControl.IsAdmin(_session);
    /// <summary>"Tüm Şubeler" yetkisini YALNIZ Süper Admin belirler.</summary>
    public bool IsSuperAdmin => _session.IsSuperAdmin;

    /// <summary>Yeni kullanıcı formunda "Tüm Şubeler" yetkisi (yalnız Süper Admin).</summary>
    [ObservableProperty] private bool _newViewAllBranches;

    public ObservableCollection<UserRow> Items { get; } = new();
    public ObservableCollection<RolePick> AssignableRoles { get; } = new();
    /// <summary>Seçili kullanıcıya şube atama listesi — DAİMA oturumun kendi firması.</summary>
    public ObservableCollection<BranchRow> Branches { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BranchPopulator => SearchPopulator.For(() => Branches, b => b.Name);
    /// <summary>Yeni kullanıcı formundaki şube listesi — SEÇİLİ firmaya göre (süper admin başka firma seçebilir).
    /// Atama listesinden ayrıdır; yoksa firma değişince seçili kullanıcının şube kutusu da bozulurdu.</summary>
    public ObservableCollection<BranchRow> FormBranches { get; } = new();

    /// <summary>Kullanıcı oluştururken firma seçimi — YALNIZ süper adminde görünür (bkz. IsSuperAdmin).
    /// Süper-admin-altı roller kendi firmasına kilitlidir; servis (RoleAssignmentGuard) da bunu fail-closed zorlar.</summary>
    public ObservableCollection<CompanyPick> Companies { get; } = new();
    [ObservableProperty] private CompanyPick? _formCompany;

    /// <summary>Yeni kullanıcının bağlanacağı firma: süper admin seçtiyse o, aksi halde oturumun firması.</summary>
    private string TargetCompanyId => IsSuperAdmin && FormCompany is not null ? FormCompany.Id : _session.CompanyId;
    /// <summary>Seçili firma kendi firmamız mı? Personel bağlama yalnız kendi firmasında mümkün
    /// (PersonnelService tenant'a kilitli — başka firmanın personeli listelenmez).</summary>
    public bool FormCompanyIsOwn => !IsSuperAdmin || FormCompany is null
        || string.Equals(FormCompany.Id, _session.CompanyId, StringComparison.Ordinal);
    /// <summary>Başka firma seçiliyken personel bağlama kutusu gizlenir, yerine açıklama gösterilir.</summary>
    public bool ShowOtherCompanyPersonnelNote => !FormCompanyIsOwn;

    // Yeni kullanıcı formundaki şube + seçili kullanıcıya atanacak şube
    [ObservableProperty] private BranchRow? _formBranch;
    [ObservableProperty] private BranchRow? _assignBranch;

    /// <summary>Firma değişti: şube + personel seçimleri sıfırlanır, listeler yeni firmaya göre yeniden yüklenir
    /// (aksi halde başka firmanın şubesi seçili kalır ve servis kaydı reddeder).</summary>
    partial void OnFormCompanyChanged(CompanyPick? value)
    {
        FormBranch = null; FormPersonnel = null;
        LoadFormBranches();
        LoadLinkablePersonnel();
        OnPropertyChanged(nameof(FormCompanyIsOwn));
        OnPropertyChanged(nameof(ShowOtherCompanyPersonnelNote));
    }

    /// <summary>Form şubelerini SEÇİLİ firmaya göre yükler. BranchService tenant'ı fail-closed çözer:
    /// süper admin olmayan aktör companyId gönderse de yalnız kendi firmasının şubelerini alır.</summary>
    private void LoadFormBranches()
    {
        FormBranches.Clear();
        try { foreach (var b in DesktopServices.Branches.List(_session, TargetCompanyId)) FormBranches.Add(b); } catch { }
    }

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
    [NotifyPropertyChangedFor(nameof(CanManageSelected))]
    [NotifyPropertyChangedFor(nameof(SelectedIsMasked))]
    private UserRow? _selected;
    public bool HasSelection => Selected != null;

    /// <summary>#8 — Süper admin herkesi; aksi halde admin OLMAYAN seçili kullanıcıyı (veya kendini) yönetebilir.</summary>
    public bool CanManageSelected => CanManageUsers && (Selected is null || _session.IsSuperAdmin
        || !Selected.IsAdmin || string.Equals(Selected.Id, _session.UserId, StringComparison.Ordinal));
    /// <summary>Seçili kullanıcı admin ve düzenlenemiyor → maskeli uyarı göster.</summary>
    public bool SelectedIsMasked => HasSelection && !CanManageSelected;

    /// <summary>Seçili kullanıcının rolleri (mevcut rolü değiştirme — yalnız Admin / Süper Admin).</summary>
    public ObservableCollection<RolePick> EditRoles { get; } = new();

    partial void OnSelectedChanged(UserRow? value) => _ = OnSelectedChangedAsync(value);

    private async Task OnSelectedChangedAsync(UserRow? value)
    {
        AssignBranch = value is null ? null : Branches.FirstOrDefault(b => b.Id == value.BranchId);
        EditRoles.Clear();
        if (value is null || !CanManageUsers) return;
        LoadAssignableRoles();
        try
        {
            // Roller: çevrimiçiyken sunucudan (server kullanıcı yerelde olmayabilir), yoksa yerel best-effort.
            List<string> cur;
            try { cur = await OrgServerClient.GetUserRolesAsync(value.Id) ?? DesktopServices.Users.GetRoleKeys(_session, value.Id).ToList(); }
            catch { cur = new(); }
            var set = cur.ToHashSet(StringComparer.Ordinal);
            EditRoles.Clear();
            foreach (var ar in AssignableRoles)
                EditRoles.Add(new RolePick(ar.Key, ar.Name) { IsSelected = set.Contains(ar.Key) });
        }
        catch { }
    }

    [RelayCommand]
    private async Task SaveRoles()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManageUsers) { Status = "Yetki yok."; return; }
        var roles = EditRoles.Where(r => r.IsSelected).Select(r => r.Key).ToList();
        var rolesText = roles.Count == 0 ? "rol YOK" : string.Join(", ", EditRoles.Where(r => r.IsSelected).Select(r => r.Name));
        if (!await ConfirmService.AskAsync($"'{Selected.Username}' rolleri güncellensin mi?\n\nRoller: {rolesText}", "Rolleri Değiştir")) return;
        try
        {
            // Kullanıcı SUNUCU-OTORİTELİ: rol değişikliği sunucuya yazılır → hedef kullanıcıya ulaşır. Çevrimdışı → uyar.
            var r = await OrgServerClient.SetRolesAsync(Selected.Id, roles);
            if (r.Offline) { Status = "Bu işlem çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (!r.Ok) { Status = r.Error ?? "Roller güncellenemedi."; return; }
            await Load();
            Status = "Roller güncellendi. (Kullanıcı yeniden giriş yapınca tam etkin olur.)";
        }
        catch (Exception ex) { Status = "Güncellenemedi: " + ex.Message; }
    }

    // Yetki şablonları (yalnız Süper Admin) — yeni kullanıcıda seçilir, yetkiler ona göre yazılır
    public ObservableCollection<PermissionTemplateRow> Templates { get; } = new();
    [ObservableProperty] private PermissionTemplateRow? _selectedTemplate;
    public bool CanUseTemplates => CanWrite; // kullanıcı-oluşturma yetkili aktör firmasına özel + tüm-firma şablonlarını görür

    // Form
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newFullName = "";
    [ObservableProperty] private string? _formError;
    // ⭐ 2026-09-06 (kullanıcı isteği): kullanıcı iletişim alanları (Migration095).
    [ObservableProperty] private string _newEmail = "";
    [ObservableProperty] private string _newPhone = "";
    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private string _newNotes = "";
    /// <summary>Form DÜZENLEME kipinde mi? Kip, formu yeniden kullanır; başlık ve düğme metni buna göre değişir.</summary>
    [ObservableProperty] private bool _isEditing;
    /// <summary>Düzenlenen kullanıcının kimliği (yalnız düzenleme kipinde dolu).</summary>
    private string? _editingUserId;
    public string FormTitle => IsEditing ? "KULLANICI BİLGİLERİNİ DÜZENLE" : "YENİ KULLANICI";
    public string FormActionText => IsEditing ? "Kaydet" : "Oluştur";
    /// <summary>Kullanıcı adı, şifre, firma, rol ve şube YALNIZ oluştururken belirlenir (yetki yükseltme kapısı açılmaz).</summary>
    public bool ShowCreateOnlyFields => !IsEditing;
    /// <summary>"Tüm Şubeler" yetkisi yalnız süper admin ve yalnız OLUŞTURMA sırasında belirlenir.</summary>
    public bool ShowAllBranchesOption => IsSuperAdmin && !IsEditing;
    /// <summary>Yetki şablonu yalnız OLUŞTURMA sırasında uygulanır (düzenlemede yetkilere dokunulmaz).</summary>
    public bool ShowTemplateOption => CanUseTemplates && !IsEditing;
    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(FormActionText));
        OnPropertyChanged(nameof(ShowCreateOnlyFields));
        OnPropertyChanged(nameof(ShowAllBranchesOption));
        OnPropertyChanged(nameof(ShowTemplateOption));
    }

    // Fikir B — "Personel seç": hesabı hangi personele bağlayacağız (hesabı olmayan personeller listelenir).
    public ObservableCollection<PersonnelRecord> LinkablePersonnel { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> LinkablePersonnelPopulator => SearchPopulator.For(() => LinkablePersonnel, p => p.FullName);
    [ObservableProperty] private PersonnelRecord? _formPersonnel;

    /// <summary>Hesabı olmayan personelleri yükler (bir personele tek kullanıcı kuralı).
    /// Başka firma seçiliyse liste BOŞ kalır — personel listesi oturumun firmasına kilitlidir.</summary>
    private void LoadLinkablePersonnel()
    {
        LinkablePersonnel.Clear();
        if (!FormCompanyIsOwn) { FormPersonnel = null; return; }
        try
        {
            var taken = DesktopServices.Users.AccountsByPersonnel(_session.CompanyId);
            foreach (var p in DesktopServices.Personnel.List(_session, new PageRequest { Limit = 500 }).Items)
                if (!taken.ContainsKey(p.Id)) LinkablePersonnel.Add(p);
        }
        catch { }
    }

    public UsersViewModel(SessionContext session)
    {
        _session = session;
        _ = Load();
    }

    [RelayCommand]
    private async Task Load()
    {
        try
        {
            LoadError = null;
            // Kullanıcılar SUNUCU-OTORİTELİ (2026-07-25): çevrimiçiyken SUNUCUDAN çek → başka makinede/web'de
            // oluşturulan firma kullanıcıları (ör. baba) da görünür. Çevrimdışı → yerel (salt-okuma).
            var server = await OrgServerClient.ListUsersAsync();
            var rows = server ?? DesktopServices.Users.ListUsers(_session);
            Items.Clear();
            foreach (var u in rows) Items.Add(u);
            Branches.Clear();
            try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            Status = server is null ? $"{Items.Count} kullanıcı (çevrimdışı — yerel)" : $"{Items.Count} kullanıcı";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    private void LoadAssignableRoles()
    {
        if (AssignableRoles.Count > 0) return;
        bool isAdmin = AccessControl.IsAdmin(_session);
        foreach (var (key, name, _) in RoleKeys.Seed)
        {
            // Yetki yükseltme koruması: Süper Admin + Kısıtlı Süper Admin YALNIZ süper adminde görünür/atanır;
            // Firma Admini yalnız admin tarafından atanabilir. (Web /api/roles ucu da aynı filtreyi uygular.)
            if (key == RoleKeys.SuperAdmin && !_session.IsSuperAdmin) continue;
            if (key == RoleKeys.RestrictedSuperAdmin && !_session.IsSuperAdmin) continue;
            if (key == RoleKeys.CompanyAdmin && !isAdmin) continue;
            AssignableRoles.Add(new RolePick(key, name));
        }
    }

    [RelayCommand]
    private void NewUser()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        LoadAssignableRoles();
        IsEditing = false; _editingUserId = null;
        NewUsername = ""; NewPassword = ""; NewFullName = ""; FormError = null; FormBranch = null;
        NewEmail = ""; NewPhone = ""; NewTitle = ""; NewNotes = "";
        // Firma seçici (yalnız süper admin) — varsayılan KENDİ firması, böylece şube listesiyle uyumlu başlar.
        if (IsSuperAdmin)
        {
            if (Companies.Count == 0)
                try { foreach (var (id, name) in DesktopServices.Companies.Selectable(_session)) Companies.Add(new CompanyPick(id, name)); } catch { }
            FormCompany = Companies.FirstOrDefault(c => c.Id == _session.CompanyId) ?? Companies.FirstOrDefault();
        }
        LoadFormBranches();
        FormPersonnel = null; LoadLinkablePersonnel();
        foreach (var r in AssignableRoles) r.IsSelected = false;
        SelectedTemplate = null;
        // G6-01: şablonlar SUNUCUDAN gelir (yerel kopya artık otorite değil). Çevrimdışıysa liste boş kalır
        // ve şablonsuz kullanıcı oluşturma çalışmaya devam eder.
        if (CanUseTemplates && Templates.Count == 0) _ = LoadTemplatesAsync();
        ShowAdd = true;
    }

    private async Task LoadTemplatesAsync()
    {
        try
        {
            var rows = await OrgServerClient.ListTemplatesForUserAsync();
            if (rows is null) return;   // çevrimdışı → şablon seçimi yok; uyarı kaydetme anında verilir
            Templates.Clear();
            foreach (var t in rows) Templates.Add(t);
        }
        catch { }
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; IsEditing = false; _editingUserId = null; }

    /// <summary>
    /// ⭐ SEÇİLİ KULLANICIYI DÜZENLE (kullanıcı isteği 2026-09-06).
    ///
    /// Bu ekranda bugüne kadar kullanıcı OLUŞTURULABİLİYOR ama düzenlenemiyordu; yeni eklenen
    /// iletişim alanlarında bir yazım hatası kalıcı olurdu. Aynı form düzenleme kipinde yeniden
    /// kullanılır: kullanıcı adı · şifre · rol · firma · şube alanları GİZLENİR (onların kendi
    /// işlemleri ve kendi yetki kapıları var), yalnız ad-soyad ve iletişim alanları açılır.
    /// </summary>
    [RelayCommand]
    private void EditSelected()
    {
        if (Selected is null) { Status = "Önce listeden bir kullanıcı seçin."; return; }
        if (!CanManageSelected) { Status = "Bu kullanıcıyı düzenleme yetkiniz yok."; return; }
        _editingUserId = Selected.Id;
        IsEditing = true;
        FormError = null;
        NewFullName = Selected.FullName ?? "";
        NewEmail = Selected.Email ?? "";
        NewPhone = Selected.Phone ?? "";
        NewTitle = Selected.Title ?? "";
        NewNotes = Selected.Notes ?? "";
        ShowAdd = true;
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }

        // ⭐ DÜZENLEME KİPİ (2026-09-06): yalnız ad-soyad + iletişim alanları güncellenir. Kullanıcı adı,
        // şifre, rol, firma ve şube burada DEĞİŞMEZ — onların kendi işlemleri ve kendi yetki kapıları var.
        if (IsEditing)
        {
            if (_editingUserId is null) { FormError = "Düzenlenecek kullanıcı bulunamadı."; return; }
            var guncelle = await OrgServerClient.UpdateUserProfileAsync(_editingUserId,
                string.IsNullOrWhiteSpace(NewFullName) ? null : NewFullName.Trim(),
                NewEmail, NewPhone, NewTitle, NewNotes);
            if (guncelle.Offline) { FormError = "Kullanıcı düzenleme çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (!guncelle.Ok) { FormError = guncelle.Error ?? "Sunucu işlemi başarısız."; return; }
            ShowAdd = false; IsEditing = false; _editingUserId = null;
            await Load();
            Status = "Kullanıcı bilgileri güncellendi.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewUsername)) { FormError = "Kullanıcı adı zorunlu."; return; }
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 4) { FormError = "Şifre en az 4 karakter olmalı."; return; }
        if (IsSuperAdmin && FormCompany is null) { FormError = "Firma seçin."; return; }

        var roles = AssignableRoles.Where(r => r.IsSelected).Select(r => r.Key).ToList();

        // Şablon seçildiyse verisini al; şablonun rolü de kullanıcıya atanır.
        // G6-01: şablon SUNUCUDAN okunur (yerel kopya otorite değil). Okunamazsa kullanıcı HİÇ oluşturulmaz —
        // aksi halde yetkisiz bir hesap açılır ve şablon uygulandı sanılırdı.
        DepoWise.Infrastructure.Security.PermissionTemplateData? tplData = null;
        if (CanUseTemplates && SelectedTemplate is not null)
        {
            tplData = await OrgServerClient.GetTemplateDataAsync(SelectedTemplate.Id);
            if (tplData is null)
            {
                FormError = "Yetki şablonu sunucudan okunamadı (çevrimdışı olabilirsiniz). Kullanıcı OLUŞTURULMADI.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(tplData.RoleKey) && !roles.Contains(tplData.RoleKey))
                roles.Add(tplData.RoleKey!);
        }

        var rolesText = roles.Count == 0 ? "rol YOK (hiçbir ekran görmez)" : string.Join(", ", roles);
        var companyText = IsSuperAdmin && FormCompany is not null ? $"\nFirma: {FormCompany.Name}" : "";
        if (!await ConfirmService.AskAsync(
                $"Kullanıcı oluşturulsun mu?\n\nKullanıcı: {NewUsername.Trim()}{companyText}\nRoller: {rolesText}\n\nModül yetkileri 'Yetkiler' ekranından verilir.", "Kullanıcı Oluştur"))
            return;
        try
        {
            // Adım 6: operasyonel (personel) kullanıcıda şube/şantiye zorunlu (hızlı ön-kontrol; sunucu da doğrular).
            DesktopServices.Users.ValidateBranchForNewUser(_session, TargetCompanyId, roles, FormBranch?.Id);

            // KULLANICILAR SUNUCU-OTORİTELİ (2026-07-25): çevrimiçiyken SUNUCUDA oluştur. Yalnız yerele yazarsak
            // web/diğer makineler görmez ve tek-otorite bozulur (veri kaybı bulgusu). Çevrimdışı → uyar.
            var fullName = string.IsNullOrWhiteSpace(NewFullName) ? null : NewFullName.Trim();
            var res = await OrgServerClient.CreateUserAsync(NewUsername.Trim(), NewPassword, fullName, roles,
                TargetCompanyId, FormBranch?.Id, IsSuperAdmin && NewViewAllBranches, FormPersonnel?.Id,
                NewEmail, NewPhone, NewTitle, NewNotes);   // ⭐ 2026-09-06 iletişim alanları
            if (res.Offline) { FormError = "Kullanıcı oluşturma çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur). İnternet bağlantısıyla tekrar deneyin."; return; }
            if (!res.Ok || res.Id is null) { FormError = res.Error ?? "Sunucu işlemi başarısız."; return; }
            var newUserId = res.Id;

            // Bu makinede anında görünsün + kalıcı olsun (sunucu id'siyle → çift kayıt olmaz).
            DesktopServices.Users.ImportServerUser(newUserId, TargetCompanyId, NewUsername.Trim(), NewPassword,
                fullName, FormBranch?.Id, IsSuperAdmin && NewViewAllBranches, mustChangePassword: true, roles);

            // G6-01: şablon yetkileri SUNUCUYA yazılır. Önceden yerele yazılıyordu; kullanıcı sunucuda
            // oluştuğu için yetkiler hedefe HİÇ ULAŞMIYOR, web'de ve diğer makinelerde kullanıcı yetkisiz
            // kalıyordu — üstelik ekran "yetkiler şablondan uygulandı" diyordu. Yazma, delegasyon tavanını
            // uygulayan PermissionService'ten (aynı uç: /api/permissions/{id}) geçer.
            // G6-07: başarısızsa "kullanıcı oluşturuldu" DEMEYİZ ama "oluşturulamadı" da demeyiz —
            // kullanıcı sunucuda GERÇEKTEN oluştu. Ne olduğu ve ne yapılacağı açıkça yazılır.
            if (tplData is not null)
            {
                var permRes = await OrgServerClient.SavePermissionsAsync(newUserId, tplData.Modules, tplData.Buttons);
                if (!permRes.Ok)
                {
                    ShowAdd = false;
                    NewViewAllBranches = false;
                    await Load();
                    Status = "Kullanıcı oluşturuldu ANCAK yetki şablonu uygulanamadı: "
                           + (permRes.Offline ? "sunucuya ulaşılamadı." : permRes.Error ?? "bilinmeyen hata.")
                           + " Kullanıcı şu an hiçbir ekranı göremez; yetkileri 'Yetkiler' ekranından verin.";
                    return;
                }
            }

            ShowAdd = false;
            NewViewAllBranches = false;
            await Load();
            Status = SelectedTemplate is not null
                ? $"Kullanıcı oluşturuldu (yetkiler '{SelectedTemplate.Name}' şablonundan)."
                : "Kullanıcı oluşturuldu.";
        }
        catch (Exception ex) { FormError = "Oluşturulamadı: " + ex.Message; }
    }

    /// <summary>Seçili kullanıcının "Tüm Şubeler" yetkisini aç/kapat — YALNIZ Süper Admin.</summary>
    [RelayCommand]
    private async Task ToggleViewAllBranches()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!IsSuperAdmin) { Status = "Bu yetkiyi yalnız Süper Admin belirleyebilir."; return; }
        bool target = !Selected.CanViewAllBranches;
        if (!await ConfirmService.AskAsync(
                target ? $"'{Selected.Username}' kullanıcısına Tüm Şubeler yetkisi verilsin mi?"
                       : $"'{Selected.Username}' kullanıcısından Tüm Şubeler yetkisi kaldırılsın mı?", "Tüm Şubeler Yetkisi"))
            return;
        try
        {
            var r = await OrgServerClient.SetAllBranchesAsync(Selected.Id, target);
            if (r.Offline) { Status = "Bu işlem çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (!r.Ok) { Status = r.Error ?? "İşlem başarısız."; return; }
            await Load();
            Status = target ? "Tüm Şubeler yetkisi verildi." : "Tüm Şubeler yetkisi kaldırıldı.";
        }
        catch (Exception ex) { Status = "İşlem başarısız: " + ex.Message; }
    }

    /// <summary>Şifre SIFIRLA: admin belirli şifre YAZMAZ; şifre kullanıcı adına sıfırlanır + kullanıcı ilk
    /// girişte kendi şifresini belirler (must_change). Şifre kullanıcı tanımından değiştirilmez.</summary>
    [RelayCommand]
    private async Task ResetPassword()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManageUsers) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{Selected.Username}' kullanıcısının şifresi SIFIRLANSIN mı?\n\nGeçici şifre kullanıcı adı olacak; kullanıcı ilk girişte kendi şifresini belirleyecek.",
                "Şifre Sıfırla", "Evet, Sıfırla", "Vazgeç")) return;
        try
        {
            var r = await OrgServerClient.ResetPasswordAsync(Selected.Id);
            if (r.Offline) { Status = "Şifre sıfırlama çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (r.Error is not null) { Status = r.Error; return; }
            Status = $"Şifre sıfırlandı. Geçici şifre: '{r.TempPassword ?? Selected.Username}'. Kullanıcı ilk girişte kendi şifresini belirleyecek.";
        }
        catch (Exception ex) { Status = "Sıfırlanamadı: " + ex.Message; }
    }

    /// <summary>Seçili kullanıcıyı aktif/pasif yapar. Süper admin kullanıcıyı yalnız süper admin değiştirebilir (servis guard).</summary>
    [RelayCommand]
    private async Task ToggleActive()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManageUsers) { Status = "Yetki yok."; return; }
        var makeActive = !Selected.IsActive;
        var verb = makeActive ? "aktif" : "pasif";
        if (!await ConfirmService.AskAsync($"'{Selected.Username}' kullanıcısı {verb} yapılsın mı?", "Kullanıcı Durumu")) return;
        try
        {
            var r = await OrgServerClient.SetActiveAsync(Selected.Id, makeActive);
            if (r.Offline) { Status = "Bu işlem çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (!r.Ok) { Status = r.Error ?? "Değiştirilemedi."; return; }
            await Load();
            Status = $"Kullanıcı {verb} yapıldı.";
        }
        catch (Exception ex) { Status = "Değiştirilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteUser()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManageUsers) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.Username}' kullanıcısı silinsin mi?", "Kullanıcı Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            var r = await OrgServerClient.DeleteUserAsync(Selected.Id);
            if (r.Offline) { Status = "Silme çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (!r.Ok) { Status = r.Error ?? "Silinemedi."; return; }
            await Load();
            Status = "Kullanıcı silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    /// <summary>Seçili kullanıcıya şube atar/değiştirir (boş = şubesiz).</summary>
    [RelayCommand]
    private async Task AssignBranchToUser()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManage) { Status = "Yetki yok."; return; }
        try
        {
            // Kullanıcı-şube ataması sunucu-otoriteli → sunucuya yaz. Çevrimdışı → uyar.
            var r = await OrgServerClient.AssignBranchAsync(Selected.Id, AssignBranch?.Id);
            if (r.Offline) { Status = "Şube ataması çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
            if (!r.Ok) { Status = r.Error ?? "Atanamadı."; return; }
            await Load();
            Status = AssignBranch is null ? "Kullanıcının şubesi kaldırıldı." : $"Şube atandı: {AssignBranch.Name}";
        }
        catch (Exception ex) { Status = "Atanamadı: " + ex.Message; }
    }
}

/// <summary>Firma seçici satırı (yalnız süper adminin gördüğü "Firma" kutusu).</summary>
public sealed record CompanyPick(string Id, string Name);

public sealed partial class RolePick : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
    public RolePick(string key, string name) { Key = key; Name = name; }
}
