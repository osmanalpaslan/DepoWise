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

    /// <summary>KLT-01c DÜZENLEME KİLİDİ: ekran açılırken sunucudan okunan yetki sürümü.
    /// Kaydederken geri gönderilir; arada başka yönetici kaydettiyse sunucu 409 döner ve üzerine yazılmaz.
    /// 0 = sürüm bilinmiyor (yerel yedekten yüklendi) → kontrol yapılmaz.</summary>
    private long _permVersion;

    public bool CanManage => AccessControl.Can(_session, "permissions", PermissionAction.Edit);

    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<ModulePermNode> Modules { get; } = new();
    public ObservableCollection<ButtonPermNode> Buttons { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUser))]
    [NotifyPropertyChangedFor(nameof(TreeEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSavePerms))]
    [NotifyPropertyChangedFor(nameof(CanResetPerms))]
    private UserRow? _selectedUser;
    public bool HasUser => SelectedUser != null;

    /// <summary>Hedef kullanıcı Admin/Süper Admin mi — öyleyse ağaç TAM işaretli + SALT-OKUNUR gösterilir
    /// (admin granular yetki tutmaz, hepsine bypass ile erişir). Kısıtlamak için önce rol Personel yapılır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSavePerms))]
    [NotifyPropertyChangedFor(nameof(CanResetPerms))]
    private bool _isTargetAdmin;
    /// <summary>Ağaç düzenlenebilir mi (admin hedefte salt-okunur).</summary>
    public bool TreeEnabled => HasUser && !IsTargetAdmin;
    /// <summary>Kaydet butonu görünür mü (admin hedefte kaydedilecek granular yetki yok).</summary>
    public bool CanSavePerms => HasUser && !IsTargetAdmin;

    [ObservableProperty] private string? _status;

    public PermissionsViewModel(SessionContext session)
    {
        _session = session;
        BuildTree(null);
        _ = LoadUsers();
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
    private async Task LoadUsers()
    {
        try
        {
            // Kullanıcılar SUNUCU-OTORİTELİ (2026-07-25): çevrimiçiyken SUNUCUDAN çek → başka makinede/web'de
            // oluşturulan firma kullanıcıları da görünür (yereldeki users tablosunda olmayabilirler). Çevrimdışı → yerel.
            var server = await OrgServerClient.ListUsersAsync();
            var rows = server ?? DesktopServices.Users.ListUsers(_session);
            Users.Clear();
            foreach (var u in rows) Users.Add(u);
            Status = server is null ? $"{Users.Count} kullanıcı (çevrimdışı — yerel)" : $"{Users.Count} kullanıcı";
        }
        catch (Exception ex) { Status = "Kullanıcılar yüklenemedi: " + ex.Message; }
    }

    partial void OnSelectedUserChanged(UserRow? value) => _ = LoadSelectedUserAsync(value);

    private async Task LoadSelectedUserAsync(UserRow? value)
    {
        if (value is null) { IsTargetAdmin = false; BuildTree(null); ResetTree(); return; }
        try
        {
            // Hedef roller + engelli modüller: çevrimiçiyken sunucudan (server kullanıcı yerelde olmayabilir), yoksa yerel.
            var targetRoles = await OrgServerClient.GetUserRolesAsync(value.Id)
                ?? SafeLocalRoles(value.Id);
            BuildTree(SafeBlocked(value.Id), targetRoles);

            // Admin/Süper Admin hedef: granular yetki TUTMAZ → TAM işaretli + SALT-OKUNUR (task 1).
            if (value.IsAdmin)
            {
                IsTargetAdmin = true;
                foreach (var m in Modules) m.Set(true, true, true, true);
                foreach (var b in Buttons) b.Granted = true;
                Status = $"{value.Username} — Admin/Süper Admin: tüm ekranlara erişir. Kısıtlamak için önce rolünü Personel yapın.";
                return;
            }

            IsTargetAdmin = false;
            ResetTree();
            // Yetkiler: çevrimiçiyken SUNUCUDAN (server kullanıcının yerel kaydı olmayabilir), yoksa yerel best-effort.
            var server = await OrgServerClient.GetPermissionsAsync(value.Id);
            var (mods, btns) = server is { } s ? (s.Modules, s.Buttons) : SafeLocalPerms(value.Id);
            // KLT-01c: düzenleme kilidi jetonu. Yerel yedekten yüklendiyse 0 kalır → kontrol yapılmaz
            // (yerel yedek zaten sunucuya yazmaz; kaydetme çevrimiçi olmayı gerektirir).
            _permVersion = server is { } sv ? sv.Version : 0;
            foreach (var m in Modules)
            {
                var p = mods.FirstOrDefault(x => x.ModuleKey == m.Key);
                m.Set(p?.CanView ?? false, p?.CanCreate ?? false, p?.CanEdit ?? false, p?.CanDelete ?? false);
            }
            foreach (var b in Buttons) b.Granted = btns.Contains(b.Key);
            Status = $"{value.Username} yetkileri yüklendi.";
        }
        catch (Exception ex) { Status = "Yetkiler yüklenemedi: " + ex.Message; }
    }

    // Yerel best-effort (server-only kullanıcı yerel DB'de yoksa → boş/null; sunucu zaten otorite).
    private System.Collections.Generic.List<string> SafeLocalRoles(string userId)
    { try { return DesktopServices.Users.GetRoleKeys(_session, userId).ToList(); } catch { return new(); } }
    private IReadOnlySet<string>? SafeBlocked(string userId)
    { try { return DesktopServices.Permissions.BlockedModulesForUser(_session, userId); } catch { return null; } }
    private (System.Collections.Generic.List<ModulePermission> Modules, System.Collections.Generic.List<string> Buttons) SafeLocalPerms(string userId)
    { try { var d = DesktopServices.Permissions.GetForUser(_session, userId); return (d.Modules.ToList(), d.Buttons.ToList()); } catch { return (new(), new()); } }

    private void ResetTree()
    {
        foreach (var m in Modules) m.Set(false, false, false, false);
        foreach (var b in Buttons) b.Granted = false;
    }

    // ── G1a: YETKİ ÖZETİ + SIFIRLAMA (2026-08-12, masaüstü ana kanal) ────────────────────────

    /// <summary>Özet satırları — hedefin ETKİN yetkileri (ham izin satırı DEĞİL).</summary>
    public ObservableCollection<SummaryRow> Summary { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string? _summaryText;
    public bool HasSummary => !string.IsNullOrEmpty(SummaryText);

    /// <summary>Kendi yetkisini sıfırlamak kullanıcıyı kendi ekranından kilitler → düğme gizli
    /// (sunucu da ayrıca reddeder; UI tek savunma değildir).</summary>
    public bool CanResetPerms => HasUser && !IsTargetAdmin && SelectedUser?.Id != _session.UserId;


    public sealed record SummaryRow(string Label, string Actions);

    [RelayCommand]
    private async Task ShowSummary()
    {
        if (SelectedUser is null) { Status = "Önce kullanıcı seçin."; return; }
        Summary.Clear(); SummaryText = null;
        var r = await OrgServerClient.GetPermissionSummaryAsync(SelectedUser.Id);
        if (r is null) { Status = "Yetki özeti alınamadı (çevrimiçi olmayı gerektirir)."; return; }
        SummaryText = r.Value.SourceText;
        foreach (var (label, actions) in r.Value.Modules) Summary.Add(new SummaryRow(label, actions));
        foreach (var b in r.Value.Buttons) Summary.Add(new SummaryRow("Özel izin: " + b, "Açık"));
        if (Summary.Count == 0) Status = "Bu kullanıcının erişebildiği hiçbir ekran yok.";
        else Status = $"{Summary.Count} satır listelendi.";
    }

    /// <summary>Yıkıcı işlem → açık onay + ne olacağının düz anlatımı (teknik terim yok).</summary>
    [RelayCommand]
    private async Task ResetPerms()
    {
        if (SelectedUser is null) { Status = "Önce kullanıcı seçin."; return; }
        if (!CanManage) { Status = "Yetki yok."; return; }
        if (!CanResetPerms) { Status = "Kendi yetkilerinizi sıfırlayamazsınız."; return; }

        if (!await ConfirmService.AskAsync(
                $"'{SelectedUser.Username}' kullanıcısının TÜM ekran ve buton yetkileri silinecek.\n\n" +
                "Sonrasında hiçbir ekrana erişemez; yetkileri yeniden verilene kadar yalnız giriş yapabilir.\n" +
                "Kullanıcı kaydı ve rolü SİLİNMEZ, yalnız yetkileri temizlenir.\n\nDevam edilsin mi?",
                "Yetkileri Sıfırla", "Evet, Sıfırla")) return;

        // Yetkiler SUNUCU-OTORİTELİ: yalnız yerele yazmak hedef kullanıcıya ulaşmaz.
        var res = await OrgServerClient.ResetPermissionsAsync(SelectedUser.Id, _permVersion);
        if (res.Offline) { Status = "Bu işlem çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
        if (res.Status == 409) { Status = "Bu kullanıcının yetkileri siz ekrandayken değişti. Kullanıcıyı yeniden seçip tekrar deneyin."; return; }
        if (!res.Ok) { Status = res.Error ?? "Sıfırlanamadı."; return; }

        Summary.Clear(); SummaryText = null;
        await LoadSelectedUserAsync(SelectedUser);
        Status = "Yetkiler sıfırlandı. Kullanıcı artık hiçbir ekrana erişemez.";
    }

    [RelayCommand]
    private async Task Save()
    {
        if (SelectedUser is null) { Status = "Önce kullanıcı seçin."; return; }
        if (!CanManage) { Status = "Yetki yok."; return; }
        if (IsTargetAdmin) { Status = "Bu kullanıcı Admin — granular yetki uygulanmaz. Kısıtlamak için önce rolünü Personel yapın."; return; }

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
            // Yetkiler + rol SUNUCU-OTORİTELİ: çevrimiçiyken SUNUCUYA yaz → değişiklik hedef kullanıcıya (ör. baba)
            // bir sonraki girişinde ulaşır. Yalnız yerele yazsaydık başka makinedeki kullanıcıya hiç gitmezdi.
            if (needUpgrade)
            {
                var r = await OrgServerClient.SetRolesAsync(SelectedUser.Id, new[] { RoleKeys.CompanyAdmin });
                if (r.Offline) { Status = "Bu işlem çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur)."; return; }
                if (!r.Ok) { Status = r.Error ?? "Rol değiştirilemedi."; return; }
                await LoadUsers();
                Status = "Kullanıcı Admin yapıldı — tüm ekranlara erişebilir. (Yeniden giriş yapınca tam etkin olur.)";
                return;
            }
            var mods = Modules.Select(m => new ModulePermission(m.Key, m.CanView, m.CanCreate, m.CanEdit, m.CanDelete)).ToList();
            var btns = Buttons.Where(b => b.Granted).Select(b => b.Key).ToList();
            var res = await OrgServerClient.SavePermissionsAsync(SelectedUser.Id, mods, btns, _permVersion);
            if (res.Offline) { Status = "Yetki kaydı çevrimiçi olmayı gerektirir (kullanıcılar sunucuda tutulur). İnternet bağlantısıyla tekrar deneyin."; return; }
            if (res.Status == 409)
            {
                // KLT-01c DÜZENLEME KİLİDİ: arada başka bir yönetici bu kullanıcının yetkilerini kaydetmiş.
                // Yazdıklarını KAYBETME — karar kullanıcının (Şube ekranındaki kanıtlanmış desenin aynısı).
                Status = res.Error ?? "Yetkiler değişti.";
                if (await ConfirmService.AskAsync(
                        "Bu kullanıcının yetkileri siz ekranı açtıktan sonra başka bir yönetici tarafından değiştirildi.\n"
                        + "Değişiklikleriniz KAYDEDİLMEDİ.\n\n"
                        + "Güncel yetkileri yüklemek ister misiniz? (\"Ekranda kal\" derseniz işaretledikleriniz durur.)",
                        "Yetkiler değişti", okText: "Güncel yetkileri yükle", cancelText: "Ekranda kal"))
                {
                    var reload = SelectedUser;
                    SelectedUser = null;
                    SelectedUser = reload;   // OnSelectedUserChanged → sunucudan taze yükleme + yeni sürüm
                }
                return;
            }
            if (!res.Ok) { Status = res.Error ?? "Kaydedilemedi."; return; }
            Status = "Yetkiler kaydedildi. (Kullanıcı yeniden giriş yapınca tam etkin olur.)";
            _permVersion++;   // sunucu sürümü artırdı; ekranı kapatmadan ikinci kayıt yapılabilsin
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
