using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uygulama kabuğu: ikon rayı + açıklamalı accordion menü + üst bar + içerik navigasyonu.
/// Yetkiye göre menü; "Eşitle"/marka korunur. Navigasyon binding'leri (NavigateCommand/GoDashboardCommand) korunur.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public string AppName { get; }
    public string CompanyName { get; }
    public string DisplayName { get; }
    public string Initial { get; }
    public string Welcome { get; }
    public string BuildStamp { get; } = BuildInfo();
    public IReadOnlyList<NavGroupVm> Groups { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _currentContext = "";
    [ObservableProperty] private string _activeKey = "dashboard";
    [ObservableProperty] private bool _isNavPanelOpen = true;

    /// <summary>Aktif kabuk — çapraz ekran navigasyonu için (ör. malzeme detayından araç ekranına).</summary>
    public static ShellViewModel? Current { get; private set; }

    public ShellViewModel(SessionContext session)
    {
        Current = this;
        _session = session;
        AppName = DesktopServices.Branding.AppName;
        CompanyName = DesktopServices.Branding.CompanyName;
        DisplayName = DesktopServices.DisplayName(session.UserId);
        Initial = string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant();
        Welcome = $"Hoş geldiniz, {DisplayName} — {DateTime.Now:dd MMMM yyyy dddd}";
        Groups = BuildGroups(session);

        Navigate("dashboard");
    }

    private static IReadOnlyList<NavGroupVm> BuildGroups(SessionContext s)
    {
        var all = new[]
        {
            new NavGroupVm("📦", "Malzemeler", "materials", new[]
            {
                new NavLinkVm("Malzeme Listesi", "materials"),
                new NavLinkVm("Yeni Kayıt", "materials:new"),
                new NavLinkVm("Kategoriler", "definitions"),
            }, expanded: true),
            new NavGroupVm("🚚", "Araçlar", "vehicles", new[]
            {
                new NavLinkVm("Araç Listesi", "vehicles"),
                new NavLinkVm("Şablonlar", "vehicles:templates"),
                new NavLinkVm("Yeni Araç Ekle", "vehicles:new"),
            }),
            new NavGroupVm("🔧", "Bakım Takibi", "maintenance", new[]
            {
                new NavLinkVm("Bakım Tanımları", "maintenance:defs"),
                new NavLinkVm("Araç Bakımları", "maintenance:records"),
                new NavLinkVm("Uyarılar", "maintenance:alerts"),
            }),
            new NavGroupVm("⛽", "Yakıt", "fuel", new[]
            {
                new NavLinkVm("Yakıt Dağıtımları", "fuel:dist"),
                new NavLinkVm("Depo Girişleri", "fuel:depot"),
                new NavLinkVm("Özet", "fuel:summary"),
            }),
            new NavGroupVm("👤", "Yönetim", "users", new[]
            {
                new NavLinkVm("Kullanıcılar", "users"),
                new NavLinkVm("Şube / Şantiye", "branches"),
                new NavLinkVm("Yetkiler", "permissions"),
            }, expanded: true),
            new NavGroupVm("📄", "Talepler", "requests", new[]
            {
                new NavLinkVm("Talep Formu", "requests:form"),
                new NavLinkVm("Talep Onaylama", "requests:approve"),
            }),
            new NavGroupVm("📊", "Raporlar", "reports", new[] { new NavLinkVm("Raporlar", "reports") }),
            new NavGroupVm("⚙️", "Tanımlar / Ayarlar", "definitions", new[] { new NavLinkVm("Tanımlar", "definitions") }),
        };

        // Alt bağlantıyı KENDİ yetkisine göre filtrele (alt-sekme anahtarı parent modüle map'lenir:
        // "maintenance:defs" → "maintenance"). Görünür alt bağlantısı kalmayan grup gizlenir.
        // Verilmeyen ekran menüde GÖRÜNMEZ (deny-by-default).
        return all
            .Select(g => new NavGroupVm(g.Icon, g.Title, g.ModuleKey,
                g.Children.Where(c => AccessControl.CanSeeMenu(s, BaseKey(c.Key))).ToList(), g.IsExpanded))
            .Where(g => g.Children.Count > 0)
            .ToList();
    }

    private static string BaseKey(string key)
    {
        var i = key.IndexOf(':');
        return i < 0 ? key : key[..i];
    }

    /// <summary>İkon rayından grup seçimi: grubu aç + birincil hedefe git.</summary>
    [RelayCommand]
    private void SelectGroup(NavGroupVm? group)
    {
        if (group is null) return;
        group.IsExpanded = true;
        Navigate(group.PrimaryKey);
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        ActiveKey = key;
        switch (key)
        {
            case "dashboard":
                CurrentPage = new DashboardViewModel(_session);
                CurrentTitle = "Genel Özet";
                CurrentContext = "Özet istatistikler ve kritik uyarılar";
                break;
            case "materials":
                CurrentPage = new MaterialsViewModel(_session);
                CurrentTitle = "Malzemeler";
                CurrentContext = "Malzeme kartları ve stok";
                break;
            case "materials:new":
                CurrentPage = new MaterialsViewModel(_session, openAdd: true);
                CurrentTitle = "Malzemeler — Yeni Kayıt";
                CurrentContext = "Yeni malzeme formu";
                break;
            case "vehicles":
            case "vehicles:new":
                CurrentPage = new VehiclesViewModel(_session);
                CurrentTitle = "Araçlar";
                CurrentContext = "Araç kartları, durum ve uyarılar";
                break;
            case "vehicles:templates":
                CurrentPage = new VehicleTemplatesViewModel(_session);
                CurrentTitle = "Araç Genel Tanım";
                CurrentContext = "Şablonlar — araç formunu otomatik doldurur";
                break;
            case "maintenance":
            case "maintenance:defs":
                CurrentPage = new MaintenanceViewModel(_session, 0);
                CurrentTitle = "Bakım Takibi";
                CurrentContext = "Bakım tanımları";
                break;
            case "maintenance:records":
                CurrentPage = new MaintenanceViewModel(_session, 1);
                CurrentTitle = "Bakım Takibi";
                CurrentContext = "Araç bakım kayıtları";
                break;
            case "maintenance:alerts":
                CurrentPage = new MaintenanceViewModel(_session, 2);
                CurrentTitle = "Bakım Takibi";
                CurrentContext = "Periyodik bakım uyarıları";
                break;
            case "fuel":
            case "fuel:dist":
                CurrentPage = new FuelViewModel(_session, 0);
                CurrentTitle = "Yakıt";
                CurrentContext = "Yakıt dağıtımları";
                break;
            case "fuel:depot":
                CurrentPage = new FuelViewModel(_session, 1);
                CurrentTitle = "Yakıt";
                CurrentContext = "Depo girişleri";
                break;
            case "fuel:summary":
                CurrentPage = new FuelViewModel(_session, 2);
                CurrentTitle = "Yakıt";
                CurrentContext = "Yakıt özeti";
                break;
            case "users":
                CurrentPage = new UsersViewModel(_session);
                CurrentTitle = "Kullanıcılar";
                CurrentContext = "Kullanıcı yönetimi ve rol atama";
                break;
            case "branches":
                CurrentPage = new BranchesViewModel(_session);
                CurrentTitle = "Şube / Şantiye";
                CurrentContext = "Şube tanımları ve atanmış kullanıcılar";
                break;
            case "permissions":
                CurrentPage = new PermissionsViewModel(_session);
                CurrentTitle = "Yetkiler";
                CurrentContext = "Kullanıcı bazlı menü + alan + buton yetkileri";
                break;
            case "requests":
            case "requests:form":
                CurrentPage = new RequestsViewModel(_session, 0);
                CurrentTitle = "Talepler";
                CurrentContext = "Talep formu ve liste";
                break;
            case "requests:approve":
                CurrentPage = new RequestsViewModel(_session, 1);
                CurrentTitle = "Talepler";
                CurrentContext = "Talep onaylama (bekleyen)";
                break;
            case "reports":
                CurrentPage = new ReportsViewModel(_session);
                CurrentTitle = "Raporlar";
                CurrentContext = "Stok ve yakıt raporları";
                break;
            case "definitions":
                CurrentPage = new SettingsViewModel(_session);
                CurrentTitle = "Tanımlar / Ayarlar";
                CurrentContext = "Marka ve uygulama ayarları";
                break;
            default:
                var label = FindLabel(key);
                CurrentPage = new PlaceholderViewModel(label);
                CurrentTitle = label;
                CurrentContext = "";
                break;
        }
        UpdateActiveStates(key);
    }

    [RelayCommand]
    private void GoDashboard() => Navigate("dashboard");

    /// <summary>Araçlar ekranına gidip ilgili aracı seçer (malzeme detayındaki uyumlu araç tıklaması).</summary>
    public void GoToVehicle(string vehicleId)
    {
        Navigate("vehicles");
        if (CurrentPage is VehiclesViewModel vm) vm.SelectById(vehicleId);
    }

    /// <summary>Çıkış Yap — oturumu kapatır, "Beni Hatırla"yı siler, giriş ekranına döner.</summary>
    [RelayCommand]
    private void Logout() => DepoWise.Desktop.App.Current?.Logout();

    /// <summary>Çalışan derlemenin damgası (doğru build'i gözle doğrulamak için).</summary>
    private static string BuildInfo()
    {
        try
        {
            var loc = typeof(DepoWise.Desktop.App).Assembly.Location;
            return string.IsNullOrEmpty(loc) ? "" : "build " + System.IO.File.GetLastWriteTime(loc).ToString("dd.MM HH:mm");
        }
        catch { return ""; }
    }

    [RelayCommand]
    private void ToggleNavPanel() => IsNavPanelOpen = !IsNavPanelOpen;

    /// <summary>Seçili modül/satır vurgularını günceller (mavi vurgu + koyu seçili satır).</summary>
    private void UpdateActiveStates(string key)
    {
        foreach (var g in Groups)
        {
            bool groupActive = false;
            foreach (var c in g.Children)
            {
                c.IsActive = c.Key == key;
                if (c.IsActive) groupActive = true;
            }
            g.IsActive = groupActive || g.ModuleKey == key;
        }
    }

    private string FindLabel(string key)
        => Groups.SelectMany(g => g.Children).FirstOrDefault(c => c.Key == key)?.Title ?? key;
}

/// <summary>Henüz UI bağlanmamış modüller için bilgilendirici yer tutucu (iş mantığı + testler hazır).</summary>
public sealed partial class PlaceholderViewModel : ViewModelBase
{
    public string Title { get; }
    public string Message { get; }

    public PlaceholderViewModel(string title)
    {
        Title = title;
        Message = $"\"{title}\" ekranı yakında. İş mantığı ve servis katmanı hazır ve testli; ekran bağlama sırada.";
    }
}
