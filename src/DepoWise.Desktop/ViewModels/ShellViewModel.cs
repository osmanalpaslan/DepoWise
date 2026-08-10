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
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _currentContext = "";
    [ObservableProperty] private string _activeKey = "dashboard";
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

        await ConfirmService.AskAsync(
            $"{durum}\n\n" +
            $"Son başarılı gönderim (push): {When("sync_last_push_ok")}\n" +
            $"Son başarılı çekme (pull):    {When("sync_last_pull_ok")}\n" +
            $"Sunucu bağlantısı: {ConnectionText}\n\n" +
            $"Ayrıntılı kayıt: {(SyncLog.FilePath ?? "sync.log")}",
            "Senkron Durumu", "Tamam", "Tamam", danger: poison is { Count: > 0 });
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
            await System.Threading.Tasks.Task.Run(() => DepoWise.Desktop.LocalPurgeService.PurgeBusinessData(cid));   // 1) yerel iş verisi HARD sil
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

    /// <param name="checkConflicts">
    /// SNK-02: çakışma bildirimi YAVAŞ gruptadır (60 sn). Bu çağrı <see cref="SyncGate"/>'in İÇİNDE
    /// kalmalı (dışarı taşımak gating davranışını değiştirirdi) → dışarı taşımak yerine parametreyle
    /// atlanır. Veri yolu (sürüm kontrolü + push + pull) bu bayraktan ETKİLENMEZ, her tur çalışır.
    /// </param>
    private async System.Threading.Tasks.Task MaybePushBusinessAsync(bool checkConflicts)
    {
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(companyId)) return;
        EnsureSyncCursorLoaded();
        // Z1: ORTAK kapı. Manuel Eşitle / Yereli Sıfırla / giriş senkronu çalışıyorsa bu tur ATLANIR
        // (eskiden ayrı bayrak kullanıldığı için reset ile tick aynı anda çalışabiliyordu → yarış).
        if (!SyncGate.TryEnter()) return;
        try
        {
            var serverV = await BusinessSyncPullService.GetServerVersionAsync();
            if (serverV is not { } sv) return;         // çevrimdışı → sessiz
            // PUSH: bu makinenin GÖNDERİLMEMİŞ yerel değişikliklerini gönder. Gönderilecekler PushAsync içinde,
            // bu makinenin KENDİ "son gönderilen watermark"ına göre belirlenir (sunucu global max'ına BAKILMAZ —
            // Z4 kök neden: başka tablo/makinenin zaman damgası artık bu makinenin kaydını atlatamaz).
            await BusinessSyncPushService.PushAsync();
            // Z2: push sonucunda sunucu kayıt atladıysa uyarı rozetini güncelle (UI thread).
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshSyncWarning);
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
            }
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
        DeveloperMode.Changed += OnDeveloperModeChanged;

        Navigate("dashboard");
        StartConnectionMonitor();
        StartUpdateWatcher();
        _ = RegisterMachineAsync();

        ServerAuthClient.SessionExpiredRaised += OnSessionExpired; // oturum düşünce tekrar giriş
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
        var all = new[]
        {
            // Uyarılar — ayrı üst menü, birleşik uyarı ekranı (şema notu).
            new NavGroupVm("🔔", "Uyarılar", "alerts", new[] { new NavLinkVm("Uyarılar", "alerts") }),
            new NavGroupVm("📦", "Malzemeler", "materials", new[]
            {
                new NavLinkVm("Malzeme Listesi", "materials"),
                new NavLinkVm("Yeni Kayıt", "materials:new"),
                new NavLinkVm("Malzeme Şablonları", "material_templates:templates"),
                new NavLinkVm("Giriş-Çıkış", "stock"),
                new NavLinkVm("Stok Hareketleri", "stock:movements"),
                new NavLinkVm("Stok Sayım", "stock:count"),
            }),
            new NavGroupVm("🚚", "Araçlar", "vehicles", new[]
            {
                new NavLinkVm("Araç Listesi", "vehicles"),
                new NavLinkVm("Yeni Araç Ekle", "vehicles:new"),
                new NavLinkVm("Şablonlar", "vehicle_templates:templates"),
                new NavLinkVm("Muayene / Sigorta", "inspection"),
            }),
            new NavGroupVm("🧑‍🔧", "Personel", "personnel", new[]
            {
                new NavLinkVm("Personel Girişi", "personnel"),
            }),
            new NavGroupVm("📋", "Günlük Faaliyet", "daily_activity", new[]
            {
                new NavLinkVm("Günlük Faaliyet Girişi", "daily_activity"),
            }),
            new NavGroupVm("🔧", "Bakım Takibi", "maintenance", new[]
            {
                new NavLinkVm("Bakım Tanımları Girişi", "maintenance:defs"),
                new NavLinkVm("Araç Bakımları Girişi", "maintenance:records"),
            }),
            new NavGroupVm("⛽", "Yakıt", "fuel", new[]
            {
                new NavLinkVm("Yakıt Dağıtımları", "fuel:dist"),
                new NavLinkVm("Depo Girişleri", "fuel:depot"),
                new NavLinkVm("Özet", "fuel:summary"),
            }),
            new NavGroupVm("👤", "Yönetim", "branches", new[]
            {
                new NavLinkVm("Şube / Şantiye", "branches"),
                new NavLinkVm("Sistem Logu", "audit"),
                new NavLinkVm("Stok Değişiklik Kaydı", "stock_change_log"),   // madde 1.5 — yetkiyle görünür
                // "Yedek Yönetimi" masaüstünden kaldırıldı (2026-07-26): yedek yönetimi yalnız WEB'de ve
                // yalnız süper admin + kısıtlı süper adminde. Arka plandaki otomatik günlük yedek yüklemesi sürer.
            }),
            new NavGroupVm("📄", "Talepler", "requests", new[]
            {
                new NavLinkVm("Talep Formu", "requests:form"),
                new NavLinkVm("Talep Onaylama", "requests:approve"),
                // Talep Operasyonları (Faz 2): Ana Depo + Satın Alma kullanır; yetkisi request_ops.
                new NavLinkVm("Talep Operasyonları", "request_ops:board"),
            }),
            new NavGroupVm("📊", "Raporlar", "reports", new[] { new NavLinkVm("Raporlar", "reports") }),
            // Yönetici Raporları — alt raporlar planlanıyor (şimdilik genel Raporlar).
            new NavGroupVm("📈", "Yönetici Raporları", "reports", new[] { new NavLinkVm("Raporlar", "reports") }),
            new NavGroupVm("🔁", "İmport / Export", "import_export", new[] { new NavLinkVm("İmport / Export", "import_export") }),
            new NavGroupVm("👥", "Kullanıcı", "users", new[]
            {
                new NavLinkVm("Kullanıcı Tanım", "users"),
                new NavLinkVm("Yetkiler", "permissions"),
                new NavLinkVm("Yetki Şablonları", "permission_templates"),
            }),
            new NavGroupVm("🛠️", "Ayarlar", "settings", new[]
            {
                new NavLinkVm("Tanım Düzenle", "definitions"),
                new NavLinkVm("Geliştirici Modu", "settings:developer"),
                new NavLinkVm("Tema", "theme"),
                new NavLinkVm("Hakkında", "about"),
            }),
            // Web Yönetimi — süper admin (Canlı Sunucu + Kota İzleme yalnız webte, masaüstünde yok).
            new NavGroupVm("🛡️", "Web Yönetimi", "companies", new[]
            {
                new NavLinkVm("Firma Tanım", "companies"),
                new NavLinkVm("Güncelleme Yönetimi", "releases"),
                new NavLinkVm("Makine Yönetimi", "machines"),
                new NavLinkVm("Sunucu Yedekleri", "server_backups"),
            }),
            // Çöp Kutusu — kendi admin menüsü.
            new NavGroupVm("🗑️", "Çöp Kutusu", "trash", new[]
            {
                new NavLinkVm("Çöp Kutusu Listesi", "trash"),
            }),
        };

        // Alt bağlantıyı KENDİ yetkisine göre filtrele (alt-sekme anahtarı parent modüle map'lenir:
        // "maintenance:defs" → "maintenance"). Görünür alt bağlantısı kalmayan grup gizlenir.
        // Verilmeyen ekran menüde GÖRÜNMEZ (deny-by-default).
        return all
            .Select(g => new NavGroupVm(g.Icon, g.Title, g.ModuleKey,
                g.Children.Where(c => CanSeeChild(s, BaseKey(c.Key))).ToList(), g.IsExpanded))
            .Where(g => g.Children.Count > 0)
            .ToList();
    }

    /// <summary>Menü görünürlüğü. İmport / Export ekranı, içe VEYA dışa aktarım yetkisinden en az biri varsa
    /// görünür (2026-07-26 ayrımı); ekran içinde her bölüm kendi yetkisiyle ayrıca korunur.</summary>
    private static bool CanSeeChild(SessionContext s, string key)
        => key == "import_export"
            ? AccessControl.Can(s, "import_export", PermissionAction.View) || AccessControl.Can(s, "export", PermissionAction.View)
            : AccessControl.CanSeeMenu(s, key);

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
            case "reports":
                CurrentPage = new ReportsViewModel(_session);
                CurrentTitle = "Raporlar";
                CurrentContext = "Stok ve yakıt raporları";
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
