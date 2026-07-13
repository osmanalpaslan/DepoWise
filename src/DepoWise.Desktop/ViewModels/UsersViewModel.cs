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

    [ObservableProperty] private string _newPasswordForSelected = "";

    public ObservableCollection<UserRow> Items { get; } = new();
    public ObservableCollection<RolePick> AssignableRoles { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();

    // Yeni kullanıcı formundaki şube + seçili kullanıcıya atanacak şube
    [ObservableProperty] private BranchRow? _formBranch;
    [ObservableProperty] private BranchRow? _assignBranch;

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

    partial void OnSelectedChanged(UserRow? value)
    {
        AssignBranch = value is null ? null : Branches.FirstOrDefault(b => b.Id == value.BranchId);
        EditRoles.Clear();
        if (value is null || !CanManageUsers) return;
        LoadAssignableRoles();
        try
        {
            var current = DesktopServices.Users.GetRoleKeys(_session, value.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var ar in AssignableRoles)
                EditRoles.Add(new RolePick(ar.Key, ar.Name) { IsSelected = current.Contains(ar.Key) });
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
            DesktopServices.Users.SetRoles(_session, Selected.Id, roles);
            Load();
            Status = "Roller güncellendi.";
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

    // Fikir B — "Personel seç": hesabı hangi personele bağlayacağız (hesabı olmayan personeller listelenir).
    public ObservableCollection<PersonnelRecord> LinkablePersonnel { get; } = new();
    [ObservableProperty] private PersonnelRecord? _formPersonnel;

    /// <summary>Hesabı olmayan personelleri yükler (bir personele tek kullanıcı kuralı).</summary>
    private void LoadLinkablePersonnel()
    {
        try
        {
            LinkablePersonnel.Clear();
            var taken = DesktopServices.Users.AccountsByPersonnel(_session.CompanyId);
            foreach (var p in DesktopServices.Personnel.List(_session, new PageRequest { Limit = 500 }).Items)
                if (!taken.ContainsKey(p.Id)) LinkablePersonnel.Add(p);
        }
        catch { }
    }

    public UsersViewModel(SessionContext session)
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
            foreach (var u in DesktopServices.Users.ListUsers(_session)) Items.Add(u);
            Branches.Clear();
            try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            Status = $"{Items.Count} kullanıcı";
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
            // Yetki yükseltme koruması: Süper Admin yalnız süper-admin tarafından, Firma Admini yalnız admin tarafından atanabilir.
            if (key == RoleKeys.SuperAdmin && !_session.IsSuperAdmin) continue;
            if (key == RoleKeys.CompanyAdmin && !isAdmin) continue;
            AssignableRoles.Add(new RolePick(key, name));
        }
    }

    [RelayCommand]
    private void NewUser()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        LoadAssignableRoles();
        NewUsername = ""; NewPassword = ""; NewFullName = ""; FormError = null; FormBranch = null;
        FormPersonnel = null; LoadLinkablePersonnel();
        foreach (var r in AssignableRoles) r.IsSelected = false;
        SelectedTemplate = null;
        if (CanUseTemplates && Templates.Count == 0)
            try { foreach (var t in DesktopServices.PermissionTemplates.ListForUserCreation(_session)) Templates.Add(t); } catch { }
        ShowAdd = true;
    }

    [RelayCommand]
    private void CancelAdd() => ShowAdd = false;

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewUsername)) { FormError = "Kullanıcı adı zorunlu."; return; }
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 4) { FormError = "Şifre en az 4 karakter olmalı."; return; }

        var roles = AssignableRoles.Where(r => r.IsSelected).Select(r => r.Key).ToList();

        // Şablon seçildiyse verisini al; şablonun rolü de kullanıcıya atanır
        DepoWise.Infrastructure.Security.PermissionTemplateData? tplData = null;
        if (CanUseTemplates && SelectedTemplate is not null)
        {
            tplData = DesktopServices.PermissionTemplates.GetData(_session, SelectedTemplate.Id);
            if (!string.IsNullOrWhiteSpace(tplData.RoleKey) && !roles.Contains(tplData.RoleKey))
                roles.Add(tplData.RoleKey!);
        }

        var rolesText = roles.Count == 0 ? "rol YOK (hiçbir ekran görmez)" : string.Join(", ", roles);
        if (!await ConfirmService.AskAsync(
                $"Kullanıcı oluşturulsun mu?\n\nKullanıcı: {NewUsername.Trim()}\nRoller: {rolesText}\n\nModül yetkileri 'Yetkiler' ekranından verilir.", "Kullanıcı Oluştur"))
            return;
        try
        {
            // Adım 6: operasyonel (personel) kullanıcıda şube/şantiye zorunlu (süper/kısıtlı-süper admin + admin muaf).
            DesktopServices.Users.ValidateBranchForNewUser(_session, _session.CompanyId, roles, FormBranch?.Id);
            var newUserId = DesktopServices.Users.CreateUser(_session, new NewUser(
                Username: NewUsername.Trim(),
                Password: NewPassword,
                FullName: string.IsNullOrWhiteSpace(NewFullName) ? null : NewFullName.Trim(),
                RoleKeys: roles,
                CompanyId: _session.CompanyId,
                BranchId: FormBranch?.Id,
                CanViewAllBranches: IsSuperAdmin && NewViewAllBranches,
                PersonnelId: FormPersonnel?.Id));   // Fikir B: hesabı personele bağla

            // Yetki şablonu seçildiyse yetkileri şablona göre yaz (yalnız Süper Admin)
            if (tplData is not null)
                DesktopServices.Permissions.SaveForUser(_session, newUserId, tplData.Modules, tplData.Buttons);

            ShowAdd = false;
            NewViewAllBranches = false;
            Load();
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
            DesktopServices.Users.SetViewAllBranches(_session, Selected.Id, target);
            Load();
            Status = target ? "Tüm Şubeler yetkisi verildi." : "Tüm Şubeler yetkisi kaldırıldı.";
        }
        catch (Exception ex) { Status = "İşlem başarısız: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ChangePassword()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManageUsers) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewPasswordForSelected) || NewPasswordForSelected.Length < 4)
        { Status = "Şifre en az 4 karakter olmalı."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.Username}' kullanıcısının şifresi değiştirilsin mi?", "Şifre Değiştir")) return;
        try
        {
            DesktopServices.Users.ChangePassword(_session, Selected.Id, NewPasswordForSelected);
            NewPasswordForSelected = "";
            Status = "Şifre değiştirildi.";
        }
        catch (Exception ex) { Status = "Değiştirilemedi: " + ex.Message; }
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
            DesktopServices.Users.SetActive(_session, Selected.Id, makeActive);
            Load();
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
            DesktopServices.Users.DeleteUser(_session, Selected.Id);
            Load();
            Status = "Kullanıcı silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    /// <summary>Seçili kullanıcıya şube atar/değiştirir (boş = şubesiz).</summary>
    [RelayCommand]
    private void AssignBranchToUser()
    {
        if (Selected is null) { Status = "Kullanıcı seçin."; return; }
        if (!CanManage) { Status = "Yetki yok."; return; }
        try
        {
            DesktopServices.Branches.AssignUser(_session, Selected.Id, AssignBranch?.Id);
            Load();
            Status = AssignBranch is null ? "Kullanıcının şubesi kaldırıldı." : $"Şube atandı: {AssignBranch.Name}";
        }
        catch (Exception ex) { Status = "Atanamadı: " + ex.Message; }
    }
}

public sealed partial class RolePick : ObservableObject
{
    public string Key { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
    public RolePick(string key, string name) { Key = key; Name = name; }
}
