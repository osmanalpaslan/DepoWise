using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Tanımlar: Malzeme ALT KATEGORİ yönetimi (kullanıcı isteği 2026-08-05, madde 10). Kategori seç →
/// o kategorinin alt kategorilerini ekle/düzenle/sil. Her alt kategori seçili KATEGORİYE bağlıdır (parent_id).
/// Yetki: "definitions". Tenant-izole (servis katmanında).
/// </summary>
public sealed partial class SubCategorySectionViewModel : ViewModelBase
{
    private readonly SessionContext _s;

    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<LookupRowViewModel> Items { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> CategoryPopulator => SearchPopulator.For(() => Categories, c => c.Name);
    [ObservableProperty] private LookupItem? _selectedCategory;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _error;

    public bool CanWrite => AccessControl.Can(_s, "definitions", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_s, "definitions", PermissionAction.Delete);
    public bool CanEdit => AccessControl.Can(_s, "definitions", PermissionAction.Edit);

    public SubCategorySectionViewModel(SessionContext s)
    {
        _s = s;
        try { foreach (var c in DesktopServices.Lookups.ListCategories(_s, null)) Categories.Add(c); }
        catch (Exception ex) { Error = ex.Message; }
    }

    partial void OnSelectedCategoryChanged(LookupItem? value) => ReloadSubs();

    private void ReloadSubs()
    {
        Error = null; NewName = "";
        Items.Clear();
        if (SelectedCategory is null) return;
        try { foreach (var sc in DesktopServices.Lookups.ListCategories(_s, SelectedCategory.Id)) Items.Add(new LookupRowViewModel(sc.Id, sc.Name, sc.IsLocked)); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void Add()
    {
        Error = null;
        if (SelectedCategory is null) { Error = "Önce kategori seçin."; return; }
        if (string.IsNullOrWhiteSpace(NewName)) { Error = "Ad girin."; return; }
        try { DesktopServices.Lookups.AddCategory(_s, NewName.Trim(), SelectedCategory.Id); NewName = ""; ReloadSubs(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task Delete(LookupRowViewModel? item)
    {
        if (item is null) return;
        if (!await ConfirmService.AskAsync($"'{item.OriginalName}' alt kategorisi silinsin mi?", "Alt Kategori Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Lookups.Delete(_s, "material_categories", item.Id); ReloadSubs(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void Rename(LookupRowViewModel? item)
    {
        if (item is null) return;
        Error = null;
        var newName = (item.Name ?? "").Trim();
        if (newName == item.OriginalName) return;   // değişmemiş → sessiz geç
        try { DesktopServices.Lookups.Rename(_s, "material_categories", item.Id, newName); ReloadSubs(); }
        catch (Exception ex) { Error = ex.Message; item.Name = item.OriginalName; }
    }
}
