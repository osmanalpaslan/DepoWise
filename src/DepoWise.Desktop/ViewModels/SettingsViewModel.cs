using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Tanımlar / Ayarlar — Marka (branding) bölümü düzenleme. Kalıcılık SettingsService.Set ile (mekanizma değişmez).
/// Hassas ayar (marka adı görünümü/başlığı etkiler) → kaydetmeden önce onay. Geri Al = servisten yeniden yükle.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    [ObservableProperty] private string _appName = "";
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string _contact = "";
    [ObservableProperty] private string _website = "";
    [ObservableProperty] private string _copyright = "";

    [ObservableProperty] private string _backupServerUrl = "";
    [ObservableProperty] private string _backupServerToken = "";

    [ObservableProperty] private bool _confirmVisible;
    [ObservableProperty] private string? _status;

    public bool CanWrite => AccessControl.Can(_session, "definitions", PermissionAction.Edit);

    /// <summary>Tanım listeleri (accordion bölümleri): kategori/birim/marka/tedarikçi/şube/araç tanımları.</summary>
    public System.Collections.ObjectModel.ObservableCollection<LookupSectionViewModel> LookupSections { get; } = new();

    // ── Geliştirici Modu ──
    [ObservableProperty] private string _developerCode = "";
    public bool DeveloperActive => DeveloperMode.IsActive;
    public string DeveloperStatusText => DeveloperMode.IsActive ? "Geliştirici modu AÇIK (süper admin yetkileri)" : "Geliştirici modu kapalı";

    public SettingsViewModel(SessionContext session)
    {
        _session = session;
        Revert();
        BuildLookupSections();
    }

    private void BuildLookupSections()
    {
        var L = DesktopServices.Lookups;
        void Add(string title, string table,
            Func<SessionContext, IReadOnlyList<DepoWise.Infrastructure.Materials.LookupItem>> load,
            Action<SessionContext, string> add)
            => LookupSections.Add(new LookupSectionViewModel(_session, title, table, load, add));

        Add("Malzeme Kategorileri", "material_categories", s => L.ListCategories(s), (s, n) => L.AddCategory(s, n));
        Add("Birimler", "units", s => L.List(s, "units"), (s, n) => L.AddUnit(s, n));
        Add("Markalar (Malzeme)", "brands", s => L.ListBrands(s, "material"), (s, n) => L.AddBrand(s, n, "material"));
        Add("Tedarikçiler", "suppliers", s => L.List(s, "suppliers"), (s, n) => L.AddSupplier(s, n));
        Add("Şube / Şantiye", "branches", s => L.List(s, "branches"), (s, n) => L.AddBranch(s, n));
        Add("Araç Tipleri", "vehicle_types", s => L.List(s, "vehicle_types"), (s, n) => L.AddVehicleType(s, n));
        Add("Araç Kategorileri", "vehicle_categories", s => L.List(s, "vehicle_categories"), (s, n) => L.AddVehicleCategory(s, n));
        Add("Markalar (Araç)", "brands", s => L.ListBrands(s, "vehicle"), (s, n) => L.AddVehicleBrand(s, n));
    }

    private void NotifyDev()
    {
        OnPropertyChanged(nameof(DeveloperActive));
        OnPropertyChanged(nameof(DeveloperStatusText));
    }

    [RelayCommand]
    private void ActivateDeveloper()
    {
        if (DeveloperCode?.Trim() != DeveloperMode.Code) { Status = "Geçersiz geliştirici kodu."; return; }
        DeveloperMode.IsActive = true;
        DeveloperCode = "";
        NotifyDev();
        Status = "Geliştirici modu açıldı (çıkışta otomatik kapanır).";
    }

    [RelayCommand]
    private void DeactivateDeveloper()
    {
        DeveloperMode.IsActive = false;
        NotifyDev();
        Status = "Geliştirici modu kapatıldı.";
    }

    /// <summary>Servisten yükle (Geri Al). Kaydedilmemiş düzenlemeleri atar.</summary>
    [RelayCommand]
    private void Revert()
    {
        var b = DesktopServices.Settings.GetBranding(_session.CompanyId);
        AppName = b.AppName;
        CompanyName = b.CompanyName;
        Contact = b.Contact ?? "";
        Website = b.Website ?? "";
        Copyright = b.Copyright ?? "";
        BackupServerUrl = DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerUrl) ?? "";
        BackupServerToken = DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerToken) ?? "";
        ConfirmVisible = false;
        Status = "Yüklendi.";
    }

    /// <summary>Hassas ayar (marka) → önce onay panelini göster.</summary>
    [RelayCommand]
    private void RequestSave()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(AppName) || string.IsNullOrWhiteSpace(CompanyName))
        {
            Status = "Uygulama adı ve şirket adı zorunlu."; return;
        }
        ConfirmVisible = true;
    }

    [RelayCommand]
    private void CancelConfirm() => ConfirmVisible = false;

    /// <summary>Onaylı kaydet — her anahtar firma override olarak yazılır (SettingsService.Set).</summary>
    [RelayCommand]
    private void ConfirmSave()
    {
        try
        {
            var c = _session.CompanyId;
            var uid = _session.UserId;
            DesktopServices.Settings.Set(c, SettingKeys.BrandAppName, AppName.Trim(), uid);
            DesktopServices.Settings.Set(c, SettingKeys.BrandCompanyName, CompanyName.Trim(), uid);
            DesktopServices.Settings.Set(c, SettingKeys.BrandContact, Nullify(Contact), uid);
            DesktopServices.Settings.Set(c, SettingKeys.BrandWebsite, Nullify(Website), uid);
            DesktopServices.Settings.Set(c, SettingKeys.BrandCopyright, Nullify(Copyright), uid);
            DesktopServices.Settings.Set(c, SettingKeys.BackupServerUrl, Nullify(BackupServerUrl), uid);
            DesktopServices.Settings.Set(c, SettingKeys.BackupServerToken, Nullify(BackupServerToken), uid);
            ConfirmVisible = false;
            Status = "Kaydedildi. Bazı değişiklikler (başlık/marka) uygulama yeniden açılınca tam yansır.";
        }
        catch (Exception ex) { ConfirmVisible = false; Status = "Kaydedilemedi: " + ex.Message; }
    }

    private static string? Nullify(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
