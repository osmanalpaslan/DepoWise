using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
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
    private UserRow? _selected;
    public bool HasSelection => Selected != null;

    partial void OnSelectedChanged(UserRow? value)
        => AssignBranch = value is null ? null : Branches.FirstOrDefault(b => b.Id == value.BranchId);

    // Form
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newFullName = "";
    [ObservableProperty] private string? _formError;

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
        foreach (var r in AssignableRoles) r.IsSelected = false;
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
        var rolesText = roles.Count == 0 ? "rol YOK (hiçbir ekran görmez)" : string.Join(", ", AssignableRoles.Where(r => r.IsSelected).Select(r => r.Name));
        if (!await ConfirmService.AskAsync(
                $"Kullanıcı oluşturulsun mu?\n\nKullanıcı: {NewUsername.Trim()}\nRoller: {rolesText}\n\nModül yetkileri 'Yetkiler' ekranından verilir.", "Kullanıcı Oluştur"))
            return;
        try
        {
            DesktopServices.Users.CreateUser(_session, new NewUser(
                Username: NewUsername.Trim(),
                Password: NewPassword,
                FullName: string.IsNullOrWhiteSpace(NewFullName) ? null : NewFullName.Trim(),
                RoleKeys: roles,
                CompanyId: _session.CompanyId,
                BranchId: FormBranch?.Id));
            ShowAdd = false;
            Load();
            Status = "Kullanıcı oluşturuldu.";
        }
        catch (Exception ex) { FormError = "Oluşturulamadı: " + ex.Message; }
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
