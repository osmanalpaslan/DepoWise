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

    /// <summary>⭐ 2026-09-03 — ağacın KATEGORİZE görünümü (menü grupları gibi). Düğümler
    /// <see cref="Modules"/> ile AYNI örneklerdir; kaydetme yolu değişmedi.</summary>
    public ObservableCollection<PermGroupNode> Groups { get; } = new();

    /// <summary>"Tümünü Seç" (kullanıcı isteği 2026-09-03): gruptaki TÜM kutuları işaretler; kullanıcı
    /// uygun olmayanları elle kaldırıp kaydeder. Yalnız görünen (verilebilir) kalemleri işaretler.</summary>
    [RelayCommand]
    private void SelectAllInGroup(PermGroupNode? grup)
    {
        if (grup is null) return;
        foreach (var m in grup.Items) m.Set(true, true, true, true);
    }

    /// <summary>Grubun tüm kutularını temizler (Tümünü Seç'in geri alma karşılığı).</summary>
    [RelayCommand]
    private void ClearAllInGroup(PermGroupNode? grup)
    {
        if (grup is null) return;
        foreach (var m in grup.Items) m.Set(false, false, false, false);
    }

    /// <summary>
    /// ⭐ YTK-05 — TÜM AĞACI TEMİZLE. Grup başına "Temizle" vardı; sıfırdan yetki kurarken
    /// kullanıcı 8 grubu tek tek temizlemek zorunda kalıyordu.
    ///
    /// ⚠️ "Yetkileri Sıfırla"dan FARKI: bu işlem <b>sunucuya hiçbir şey yazmaz</b> — yalnız ekrandaki
    /// kutuları boşaltır. Kaydet'e basılmazsa hiçbir şey değişmez, Vazgeç eski hâli geri getirir.
    /// Sıfırla ise doğrudan sunucuda siler. İkisi bilinçli olarak ayrı butondur.
    /// </summary>
    [RelayCommand]
    private async Task ClearAllPerms()
    {
        if (!IsEditing) return;   // salt-okunur ekranda tetiklenmez (buton da görünmez)
        if (!await ConfirmService.AskAsync(
                "Ekrandaki TÜM yetki işaretleri kaldırılsın mı?\n\n"
                + "Bu işlem sunucuya hiçbir şey yazmaz — Kaydet'e basmazsanız değişiklik olmaz.",
                "Tümünü Temizle")) return;
        ResetTree();
    }
    public ObservableCollection<ButtonPermNode> Buttons { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUser))]
    [NotifyPropertyChangedFor(nameof(TreeEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSavePerms))]
    [NotifyPropertyChangedFor(nameof(CanBeginEdit))]
    [NotifyPropertyChangedFor(nameof(CanResetPerms))]
    [NotifyPropertyChangedFor(nameof(RoleEnabled))]
    private UserRow? _selectedUser;
    public bool HasUser => SelectedUser != null;

    // ═══ YET-C3 (kullanıcı isteği 2026-08-19) — DÜZENLE → KAYDET AKIŞI ═══════════════════════
    // Eskiden ağaç DAİMA açıktı: yanlışlıkla tıklanan kutu sessizce değişiyordu ve "düzenliyorum"
    // ile "bakıyorum" ayrımı yoktu. Artık ekran SALT-OKUNUR açılır; Düzenle ile açılır, Vazgeç ile
    // sunucudan taze yüklenir. ⭐ Düzenleme moduna geçmek YETKİLERİ DEĞİŞTİRMEZ — yüklenen işaretler
    // olduğu gibi durur, yalnız kutular tıklanabilir olur.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSavePerms))]
    [NotifyPropertyChangedFor(nameof(CanBeginEdit))]
    [NotifyPropertyChangedFor(nameof(RoleEnabled))]
    [NotifyPropertyChangedFor(nameof(TemplateEnabled))]
    private bool _isEditing;

    /// <summary>Atanabilir roller (Süper Admin / Kısıtlı Süper Admin yalnız süper admine listelenir).</summary>
    public ObservableCollection<RoleOption> Roles { get; } = new();

    // ═══ A3 (ADR-116) — ŞABLONDAN DOLDURMA ═══════════════════════════════════════════════════
    // Şablon uygulamak için ayrı ekrana gitmek gerekmez. Şablon YALNIZ KUTULARI DOLDURUR;
    // sunucuya hiçbir şey yazılmaz — kararı "Kaydet" verir.
    public ObservableCollection<TemplateOption> Templates { get; } = new();

    [ObservableProperty] private TemplateOption? _selectedTemplate;

    /// <summary>Şablon kutusu yalnız düzenleme modunda ve şablon varken açıktır.</summary>
    public bool TemplateEnabled => IsEditing && HasUser && !IsTargetAdmin && Templates.Count > 0;

    /// <summary>Seçili kullanıcının YÜKLENDİĞİ andaki rolü — "değişti mi" karşılaştırması bununla yapılır.</summary>
    private string? _loadedRoleKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleChanged))]
    private RoleOption? _selectedRole;

    /// <summary>Rol kutusu düzenlenebilir mi (kendi rolünü bu ekrandan değiştirmek kilitlenmeye yol açar).</summary>
    public bool RoleEnabled => IsEditing && HasUser && SelectedUser?.Id != _session.UserId;

    /// <summary>Rol değişikliği bekliyor mu — arayüzde uyarı olarak gösterilir.</summary>
    public bool RoleChanged => SelectedRole is not null && _loadedRoleKey is not null
                               && !string.Equals(SelectedRole.Key, _loadedRoleKey, StringComparison.Ordinal);

    /// <summary>Hedef kullanıcı Admin/Süper Admin mi — öyleyse ağaç TAM işaretli + SALT-OKUNUR gösterilir
    /// (admin granular yetki tutmaz, hepsine bypass ile erişir). Kısıtlamak için önce rol Personel yapılır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSavePerms))]
    [NotifyPropertyChangedFor(nameof(CanBeginEdit))]
    [NotifyPropertyChangedFor(nameof(CanResetPerms))]
    private bool _isTargetAdmin;
    /// <summary>Ağaç düzenlenebilir mi — admin hedefte salt-okunur, ayrıca DÜZENLEME MODU şart.</summary>
    public bool TreeEnabled => HasUser && !IsTargetAdmin && IsEditing;
    /// <summary>"Düzenle" görünür mü — yetki varsa, kullanıcı seçiliyse ve henüz düzenlemiyorsa.</summary>
    public bool CanBeginEdit => HasUser && !IsTargetAdmin && CanManage && !IsEditing;
    /// <summary>Kaydet/Vazgeç görünür mü — yalnız düzenleme modunda.</summary>
    public bool CanSavePerms => HasUser && !IsTargetAdmin && IsEditing;

    [ObservableProperty] private string? _status;

    public PermissionsViewModel(SessionContext session)
    {
        _session = session;
        BuildTree(null);
        LoadRoles();
        _ = LoadUsers();
        _ = LoadTemplatesAsync();
    }

    /// <summary>Ağacı kurar. <paramref name="blocked"/> = Rol Yetki Kontrol ile hedefin ROLÜNE kapatılmış
    /// modüller (HİÇ görünmez). <paramref name="targetRoles"/> verilirse süper-admin-only ekranlar yalnız
    /// (Kısıtlı) Süper Admin hedefe gösterilir; verilemeyecek kalemler kilitle DEĞİL, tamamen gizli.</summary>
    private void BuildTree(IReadOnlySet<string>? blocked, IReadOnlyList<string>? targetRoles = null)
    {
        Modules.Clear();
        Groups.Clear();
        Buttons.Clear();
        bool hasTarget = targetRoles is not null;
        bool targetCanReceiveSuperOnly = targetRoles is not null &&
            (targetRoles.Contains(RoleKeys.RestrictedSuperAdmin) || targetRoles.Contains(RoleKeys.SuperAdmin));

        // ⭐ 2026-09-03 (kullanıcı isteği): ağaç artık MENÜ GİBİ KATEGORİZE kurulur (AppModules.Grouped —
        // rapor kalemleri "Raporlar" grubunda). Süzme kuralları BİREBİR korunur. `Modules` düz listesi
        // AYNI düğüm örnekleriyle dolmaya devam eder → kaydetme/yükleme yolları HİÇ DEĞİŞMEZ;
        // grup yalnız görünümdür ve "Tümünü Seç" aynı düğümleri işaretler.
        foreach (var grup in AppModules.Grouped())
        {
            var grupNode = new PermGroupNode(grup.Title);
            foreach (var (key, label) in grup.Items)
            {
                if (AppModules.IsPublic(key)) continue;                       // Dashboard/About/Tema herkese açık
                if (!AccessControl.CanGrantModule(_session, key)) continue;   // delegasyon tavanı + süper-admin-only görünürlük
                if (blocked is not null && blocked.Contains(key)) continue;   // Rol Yetki Kontrol: bu role kapalı
                // ⭐ B5 (2026-08-19): SÜPER ADMIN bu gizlemeden MUAFTIR — bu ekranları istediği role
                // verebildiği için ağaçta da görmelidir. Alt roller için kural aynen sürer.
                if (hasTarget && !_session.IsSuperAdmin && AppModules.IsSuperAdminOnly(key) && !targetCanReceiveSuperOnly) continue;
                var node = new ModulePermNode(key, label);
                Modules.Add(node);
                grupNode.Items.Add(node);
            }
            if (grupNode.Items.Count > 0) Groups.Add(grupNode);
        }
        foreach (var (key, label) in SpecialButtons.All)
        {
            if (!AccessControl.CanGrantButton(_session, key)) continue;   // aktörün veremeyeceği buton ağaçta yok
            Buttons.Add(new ButtonPermNode(key, label));
        }
    }

    /// <summary>Atanabilir rol listesi. Süper Admin / Kısıtlı Süper Admin YALNIZ süper admine
    /// gösterilir — API ucu (<c>/api/roles</c>) da aynı kuralı uygular, arayüz onunla hizalıdır.</summary>
    private void LoadRoles()
    {
        Roles.Clear();
        foreach (var (key, name, _) in RoleKeys.Seed)
        {
            if ((key == RoleKeys.SuperAdmin || key == RoleKeys.RestrictedSuperAdmin) && !_session.IsSuperAdmin) continue;
            Roles.Add(new RoleOption(key, name));
        }
    }

    /// <summary>Şablon seçilince kutular DOLDURULUR (kaydetmez). Kullanıcı üzerinde değişiklik
    /// yapıp "Kaydet" diyebilir; şablon "uygulandı ve yazıldı" sürprizi olmaz.</summary>
    partial void OnSelectedTemplateChanged(TemplateOption? value)
    {
        if (value is null || !IsEditing) return;
        _ = UygulaSablonAsync(value);
    }

    private async Task UygulaSablonAsync(TemplateOption t)
    {
        var d = await OrgServerClient.GetTemplateDataAsync(t.Id);
        if (d is null) { Status = "Şablon alınamadı (çevrimiçi olmayı gerektirir)."; return; }
        foreach (var m in Modules)
        {
            var p = d.Modules.FirstOrDefault(x => x.ModuleKey == m.Key);
            m.Set(p?.CanView ?? false, p?.CanCreate ?? false, p?.CanEdit ?? false, p?.CanDelete ?? false);
        }
        foreach (var b in Buttons) b.Granted = d.Buttons.Contains(b.Key);
        Status = $"\"{t.Name}\" şablonu kutulara uygulandı. Gözden geçirip Kaydet deyin.";
    }

    /// <summary>Uygulanabilir şablonlar (kendi firması + tüm-firma). Çevrimdışıysa liste boş kalır
    /// ve kutu görünmez — özellik yokmuş gibi davranır, hata vermez.</summary>
    private async Task LoadTemplatesAsync()
    {
        try
        {
            var rows = await OrgServerClient.ListTemplatesForUserAsync();
            Templates.Clear();
            foreach (var r in rows ?? new()) Templates.Add(new TemplateOption(r.Id, r.Name));
            OnPropertyChanged(nameof(TemplateEnabled));
        }
        catch { /* şablon yoksa ekran normal çalışır */ }
    }

    /// <summary>Düzenleme moduna geç. ⭐ Ağaçtaki işaretlere DOKUNMAZ — yalnız kilidi açar.</summary>
    [RelayCommand]
    private void BeginEdit()
    {
        if (!CanBeginEdit) return;
        IsEditing = true;
        Status = "Düzenleme açık — değişiklikleri Kaydet ile yazın, Vazgeç ile geri alın.";
    }

    /// <summary>Vazgeç — sunucudan TAZE yükleyip düzenleme modundan çıkar (yarım değişiklik kalmaz).</summary>
    [RelayCommand]
    private async Task CancelEdit()
    {
        IsEditing = false;
        await LoadSelectedUserAsync(SelectedUser);
        Status = "Değişiklikler geri alındı.";
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

    partial void OnSelectedUserChanged(UserRow? value)
    {
        IsEditing = false;   // başka kullanıcıya geçerken yarım düzenleme taşınmaz
        _ = LoadBranchScopeAsync(value?.Id);   // G4-3e: şube kapsamı da kullanıcıyla birlikte gelir
        _ = LoadSelectedUserAsync(value);
    }

    private async Task LoadSelectedUserAsync(UserRow? value)
    {
        if (value is null)
        {
            IsTargetAdmin = false; BuildTree(null); ResetTree();
            _loadedRoleKey = null; SelectedRole = null; OnPropertyChanged(nameof(RoleChanged));
            return;
        }
        try
        {
            // Hedef roller + engelli modüller: çevrimiçiyken sunucudan (server kullanıcı yerelde olmayabilir), yoksa yerel.
            var targetRoles = await OrgServerClient.GetUserRolesAsync(value.Id)
                ?? SafeLocalRoles(value.Id);
            BuildTree(SafeBlocked(value.Id), targetRoles);

            // Rol kutusu: kullanıcının MEVCUT rolü seçili gelir. Listede yoksa (ör. aktör süper admin
            // değil ama hedef süper admin) kutu boş kalır ve rol değişikliği yapılamaz.
            _loadedRoleKey = targetRoles.FirstOrDefault();
            SelectedRole = Roles.FirstOrDefault(r => string.Equals(r.Key, _loadedRoleKey, StringComparison.Ordinal));
            OnPropertyChanged(nameof(RoleChanged));

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
            // Kaydetme ÖZETİ için başlangıç durumu (kullanıcı isteği 2026-09-04) — web ile aynı davranış.
            _yuklenenEkranlar = Modules.Where(m => m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
                .Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
            Status = $"{value.Username} yetkileri yüklendi.";
        }
        catch (Exception ex) { Status = "Yetkiler yüklenemedi: " + ex.Message; }
    }

    /// <summary>Ekran açıldığında kullanıcının SAHİP OLDUĞU ekranlar — "Kaydet" özeti bununla karşılaştırır.</summary>
    private HashSet<string> _yuklenenEkranlar = new(StringComparer.Ordinal);

    /// <summary>
    /// ⭐ KAYDETME ÖZETİ (kullanıcı isteği 2026-09-04) — web'deki ile AYNI metin/davranış.
    ///
    /// Eskiden onay yalnız "yetkiler kaydedilsin mi?" diyordu; kullanıcı NE kaydettiğini göremiyordu.
    /// Gerçek bir olayda bu acıttı: bazı yetkiler kaldırıldı sanıldı, veritabanına 60 modülün 60'ı da
    /// TAM yetkiyle yazılmıştı ve fark kaydetme anında fark edilemedi.
    /// </summary>
    private string DegisiklikOzeti()
    {
        var secili = Modules.Where(m => m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
            .Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        string Ad(string k) => Modules.FirstOrDefault(m => m.Key == k)?.Label ?? k;

        var eklenen = secili.Except(_yuklenenEkranlar).Select(Ad).OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        var kaldirilan = _yuklenenEkranlar.Except(secili).Select(Ad).OrderBy(x => x, StringComparer.CurrentCulture).ToList();

        var kullanici = SelectedUser?.Username ?? "";
        if (eklenen.Count == 0 && kaldirilan.Count == 0)
            return $"'{kullanici}' için ekran yetkilerinde değişiklik yok.\n\n" +
                   "(İşlem hakları — ekle/düzenle/sil — değişmiş olabilir.)\n\nYine de kaydedilsin mi?";

        var sb = new System.Text.StringBuilder();
        sb.Append($"'{kullanici}' kullanıcısı için:\n\n");
        if (kaldirilan.Count > 0)
        {
            sb.Append($"KALDIRILACAK ({kaldirilan.Count} ekran):\n• ");
            sb.Append(string.Join("\n• ", kaldirilan.Take(12)));
            if (kaldirilan.Count > 12) sb.Append($"\n• … ve {kaldirilan.Count - 12} ekran daha");
            sb.Append("\n\n");
        }
        if (eklenen.Count > 0)
        {
            sb.Append($"EKLENECEK ({eklenen.Count} ekran):\n• ");
            sb.Append(string.Join("\n• ", eklenen.Take(12)));
            if (eklenen.Count > 12) sb.Append($"\n• … ve {eklenen.Count - 12} ekran daha");
            sb.Append("\n\n");
        }
        sb.Append("Kaydedilsin mi?");
        return sb.ToString();
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

    /// <summary>Rol seçimi kutusunun öğesi (anahtar + görünen ad).</summary>
    public sealed record RoleOption(string Key, string Name);

    /// <summary>Şablon seçimi kutusunun öğesi.</summary>
    public sealed record TemplateOption(string Id, string Name);

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

        // ── YET-C3: ROL DEĞİŞİKLİĞİ (varsa) ÖNCE uygulanır ──────────────────────────────────
        // Rol yetki tavanını belirler; önce rol yazılır, sonra ağaç o role göre yeniden kurulur.
        // Böylece "role verilemeyecek bir ekran" yanlışlıkla kaydedilmiş olmaz.
        if (RoleChanged && SelectedRole is not null)
        {
            if (SelectedUser.Id == _session.UserId) { Status = "Kendi rolünüzü bu ekrandan değiştiremezsiniz."; return; }
            if (!await ConfirmService.AskAsync(
                    $"'{SelectedUser.Username}' kullanıcısının rolü '{SelectedRole.Name}' olarak değiştirilecek.\n\n" +
                    "Rol, kullanıcının alabileceği yetkilerin ÜST SINIRINI belirler; kaydedildikten sonra " +
                    "yetki ağacı yeni role göre yeniden yüklenir.\n\nDevam edilsin mi?",
                    "Evet, Rolü Değiştir")) return;

            var rr = await OrgServerClient.SetRolesAsync(SelectedUser.Id, new[] { SelectedRole.Key });
            if (rr.Offline) { Status = "Rol değişikliği çevrimiçi olmayı gerektirir."; return; }
            if (!rr.Ok) { Status = rr.Error ?? "Rol değiştirilemedi."; return; }

            IsEditing = false;
            await LoadUsers();
            var yeniden = Users.FirstOrDefault(u => u.Id == SelectedUser.Id);
            SelectedUser = null; SelectedUser = yeniden;   // ağaç yeni role göre taze kurulur
            Status = $"Rol '{SelectedRole.Name}' olarak değiştirildi. Yetki ağacı yenilendi — " +
                     "ekran yetkilerini şimdi düzenleyip kaydedebilirsiniz.";
            return;
        }

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
        else if (!await ConfirmService.AskAsync(DegisiklikOzeti(), "Yetkileri Kaydet")) return;

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
            IsEditing = false;   // kaydedildi → ekran tekrar salt-okunur
            Status = "Yetkiler kaydedildi. (Kullanıcı yeniden giriş yapınca tam etkin olur.)";
            _permVersion++;   // sunucu sürümü artırdı; ekranı kapatmadan ikinci kayıt yapılabilsin
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  G4-3e — ŞUBE KAPSAMI YÖNETİMİ (masaüstü). Web'deki Permissions.razor bölümünün karşılığı.
    //
    //  İKİNCİ BİR YETKİ AĞACI DEĞİLDİR: modül yetkileri yukarıdaki matriste kalır; burada yalnız
    //  "hangi şubelerde" sorusu yönetilir.
    //  ETKİN ERİŞİM = MODÜL YETKİSİ ∧ ŞUBE KAPSAMI ∧ PLATFORM ∧ diğer AccessControl kuralları.
    //
    //  ⚠️ Liste AKTÖRÜN kapsamıyla KIRPILMIŞ gelir (PermissionService.GetBranchScope);
    //     asıl kapı serviste (BranchAccess.RequireGrantable) — yetkisiz şube gönderilirse HATA verir.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Kapsam ekranındaki işaretlenebilir şube satırı.</summary>
    public sealed partial class ScopePick : ObservableObject
    {
        public ScopePick(string id, string name, bool selected)
        {
            Id = id; Name = name; _isChecked = selected;
        }
        public string Id { get; }
        public string Name { get; }
        [ObservableProperty] private bool _isChecked;
    }

    /// <summary>Hedef kullanıcıya atanabilecek şubeler (aktörün kapsamıyla kırpılmış).</summary>
    public ObservableCollection<ScopePick> ScopeBranches { get; } = new();

    [ObservableProperty] private string _scopeModeText = "";
    [ObservableProperty] private bool _scopeLoaded;

    /// <summary>GUI-05 — kapsam okunamadıysa sebebi (boş = sorun yok). Panelde görünür.</summary>
    [ObservableProperty] private string? _scopeError;

    /// <summary>Kendi kapsamını değiştiremez — yetki sıfırlamadaki kuralın aynısı.
    /// Kapsam okunamadıysa da düzenleme AÇILMAZ (boş listeyi kaydedip kapsamı silmeyi önler).</summary>
    public bool CanEditScope => HasUser && CanManage && SelectedUser?.Id != _session.UserId && ScopeError is null;

    /// <summary>Kapsam bölümünde gösterilecek açıklama (kullanıcı ne olduğunu tahmin etmesin).</summary>
    public string ScopeHint => SelectedUser?.Id == _session.UserId
        ? "Kendi şube kapsamınızı değiştiremezsiniz. Bunu başka bir yetkili yapmalıdır."
        : ScopeBranches.Count == 0
            ? "Devredebileceğiniz şube yok — yalnız kendi kapsamınızdaki şubeleri verebilirsiniz."
            : "Hiçbiri işaretlenmezse açık kapsam kaldırılır; kullanıcı kendi şubesi/varsayılan davranışına döner.";

    /// <summary>Seçili kullanıcının şube kapsamını + aktörün verebileceği şubeleri okur.</summary>
    private async Task LoadBranchScopeAsync(string? userId)
    {
        ScopeBranches.Clear();
        ScopeModeText = "";
        ScopeError = null;
        ScopeLoaded = false;
        if (string.IsNullOrWhiteSpace(userId) || !CanManage) { Notify(); return; }
        try
        {
            // ⭐ GUI-05: kapsam ÖNCE SUNUCUDAN (kullanıcı listesi ve yetkiler de sunucudan geliyor).
            // Web'de oluşturulmuş kullanıcı bu makinenin yerel veritabanında OLMAYABİLİR; yerelden okumak
            // "kullanıcı bulunamadı" ile düşüyor ve panel sessizce kayboluyordu. Çevrimdışıysa yerele düşer.
            var uzak = await OrgServerClient.GetBranchScopeAsync(userId!);
            if (uzak is { } u)
            {
                ScopeModeText = u.ModeText;
                var acik = u.ScopeBranchIds.ToHashSet(StringComparer.Ordinal);
                foreach (var (id, name) in u.Assignable)
                    ScopeBranches.Add(new ScopePick(id, name, acik.Contains(id)));
                ScopeLoaded = true;
                Notify();
                return;
            }

            var v = DesktopServices.Permissions.GetBranchScope(_session, userId!);
            ScopeModeText = v.ModeText;
            var mevcut = v.ScopeBranchIds.ToHashSet(StringComparer.Ordinal);
            foreach (var b in v.AssignableBranches)
                ScopeBranches.Add(new ScopePick(b.Id, b.Name, mevcut.Contains(b.Id)));
            ScopeLoaded = true;
        }
        catch (Exception ex)
        {
            // ⭐ GUI-05 (2026-08-13, gerçek masaüstü GUI testinde bulundu): hata Status'a yazılıyordu ama
            // hemen ardından çalışan yetki yüklemesi Status'u EZİYORDU → panel sessizce KAYBOLUYORDU.
            // Kullanıcı "Şube Kapsamı bölümü neden yok?" sorusunun cevabını hiçbir yerde göremiyordu.
            // (Tipik neden: kullanıcı sunucuda var ama BU MAKİNENİN yerel veritabanında yok — masaüstüne
            //  yalnız o makinede giriş yapmış kullanıcılar iner. Artık sebep ekranda yazar.)
            ScopeError = ex.Message;
            ScopeLoaded = true;   // panel görünsün ki sebep okunabilsin
            ScopeModeText = "okunamadı";
        }
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanEditScope));
        OnPropertyChanged(nameof(ScopeHint));
    }

    /// <summary>
    /// Kapsamı kaydeder. Yetkisiz şube gönderilirse servis AÇIK HATA döner (sessiz kırpma YOK) —
    /// mesaj kullanıcıya olduğu gibi gösterilir. Audit ve snapshot tazeleme servistedir.
    /// </summary>
    [RelayCommand]
    private async Task SaveBranchScope()
    {
        if (SelectedUser is null || !CanEditScope) return;
        try
        {
            var secili = ScopeBranches.Where(x => x.IsChecked).Select(x => x.Id).ToList();
            // ⭐ GUI-05: kayıt da SUNUCUYA gider (kapsam sunucu-otoriteli; hedef kullanıcı bir sonraki
            // girişte alır — kullanıcı paketiyle birlikte iner). Çevrimdışıysa yerele yazılır.
            var r = await OrgServerClient.SaveBranchScopeAsync(SelectedUser.Id, secili);
            if (r.Offline) DesktopServices.Permissions.SaveBranchScope(_session, SelectedUser.Id, secili);
            else if (!r.Ok) { Status = r.Error ?? "Şube kapsamı kaydedilemedi."; return; }

            Status = secili.Count == 0
                ? "Şube kapsamı kaldırıldı (kullanıcı varsayılan davranışına döndü)."
                : $"Şube kapsamı kaydedildi ({secili.Count} şube).";
            await LoadBranchScopeAsync(SelectedUser.Id);   // kip değişmiş olabilir
        }
        catch (Exception ex) { Status = ex.Message; }
    }
}

/// <summary>Yetki ağacı GRUBU (2026-09-03) — menü kategorisi gibi başlık + o gruptaki modül düğümleri.</summary>
public sealed class PermGroupNode
{
    public string Title { get; }
    public ObservableCollection<ModulePermNode> Items { get; } = new();
    public PermGroupNode(string title) { Title = title; }
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

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  G4-3e — ŞUBE KAPSAMI YÖNETİMİ (masaüstü). Web'deki Permissions.razor bölümünün karşılığı.
}
