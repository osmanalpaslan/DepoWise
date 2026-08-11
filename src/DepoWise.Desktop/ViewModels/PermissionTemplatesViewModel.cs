using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Yetki şablonu düzenleyici satırı — bir modül + 4 işlem bayrağı.</summary>
public sealed partial class TemplateModuleRow : ObservableObject
{
    public string ModuleKey { get; }
    public string Label { get; }
    [ObservableProperty] private bool _canView;
    [ObservableProperty] private bool _canCreate;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private bool _canDelete;
    public TemplateModuleRow(string key, string label) { ModuleKey = key; Label = label; }
    public ModulePermission ToPermission() => new(ModuleKey, CanView, CanCreate, CanEdit, CanDelete);
}

/// <summary>
/// Yetki Şablonları (yalnız Süper Admin) — isimli şablon oluştur/sil. Yeni kullanıcı oluştururken seçilir,
/// yetkiler bu şablona göre yazılır (Kullanıcılar ekranı).
///
/// G6-01 (2026-08-11): şablonlar SUNUCU-OTORİTELİdir. Önceden yerel SQLite'a yazılıyordu; oysa
/// <c>permission_templates</c> iş senkronunda YOKTUR → masaüstünde oluşturulan şablon web'de ve diğer
/// makinelerde hiç görünmüyordu. Kullanıcı/yetki ekranlarındaki kanıtlanmış desen uygulandı: çevrimiçiyken
/// doğrudan sunucu API'si, çevrimdışıyken açık uyarı (yerele yazılmaz — yazsak yine kaybolurdu).
/// Yetki/tenant kuralları sunucuda aynen işler (şablon oluştur/sil yalnız süper admin).
/// </summary>
public sealed partial class PermissionTemplatesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<PermissionTemplateRow> Templates { get; } = new();
    public ObservableCollection<TemplateModuleRow> Modules { get; } = new();
    public ObservableCollection<RolePick> Roles { get; } = new();

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private RolePick? _selectedRole;
    [ObservableProperty] private string? _status;

    public bool IsSuperAdmin => _session.IsSuperAdmin;

    public PermissionTemplatesViewModel(SessionContext session)
    {
        _session = session;
        foreach (var (key, label) in AppModules.All)
        {
            if (AppModules.IsPublic(key)) continue;
            if (!AccessControl.CanGrantModule(_session, key)) continue; // delegasyon tavanı + süper-admin-only görünürlük
            Modules.Add(new TemplateModuleRow(key, label));
        }
        // Şablona rol seçimi (Süper Admin hariç roller — süper admin şablonla atanmaz)
        foreach (var (key, name, _) in RoleKeys.Seed)
            if (key != RoleKeys.SuperAdmin) Roles.Add(new RolePick(key, name));
        _ = Load();
    }

    [RelayCommand]
    private async Task Load()
    {
        Templates.Clear();
        try
        {
            var rows = await OrgServerClient.ListTemplatesAsync();
            if (rows is null)
            {
                Status = "Şablon listesi çevrimiçi olmayı gerektirir (şablonlar sunucuda tutulur).";
                await ReportLocalLeftoversAsync();
                return;
            }
            foreach (var t in rows) Templates.Add(t);
            Status = $"{Templates.Count} şablon";
            await ReportLocalLeftoversAsync();
        }
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
    }

    /// <summary>
    /// G6-01 geçişi: sunucu-otoriteli modelden ÖNCE bu bilgisayarda yerele yazılmış şablonlar olabilir.
    /// Hiçbiri SİLİNMEZ ve değiştirilmez; ama sessizce yok sayılmaları da doğru olmaz — varsa sayısı
    /// kullanıcıya bildirilir (yeni şablonlar sunucuda oluşturulur ve her yerde görünür).
    /// </summary>
    private Task ReportLocalLeftoversAsync()
    {
        try
        {
            var local = DesktopServices.PermissionTemplates.List(_session);
            var serverIds = Templates.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var leftover = local.Count(t => !serverIds.Contains(t.Id));
            if (leftover > 0)
                Status += $"  •  Bu bilgisayarda yalnız yerelde kalmış {leftover} eski şablon var "
                        + "(silinmedi, listelenmiyor). Kullanmak isterseniz sunucuda yeniden oluşturun.";
        }
        catch { /* yerel okuma başarısızsa sessiz geç — bilgilendirme amaçlıdır */ }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "Şablon adı girin."; return; }
        try
        {
            var res = await OrgServerClient.CreateTemplateAsync(NewName.Trim(), SelectedRole?.Key,
                Modules.Select(m => m.ToPermission()), Array.Empty<string>(),
                companyId: null, scopeAll: false);   // masaüstünde kapsam: oturumun firması (web'deki gibi seçilmez)
            if (res.Offline) { Status = "Şablon kaydetme çevrimiçi olmayı gerektirir (şablonlar sunucuda tutulur)."; return; }
            if (!res.Ok) { Status = res.Error ?? "Şablon kaydedilemedi."; return; }
            Status = $"Şablon kaydedildi: {NewName.Trim()}";
            NewName = ""; SelectedRole = null;
            foreach (var m in Modules) { m.CanView = m.CanCreate = m.CanEdit = m.CanDelete = false; }
            await Load();
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete(PermissionTemplateRow? t)
    {
        if (t is null) return;
        if (!await ConfirmService.AskAsync($"'{t.Name}' şablonu silinsin mi?", "Şablon Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            var res = await OrgServerClient.DeleteTemplateAsync(t.Id);
            if (res.Offline) { Status = "Şablon silme çevrimiçi olmayı gerektirir (şablonlar sunucuda tutulur)."; return; }
            if (!res.Ok) { Status = res.Error ?? "Şablon silinemedi."; return; }
            await Load();
            Status = "Şablon silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
