using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Genel Özet — KPI kartları + kritik uyarılar (DashboardService). İş verisi/sayımlar değişmez;
/// yalnız sunum. Yalnız ilk (en önemli) kart vurgulu (Primary), diğerleri nötr yüzey.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<KpiCard> Cards { get; } = new();
    public ObservableCollection<DashboardAlert> Alerts { get; } = new();
    private readonly List<DashboardAlert> _allAlerts = new();

    public bool HasAlerts => Alerts.Count > 0;
    // Görünürlük: ilk açılışta (kategori seçilmeden) hiçbir uyarı listelenmez — yalnız butonlar+sayılar (kullanıcı isteği 2026-07-26).
    public bool ShowSelectPrompt => AlertFilter is null;                       // "Bir kategori seçin" ipucu
    public bool ShowEmptyCategory => AlertFilter is not null && Alerts.Count == 0; // seçili kategori boş

    // Uyarı kategori sayıları (butonlarda gösterilir) — kullanıcı isteği 2026-07-25.
    public int MalzemeCount => _allAlerts.Count(a => a.Kind == AlertKind.LowStock);
    public int BakimCount => _allAlerts.Count(a => a.Kind == AlertKind.Maintenance);
    public int MuayeneCount => _allAlerts.Count(a => a.Kind == AlertKind.Inspection);
    public int YakitCount => _allAlerts.Count(a => a.Kind == AlertKind.Fuel);
    // PAN-01 (ADR-175, PK-L2): 8 kategori HEP görünür — ana ekran, çan ve Uyarılar ekranı hizalı.
    public int EvrakCount => _allAlerts.Count(a => a.Kind == AlertKind.Document);
    public int IsEmriCount => _allAlerts.Count(a => a.Kind == AlertKind.WorkOrder);
    public int TalepCount => _allAlerts.Count(a => a.Kind == AlertKind.Request);
    public int DuyuruCount => _allAlerts.Count(a => a.Kind == AlertKind.Announcement);
    public int KiralikCount => _allAlerts.Count(a => a.Kind == AlertKind.Rental);   // 2026-10-10

    // PAN-01 (PK-L1): Bugünün Takvimi + Aktif Duyurular şeritleri (yetki yoksa GİZLİ — summary null verir).
    public ObservableCollection<DashboardCalendarRow> TodayCalendar { get; } = new();
    public ObservableCollection<DashboardAnnouncementRow> ActiveAnnouncements { get; } = new();
    [ObservableProperty] private bool _showTodayCalendar;
    [ObservableProperty] private bool _showAnnouncements;
    public bool TodayCalendarEmpty => ShowTodayCalendar && TodayCalendar.Count == 0;

    /// <summary>Etkin uyarı filtresi: "material"|"maintenance"|"inspection"|"fuel"|null(=Tümü).</summary>
    [ObservableProperty] private string? _alertFilter;
    partial void OnAlertFilterChanged(string? value) => ApplyAlertFilter();

    /// <summary>Kategori butonuna tıkla → yalnız o kategori (tekrar tıkla → Tümü).</summary>
    [RelayCommand]
    private void SelectAlertCategory(string? kind) => AlertFilter = (AlertFilter == kind) ? null : kind;

    private void ApplyAlertFilter()
    {
        Alerts.Clear();
        // Kategori seçili DEĞİLSE hiçbir uyarı gösterilmez (yalnız butonlar). Seçiliyse yalnız o kategori.
        var kind = AlertFilter switch
        {
            "material" => AlertKind.LowStock, "maintenance" => AlertKind.Maintenance,
            "inspection" => AlertKind.Inspection, "fuel" => AlertKind.Fuel,
            "document" => AlertKind.Document, "work_order" => AlertKind.WorkOrder,       // PAN-01
            "request" => AlertKind.Request, "announcement" => AlertKind.Announcement,    // PAN-01
            "rental" => AlertKind.Rental,    // 2026-10-10
            _ => (AlertKind?)null,
        };
        if (kind is { } k)
            foreach (var a in _allAlerts) if (a.Kind == k) Alerts.Add(a);
        OnPropertyChanged(nameof(HasAlerts));
        OnPropertyChanged(nameof(ShowSelectPrompt));
        OnPropertyChanged(nameof(ShowEmptyCategory));
    }

    /// <summary>Yükleme/hata/boş durumları (minimum durum modeli; iş mantığı değiştirilmedi).</summary>
    public bool IsLoading { get; }
    public string? LoadError { get; }
    public bool HasError => LoadError is not null;
    public bool IsLoaded => !IsLoading && !HasError;

    // ── Güncelleme (Ana Ekran "Güncelle" + % ilerleme) ──
    [ObservableProperty] private string _currentVersion = "—";
    [ObservableProperty] private string? _updateMessage;
    /// <summary>⭐ GNC-02: istemci sürümü sunucunun desteklediği asgarinin ALTINDA mı.
    /// Engellemez — yalnız görünür kılar; kullanıcı uzakta ve kilitlenmemeli.</summary>
    [ObservableProperty] private bool _desteklenmeyenSurum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _updateAvailable;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateError))]
    private string? _updateErrorDetail;
    public bool HasUpdateError => !string.IsNullOrEmpty(UpdateErrorDetail);
    public bool CanApplyUpdate => UpdateAvailable && !IsUpdating;
    private DepoWise.Application.Update.UpdatePackage? _latestPackage;

    /// <summary>Bu makinenin adı — ana ekranda gösterilir.</summary>
    public string MachineName => Environment.MachineName;

    /// <summary>Ana ekranda gösterilen şube = MAKİNENİN (admin'in web'den atadığı) şubesi. Çalışma şubesi
    /// (login'de seçilen) makine şubesinden farklıysa parantez içinde belirtilir. Makine şubesi yoksa
    /// (yalnız süper admin senaryosu) çalışma şubesine düşer.</summary>
    public string BranchName
    {
        get
        {
            var machine = DesktopServices.MachineBranchName;
            var working = DesktopServices.CurrentBranchName;
            if (string.IsNullOrEmpty(machine))
                return string.IsNullOrEmpty(working) ? "Tüm / Belirsiz" : working!;
            if (!DesktopServices.CurrentAllBranches && !string.IsNullOrEmpty(working) && working != machine)
                return $"{machine}  (çalışma: {working})";
            return machine!;
        }
    }
    public bool HasBranch => !string.IsNullOrEmpty(DesktopServices.MachineBranchName) || !string.IsNullOrEmpty(DesktopServices.CurrentBranchName);

    /// <summary>Otomatik güncelleme açık/kapalı (app_settings). Kapalıysa ShellViewModel 10 dk'lık oto-uyarıyı atlar.</summary>
    public const string AutoUpdateKey = "auto_update_enabled";
    [ObservableProperty] private bool _autoUpdateEnabled = true;
    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        try { DesktopServices.Settings.Set(_companyId, AutoUpdateKey, value ? "1" : "0"); } catch { }
    }

    private readonly string _companyId;
    private readonly SessionContext _session;

    /// <summary>⭐ M-65 (AR-GE 2026-10-25, F-130 · tasarım belgesi 08 §4 "Dashboard"): en sık işlemlere tek tıkla giriş.
    /// Düğme yalnız o ekran kullanıcının MENÜSÜNDE görünüyorsa çıkar — yetki/platform/şube kapısı menüyle aynı (ayrı kural yazılmaz).</summary>
    public static readonly IReadOnlyList<HizliIslem> HizliIslemKatalogu = new[]
    {
        new HizliIslem("Giriş-Çıkış", "stock"),
        new HizliIslem("Zimmet", "assignments"),
        new HizliIslem("Stok Sayımı", "stock:count"),
    };

    /// <summary>Katalogdan, menüde görünen ekranlara ait olanlar (katalog sırasıyla).</summary>
    public static IReadOnlyList<HizliIslem> HizliIslemSec(IEnumerable<string> menudekiEkranlar)
    {
        var menu = new HashSet<string>(menudekiEkranlar, StringComparer.Ordinal);
        return HizliIslemKatalogu.Where(h => menu.Contains(h.NavKey)).ToList();
    }

    public IReadOnlyList<HizliIslem> HizliIslemler { get; } = HizliIslemSec(
        ShellViewModel.Current?.Groups.SelectMany(g => g.Children).Select(c => c.Key) ?? Enumerable.Empty<string>());
    public bool HizliIslemVar => HizliIslemler.Count > 0;

    // ⭐ iOS tasarım (2026-10-28): "Toplam Araç" kartının durum dağılımı (Aktif / Bakımda / Arızalı / Pasif).
    public int AracToplam { get; private set; }
    public int AracAktif { get; private set; }
    public int AracBakimda { get; private set; }
    public int AracArizali { get; private set; }
    public int AracPasif { get; private set; }
    public bool AracPasifVar => AracPasif > 0;

    // ⭐ iOS tasarım madde 7 (2026-10-29): "Son 6 Ay Yakıt" — Yakıt Özeti'nin çubuk dili (ay · litre · en yoğun aya göre çubuk).
    // Veri Yakıt ekranının AYNI servisinden (FuelService.MonthlySummary: yetki + şube kapsamı orada); yakıt yetkisi yoksa ya da
    // 6 ayda hiç dağıtım yoksa kart HİÇ görünmez (boş grafik gürültüdür).
    public ObservableCollection<YakitAyCubugu> YakitAylari { get; } = new();
    public bool YakitGrafigiVar => YakitAylari.Count > 0;

    private void YakitGrafigiYukle(SessionContext session)
    {
        YakitAylari.Clear();
        try
        {
            if (!AccessControl.Can(session, "fuel", PermissionAction.View)) return;
            var aylar = DesktopServices.Fuel.MonthlySummary(session, 6).Months;   // en yeni ay üstte
            if (aylar.All(a => a.Liters <= 0)) return;
            var enYuksek = aylar.Max(a => a.Liters);
            foreach (var a in aylar)
                YakitAylari.Add(new YakitAyCubugu(a.IsCurrentMonth ? a.MonthName + " (bu ay)" : a.MonthName + " " + a.YearText,
                    a.LitersText, DepoWise.Infrastructure.Operations.FuelMonthSummary.Oran(a.Liters, enYuksek),
                    a.CountText + " · günlük ort. " + a.DailyAverageText));
        }
        catch { YakitAylari.Clear(); }   // grafik ek bilgidir: hata ana ekranı düşürmez
        finally { OnPropertyChanged(nameof(YakitGrafigiVar)); }
    }

    public DashboardViewModel(SessionContext session)
    {
        _companyId = session.CompanyId;
        _session = session;
        try { CurrentVersion = DesktopServices.Update.CurrentVersion(); } catch { }
        try { _autoUpdateEnabled = DesktopServices.Settings.Get(_companyId, AutoUpdateKey) != "0"; } catch { }
        try
        {
            var s = DesktopServices.Dashboard.GetSummary(session);
            // Yalnız ilk kart vurgulu (Primary=true); diğerleri nötr koyu yüzey.
            // ⭐ iOS tasarım (2026-10-28): "Toplam Araç" ayrı geniş kartta (durum dağılım çubuğuyla); diğerleri renkli simgeli kartlar.
            AracToplam = s.VehicleCount;
            var d = s.VehicleStatuses ?? new VehicleStatusCounts(s.VehicleCount, 0, 0, 0);
            (AracAktif, AracBakimda, AracArizali, AracPasif) = (d.Active, d.Maintenance, d.Faulty, d.Passive);
            Cards.Add(new KpiCard(s.MaterialCount.ToString(), "Malzeme Çeşidi", "neutral", Primary: false, NavKey: "materials", IconKey: "IconMaterials",
                Tile: "#A2845E"));
            Cards.Add(new KpiCard(s.LowStockCount.ToString(), "Düşük Stok", "warning", Primary: false, NavKey: "materials", IconKey: "IconWarning",
                Tile: "#FF9F0A", Caption: s.LowStockCount > 0 ? "Asgari miktarın altında" : "Her şey yeterli", CaptionWarn: s.LowStockCount > 0));
            Cards.Add(new KpiCard(s.PendingRequestCount.ToString(), "Bekleyen Talep", "neutral", Primary: false, NavKey: "requests:approve", IconKey: "IconRequests",
                Tile: "#4F5BD5", Caption: s.PendingRequestCount > 0 ? "Onay bekliyor" : null));
            Cards.Add(new KpiCard(s.PersonnelCount.ToString(), "Aktif Personel", "success", Primary: false, NavKey: null, IconKey: "IconUsers",
                Tile: "#30D158"));
            // PAN-01 (PK-L1): yeni özet kartları — YALNIZ kaynak yetkisi olana (null = yetki yok → kart yok).
            if (s.OpenWorkOrderCount is { } wo)
                Cards.Add(new KpiCard(wo.ToString(), "Açık İş Emri",
                    (s.OverdueWorkOrderCount ?? 0) > 0 ? "warning" : "neutral", Primary: false,
                    NavKey: "work_orders", IconKey: "IconDailyActivity", Tile: "#FF453A",
                    Caption: (s.OverdueWorkOrderCount ?? 0) > 0 ? $"{s.OverdueWorkOrderCount} tanesi gecikmiş" : null,
                    CaptionWarn: (s.OverdueWorkOrderCount ?? 0) > 0));
            if (s.OpenPurchaseOrderCount is { } po)
                Cards.Add(new KpiCard(po.ToString(), "Açık Sipariş", "neutral", Primary: false,
                    NavKey: "purchasing", IconKey: "IconMaterials", Tile: "#32ADE6", Caption: po > 0 ? "Teslim bekleniyor" : null));
            // PAN-01: şeritler.
            ShowTodayCalendar = s.TodayCalendar is not null;
            foreach (var t in s.TodayCalendar ?? (IReadOnlyList<DashboardCalendarRow>)Array.Empty<DashboardCalendarRow>())
                TodayCalendar.Add(t);
            foreach (var a in s.ActiveAnnouncements ?? (IReadOnlyList<DashboardAnnouncementRow>)Array.Empty<DashboardAnnouncementRow>())
                ActiveAnnouncements.Add(a);
            ShowAnnouncements = ActiveAnnouncements.Count > 0;
            OnPropertyChanged(nameof(TodayCalendarEmpty));
            YakitGrafigiYukle(session);   // iOS madde 7
            foreach (var a in s.Alerts) if (!a.Read) _allAlerts.Add(a); // #18: okunmuşları ana ekranda gösterme
            ApplyAlertFilter();
        }
        catch (Exception ex)
        {
            LoadError = "Özet verileri yüklenemedi: " + ex.Message;
        }
        IsLoading = false;
        _ = CheckUpdate(); // açılışta güncelleme var mı otomatik kontrol → uyarı + buton

        // Periyodik otomatik kontrol: sunucuya yeni paket yüklenince (uygulama açıkken) uyarı KENDİLİĞİNDEN çıkar.
        _updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _updateTimer.Tick += (_, _) => { if (!IsUpdating) _ = CheckUpdate(); };
        _updateTimer.Start();
    }

    private readonly Avalonia.Threading.DispatcherTimer _updateTimer;

    /// <summary>
    /// ⭐ MAS-02 (denetim 2026-08-26) — SAYFA KAPANINCA ZAMANLAYICI DURUR.
    ///
    /// Bu ekran 60 saniyede bir güncelleme sunucusuna istek atan bir zamanlayıcı başlatır. Zamanlayıcı
    /// durdurulmazsa, kullanıcı başka ekrana geçtiğinde bile çalışmaya devam eder ve kendi işleyicisi
    /// üzerinden bu nesneyi canlı tutar. Ana Ekran'a her dönüşte yeni bir kopya oluştuğu için
    /// zamanlayıcılar birikir → dakikada N ağ isteği + sürekli büyüyen bellek.
    ///
    /// Kabuk (ShellViewModel) açık sayfa değişince bunu çağırır. Birden çok kez çağrılması güvenlidir.
    /// </summary>
    public void Dispose()
    {
        _updateTimer.Stop();
    }

    /// <summary>KPI kartına tıklayınca ilgili ekrana git (köprü). NavKey boşsa hedef ekran henüz yok → işlem yok.</summary>
    [RelayCommand]
    private void Open(string? navKey)
    {
        if (string.IsNullOrEmpty(navKey)) return;
        ShellViewModel.Current?.NavigateCommand.Execute(navKey);
    }

    /// <summary>Uyarıya tıkla → ilgili ekran + ilgili kaydın detayı/işlemi otomatik açılır.</summary>
    [RelayCommand]
    private void OpenAlert(DashboardAlert? alert)
    {
        if (alert is null) return;
        ShellViewModel.Current?.NavigateTo(alert.NavigateKey, alert.EntityId);
    }

    /// <summary>#18 — Uyarıyı okundu işaretle → ana ekrandan kaldır (ilgili modül ekranında kalır).</summary>
    [RelayCommand]
    private void MarkAlertRead(DashboardAlert? alert)
    {
        if (alert is null) return;
        try { DesktopServices.Dashboard.MarkAlertRead(_session, alert.Key, alert.Signature); } catch { }
        _allAlerts.Remove(alert);   // sayaç da düşsün (buton etiketi güncellensin)
        Alerts.Remove(alert);
        OnPropertyChanged(nameof(HasAlerts));
        OnPropertyChanged(nameof(ShowEmptyCategory));
        NotifyAlertCounts();
    }

    private void NotifyAlertCounts()
    {
        // 2026-10-10: Evrak/İş Emri/Talep/Duyuru sayıları da yenilenir (eskiden yalnız ilk 4 bildiriliyordu) + Kiralık.
        foreach (var n in new[] { nameof(MalzemeCount), nameof(BakimCount), nameof(MuayeneCount), nameof(YakitCount),
                                  nameof(EvrakCount), nameof(IsEmriCount), nameof(TalepCount), nameof(DuyuruCount), nameof(KiralikCount) })
            OnPropertyChanged(n);
    }

    /// <summary>Kurulum aracının uygulama klasörüne yazdığı serverurl.txt (varsa). Bağlantı ayarı otomatik gelsin diye.</summary>
    private static string? ReadInstalledServerUrl()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (System.IO.File.Exists(path))
            {
                var v = System.IO.File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Güncelleme kontrolü: sunucu (API) tanımlıysa `/api/releases/latest`'ten, değilse yerel app_releases'ten
    /// en son sürümü alıp mevcutla karşılaştırır.</summary>
    [RelayCommand]
    private async Task CheckUpdate()
    {
        try
        {
            // Sunucu adresi: DB ayarı YOKSA kurulum aracının yazdığı serverurl.txt'ten okunur (elle ayar gerekmez).
            var serverUrl = DesktopServices.Settings.Get(_companyId, SettingKeys.UpdateServerUrl) ?? ReadInstalledServerUrl();
            var latest = !string.IsNullOrWhiteSpace(serverUrl)
                ? await DesktopServices.UpdateApi.GetLatestAsync(serverUrl!) ?? DesktopServices.Releases.Latest()
                : DesktopServices.Releases.Latest();
            _latestPackage = latest;
            var res = DesktopServices.Update.Check(latest);
            UpdateAvailable = res.UpdateAvailable;
            UpdateMessage = res.UpdateAvailable
                ? $"Yeni sürüm mevcut: {res.LatestVersion} (mevcut {res.CurrentVersion})"
                  + (res.SignedWarning ? " — UYARI: paket imzası doğrulanamadı, kurulmayacak." : "")
                : $"Uygulama güncel (sürüm {res.CurrentVersion}).";
        }
        catch (Exception ex) { UpdateMessage = "Güncelleme kontrolü başarısız: " + ex.Message; }
    }

    /// <summary>Güncellemeyi indir + kur (yüzde ana ekranda). DB'ye DOKUNULMAZ (yalnız uygulama dizini);
    /// bozuk/checksum'suz paket kurulmaz, hata olursa eski sürüme rollback.</summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_latestPackage is null || !UpdateAvailable) { UpdateMessage = "Önce güncelleme kontrol edin."; return; }
        if (string.IsNullOrWhiteSpace(_latestPackage.DownloadUrl))
        { UpdateMessage = "Paket indirme adresi tanımlı değil (sunucuya yüklenmemiş)."; return; }
        if (!await ConfirmService.AskAsync(
                $"Sürüm {_latestPackage.Version} indirilip kurulsun mu?\nVeritabanınıza dokunulmaz; hata olursa eski sürüme dönülür.",
                "Güncellemeyi Yükle")) return;

        IsUpdating = true; UpdateProgress = 0; UpdateErrorDetail = null;
        try
        {
            var pkg = _latestPackage;
            UpdateMessage = "İndiriliyor…";
            int dlPct = 0;
            var bytes = await DesktopServices.UpdateDownload.DownloadAsync(
                pkg.DownloadUrl!,
                p => { dlPct = p; UpdateProgress = p * 60 / 100; },              // indirme: 0–60
                speedBytesPerSec: bps => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    UpdateMessage = bps > 0 ? $"İndiriliyor… %{dlPct} • {FormatSpeed(bps)}" : "Kuruluyor…"));
            UpdateMessage = "Kuruluyor…";
            UpdateProgress = 100;
            // Yeniden başlatma öncesi bilgilendirme — Tamam'a basınca kapanıp yeniden açılır.
            await ConfirmService.InfoAsync(
                "Güncelleme indirildi. Uygulamanız yeniden başlatılacaktır, lütfen bekleyiniz…",
                "Yeniden Başlatılıyor", "Tamam");
            UpdateMessage = "Yeniden başlatılıyor…";
            // GERÇEK kurulum: dosyaları kurulum dizinine kopyalar + sürümü yazar + uygulamayı yeniden açar.
            DepoWise.Desktop.UpdateInstaller.InstallAndRestart(bytes, pkg.Version, pkg.ChecksumSha256, pkg.Signature);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            UpdateMessage = "Güncelleme başarısız: " + ex.Message;
            // Hatanın ne olduğu + detayları (iç hata + güncelleme log kuyruğu) ekranda gösterilir.
            var detail = ex.InnerException is { } inner ? $"{ex.Message}\nAyrıntı: {inner.Message}" : ex.Message;
            string log = "";
            try { log = DesktopServices.Update.ReadLogTail(); } catch { }
            UpdateErrorDetail = string.IsNullOrWhiteSpace(log) ? detail : $"{detail}\n\n— Güncelleme günlüğü —\n{log}";
        }
        finally { IsUpdating = false; }
    }
}

// (yardımcı) İndirme hızını okunur biçime çevirir.
public sealed partial class DashboardViewModel
{
    private static string FormatSpeed(double bytesPerSec)
        => bytesPerSec >= 1024 * 1024
            ? $"{bytesPerSec / 1024 / 1024:0.0} MB/sn"
            : $"{bytesPerSec / 1024:0} KB/sn";
}

/// <summary>Kind: accent|neutral|warning|success|danger (durum tonu). Primary: tek vurgulu kart. NavKey: tıklayınca gidilecek ekran.</summary>
/// <summary>Ana ekran özet kartı. <c>IconKey</c> M6 ikon sözlüğündeki anahtardır
/// (Themes/Icons.axaml); bulunamazsa kart ikonsuz çizilir — daha önce BEŞ kartın hepsi aynı
/// kutu ikonunu taşıyordu ve kartlar birbirinden ayırt edilemiyordu.</summary>
/// <summary>⭐ M-65: ana ekrandaki hızlı işlem düğmesi.</summary>
public sealed record HizliIslem(string Baslik, string NavKey)
{
    // ⭐ iOS tasarım (2026-10-28): kutucuk açıklaması, renkli simge kutusu ve simge — ekran anahtarına göre.
    public string Aciklama => NavKey switch
    {
        "stock" => "Stok girişi ya da çıkışı kaydet",
        "assignments" => "Personele teslim ya da iade",
        "stock:count" => "Sayım başlat ya da taslağa dön",
        _ => "",
    };
    public Avalonia.Media.IBrush TileBrush => Avalonia.Media.Brush.Parse(NavKey switch
    {
        "stock" => "#0A84FF", "assignments" => "#5E5CE6", "stock:count" => "#30D158", _ => "#8E8E93",
    });
    public Avalonia.Media.Geometry? Icon => DesktopIcons.ForKpi(NavKey switch
    {
        "stock" => "IconMovements", "assignments" => "IconAssignments", "stock:count" => "IconCount", _ => "IconList",
    });
}

public sealed record KpiCard(string Value, string Label, string Kind, bool Primary, string? NavKey = null,
    string? IconKey = null, string Tile = "#8E8E93", string? Caption = null, bool CaptionWarn = false)
{
    public Avalonia.Media.Geometry? Icon => DesktopIcons.ForKpi(IconKey);
    public bool HasIcon => Icon is not null;
    /// <summary>iOS tasarım: renkli simge kutusunun zemini (beyaz simge).</summary>
    public Avalonia.Media.IBrush TileBrush => Avalonia.Media.Brush.Parse(Tile);
    public bool HasCaption => !string.IsNullOrEmpty(Caption);
}

/// <summary>⭐ iOS madde 7: ana ekran yakıt grafiğinin bir ayı — Oran 0–100 (en yoğun aya göre; dağıtım varsa en az 4, görünsün).</summary>
public sealed record YakitAyCubugu(string Ay, string Litre, double Oran, string Ayrinti);
