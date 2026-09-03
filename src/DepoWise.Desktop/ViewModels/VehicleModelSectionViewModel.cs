using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ═══ TANIMLAR — ARAÇ MODELLERİ BÖLÜMÜ (kullanıcı isteği 2026-09-03) ═══
///
/// Kullanıcı: "Tanımlar ekranında bir çok kayıt tipi eklenilen alan eksik — örnek araçlar için model
/// alanı listelenmiyor." Model, MARKAYA bağlıdır → Alt Kategori bölümüyle AYNI desen: marka seç →
/// modellerini listele/ekle/yeniden adlandır/sil. Yetkiler "definitions" modülünden (mevcut kural).
/// </summary>
public sealed partial class VehicleModelSectionViewModel : ViewModelBase
{
    private readonly SessionContext _s;

    public ObservableCollection<LookupItem> Brands { get; } = new();
    public ObservableCollection<LookupRowViewModel> Items { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BrandPopulator => SearchPopulator.For(() => Brands, b => b.Name);
    [ObservableProperty] private LookupItem? _selectedBrand;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _error;

    public bool CanWrite => AccessControl.Can(_s, "definitions", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_s, "definitions", PermissionAction.Delete);
    public bool CanEdit => AccessControl.Can(_s, "definitions", PermissionAction.Edit);

    public VehicleModelSectionViewModel(SessionContext s)
    {
        _s = s;
        try { foreach (var b in DesktopServices.Lookups.ListBrands(_s, "vehicle")) Brands.Add(b); }
        catch (Exception ex) { Error = ex.Message; }
    }

    partial void OnSelectedBrandChanged(LookupItem? value) => ReloadModels();

    private void ReloadModels()
    {
        Error = null; NewName = "";
        Items.Clear();
        if (SelectedBrand is null) return;
        try { foreach (var m in DesktopServices.Lookups.ListVehicleModels(_s, SelectedBrand.Id)) Items.Add(new LookupRowViewModel(m.Id, m.Name, m.IsLocked)); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void Add()
    {
        Error = null;
        if (SelectedBrand is null) { Error = "Önce marka seçin."; return; }
        if (string.IsNullOrWhiteSpace(NewName)) { Error = "Model adı girin."; return; }
        try { DesktopServices.Lookups.AddVehicleModel(_s, SelectedBrand.Id, NewName.Trim()); NewName = ""; ReloadModels(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task Delete(LookupRowViewModel? item)
    {
        if (item is null) return;
        if (!await ConfirmService.AskAsync($"'{item.OriginalName}' modeli silinsin mi? Bu modele bağlı araçlar model bilgisiz kalmaz — kayıtları değişmez, yalnız yeni seçimlerde görünmez.",
                "Model Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Lookups.Delete(_s, "vehicle_models", item.Id); ReloadModels(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void Rename(LookupRowViewModel? item)
    {
        if (item is null) return;
        Error = null;
        var newName = (item.Name ?? "").Trim();
        if (newName == item.OriginalName) return;   // değişmemiş → sessiz geç
        try { DesktopServices.Lookups.Rename(_s, "vehicle_models", item.Id, newName); ReloadModels(); }
        catch (Exception ex) { Error = ex.Message; item.Name = item.OriginalName; }
    }
}
