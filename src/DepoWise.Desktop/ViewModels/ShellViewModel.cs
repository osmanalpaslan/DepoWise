using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;

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
    /// <summary>AKTİF FİRMA — oturumun bağlı olduğu firmanın adı. Üst barda DAİMA görünür: süper admin
    /// birden çok firma yönetebildiği için "hangi firmaya kayıt açıyorum?" sorusu ekranda cevaplı olmalı.
    /// Masaüstünde firma GİRİŞTE seçilir (yerel veri o firmaya göre eşitlenir); değiştirmek için çıkış/giriş.</summary>
    public string ActiveCompanyName { get; }
    /// <summary>Süper adminde firmanın yanında rol rozeti gösterilir.</summary>
    public string ActiveCompanyTip { get; }

    /// <summary>ÇALIŞMA ŞUBESİ — girişte seçilen şube ya da "Tüm Şubeler". Üst barda görünür.
    /// "Tüm Şubeler" modunda şube bazlı ekranlarda işlem YAPILAMAZ (bkz. BranchGuard).</summary>
    public string ActiveBranchName { get; }
    /// <summary>"Tüm Şubeler" modunda rozet uyarı rengine döner (işlem yapılamayacağı ekranda belli olsun).</summary>
    public bool IsAllBranches { get; }
    public string ActiveBranchTip { get; }
    public string Initial { get; }
    public string Welcome { get; }
    public string BuildStamp { get; } = BuildInfo();
    /// <summary>Kurulu uygulama sürümü (üst barda build bilgisinin altında gösterilir).</summary>
    public string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        try { return "Sürüm " + DesktopServices.Update.CurrentVersion(); } catch { return "Sürüm —"; }
    }
    /// <summary>Ekran Bilgisi butonları yalnız Süper Admin'e (veya geliştirici modunda) görünür.</summary>
    public bool IsSuperAdmin => _session.IsSuperAdmin || DeveloperMode.IsActive;
    [ObservableProperty] private IReadOnlyList<NavGroupVm> _groups = System.Array.Empty<NavGroupVm>();
    /// <summary>SEC — menünün en üst seviyesi (üst grup ya da doğrudan üst menü). Groups DÜZ kalır:
    /// ikon rayı ve mevcut davranışlar ondan beslenir, dokunulmadı.</summary>
    [ObservableProperty] private IReadOnlyList<NavSectionVm> _sections = System.Array.Empty<NavSectionVm>();

    // ── Menü arama: kutuya yazınca eşleşen ekranlar düz liste olarak altında çıkar; tıklayınca açılır ──
    [ObservableProperty] private string _menuSearch = "";
    public System.Collections.ObjectModel.ObservableCollection<MenuSearchItem> MenuSearchResults { get; } = new();
    public bool IsSearchingMenu => !string.IsNullOrWhiteSpace(MenuSearch);

    partial void OnMenuSearchChanged(string value)
    {
        MenuSearchResults.Clear();
        OnPropertyChanged(nameof(IsSearchingMenu));
        var q = (value ?? "").Trim();
        if (q.Length == 0) return;
        var ci = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        bool Has(string s) => ci.CompareInfo.IndexOf(s ?? "", q,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
        foreach (var g in Groups)
            foreach (var c in g.Children)
                if (Has(c.Title) || Has(g.Title))
                    MenuSearchResults.Add(new MenuSearchItem($"{g.Title} › {c.Title}", c.Key));
    }

    [ObservableProperty] private ViewModelBase? _currentPage;

    /// <summary>
    /// ⭐ MAS-02 (denetim 2026-08-26) — AÇIK EKRAN DEĞİŞİNCE ESKİSİ SERBEST BIRAKILIR.
    ///
    /// <b>Bulunan durum:</b> her gezinmede yeni bir sayfa ViewModel'i oluşuyor, eskisi yalnız
    /// referanstan düşürülüyordu. <c>DashboardViewModel</c> 60 saniyelik bir <c>DispatcherTimer</c>
    /// başlatır ve onu HİÇBİR yerde durdurmuyordu → çalışan zamanlayıcı kendi işleyicisini (dolayısıyla
    /// ViewModel'i) canlı tutar. Kullanıcı "Ana Ekran ↔ başka ekran" arasında N kez gidip geldiğinde
    /// N zamanlayıcı birikir ve her biri <b>dakikada bir GÜNCELLEME SUNUCUSUNA istek</b> atar
    /// (<c>DashboardViewModel.CheckUpdate</c>). Bellek de sürekli büyür.
    ///
    /// MAS-01 ile aynı sınıftan bir hatadır; oradaki ders burada genel bir kurala dönüştürüldü:
    /// <b>kaynak tutan her sayfa <see cref="IDisposable"/> uygular ve kabuk onu bırakır.</b>
    /// Bugün yalnız Dashboard etkilenir (tek zamanlayıcı orada); diğer sayfalar IDisposable
    /// olmadığı için davranışları DEĞİŞMEZ.
    /// </summary>
    partial void OnCurrentPageChanging(ViewModelBase? value)
    {
        if (!ReferenceEquals(_currentPage, value)) (_currentPage as IDisposable)?.Dispose();
    }
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _currentContext = "";
    [ObservableProperty] private string _activeKey = "dashboard";

    // ── LOG-01 (kullanıcı isteği 2026-08-27) — EKRANA ÖZEL KAYIT GEÇMİŞİ ─────────────────────

    /// <summary>Aktif ekranın YETKİ MODÜLÜ. Nav anahtarı ("stock:entry") ile modül ("stock") aynı
    /// şey DEĞİLDİR; katalogdan çözülür ki eşleme uydurulmasın. Bulunamazsa null.</summary>
    private string? AktifModul
    {
        get
        {
            var temel = BaseKey(ActiveKey ?? "");
            foreach (var sc in AppScreens.All)
                if (string.Equals(sc.DesktopNavKey, ActiveKey, StringComparison.Ordinal)
                 || string.Equals(sc.DesktopNavKey, temel, StringComparison.Ordinal)) return sc.ModuleKey;
            return null;
        }
    }

    /// <summary>Ekran log düğmesi görünsün mü. ÜÇ koşul: yetki (btn-screen-log) · ekranın modülünde
    /// okuma izni · o modül için tanımlı log eşlemesi. Asıl kapı serviste (AuditLogService.ForModule);
    /// burası yalnız görünürlük — görünmeyen düğme güvenlik sayılmaz.</summary>
    public bool CanShowScreenLog
    {
        get
        {
            var m = AktifModul;
            return m is not null
                && ScreenAuditMap.Has(m)
                && AccessControl.CanUseButton(_session, SpecialButtons.ScreenLog)
                && AccessControl.Can(_session, m, PermissionAction.View);
        }
    }

    /// <summary>Menüde gösterilecek başlık — hangi ekranın geçmişi olduğu açıkça yazar.</summary>
    public string ScreenLogHeader => $"Kayıt Geçmişi — {CurrentTitle}";
    [ObservableProperty] private bool _isNavPanelOpen = true;

    /// <summary>Aktif kabuk — çapraz ekran navigasyonu için (ör. malzeme detayından araç ekranına).</summary>
    public static ShellViewModel? Current { get; private set; }

    // ── Sunucu bağlantı durumu (üst bar) ──
    [ObservableProperty] private string _connectionText = "Bağlanıyor…";
    [ObservableProperty] private Avalonia.Media.IBrush _connectionBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F59E0B"));
    private Avalonia.Threading.DispatcherTimer? _connTimer;
    private static readonly System.Net.Http.HttpClient _pingHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>serverurl.txt / ayar yoksa (ör. kaynaktan çalıştırma) varsayılan bulut adresi.</summary>
    private const string DefaultServerUrl = "https://depowise-erp.fly.dev";

    // ── Eşitle: sunucudan tanımları çek (%'li ilerleme + başarı bildirimi) ──
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private int _syncProgress;

    // Z2 (2026-07-19): son push'ta sunucunun ATLADIĞI/HATA verdiği kayıt varsa üst barda uyarı rozeti göster
    // (sessiz başarısızlığı görünür kıl). Boşsa rozet gizli.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncWarning))]
    [NotifyPropertyChangedFor(nameof(SyncStatusChip))]
    private string _syncWarning = "";
    public bool HasSyncWarning => !string.IsNullOrEmpty(SyncWarning);
    /// <summary>Z5 — üst bardaki tıklanabilir senkron rozeti. Sorun yoksa "✓ Senkron".</summary>
    public string SyncStatusChip => string.IsNullOrEmpty(SyncWarning) ? "✓ Senkron" : SyncWarning;

    /// <summary>Son push sonucuna bakıp uyarı rozetini günceller (arka plan + manuel eşitleme sonrası çağrılır).</summary>
    private void RefreshSyncWarning()
    {
        // Z3: önce KALICI durum. "Poison" (ısrarla gönderilemeyen) varsa rozet, sorun çözülene kadar KALIR —
        // eskiden rozet yalnız SON push'u yansıttığı için sorun sürerken bile kayboluyordu (kullanıcı bulgusu).
        if (BusinessSyncPushService.Poison() is { } p && p.Count > 0)
        {
            SyncWarning = $"⚠ {p.Count} kayıt gönderilemiyor";
            return;
        }
        var r = BusinessSyncPushService.LastPushResult;
        SyncWarning = (r is not null && r.HasProblem)
            ? $"⟳ {r.Skipped} kayıt yeniden denenecek ({BusinessSyncPushService.RetryAttempts()}/5)"
            : "";
    }

    /// <summary>Z5 — SENKRON DURUMU paneli: "her şey gönderildi mi?" sorusunun tek yerden cevabı.
    /// Üst bardaki rozete tıklayınca açılır.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ShowSyncStatus()
    {
        var cid = _session.CompanyId;
        string When(string key)
        {
            try
            {
                var raw = DesktopServices.Settings.Get(cid, key);
                if (long.TryParse(raw, out var ms))
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            }
            catch { }
            return "—";
        }

        var poison = BusinessSyncPushService.Poison();
        var tries = BusinessSyncPushService.RetryAttempts();
        var durum = poison is { } p && p.Count > 0
            ? $"⚠ {p.Count} kayıt GÖNDERİLEMİYOR (otomatik deneme durduruldu)\nSebep: {p.Reason}"
            : tries > 0
                ? $"⟳ Bazı kayıtlar sunucuya uygulanamadı — otomatik yeniden deneniyor ({tries}/5)"
                : "✓ Bekleyen sorun yok — her şey gönderildi";

        var govde =
            $"{durum}\n\n" +
            $"Son başarılı gönderim (push): {When("sync_last_push_ok")}\n" +
            $"Son başarılı çekme (pull):    {When("sync_last_pull_ok")}\n" +
            $"Sunucu bağlantısı: {ConnectionText}\n\n" +
            $"Ayrıntılı kayıt: {(SyncLog.FilePath ?? "sync.log")}";

        // ⭐ B4: kalıcı uyarı varsa TEMİZLEME seçeneği sunulur. Bu kayıtlar için otomatik deneme zaten
        // durdurulmuştur (bir daha gönderilmezler); uyarı sonsuza kadar ekranda kalmamalı.
        if (poison is { Count: > 0 })
        {
            var temizle = await ConfirmService.AskAsync(
                govde + "\n\n" +
                "Bu kayıtlar için otomatik gönderim DURDURULDU; bir daha denenmeyecekler.\n" +
                "Uyarıyı temizlemek yalnız bu mesajı kaldırır — hiçbir veriyi silmez, hiçbir şey göndermez.\n\n" +
                "Uyarı temizlensin mi?",
                "Senkron Durumu", "Uyarıyı Temizle", "Kapat", danger: true);
            if (temizle)
            {
                BusinessSyncPushService.ClearPoison(cid);
                RefreshSyncWarning();
                OnPropertyChanged(nameof(SyncStatusChip));
                OnPropertyChanged(nameof(HasSyncWarning));
            }
            return;
        }

        await ConfirmService.AskAsync(govde, "Senkron Durumu", "Tamam", "Tamam", danger: false);
    }

    /// <summary>Push sonucunu kullanıcıya gösterilecek okunur metne çevirir (manuel Eşitle diyaloğu için).</summary>
    private static string PushResultDetail()
    {
        var r = BusinessSyncPushService.LastPushResult;
        if (r is null || !r.HasProblem) return "";
        var detail = r.Errors.Count > 0
            ? "\n\nAyrıntı:\n• " + string.Join("\n• ", System.Linq.Enumerable.Take(r.Errors, 10))
            : "";
        return $"\n\n⚠ {r.Skipped} kayıt sunucu tarafından uygulanmadı (yetki/doğrulama). " +
               $"Ayrıntılı kayıt: sync.log dosyası.{detail}";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Sync()
    {
        if (IsSyncing) return;
        // Z1: tek eşitleme kapısı — arka plan tick / reset ile aynı anda çalışmaz.
        if (!await SyncGate.EnterAsync()) return;
        IsSyncing = true; SyncProgress = 0;
        try
        {
            var ok = await LookupSyncService.SyncNowAsync(p =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncProgress = p));
            // Tanım çekme + iş verisini gönder (web görünürlüğü) + DİĞER makinelerin verisini geri çek (çok makineli görünürlük).
            await BusinessSyncPushService.PushAsync();
            await BusinessSyncPullService.PullAsync();
            // Kullanıcı bulgusu 2026-07-19: push sessizce başarısız olabiliyordu (büyük veri → zaman aşımı) ve
            // "eşitleme tamamlandı" yanıltıcı görünüyordu. LastPushFailed artık bunu da yansıtır.
            RefreshSyncWarning(); // Z2: sunucunun atladığı kayıt varsa rozeti güncelle
            var hasSkips = BusinessSyncPushService.LastPushResult?.HasProblem == true;
            var allOk = ok && !BusinessSyncPushService.LastPushFailed && !hasSkips;
            // SNK-03: manuel "Eşitle" backoff'a TABİ DEĞİLDİR (bu yol MaybePushBusinessAsync'ten geçmez);
            // başarılı olduysa sunucu geri gelmiş demektir → otomatik tur da normal kadansa dönsün.
            if (allOk) ResetSyncBackoff();
            await ConfirmService.AskAsync(
                (ok && !BusinessSyncPushService.LastPushFailed && !hasSkips) ? "Eşitleme tamamlandı. Tanımlar ve diğer makinelerin verileri güncellendi." :
                BusinessSyncPushService.LastPushFailed ? "Veri gönderimi başarısız oldu (sunucuya ulaşılamadı ya da zaman aşımı). İnternet bağlantısını kontrol edip tekrar deneyin." :
                hasSkips ? "Eşitleme yapıldı ama BAZI KAYITLAR sunucuya uygulanamadı." + PushResultDetail() :
                     "Eşitleme yapılamadı. İnternet bağlantısını kontrol edin (çevrimdışı olabilirsiniz).",
                "Eşitle", "Tamam", "Tamam", danger: !allOk);
        }
        finally { IsSyncing = false; SyncProgress = 0; SyncGate.Exit(); }
    }

    /// <summary>"Yereli Sıfırla ve Yeniden Çek" (kullanıcı isteği 2026-07-19: "yerelimi temizle, sunucudan tam
    /// çek — sıfır PC gibi"). Yalnız YEREL iş verisini (malzeme/araç/stok…) HARD siler (yayılmaz; firma/kullanıcı
    /// korunur), sonra sunucudan TAM çeker. Sunucudaki veri ETKİLENMEZ.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ResetLocalAndResync()
    {
        if (IsSyncing) return;
        var companyId = _session.CompanyId;
        if (!await ConfirmService.AskAsync(
            "Bu makinedeki YEREL malzeme/araç/stok/bakım/yakıt verileri temizlenip SUNUCUDAN yeniden çekilecek " +
            "(sıfır bir PC'den giriyormuş gibi). Sunucudaki ve diğer makinelerdeki veri ETKİLENMEZ.\n\nDevam edilsin mi?",
            "Yerel Veriyi Sıfırla ve Yeniden Çek", "Evet, Yenile", "Vazgeç", danger: true)) return;
        EnsureSyncCursorLoaded();
        // Z1: tek eşitleme kapısı — reset (purge + tam çekme) sürerken arka plan tick AYNI DB'ye giremez.
        if (!await SyncGate.EnterAsync()) return;
        IsSyncing = true; SyncProgress = 0;
        try
        {
            var cid = companyId;
            await System.Threading.Tasks.Task.Run(() => { DepoWise.Desktop.LocalPurgeService.PurgeBusinessData(cid); DepoWise.Desktop.LocalPurgeService.ResetSyncState(cid); });   // 1) yerel iş verisi HARD sil + eşitleme defterini sıfırla
            var ok = await BusinessSyncPullService.PullAsync(0);                                                       // 2) sunucudan TAM çek
            var sv = await BusinessSyncPullService.GetServerVersionAsync();                                            // 3) imleci güncelle
            if (sv is { } v) { _lastServerVersionPulled = v; try { DesktopServices.Settings.Set(cid, "sync_pull_cursor", v.ToString(), _session.UserId); } catch { } }
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => (CurrentPage as IRefreshable)?.RefreshData());
            await ConfirmService.AskAsync(
                ok ? "Yerel veri temizlendi ve sunucudan yeniden çekildi." :
                     "Yerel veri temizlendi ama sunucudan çekilemedi (çevrimdışı olabilirsiniz). İnternet gelince üst bardaki “Eşitle”ye basın.",
                "Yerel Sıfırlama", "Tamam", "Tamam", danger: !ok);
        }
        catch (Exception ex) { await ConfirmService.AskAsync("Hata: " + ex.Message, "Yerel Sıfırlama", "Tamam", "Tamam", danger: true); }
        finally { IsSyncing = false; SyncProgress = 0; SyncGate.Exit(); }
    }

    // İş verisi eşitleme — DUYARLI + DELTA (kullanıcı bulgusu 2026-07-19: 2508 kayıtlı firmada tam snapshot
    // 120sn'yi aşıp zaman aşımına uğruyordu). Her tick (~15 sn): ÜCUZ sürüm kontrolü (max updated_at).
    //   PUSH: yerel sürüm > SUNUCU sürümü ise → YALNIZ sunucudan yeni satırları gönder (delta; server'da olanı
    //         tekrar göndermez → hızlı, zaman aşımı yok).
    //   PULL: sunucu sürümü > en son çektiğimiz ise → sunucudan YALNIZ yeni satırları çek (delta) + açık ekranı yenile.
    // Pull imleci KALICIDIR (SettingsService) → uygulama yeniden açılınca her şeyi baştan çekmez.
    private long _lastServerVersionPulled = -1;
    private bool _syncCursorLoaded;

    private void EnsureSyncCursorLoaded()
    {
        if (_syncCursorLoaded) return;
        _syncCursorLoaded = true;
        try { if (long.TryParse(DesktopServices.Settings.Get(_session.CompanyId, "sync_pull_cursor"), out var v)) _lastServerVersionPulled = v; }
        catch { }
    }

    // ── SNK-03 (2026-08-10): GEÇİCİ hata sonrası üstel geri çekilme (exponential backoff). ──
    // Yalnız İŞ VERİSİ senkron turu (business-version + push + pull) için; uç bazında DEĞİL, tur bazında
    // (üçü aynı sunucuya, aynı SyncGate bloğunda, sıralı gider → birlikte başarısız olurlar).
    // authsig / machines/register / health SNK-02 kadanslarında KALIR (güvenlik ve rozet gecikmemeli).
    // Task.Delay YOK, yeni timer YOK: yalnız "sonraki deneme zamanı" damgası — bekleme sırasında hiçbir
    // kilit tutulmaz. Bellek içi: uygulama kapanınca sıfırlanması DOĞRU davranıştır.
    private const int SyncBackoffBaseSeconds = 15;    // normal kadans = ilk adım
    private const int SyncBackoffMaxSeconds = 300;    // SNK-03 için belirlenen maksimum otomatik senkron
                                                      // backoff süresi (jitter dahil ASLA aşılmaz)
    private static readonly Random _syncJitter = new();
    private int _syncFailStreak;
    private DateTime _syncNextAttemptUtc = DateTime.MinValue;

    /// <summary>GEÇİCİ hata (ağ/zaman aşımı/5xx/429): 15 → 30 → 60 → 120 → 240 → 300 sn (tavan), ±%20 jitter.
    /// Jitter, birden çok makinenin aynı anda toparlanıp sunucuya dalga hâlinde yüklenmesini önler.</summary>
    private void NoteSyncTransientFailure()
    {
        _syncFailStreak++;
        var seconds = Math.Min(SyncBackoffBaseSeconds * Math.Pow(2, _syncFailStreak - 1), SyncBackoffMaxSeconds);
        // Jitter ±%20; tavan jitter'dan SONRA da uygulanır → 300 sn hiçbir durumda aşılmaz
        // (tavan seviyesinde jitter yalnız aşağı yönlü çalışır: 240–300 sn, dalga önleme korunur).
        var jittered = Math.Min(seconds * (0.8 + _syncJitter.NextDouble() * 0.4), SyncBackoffMaxSeconds);
        _syncNextAttemptUtc = DateTime.UtcNow.AddSeconds(jittered);
    }

    /// <summary>Başarılı senkron turu → normal 15 sn kadansa DÖN (en geç bir sonraki tick'te).
    /// Manuel "Eşitle" başarılı olduğunda da çağrılır.</summary>
    private void ResetSyncBackoff()
    {
        _syncFailStreak = 0;
        _syncNextAttemptUtc = DateTime.MinValue;
    }

    // ── SIF-02 (2026-08-25): açık oturumda sıfırlama isteği ────────────────────────────────────
    /// <summary>Sunucu sıfırlama istedi ve BU MAKİNE henüz uygulamadı → gönderim YASAK.</summary>
    private bool _localResetPending;
    /// <summary>Kullanıcı bir kez bilgilendirildi (her turda yeniden pencere açılmasın).</summary>
    private bool _localResetHandled;

    /// <summary>
    /// Sunucuda bekleyen bir "yerel sıfırlama" isteği var mı? Giriş akışındaki
    /// <c>LoginViewModel.HandleCompanyLocalResetAsync</c> ile <b>aynı karşılaştırmayı</b> yapar
    /// (sunucu zamanı &gt; bu makinenin uyguladığı zaman); ikinci bir kural tanımlanmadı.
    ///
    /// Uç erişilemezse <c>null</c> döner → bayrak AÇILMAZ (çevrimdışıyken sessiz kalmak DOĞRU davranıştır:
    /// çevrimdışı makine zaten sunucuya bir şey gönderemez).
    /// </summary>
    private async System.Threading.Tasks.Task RefreshLocalResetFlagAsync(string companyId)
    {
        try
        {
            var serverAt = await ServerAuthClient.GetLocalResetRequestedAtAsync();
            if (serverAt is null) return;
            var localAt = LocalResetService.GetAppliedAt(companyId);
            if (localAt is not null && localAt.Value >= serverAt.Value) return;   // zaten uygulanmış
            _localResetPending = true;
        }
        catch { /* ağ hatası → bayrak açılmaz (fail-safe) */ }
    }

    /// <summary>
    /// Kullanıcıyı BİR KEZ bilgilendirir ve oturumu güvenle kapatır. Sıfırlama, kullanıcı tekrar giriş
    /// yaptığında giriş akışında uygulanır (tek uygulama noktası korunur — burada veri SİLİNMEZ).
    /// Desen, "makine pasife alındı" akışının aynısıdır.
    /// </summary>
    private async System.Threading.Tasks.Task WarnLocalResetOnceAsync()
    {
        if (_localResetHandled) return;
        _localResetHandled = true;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _connTimer?.Stop();
            await ConfirmService.AskAsync(
                "Yöneticiniz bu firmanın verisini sunucuda sıfırladı. Bu bilgisayardaki eski veriler " +
                "sunucuya GÖNDERİLMEDİ (eski kayıtların geri gelmesi böylece önlendi).\n\n" +
                "Oturumunuz kapatılıyor. Tekrar giriş yaptığınızda bu bilgisayardaki veriler temizlenip " +
                "sunucudan yeniden çekilecek.",
                "Veri Sıfırlandı", "Tamam", "Tamam", danger: true);
            DepoWise.Desktop.App.Current?.Logout();
        });
    }

    /// <param name="checkConflicts">
    /// SNK-02: çakışma bildirimi YAVAŞ gruptadır (60 sn). Bu çağrı <see cref="SyncGate"/>'in İÇİNDE
    /// kalmalı (dışarı taşımak gating davranışını değiştirirdi) → dışarı taşımak yerine parametreyle
    /// atlanır. Veri yolu (sürüm kontrolü + push + pull) bu bayraktan ETKİLENMEZ, her tur çalışır.
    /// </param>
    private async System.Threading.Tasks.Task MaybePushBusinessAsync(bool checkConflicts)
    {
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(companyId)) return;
        // SNK-03: geçici hata sonrası geri çekilme. Kontrol SyncGate'ten ÖNCE → bekleme sırasında kapı
        // TUTULMAZ; manuel "Eşitle" (EnterAsync) ve özel push'lar bu koddan hiç geçmediği için serbesttir.
        if (DateTime.UtcNow < _syncNextAttemptUtc) return;
        EnsureSyncCursorLoaded();

        // ⭐ SIF-02 (2026-08-25) — AÇIK OTURUMDA SIFIRLAMA İSTEĞİ.
        //
        // ADR-084 "yerelini sıfırla" isteği bugüne kadar YALNIZ giriş anında kontrol ediliyordu
        // (LoginViewModel.HandleCompanyLocalResetAsync). Program açıkken süper admin sıfırlama isterse
        // bu tur dönmeye ve AZ ÖNCE SIFIRLANAN veriyi sunucuya GERİ GÖNDERMEYE devam ediyordu —
        // sıfırlama fiilen geri alınıyordu. Bugüne kadarki önlem yalnız operasyoneldi
        // ("sıfırlamadan önce tüm programları kapatın").
        //
        // Kontrol SyncGate'ten ve PUSH'tan ÖNCEdir: veri kaybı yönü GÖNDERİM'dir.
        // Çevrimdışıysa uç null döner → bayrak açılmaz → davranış eskisiyle birebir aynı (fail-safe).
        if (checkConflicts && !_localResetPending) await RefreshLocalResetFlagAsync(companyId!);
        if (_localResetPending) { await WarnLocalResetOnceAsync(); return; }
        // Z1: ORTAK kapı. Manuel Eşitle / Yereli Sıfırla / giriş senkronu çalışıyorsa bu tur ATLANIR
        // (eskiden ayrı bayrak kullanıldığı için reset ile tick aynı anda çalışabiliyordu → yarış).
        if (!SyncGate.TryEnter()) return;
        try
        {
            var serverV = await BusinessSyncPullService.GetServerVersionAsync();
            if (serverV is not { } sv)
            {
                // SNK-03: yalnız GEÇİCİ hatada geri çekil. Kalıcı (401/403/4xx/JSON) ya da hiç istek
                // denenmediyse (token/URL yok) kadans bozulmaz — normal hata akışı sürer.
                if (BusinessSyncPullService.LastFailure == SyncFailureKind.Transient) NoteSyncTransientFailure();
                return;                                // çevrimdışı → sessiz
            }
            // PUSH: bu makinenin GÖNDERİLMEMİŞ yerel değişikliklerini gönder. Gönderilecekler PushAsync içinde,
            // bu makinenin KENDİ "son gönderilen watermark"ına göre belirlenir (sunucu global max'ına BAKILMAZ —
            // Z4 kök neden: başka tablo/makinenin zaman damgası artık bu makinenin kaydını atlatamaz).
            await BusinessSyncPushService.PushAsync();
            // SNK-12: ŞUBE/DEPO listesini de tazele. Şubeler iş-senkronunda TAŞINMAZ (web-otoriteli) —
            // eskiden yalnız girişte aynalanıyordu, bu yüzden oturum açıkken web'de açılan yeni depo
            // masaüstünde görünmüyor ve o depoya stok işlemi YAPILAMIYORDU (EnsureLocationOwned reddeder).
            // force:false → BranchMirror.MinInterval ile kısılır; 15 sn'lik senkron kadansı sunucuyu yormaz.
            // Çevrimdışıysa çağrı sessizce döner; yerelde inmiş depolarla çalışma sürer.
            if (companyId is not null) await BranchMirror.RefreshAsync(companyId, force: false);
            // Z2: push sonucunda sunucu kayıt atladıysa uyarı rozetini güncelle (UI thread).
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshSyncWarning);
            // SNK-03: turun GEÇİCİ hata gördüğü an. (Z3'ün "skipped satır" durumu buraya GİRMEZ —
            // sunucu yanıt vermiştir, kendi retry'ı vardır; backoff konusu değildir.)
            var transient = BusinessSyncPushService.LastFailure == SyncFailureKind.Transient;
            // PULL DELTA: sunucuda en son uyguladığımızdan yeni varsa çek + açık ekranı yenile.
            if (sv > _lastServerVersionPulled)
            {
                var ok = await BusinessSyncPullService.PullAsync(sinceVersion: _lastServerVersionPulled > 0 ? _lastServerVersionPulled : 0);
                if (ok)
                {
                    _lastServerVersionPulled = sv;
                    try { DesktopServices.Settings.Set(companyId!, "sync_pull_cursor", sv.ToString(), _session.UserId); } catch { }
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => (CurrentPage as IRefreshable)?.RefreshData());
                }
                else if (BusinessSyncPullService.LastFailure == SyncFailureKind.Transient) transient = true;
            }
            // SNK-03: geçici hata varsa geri çekil; yoksa tur BAŞARILI sayılır → normal 15 sn kadansa dön.
            // Kalıcı hatada (401/403/4xx) mevcut durum korunur: ne ilerletilir ne sıfırlanır.
            if (transient) NoteSyncTransientFailure();
            else if (BusinessSyncPushService.LastFailure == SyncFailureKind.None
                  && BusinessSyncPullService.LastFailure == SyncFailureKind.None) ResetSyncBackoff();
            if (checkConflicts) await WarnConflictsAsync();   // SNK-02: yavaş grup (60 sn)
        }
        catch { }
        finally { SyncGate.Exit(); }
    }

    // Push sonrası: admin ile çakışılan kayıtlar varsa personeli bilgilendir (bir kez), sonra 'görüldü' işaretle.
    private async System.Threading.Tasks.Task WarnConflictsAsync()
    {
        try
        {
            var items = await BusinessSyncPushService.GetUnseenConflictsAsync();
            if (items.Count == 0) return;
            var msg = "Aşağıdaki kayıtlarda admin (web) ile aynı anda değişiklik yapıldı.\n" +
                      "En son düzenleyen geçerli oldu; kayıt tekrar düzenlenene kadar bu geçerlidir:\n\n" +
                      string.Join("\n", items);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                await ConfirmService.AskAsync(msg, "Senkron Çakışması", "Tamam", "Tamam"));
            await BusinessSyncPushService.MarkSeenAsync();
        }
        catch { }
    }

    // ── SNK-02 (2026-08-10): tick kadansı. Timer 15 sn'de kalır; GECİKMEYE DAYANIKLI iki uç
    // (bağlantı rozeti + çakışma bildirimi) her 4. turda çalışır → boşta ~%30 daha az istek.
    // YENİ TIMER YOK, kullanıcı aktivite takibi YOK, veri yolu (push/pull/watermark/LWW) DEĞİŞMEDİ. ──

    /// <summary>Hızlı grup aralığı. ADR-099 kararı: veri "anlık" görünmeli → 15 sn KORUNUR.</summary>
    private const int FastTickSeconds = 15;

    /// <summary>Yavaş grup kaç hızlı turda bir çalışır (4 × 15 sn = 60 sn).</summary>
    private const int SlowEveryNTicks = 4;

    /// <summary>Tick sayacı — yalnız UI thread'de artar, kilit gerekmez.</summary>
    private int _tick;

    // Not: MenuSearchItem record'u dosya sonunda (namespace düzeyinde) tanımlı.
    private void StartConnectionMonitor()
    {
        _ = PingAsync();
        // 15 sn: eşitleme artık her tick'te ÜCUZ sürüm kontrolü yapıp yalnız değişince aktarıyor (duyarlı).
        _connTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(FastTickSeconds) };
        _connTimer.Tick += async (_, _) =>
        {
            // İlk tur (0) yavaş grubu DA çalıştırır → açılıştan sonra rozet/çakışma geç kalmaz.
            bool slow = (_tick++ % SlowEveryNTicks) == 0;

            // Çağrı SIRASI bilinçli olarak DEĞİŞTİRİLMEDİ; yalnız iki uç koşullu hale geldi.
            if (slow) await PingAsync();      // YAVAŞ (60 sn): bağlantı rozeti — veri akışı buna bağlı DEĞİL
            await RegisterMachineAsync();     // HIZLI (15 sn): makine iptali algılama — kullanıcı kararı 2a
            await CheckUserChangedAsync();    // HIZLI (15 sn): yetki/şifre değişikliği algılama
            await MaybePushBusinessAsync(checkConflicts: slow);  // HIZLI: sürüm+push+pull · çakışma bildirimi YAVAŞ
            await MaybeDailyBackupAsync();    // kendi saatlik kısıtı var
        };
        _connTimer.Start();
    }

    private async System.Threading.Tasks.Task PingAsync()
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) { SetConn("#94A3B8", "Yerel (sunucu tanımsız)"); return; }
        try
        {
            SetConn("#3B82F6", "Veri alınıyor…");
            using var resp = await _pingHttp.GetAsync(url!.TrimEnd('/') + "/health");
            SetConn(resp.IsSuccessStatusCode ? "#22C55E" : "#EF4444", resp.IsSuccessStatusCode ? "Bağlı" : "Çevrimdışı");
        }
        catch { SetConn("#EF4444", "Çevrimdışı"); }
    }

    private bool _machineBlockHandled;
    private readonly string? _authSig = ServerAuthClient.AuthSig;
    private bool _userChangeHandled;

    // #2 — Otomatik günlük yedek: bugün alınmış yerel yedek yoksa bir kez alır (VACUUM INTO + 30 gün rotasyon
    // BackupService içinde). Sunucu adresi tanımlıysa buluta yükler. Kontrol saatte bir yapılır (disk taramasını sınırlar).
    private DateTime _lastBackupCheck = DateTime.MinValue;
    private async System.Threading.Tasks.Task MaybeDailyBackupAsync()
    {
        if ((DateTime.UtcNow - _lastBackupCheck).TotalHours < 1) return;
        _lastBackupCheck = DateTime.UtcNow;
        try
        {
            if (!AccessControl.Can(_session, "backup", PermissionAction.Create)) return;
            var today = DateTime.Today;
            var hasToday = DesktopServices.Backup.ListBackups()
                .Any(b => DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedAt).LocalDateTime.Date == today);
            if (hasToday) return;
            var path = DesktopServices.Backup.Backup(); // yerel yedek (retention dahil)
            // Sunucu yedek ucu: ayrı ayarlanmışsa onu kullan, YOKSA API sunucusuna düş (varsayılan olarak
            // günlük yedekler sunucuya gider; sunucu ay sonunda zip'ler + 3 yıl saklar → "Makine Yedekleri" ekranı).
            var url = DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                var b = ResolveServerUrl();
                if (!string.IsNullOrWhiteSpace(b)) url = b.TrimEnd('/') + "/api/backups";
            }
            if (!string.IsNullOrWhiteSpace(url))
            {
                // Token: özel ayar yoksa oturum token'ına düş (yedek ucu Bearer varlığı arar).
                var token = DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerToken);
                if (string.IsNullOrWhiteSpace(token)) token = ServerAuthClient.Token;
                await DesktopServices.BackupUpload.UploadAsync(url!, token, _session.CompanyId, Environment.MachineName, path);
            }
        }
        catch { /* yedek başarısızlığı uygulamayı etkilemez */ }
    }

    /// <summary>Giriş yapılmışken web'de kullanıcının yetki/şifresi değişirse (imza değişir) uyarı + otomatik
    /// çıkış → tekrar giriş gerekir. Yalnız çevrimiçi + JWT varsa çalışır; çevrimdışında tetiklenmez.</summary>
    private async System.Threading.Tasks.Task CheckUserChangedAsync()
    {
        if (_userChangeHandled || _authSig is null || _session.IsSuperAdmin) return;
        var cur = await ServerAuthClient.FetchAuthSigAsync();
        if (cur is null || cur == _authSig) return; // erişilemedi ya da değişmemiş
        _userChangeHandled = true;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _connTimer?.Stop();
            await ConfirmService.AskAsync(
                "Kullanıcınızda güncelleme yapılmıştır (yetki/şifre değişti). Değişikliklerin geçerli olması için " +
                "çıkış yapıp tekrar giriş yapmanız gerekir.",
                "Kullanıcı Güncellendi", "Çıkış Yap", "Çıkış Yap", danger: true);
            DepoWise.Desktop.App.Current?.Logout();
        });
    }

    /// <summary>Bu makineyi buluta kaydeder (heartbeat). Dönen durum 'revoked'/'pending' ise (kota dışı ya da
    /// pasife alınmış) çalışan oturum ANINDA sonlandırılır: uyarı gösterilir + otomatik çıkış yapılır.
    /// Süper admin oturumu etkilenmez.</summary>
    private async System.Threading.Tasks.Task RegisterMachineAsync()
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            // ÇİFT KAYIT DÜZELTMESİ (kullanıcı bulgusu 2026-07-19): makine, oturum firmasına DEĞİL kendi BAĞLI
            // firmasına (MachineCompanyId) kaydedilir. Süper admin farklı bir firmaya (ör. ev firması) geçince
            // heartbeat makineyi İKİNCİ bir firmaya kaydediyordu → "aynı makine birden çok görünüyor". Makine
            // bir firmaya bağlıysa (bilinen), heartbeat DAİMA o firmaya gider; yalnız ilk kurulumda (henüz
            // bağlı değilken) oturum firması kullanılır.
            var companyId = DesktopServices.MachineCompanyId ?? DesktopServices.Session?.CompanyId ?? DesktopServices.DefaultCompanyId;
            // Makine şubesi login şubesinden yazılmaz (admin atar / ilk kurulumda SelfAssignMachineBranch yapar) — göndermiyoruz.
            var json = System.Text.Json.JsonSerializer.Serialize(new { companyId, machineName = Environment.MachineName });
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _pingHttp.PostAsync(url!.TrimEnd('/') + "/api/machines/register", content);
            if (!resp.IsSuccessStatusCode) return;
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            // Makinenin (admin-atanmış) şubesini güncel tut → ana ekran anlık yansır (admin web'den değiştirirse).
            var mBid = root.TryGetProperty("branchId", out var bi) && bi.ValueKind != System.Text.Json.JsonValueKind.Null ? bi.GetString() : null;
            var mBn = root.TryGetProperty("branchName", out var bn) && bn.ValueKind != System.Text.Json.JsonValueKind.Null ? bn.GetString() : null;
            DesktopServices.MachineBranchId = string.IsNullOrWhiteSpace(mBid) ? null : mBid;
            DesktopServices.MachineBranchName = string.IsNullOrWhiteSpace(mBn) ? null : mBn;

            if (_session.IsSuperAdmin || _machineBlockHandled) return;
            if (status is "revoked" or "pending")
            {
                _machineBlockHandled = true;
                var msg = status == "revoked"
                    ? "Bu makine süper admin tarafından PASİFE alındı. Oturumunuz kapatılıyor. Tekrar giriş için makinenin yeniden aktifleştirilmesi gerekir."
                    : "Bu makine firmanın makine kotasını aştığı için ONAY BEKLİYOR. Oturumunuz kapatılıyor. Süper adminin makineyi onaylaması gerekir.";
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    _connTimer?.Stop();
                    await ConfirmService.AskAsync(msg, "Makine Erişimi Kapatıldı", "Tamam", "Tamam", danger: true);
                    DepoWise.Desktop.App.Current?.Logout();
                });
            }
        }
        catch { }
    }

    private void SetConn(string hex, string text)
    {
        ConnectionBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        ConnectionText = text;
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var companyId = DesktopServices.Session?.CompanyId ?? DesktopServices.DefaultCompanyId;
            var s = DesktopServices.Settings.Get(companyId, SettingKeys.UpdateServerUrl);
            if (!string.IsNullOrWhiteSpace(s)) return s;
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (System.IO.File.Exists(path))
            {
                var v = System.IO.File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }
        return DefaultServerUrl; // serverurl.txt/ayar yoksa varsayılan buluta bağlan
    }

    public ShellViewModel(SessionContext session)
    {
        Current = this;
        _session = session;
        AppName = DesktopServices.Branding.AppName;
        CompanyName = DesktopServices.Branding.CompanyName;
        // Aktif firma adı oturumun firmasından okunur (marka adı değil) — yanlış firmaya kayıt açmayı önler.
        string activeCompany;
        try { activeCompany = DesktopServices.Companies.GetName(session.CompanyId); } catch { activeCompany = ""; }
        ActiveCompanyName = string.IsNullOrWhiteSpace(activeCompany) ? session.CompanyId : activeCompany;
        ActiveCompanyTip = session.IsSuperAdmin
            ? $"Aktif firma: {ActiveCompanyName} (Süper Admin)\nKayıtlar bu firmaya yazılır. Firmayı değiştirmek için çıkış yapıp giriş ekranından seçin."
            : $"Aktif firma: {ActiveCompanyName}";
        IsAllBranches = BranchGuard.IsAllBranches(session);
        ActiveBranchName = IsAllBranches ? "Tüm Şubeler" : (DesktopServices.CurrentBranchName ?? "—");
        ActiveBranchTip = IsAllBranches
            ? "Tüm Şubeler modu: tüm şubelerin kayıtlarını GÖREBİLİRSİNİZ ama malzeme/araç/stok gibi şube bazlı ekranlarda İŞLEM YAPAMAZSINIZ.\nİşlem için çıkış yapıp ilgili şubeyi seçerek girin."
            : $"Çalışma şubesi: {ActiveBranchName}\nBu oturumdaki işlemler bu şubeye yazılır.";
        DisplayName = DesktopServices.DisplayName(session.UserId);
        Initial = string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant();
        Welcome = $"Hoş geldiniz, {DisplayName} — {DateTime.Now:dd MMMM yyyy dddd}";
        Groups = BuildGroups(session);
        Sections = BuildSections(session);
        DeveloperMode.Changed += OnDeveloperModeChanged;

        Navigate("dashboard");
        StartConnectionMonitor();
        StartUpdateWatcher();
        _ = RegisterMachineAsync();

        ServerAuthClient.SessionExpiredRaised += OnSessionExpired; // oturum düşünce tekrar giriş
    }

    /// <summary>
    /// ⭐ MAS-01 (denetim 2026-08-26) — KABUĞU SERBEST BIRAK (çıkış/giriş döngüsü).
    ///
    /// <b>Neden gerekli:</b> her girişte YENİ bir <see cref="ShellViewModel"/> oluşturulur, ama eskisi
    /// iki STATİK olaya abone kaldığı (<c>DeveloperMode.Changed</c>, <c>SessionExpiredRaised</c>) ve
    /// güncelleme zamanlayıcısı hiç durdurulmadığı için asla serbest kalmıyordu. Aynı uygulama
    /// oturumunda N kez çıkış→giriş yapılınca dakikada N kez güncelleme kontrolü çalışıyor, yeni sürüm
    /// çıktığında birden çok pencere tetiklenebiliyor ve kapanmış pencerelerin işleyicileri çağrılıyordu.
    ///
    /// Çağıran: <c>App.ShowLogin()</c> — yeni kabuk oluşturulmadan ÖNCE eskisini bırakır.
    /// Birden çok kez çağrılması güvenlidir (idempotent).
    /// </summary>
    public void Release()
    {
        _connTimer?.Stop(); _connTimer = null;
        _updateTimer?.Stop(); _updateTimer = null;
        DeveloperMode.Changed -= OnDeveloperModeChanged;
        ServerAuthClient.SessionExpiredRaised -= OnSessionExpired;
    }

    private bool _sessionExpiredHandled;

    /// <summary>Oturum süresi doldu ve yenilenemedi → kullanıcıya bilgi ver, tekrar girişe yönlendir.</summary>
    private void OnSessionExpired()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (_sessionExpiredHandled) return;
            _sessionExpiredHandled = true;
            await ConfirmService.AskAsync(
                "Oturum süreniz doldu (uzun süre çevrimdışı kalınmış olabilir). Lütfen tekrar giriş yapın.",
                "Oturum Süresi Doldu", "Tekrar Giriş", "Tekrar Giriş");
            ServerAuthClient.SessionExpiredRaised -= OnSessionExpired;
            DepoWise.Desktop.App.Current?.Logout();
        });
    }

    // ── Otomatik güncelleme: giriş sonrası + her 10 dk'da bir kontrol; yeni sürüm varsa ONAY sorar.
    // KURALLAR (kullanıcı isteği):
    //  • Aynı anda TEK güncelleme penceresi açılır (birikmez). Pencere açıkken yeni paket çıkarsa
    //    yeni pencere açılmaz — açık pencerenin MESAJI güncellenir.
    //  • "Ertele" = 10 dakika; süre pencerede yazılıdır. İndirilen paket saklanır (erteleyince tekrar inmez).
    //  • Yeniden başlatma onayı ayrı: "Şimdi Yeniden Başlat" / "10 Dakika Ertele". ──
    private Avalonia.Threading.DispatcherTimer? _updateTimer;
    private bool _updateBusy;                              // indir/kur kritik bölümü — tek akış
    private Views.ConfirmWindow? _availableWindow;         // açık "güncelleme mevcut" penceresi (varsa)
    private DepoWise.Application.Update.UpdatePackage? _latestForPrompt; // açık pencere için güncel hedef
    // İndirilmiş/bekleyen paket + erteleme zamanı ORTAK yerde (AutoUpdateService): eşitleme ekranı, bu
    // zamanlayıcı ve MainWindow kapatma-kilidi aynı durumu paylaşır (mükerrer indirme olmaz).
    private const int SnoozeMinutes = AutoUpdateService.SnoozeMinutes;

    private void StartUpdateWatcher()
    {
        _ = CheckForUpdateAndPromptAsync();
        _updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAndPromptAsync();
        _updateTimer.Start();
    }

    private static Avalonia.Controls.Window? MainWin()
        => Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;

    private string AvailableMsg(DepoWise.Application.Update.UpdatePackage latest, string current)
        => $"Yeni sürüm mevcut: {latest.Version} (mevcut {current}).\n\n" +
           "Şimdi indirilip kurulsun mu? Uygulama otomatik kapanıp yeniden açılacak. Veritabanınıza dokunulmaz.\n\n" +
           $"Ertelerseniz {SnoozeMinutes} dakika sonra tekrar hatırlatılır.";

    private async System.Threading.Tasks.Task CheckForUpdateAndPromptAsync()
    {
        try
        {
            // Otomatik güncelleme kapalıysa oto-uyarı atlanır (kullanıcı ana ekrandan elle kurar).
            if (DesktopServices.Settings.Get(_session.CompanyId, DashboardViewModel.AutoUpdateKey) == "0") return;
            var url = ResolveServerUrl();
            if (string.IsNullOrWhiteSpace(url)) return;
            var latest = await DesktopServices.UpdateApi.GetLatestAsync(url!);
            if (latest is null || string.IsNullOrWhiteSpace(latest.DownloadUrl)) return;
            var res = DesktopServices.Update.Check(latest);
            if (!res.UpdateAvailable) return;

            // (D) Pencere zaten açıksa: yeni sürüm geldiyse SADECE mesajı güncelle, yeni pencere AÇMA.
            if (_availableWindow is not null)
            {
                _latestForPrompt = latest;
                _availableWindow.SetMessage(AvailableMsg(latest, res.CurrentVersion));
                return;
            }
            // Erteleme süresi dolmadıysa sessiz geç.
            if (DateTime.UtcNow < AutoUpdateService.SnoozeUntilUtc) return;
            // İndir/kur akışı zaten sürüyorsa tekrar başlatma.
            if (_updateBusy) return;
            _updateBusy = true;

            try
            {
                // Erteleme sonrası: paket zaten indirilmiş ve sürüm değişmemişse doğrudan yeniden-başlat onayına git.
                bool havePending = AutoUpdateService.HasPending && AutoUpdateService.PendingVersion == latest.Version;
                if (!havePending)
                {
                    _latestForPrompt = latest;
                    var win = new Views.ConfirmWindow("Güncelleme Mevcut", AvailableMsg(latest, res.CurrentVersion), "İndir ve Kur", $"{SnoozeMinutes} Dakika Ertele", false);
                    _availableWindow = win;
                    bool install;
                    try { var owner = MainWin(); install = owner is null ? false : await win.ShowDialog<bool>(owner); }
                    finally { _availableWindow = null; }
                    if (!install) { AutoUpdateService.Snooze(); return; }

                    // Kullanıcı beklerken yeni paket gelmiş olabilir → en güncel hedefi kullan.
                    latest = _latestForPrompt ?? latest;
                    SetConn("#3B82F6", "Güncelleme indiriliyor… %0");
                    var dl = await DesktopServices.UpdateDownload.DownloadAsync(latest.DownloadUrl!,
                        p => Avalonia.Threading.Dispatcher.UIThread.Post(() => SetConn("#3B82F6", $"Güncelleme indiriliyor… %{p}")));
                    AutoUpdateService.SetPending(dl, latest.Version, latest.ChecksumSha256);
                    SetConn("#3B82F6", "İndirildi — yeniden başlatma bekleniyor.");
                }

                // (C) Yeniden başlatma onayı: Ertele (10 dk) / Şimdi Yeniden Başlat.
                var restart = await ConfirmService.AskAsync(
                    $"Güncelleme indirildi (sürüm {AutoUpdateService.PendingVersion}). Kurulumun tamamlanması için uygulama yeniden başlatılmalı.\n\n" +
                    $"Şimdi yeniden başlatabilir veya erteleyebilirsiniz. Her erteleme {SnoozeMinutes} dakikadır; süre dolunca tekrar sorulur.",
                    "Güncelleme Hazır — Yeniden Başlat", "Şimdi Yeniden Başlat", $"{SnoozeMinutes} Dakika Ertele");
                if (!restart) { AutoUpdateService.Snooze(); return; }

                SetConn("#3B82F6", "Yeniden başlatılıyor…");
                AutoUpdateService.InstallPendingNow(); // uygulamayı kapat → yardımcı kopyalar + yeniden açar
            }
            finally { _updateBusy = false; }
        }
        catch (Exception ex)
        {
            _updateBusy = false; _availableWindow = null;
            try { await ConfirmService.AskAsync("Güncelleme başarısız: " + ex.Message, "Güncelleme", "Tamam", "Tamam"); } catch { }
        }
    }

    private static IReadOnlyList<NavGroupVm> BuildGroups(SessionContext s)
    {
        // ═══ G2/G6 (2026-08-12): MENÜ ARTIK MERKEZİ KATALOGDAN ÜRETİLİR ═══
        // Eskiden burada 17 grup / 40 bağlantı ELLE yazılıydı ve web'deki NavMenu.razor bunun elle
        // tutulan aynasıydı (sayılar zaten ayrışmıştı: web 46 · masaüstü 40). Artık ikisi de
        // AppScreens'ten türetilir → yeni ekran TEK satırla iki menüye birden gelir.
        // Grup sırası, başlıklar, ikonlar ve bağlantı sırası AppScreens'te BİREBİR korunmuştur.
        // G5: firmanın platform kısıtları (kayıt yoksa katalog varsayılanı → hiçbir ekran kapanmaz).
        var vis = SafeOverrides(s);
        // MNU (2026-08-18): firmanın menü DÜZENİ — ad · üst menü · sıra. Kayıt yoksa MenuLayoutSet.Empty
        // döner ve menü katalogdaki hâliyle BİREBİR aynı çizilir (davranış korunumu).
        var duzen = SafeLayout(s);

        // ⭐ Sıralama/ad/grup çözümlemesi web ile AYNI kodda (MenuLayout.Build) yapılır → iki platform
        // asla ayrışamaz. Süzgeç (isOpen) erişim kararıdır ve BURADA kalır:
        //  · platform: MASAÜSTÜNDE kapatılmış ekran menüde YER ALMAZ (G5),
        //  · yetki: verilmeyen ekran menüde GÖRÜNMEZ (deny-by-default). Alt-sekme anahtarı parent
        //    modüle map'lenir ("maintenance:defs" → "maintenance").
        // Görünür alt bağlantısı kalmayan grup gizlenir — mevcut davranışın aynısı.
        return MenuLayout.Build(ScreenPlatform.Desktop, duzen,
                sc => ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, vis)
                      && ScreenGateAllows(s, sc)
                      && CanSeeChild(s, BaseKey(sc.DesktopNavKey ?? "")))
            .Select(g => new NavGroupVm(g.DesktopIcon, g.Title, GroupModuleKey(g),
                g.Entries.Select(e => new NavLinkVm(e.Label, e.Screen.DesktopNavKey!)).ToList())
                { IconGeometry = DesktopIcons.ForGroup(g.Title) })   // M6: grup ikonu (baslik -> geometri)
            .Where(g => g.Children.Count > 0)
            .ToList();
    }

    /// <summary>
    /// SEC (2026-08-19) — menünün üç seviyeli hâli: ÜST GRUP → ÜST MENÜ → EKRAN.
    ///
    /// <b>BuildGroups DEĞİŞTİRİLMEDİ</b>; bu metot onun ürettiği grupları <see cref="MenuLayout.BuildTree"/>
    /// sırasına göre düğümlere paketler. Üst grup tanımlı değilse her grup kendi düğümü olur →
    /// menü bugünkü hâliyle BİREBİR aynı çizilir. İkon rayı düz <c>Groups</c> listesini kullanmaya
    /// devam eder; bu yüzden ray hiç etkilenmez.
    /// </summary>
    private static IReadOnlyList<NavSectionVm> BuildSections(SessionContext s)
    {
        var gruplar = BuildGroups(s);
        if (gruplar.Count == 0) return System.Array.Empty<NavSectionVm>();

        var haritada = gruplar.ToDictionary(g => g.Title, g => g, StringComparer.Ordinal);
        var vis = SafeOverrides(s);
        var duzen = SafeLayout(s);

        var agac = MenuLayout.BuildTree(ScreenPlatform.Desktop, duzen,
            sc => ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, vis)
                  && ScreenGateAllows(s, sc)
                      && CanSeeChild(s, BaseKey(sc.DesktopNavKey ?? "")));

        var sonuc = new List<NavSectionVm>(agac.Count);
        foreach (var node in agac)
        {
            // Düğümün grupları, BuildGroups'un ürettiği hazır NavGroupVm'lerle eşleştirilir
            // (başlık = görünen ad). Eşleşmeyen olursa sessizce atlanır — menü bozulmaz.
            var kids = node.Groups
                .Select(g => haritada.TryGetValue(g.Title, out var vm) ? vm : null)
                .Where(vm => vm is not null)!
                .Cast<NavGroupVm>()
                .ToList();
            if (kids.Count == 0) continue;
            sonuc.Add(new NavSectionVm(node.Title, node.IsSection, kids)
                { IconGeometry = node.IsSection ? DesktopIcons.ForSection(node.Title) : null });   // M6: üst grup ikonu
        }
        return sonuc;
    }

    /// <summary>İkon rayının kullandığı grup modülü. Kullanıcının oluşturduğu grupta katalog karşılığı
    /// yoktur → grubun ilk ekranının modülü kullanılır (ray yine doğru yere gider).</summary>
    private static string GroupModuleKey(MenuGroupView g)
    {
        foreach (var cg in AppScreens.Groups)
            if (string.Equals(cg.Title, g.Key, StringComparison.Ordinal)) return cg.ModuleKey;
        return g.Entries.Count > 0 ? g.Entries[0].Screen.ModuleKey : AppModules.Dashboard;
    }

    /// <summary>MNU — firmanın menü düzeni. Okuma başarısızsa (çevrimdışı/eski şema) BOŞ küme döner
    /// ve katalog varsayılanı geçerli kalır → menü hiçbir zaman boş kalmaz.</summary>
    private static MenuLayoutSet SafeLayout(SessionContext s)
    {
        try { return DesktopServices.MenuLayout.LayoutFor(s.CompanyId); }
        catch { return MenuLayoutSet.Empty; }
    }

    /// <summary>Menü görünürlüğü. İmport / Export ekranı, içe VEYA dışa aktarım yetkisinden en az biri varsa
    /// görünür (2026-07-26 ayrımı); ekran içinde her bölüm kendi yetkisiyle ayrıca korunur.</summary>
    private static bool CanSeeChild(SessionContext s, string key)
        => key == "import_export"
            ? AccessControl.Can(s, "import_export", PermissionAction.View) || AccessControl.Can(s, "export", PermissionAction.View)
            : AccessControl.CanSeeMenu(s, key);

    /// <summary>
    /// ⭐ SEC-03 (2026-08-25) — EKRAN DÜZEYİ KAPI (sözde-anahtar).
    ///
    /// Bazı ekranların modülü PAYLAŞILIR (ör. Geliştirici Modu → <c>settings</c>) ama ekranın kendisi
    /// daha dardır. Web menüsü bunu <c>WebPermOverride</c> sözde-anahtarlarıyla (<c>@admin</c> /
    /// <c>@super</c> / <c>@superr</c>) uzun süredir uyguluyordu; MASAÜSTÜ bu kuralı hiç görmüyordu →
    /// aynı ekran web'de gizli, masaüstünde açıktı. Kural artık İKİ platformda da AYNI kaynaktan gelir.
    ///
    /// Sözde-anahtarı olmayan ekranlarda davranış DEĞİŞMEZ (modül yetkisi neyse o).
    /// </summary>
    private static bool ScreenGateAllows(SessionContext s, AppScreen sc) => sc.WebPermOverride switch
    {
        "@admin" => AccessControl.IsAdmin(s),
        "@super" => s.IsSuperAdmin,
        "@superr" => s.IsSuperAdmin || s.IsRestrictedSuperAdmin,
        _ => true,
    };

    /// <summary>LOG-01: ekran değişince log düğmesinin görünürlüğü ve başlığı tazelenir.</summary>
    partial void OnActiveKeyChanged(string value)
    {
        OnPropertyChanged(nameof(CanShowScreenLog));
        OnPropertyChanged(nameof(ScreenLogHeader));
    }

    private static string BaseKey(string key)
    {
        var i = key.IndexOf(':');
        return i < 0 ? key : key[..i];
    }

    /// <summary>G5 — firmanın platform kısıtları. Okuma başarısızsa (çevrimdışı/eski şema) null döner
    /// ve katalog varsayılanları geçerli kalır → menü hiçbir zaman boş kalmaz.</summary>
    private static IReadOnlyDictionary<string, ScreenVisibilityOverride>? SafeOverrides(SessionContext s)
    {
        try { return DesktopServices.ScreenVisibility.OverridesFor(s.CompanyId); }
        catch { return null; }
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        // ═══ G5 — MERKEZİ GEZİNME KAPISI (2026-08-12) ═══
        // Menüden gizlemek YETMEZ: Navigate kod içinden de tetiklenebilir (kısayol, uyarı ekranından
        // atlama, grup ikonu). Platform kapalıysa ekran BURADA da açılmaz. Yetki kontrolü ekranların
        // kendi servis çağrılarında ZATEN var; burada yalnız PLATFORM kapısı uygulanır — iki kavram
        // birbirine karıştırılmaz (ERİŞİM = PLATFORM_AKTİF && YETKİ_VAR).
        var screen = AppScreens.ByDesktopNavKey(key);
        if (screen is not null && !ScreenVisibility.IsEnabled(screen, ScreenPlatform.Desktop, SafeOverrides(_session)))
        {
            CurrentTitle = "Ekran kapalı";
            CurrentContext = $"\"{screen.Label}\" bu uygulamada kullanıma kapatılmış. Yöneticinize başvurun.";
            CurrentPage = null;
            ActiveKey = key;
            return;
        }

        ActiveKey = key;
        switch (key)
        {
            case "dashboard":
                CurrentPage = new DashboardViewModel(_session);
                CurrentTitle = "Genel Özet";
                CurrentContext = "Özet istatistikler ve kritik uyarılar";
                break;
            case "alerts":
                CurrentPage = new AlertsViewModel(_session);
                CurrentTitle = "Uyarılar";
                CurrentContext = "Tüm aktif uyarılar (bakım, muayene, stok, yakıt)";
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
            case "stock":
                CurrentPage = new StockEntryViewModel(_session);
                CurrentTitle = "Malzeme Giriş-Çıkış";
                CurrentContext = "Stok giriş / çıkış / transfer";
                break;
            case "stock:movements":
                CurrentPage = new StockMovementsViewModel(_session);
                CurrentTitle = "Stok Hareketleri";
                CurrentContext = "Tüm giriş/çıkış/transfer hareketleri (tarih + arama)";
                break;
            case "stock:count":
                CurrentPage = new StockCountViewModel(_session);
                CurrentTitle = "Stok Sayım";
                CurrentContext = "Sayım ve fark düzeltmesi";
                break;
            // STK-08: geçmişte deposu girilmemiş ("Atanmamış") stoğun kullanıcı tarafından dağıtımı.
            // ÇEVRİMDIŞI çalışır — ekran API'ye gitmez, yerel SQLite üzerinden yazar.
            case "stock:distribute":
                CurrentPage = new StockDistributeViewModel(_session);
                CurrentTitle = "Atanmamış Stok Dağıtımı";
                CurrentContext = "Geçmişte deposu girilmemiş stoğu depolara dağıtın";
                break;
            case "inspection":
                CurrentPage = new InspectionViewModel(_session);
                CurrentTitle = "Muayene / Sigorta";
                CurrentContext = "Araç muayene/sigorta belgeleri ve uyarılar";
                break;
            case "personnel":
                CurrentPage = new PersonnelViewModel(_session);
                CurrentTitle = "Personel";
                CurrentContext = "Personel yönetimi";
                break;
            case "daily_activity":
                CurrentPage = new DailyActivityViewModel(_session);
                CurrentTitle = "Günlük Faaliyet";
                CurrentContext = "Araç hareket / transfer + bakım faaliyetleri";
                break;
            case "vehicles":
            case "vehicles:new":
                CurrentPage = new VehiclesViewModel(_session);
                CurrentTitle = "Araçlar";
                CurrentContext = "Araç kartları, durum ve uyarılar";
                break;
            case "request_ops:board":
                CurrentPage = new RequestOperationsViewModel(_session);
                CurrentTitle = "Talep Operasyonları";
                CurrentContext = "Onaylı taleplerin operasyon süreci (Ana Depo / Satın Alma)";
                break;
            case "material_templates:templates":
                CurrentPage = new MaterialTemplatesViewModel(_session);
                CurrentTitle = "Malzeme Şablonları";
                CurrentContext = "Şablonlar — malzeme formunu otomatik doldurur";
                break;
            case "vehicle_templates:templates":
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
            // PRJ-01 (ADR-164): Projeler — sunucu-otoriteli; yetki branches modülü (PK-C4).
            // EMR-01 (ADR-170): İş Emirleri — tek ekran + güçlü detay; yerel + senkron.
            case "work_orders":
                CurrentPage = new WorkOrdersViewModel(_session);
                CurrentTitle = "İş Emirleri";
                CurrentContext = "İş emri: durum, atamalar, malzeme tüketimi, maliyet, geçmiş";
                break;
            // TKV-01 (ADR-171): Takvim — türetilmiş kaynaklar + el ile plan kayıtları; yerel + senkron.
            case "calendar":
                CurrentPage = new CalendarViewModel(_session);
                CurrentTitle = "Takvim";
                CurrentContext = "Takvim: iş emri planları, muayene/sigorta, evrak geçerlilik, proje, bakım hedefleri + el ile kayıtlar";
                break;
            // STN-01 (ADR-169): Satın Alma — sipariş + mal kabul; yerel + senkron.
            case "purchasing":
                CurrentPage = new PurchasingViewModel(_session);
                CurrentTitle = "Satın Alma";
                CurrentContext = "Talep → Sipariş → Mal Kabul → Stok zinciri";
                break;
            // MLY-01 (ADR-168): Maliyet Merkezleri — tanım + özet; yerel + senkron.
            case "cost_centers":
                CurrentPage = new CostCentersViewModel(_session);
                CurrentTitle = "Maliyet Merkezleri";
                CurrentContext = "Maliyet merkezi tanımları ve merkez bazlı maliyet özeti";
                break;
            // ZMT-01 (ADR-167): Zimmet — kimde ne var + hareket defteri; yerel + senkron.
            case "assignments":
                CurrentPage = new AssignmentsViewModel(_session);
                CurrentTitle = "Zimmet";
                CurrentContext = "Personel zimmetleri: teslim / iade / devir / kayıp";
                break;
            // EKP-01 (ADR-166): Ekipman — araçtan ayrı varlık kartları; yerel + senkron.
            case "equipment":
                CurrentPage = new EquipmentViewModel(_session);
                CurrentTitle = "Ekipman";
                CurrentContext = "Ekipman kartları (jeneratör, kompresör, konteyner...)";
                break;
            // EVR-01 (ADR-165): Evrak/Belgeler — sunucu-otoriteli; yetki files modülü.
            case "documents":
                CurrentPage = new DocumentsViewModel(_session);
                CurrentTitle = "Evrak / Belgeler";
                CurrentContext = "Kayıtlara bağlı belgeler (PDF, Office, görsel)";
                break;
            case "projects":
                CurrentPage = new ProjectsViewModel(_session);
                CurrentTitle = "Projeler";
                CurrentContext = "Proje kartları ve bağlı şantiyeler";
                break;
            case "permissions":
                CurrentPage = new PermissionsViewModel(_session);
                CurrentTitle = "Yetkiler";
                CurrentContext = "Kullanıcı bazlı menü + alan + buton yetkileri";
                break;
            case "companies":
                CurrentPage = new CompaniesViewModel(_session);
                CurrentTitle = "Firma Tanım";
                CurrentContext = "Firma kayıtları (yalnız Süper Admin)";
                break;
            // G4-1 — ÖN MUHASEBE / CARİ. "parties:new" aynı ekranı açar (liste + kart tek ekranda).
            case "parties":
            case "parties:new":
                CurrentPage = new PartiesViewModel(_session);
                CurrentTitle = "Cari Hesaplar";
                CurrentContext = "Müşteri / tedarikçi kartları ve cari hesap hareketleri";
                break;
            // G4-2 — ÖN MUHASEBE / FATURA. "invoices:new" aynı ekranı açar (liste + form tek ekranda).
            case "invoices":
            case "invoices:new":
                CurrentPage = new InvoicesViewModel(_session);
                CurrentTitle = "Faturalar";
                CurrentContext = "Alış / satış faturaları — stok ve cari etkisi tek işlemde";
                break;
            // G4-3 — ÖN MUHASEBE / KASA-BANKA. "finance:new" aynı ekranı açar (liste + form tek ekranda).
            case "finance":
            case "finance:new":
                CurrentPage = new FinanceViewModel(_session);
                CurrentTitle = "Kasa / Banka";
                CurrentContext = "Kasa ve banka hesapları, ekstre ve iç transfer";
                break;
            case "payments":
                CurrentPage = new PaymentsViewModel(_session);
                CurrentTitle = "Tahsilat / Ödeme";
                CurrentContext = "Cari tahsilat ve ödemeleri — fatura kapama dahil";
                break;
            case "trash":
                CurrentPage = new TrashViewModel(_session);
                CurrentTitle = "Çöp Kutusu";
                CurrentContext = "Silinen kayıtları geri yükle";
                break;
            case "audit":
                CurrentPage = new AuditLogViewModel(_session);
                CurrentTitle = "Sistem Logu";
                CurrentContext = "İşlem kayıtları (salt okunur, silinemez)";
                break;
            case "stock_change_log":
                CurrentPage = new StockChangeLogViewModel(_session);
                CurrentTitle = "Stok Değişiklik Kaydı";
                CurrentContext = "Doğrudan stok değişikliği uyarı kayıtları (salt okunur)";
                break;
            case "backup":
                CurrentPage = new BackupViewModel(_session);
                CurrentTitle = "Yedek Yönetimi";
                CurrentContext = "Yedek al / geri yükle";
                break;
            case "server_backups":
                CurrentPage = new ServerBackupsViewModel(_session);
                CurrentTitle = "Sunucu Yedekleri";
                CurrentContext = "İki tarih arası toplu silme (Süper Admin)";
                break;
            case "machines":
                CurrentPage = new MachineManagementViewModel(_session);
                CurrentTitle = "Makine Yönetimi";
                CurrentContext = "Makine onay/aktif-pasif + kota (Süper Admin)";
                break;
            case "theme":
                CurrentPage = new ThemeSettingsViewModel();
                CurrentTitle = "Ayarlar — Tema";
                CurrentContext = "Koyu / Açık / Sistem tema seçimi";
                break;
            case "settings:developer":
                // ⭐ SEC-03 (2026-08-25) — GEZİNME KAPISI. Menüden gizlemek YETMEZ: Navigate kod içinden
                // de tetiklenebilir (kısayol, arama, grup ikonu). Kapı DeveloperMode.CanActivate'tir —
                // ham süper admin rolüne bakar, AccessControl.IsAdmin'e DEĞİL (o, modun kendisini sayar).
                if (!DeveloperMode.CanActivate(_session))
                {
                    CurrentPage = null;
                    CurrentTitle = "Yetkiniz yok";
                    CurrentContext = "Geliştirici Modu yalnız Süper Admin içindir.";
                    break;
                }
                CurrentPage = new DeveloperSettingsViewModel(_session);
                CurrentTitle = "Ayarlar — Geliştirici Modu";
                CurrentContext = "Geliştirici modu etkinleştir/kapat";
                break;
            case "permission_templates":
                CurrentPage = new PermissionTemplatesViewModel(_session);
                CurrentTitle = "Yetki Şablonları";
                CurrentContext = "İsimli yetki şablonları (Süper Admin)";
                break;
            case "about":
                CurrentPage = new AboutViewModel(_session);
                CurrentTitle = "Hakkında";
                CurrentContext = "";
                break;
            case "releases":
                CurrentPage = new ReleasesViewModel(_session);
                CurrentTitle = "Güncelleme Yönetimi";
                CurrentContext = "Paket yayınla ve % ilerlemeyle kur (yalnız Süper Admin)";
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
            // ⭐ RPR-07 (2026-08-25): iki rapor ekranı artık GERÇEKTEN ayrı — fark ŞUBE KAPSAMINDA.
            case "reports":
                CurrentPage = new ReportsViewModel(_session, managerMode: false);
                CurrentTitle = "Operasyon Raporları";
                CurrentContext = ReportsViewModel.OperationContext(_session);
                break;
            case "reports:manager":
                // ⭐ RPR-07 GEZİNME KAPISI: menüden gizlemek YETMEZ — Navigate kod içinden de tetiklenebilir.
                // Veri zaten sunucuda/serviste korunuyor (ReportService.Run yönetici raporunu personele
                // reddeder); bu kapı menüdeki kuralı gezinme yolunda da uygular.
                if (!AccessControl.IsAdmin(_session))
                {
                    CurrentPage = null;
                    CurrentTitle = "Yetkiniz yok";
                    CurrentContext = "Yönetici Raporları yalnız yönetici yetkisiyle açılır.";
                    break;
                }
                CurrentPage = new ReportsViewModel(_session, managerMode: true);
                CurrentTitle = "Yönetici Raporları";
                CurrentContext = "Yetkili olduğunuz şubeler — şube seçimi yapılabilir";
                break;
            case "import_export":
                CurrentPage = new ImportExportViewModel(_session);
                CurrentTitle = "İmport / Export";
                CurrentContext = "Excel ile içe/dışa aktarım";
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

    /// <summary>Köprü: ilgili ekrana git + (varsa) kaydın detayını/işlemini otomatik aç.</summary>
    public void NavigateTo(string key, string? entityId)
    {
        Navigate(key);
        if (!string.IsNullOrEmpty(entityId) && CurrentPage is IDeepLinkTarget t) t.OpenEntity(entityId);
    }

    /// <summary>Geliştirici modu açıl/kapanınca menü + süper-admin görünürlüğü tazelenir.</summary>
    private void OnDeveloperModeChanged()
    {
        Groups = BuildGroups(_session);
        Sections = BuildSections(_session);
        OnPropertyChanged(nameof(IsSuperAdmin));
    }

    [RelayCommand]
    private void GoDashboard() => Navigate("dashboard");

    /// <summary>Aktif ekranın gerçek kod bilgisini (View/ViewModel + kaynak) kopyalanabilir pencerede gösterir.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ShowScreenInfo()
    {
        var (title, body) = ScreenInfoBuilder.Build(CurrentPage, ActiveKey, CurrentTitle);
        await ScreenInfoService.ShowAsync(title, body);
    }

    /// <summary>Basit görünüm: yalnız ekran adı + alan adları (teknik bilgi yok).</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ShowSimpleScreenInfo()
    {
        var (title, body) = ScreenInfoBuilder.BuildSimple(CurrentPage, ActiveKey, CurrentTitle);
        await ScreenInfoService.ShowAsync(title, body);
    }

    /// <summary>
    /// ⭐ LOG-01 — AKTİF EKRANIN KAYIT GEÇMİŞİ.
    ///
    /// Yalnız bu ekranın varlık tiplerini gösterir (ScreenAuditMap); Sistem Logu ekranından farkı budur.
    /// Gösterilen zaman <c>created_at</c>: kaydın sisteme GERÇEKTEN girildiği an — işlem tarihi (iş günü)
    /// geri/ileri alınmış olsa bile burada gerçek saat görünür (TRH-01 ile birlikte okunur).
    /// Yetki kapısı SERVİSTEDİR; buradaki görünürlük yalnız arayüz kolaylığıdır.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ShowScreenLog()
    {
        var modul = AktifModul;
        if (modul is null) return;
        try
        {
            var satirlar = DesktopServices.Audit.ForModule(_session, modul, limit: 200);
            var govde = satirlar.Count == 0
                ? "Bu ekran için henüz kayıt geçmişi yok."
                : string.Join(System.Environment.NewLine,
                    satirlar.Select(x => $"{x.DateText}  ·  {x.UserText}  ·  {x.ActionText}  ·  {x.EntityType}"));
            await ScreenInfoService.ShowAsync($"Kayıt Geçmişi — {CurrentTitle}",
                "Kaydın sisteme GİRİLDİĞİ an gösterilir (işlem tarihinden bağımsız)." +
                System.Environment.NewLine + System.Environment.NewLine + govde);
        }
        catch (System.Exception ex)
        {
            await ScreenInfoService.ShowAsync("Kayıt Geçmişi", "Geçmiş okunamadı: " + ex.Message);
        }
    }

    /// <summary>Araçlar ekranına gidip ilgili aracı seçer (malzeme detayındaki uyumlu araç tıklaması).</summary>
    public void GoToVehicle(string vehicleId)
    {
        Navigate("vehicles");
        if (CurrentPage is VehiclesViewModel vm) vm.SelectById(vehicleId);
    }

    /// <summary>Çıkış Yap — ÖNCE bekleyen veriyi sunucuya gönder (kullanıcı isteği 2026-07-19: butona
    /// basmadan da veri gitsin), sonra oturumu kapat. Push en fazla 10 sn bekletir (küçük değişiklikler
    /// hızlıdır; çevrimdışıysa anında döner) — çıkışı kilitlemesin diye sınırlı.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task Logout()
    {
        // Z1: başka bir eşitleme sürüyorsa push'u ATLA (o zaten gönderiyor). ÇIKIŞ her hâlükârda yapılır.
        if (SyncGate.TryEnter())
        {
            try { await System.Threading.Tasks.Task.WhenAny(BusinessSyncPushService.PushAsync(), System.Threading.Tasks.Task.Delay(10000)); } catch { }
            finally { SyncGate.Exit(); }
        }
        DepoWise.Desktop.App.Current?.Logout();
    }

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

public sealed record MenuSearchItem(string Display, string Key);
