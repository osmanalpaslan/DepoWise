using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Yetkiler — önce KULLANICI seçilir, sonra yetki ağacı oluşur. Modül kataloğu AppModules.All'dan,
/// butonlar SpecialButtons.All'dan OTOMATİK gelir (yeni ekran/buton eklenince kendiliğinden listelenir).
/// Her modül: Görüntüle/Ekle/Düzenle/Sil; ayrıca özel "+"/buton izinleri. Kaydet → user_permissions +
/// user_button_permissions (tam değiştirir). Verilmeyen yetki = gizli (deny-by-default).
/// </summary>
public sealed partial class PermissionsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanManage => AccessControl.Can(_session, "permissions", PermissionAction.Edit);

    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<ModulePermNode> Modules { get; } = new();
    public ObservableCollection<ButtonPermNode> Buttons { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUser))]
    private UserRow? _selectedUser;
    public bool HasUser => SelectedUser != null;

    [ObservableProperty] private string? _status;

    public PermissionsViewModel(SessionContext session)
    {
        _session = session;
        BuildTree(null);
        LoadUsers();
    }

    /// <summary>Ağacı kurar. <paramref name="blocked"/> = Rol Yetki Kontrol ile hedefin ROLÜNE kapatılmış
    /// modüller (HİÇ görünmez). <paramref name="targetRoles"/> verilirse süper-admin-only ekranlar yalnız
    /// (Kısıtlı) Süper Admin hedefe gösterilir; verilemeyecek kalemler kilitle DEĞİL, tamamen gizli.</summary>
    private void BuildTree(IReadOnlySet<string>? blocked, IReadOnlyList<string>? targetRoles = null)
    {
        Modules.Clear();
        Buttons.Clear();
        bool hasTarget = targetRoles is not null;
        bool targetCanReceiveSuperOnly = targetRoles is not null &&
            (targetRoles.Contains(RoleKeys.RestrictedSuperAdmin) || targetRoles.Contains(RoleKeys.SuperAdmin));
        foreach (var (key, label) in AppModules.All)
        {
            if (AppModules.IsPublic(key)) continue;                       // Dashboard/About/Tema herkese açık
            if (!AccessControl.CanGrantModule(_session, key)) continue;   // delegasyon tavanı + süper-admin-only görünürlük
            if (blocked is not null && blocked.Contains(key)) continue;   // Rol Yetki Kontrol: bu role kapalı
            if (hasTarget && AppModules.IsSuperAdminOnly(key) && !targetCanReceiveSuperOnly) continue; // hedefe verilemez → gizli
            Modules.Add(new ModulePermNode(key, label));
        }
        foreach (var (key, label) in SpecialButtons.All)
        {
            if (!AccessControl.CanGrantButton(_session, key)) continue;   // aktörün veremeyeceği buton ağaçta yok
            Buttons.Add(new ButtonPermNode(key, label));
        }
    }

    [RelayCommand]
    private void LoadUsers()
    {
        try
        {
            Users.Clear();
            foreach (var u in DesktopServices.Users.ListUsers(_session)) Users.Add(u);
            Status = $"{Users.Count} kullanıcı";
        }
        catch (Exception ex) { Status = "Kullanıcılar yüklenemedi: " + ex.Message; }
    }

    partial void OnSelectedUserChanged(UserRow? value)
    {
        if (value is null) { BuildTree(null); ResetTree(); return; }
        try
        {
            // Ağacı hedefin ROLÜNE göre yeniden kur: kapalı + verilemeyecek (süper-admin-only) ekranlar listelenmez.
            var blocked = DesktopServices.Permissions.BlockedModulesForUser(_session, value.Id);
            var targetRoles = DesktopServices.Users.GetRoleKeys(_session, value.Id);
            BuildTree(blocked, targetRoles);
            ResetTree();

            var data = DesktopServices.Permissions.GetForUser(_session, value.Id);
            foreach (var m in Modules)
            {
                var p = data.Modules.FirstOrDefault(x => x.ModuleKey == m.Key);
                m.Set(p?.CanView ?? false, p?.CanCreate ?? false, p?.CanEdit ?? false, p?.CanDelete ?? false);
            }
            foreach (var b in Buttons) b.Granted = data.Buttons.Contains(b.Key);
            Status = $"{value.Username} yetkileri yüklendi.";
        }
        catch (Exception ex) { Status = "Yetkiler yüklenemedi: " + ex.Message; }
    }

    private void ResetTree()
    {
        foreach (var m in Modules) m.Set(false, false, false, false);
        foreach (var b in Buttons) b.Granted = false;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (SelectedUser is null) { Status = "Önce kullanıcı seçin."; return; }
        if (!CanManage) { Status = "Yetki yok."; return; }

        // #3: Kısıtlı modül seçili + hedef Admin değil + aktör süper admin değil → önce Admin'e yükselt (uyarı).
        var causeNodes = Modules.Where(m => (m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
                                            && AppModules.IsAdminRestricted(m.Key)).ToList();
        bool needUpgrade = causeNodes.Count > 0 && !SelectedUser.IsAdmin && !_session.IsSuperAdmin;
        if (needUpgrade)
        {
            var causeList = "• " + string.Join("\n• ", causeNodes.Select(m => m.Label));
            if (!await ConfirmService.AskAsync(
                    $"Seçtiğiniz şu ekranlar yalnız Admin'e verilebilir:\n{causeList}\n\n" +
                    $"'{SelectedUser.Username}' kullanıcısının rolü ADMIN olarak değiştirilecek ve TÜM ekranlara erişebilecektir. Devam edilsin mi?",
                    "Evet, Admin Yap")) return;
        }
        else if (!await ConfirmService.AskAsync($"'{SelectedUser.Username}' kullanıcısının yetkileri kaydedilsin mi?", "Yetkileri Kaydet")) return;

        try
        {
            if (needUpgrade)
            {
                // Admin'e yükselt → Admin zaten tüm ekranlara erişir; granular yetki kaydına gerek yok.
                DesktopServices.Users.SetRoles(_session, SelectedUser.Id, new[] { RoleKeys.CompanyAdmin });
                LoadUsers();
                Status = "Kullanıcı Admin yapıldı — tüm ekranlara erişebilir. (Yeniden giriş yapınca tam etkin olur.)";
                return;
            }
            var mods = Modules.Select(m => new ModulePermission(m.Key, m.CanView, m.CanCreate, m.CanEdit, m.CanDelete)).ToList();
            var btns = Buttons.Where(b => b.Granted).Select(b => b.Key).ToList();
            DesktopServices.Permissions.SaveForUser(_session, SelectedUser.Id, mods, btns);
            Status = "Yetkiler kaydedildi. (Kullanıcı yeniden giriş yapınca tam etkin olur.)";
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }
}

public sealed partial class ModulePermNode : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _canView;
    [ObservableProperty] private bool _canCreate;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private bool _canDelete;

    public ModulePermNode(string key, string label) { Key = key; Label = label; }

    public void Set(bool v, bool c, bool e, bool d) { CanView = v; CanCreate = c; CanEdit = e; CanDelete = d; }
}

public sealed partial class ButtonPermNode : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _granted;
    public ButtonPermNode(string key, string label) { Key = key; Label = label; }
}
