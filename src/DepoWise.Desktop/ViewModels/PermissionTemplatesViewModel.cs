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
/// </summary>
public sealed partial class PermissionTemplatesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<PermissionTemplateRow> Templates { get; } = new();
    public ObservableCollection<TemplateModuleRow> Modules { get; } = new();

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _status;

    public bool IsSuperAdmin => _session.IsSuperAdmin;

    public PermissionTemplatesViewModel(SessionContext session)
    {
        _session = session;
        foreach (var (key, label) in AppModules.All)
        {
            if (AppModules.IsPublic(key) || AppModules.IsSuperAdminOnly(key)) continue;
            Modules.Add(new TemplateModuleRow(key, label));
        }
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        Templates.Clear();
        try { foreach (var t in DesktopServices.PermissionTemplates.List(_session)) Templates.Add(t); Status = $"{Templates.Count} şablon"; }
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "Şablon adı girin."; return; }
        try
        {
            DesktopServices.PermissionTemplates.Create(_session, NewName.Trim(),
                Modules.Select(m => m.ToPermission()), Array.Empty<string>());
            Status = $"Şablon kaydedildi: {NewName.Trim()}";
            NewName = "";
            foreach (var m in Modules) { m.CanView = m.CanCreate = m.CanEdit = m.CanDelete = false; }
            Load();
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete(PermissionTemplateRow? t)
    {
        if (t is null) return;
        if (!await ConfirmService.AskAsync($"'{t.Name}' şablonu silinsin mi?", "Şablon Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.PermissionTemplates.Delete(_session, t.Id); Load(); Status = "Şablon silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
