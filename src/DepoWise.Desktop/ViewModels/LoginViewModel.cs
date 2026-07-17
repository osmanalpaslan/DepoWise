using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// 2 AŞAMALI GİRİŞ: Adım 1 kullanıcı adı+parola doğrulanır; Adım 2 YALNIZ o kullanıcının firmasının şubeleri
/// gösterilir (kullanıcılar firma listesini görmez — firma kullanıcının kendi verisinden gelir).
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _rememberMe = true;

    // Adım: 1 = kimlik, 2 = şube seçimi, 4 = ilk giriş şifre belirleme
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStepSetPassword))]
    private int _step = 1;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStepSetPassword => Step == 4;

    // İlk giriş şifre belirleme (Adım 4)
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newPassword2 = "";

    /// <summary>Web'de giriş yap — tarayıcıda web uygulamasını açar.</summary>
    [RelayCommand]
    private void OpenWeb()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://depowise-web.fly.dev") { UseShellExecute = true }); }
        catch { /* tarayıcı açılamazsa sessiz geç */ }
    }

    public string AppName => DesktopServices.Branding.AppName;
    [ObservableProperty] private string _companyName = "";

    // Çevrimiçi mi giriş yapıldı (ADIM 1'de belirlenir): çevrimdışıysa makine şubesine otomatik giriş yapılır.
    private bool _online;
    // İlk kurulum mu (makinenin şubesi henüz yok): seçilen şube, onay sonrası makineye tanımlanır.
    private bool _firstMachineSetup;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    /// <summary>Uyarı sesi (Windows). Diğer platformlarda/başarısız olursa sessiz geçer.</summary>
    private static void PlayWarningSound()
    {
        try { if (OperatingSystem.IsWindows()) MessageBeep(0x00000030); } catch { } // MB_ICONWARNING
    }

    // Adım 1 sonrası saklanan doğrulanmış oturum + firma (Adım 2 bunları kullanır)
    private SessionContext? _authedSession;
    private string? _authedCompanyId;

    // ── Şube seçimi (Adım 2) ──
    public System.Collections.ObjectModel.ObservableCollection<ServerAuthClient.LoginBranch> Branches { get; } = new();
    [ObservableProperty] private ServerAuthClient.LoginBranch? _selectedBranch;
    [ObservableProperty] private string _branchPassword = "";
    public bool HasBranches => Branches.Count > 0;

    /// <summary>Seçilen şube, makinenin (admin-atanmış) şubesi mi? (öyleyse şube şifresi istenmez — L2)</summary>
    public bool SelectedIsMachineBranch => SelectedBranch is not null && !string.IsNullOrEmpty(DesktopServices.MachineBranchId)
        && SelectedBranch.Id == DesktopServices.MachineBranchId;

    /// <summary>Şube şifresi alanı: şube şifreli VE makinenin kendi şubesi değilse gösterilir (L1/L2).</summary>
    public bool ShowBranchPassword => SelectedBranch?.HasPassword == true && !SelectedIsMachineBranch;

    /// <summary>Seçilen şubenin kodu (login'de otomatik gösterilir — L1).</summary>
    public string? SelectedBranchCode => SelectedBranch?.Code;
    public bool ShowBranchCode => !string.IsNullOrEmpty(SelectedBranch?.Code);

    partial void OnSelectedBranchChanged(ServerAuthClient.LoginBranch? value)
    {
        OnPropertyChanged(nameof(ShowBranchPassword));
        OnPropertyChanged(nameof(SelectedIsMachineBranch));
        OnPropertyChanged(nameof(SelectedBranchCode));
        OnPropertyChanged(nameof(ShowBranchCode));
    }

    // ── SÜPER ADMIN girişi (Adım 2): makine firması/şubesi ile giriş VEYA firma+şube seç ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompanyPicker))]
    [NotifyPropertyChangedFor(nameof(ShowSuperBranchPicker))]
    [NotifyPropertyChangedFor(nameof(ShowBranchSelector))]
    private bool _isSuperAdminMode;

    public System.Collections.ObjectModel.ObservableCollection<ServerAuthClient.LoginCompany> Companies { get; } = new();
    [ObservableProperty] private ServerAuthClient.LoginCompany? _selectedCompany;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompanyPicker))]
    [NotifyPropertyChangedFor(nameof(ShowSuperBranchPicker))]
    [NotifyPropertyChangedFor(nameof(ShowBranchSelector))]
    private bool _useMachineCompany = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuperBranchPicker))]
    [NotifyPropertyChangedFor(nameof(ShowBranchSelector))]
    private bool _useMachineBranch = true;

    public bool HasMachineCompany => !string.IsNullOrEmpty(DesktopServices.MachineCompanyId);
    public bool HasMachineBranch => !string.IsNullOrEmpty(DesktopServices.MachineBranchId);
    public string MachineCompanyLabel => "Makine firması ile giriş" + (string.IsNullOrEmpty(DesktopServices.MachineCompanyName) ? "" : $" ({DesktopServices.MachineCompanyName})");
    public string MachineBranchLabel => "Makine şubesi ile giriş" + (string.IsNullOrEmpty(DesktopServices.MachineBranchName) ? "" : $" ({DesktopServices.MachineBranchName})");

    /// <summary>Firma seçici: süper admin modunda VE "makine firması" işaretli DEĞİLKEN gösterilir.</summary>
    public bool ShowCompanyPicker => IsSuperAdminMode && !UseMachineCompany;
    /// <summary>Süper admin şube seçici: süper admin modunda VE "makine şubesi" işaretli DEĞİLKEN gösterilir.</summary>
    public bool ShowSuperBranchPicker => IsSuperAdminMode && !UseMachineBranch;
    /// <summary>Şube seçici (ComboBox) görünürlüğü: normal kullanıcıda daima; süper adminde yalnız "makine şubesi" işaretsizken.</summary>
    public bool ShowBranchSelector => !IsSuperAdminMode || ShowSuperBranchPicker;

    // Kurulum sırasında (özellikler toplu atanırken) tekrar-tekrar şube yüklemeyi engelleyen bayrak.
    private bool _suppressBranchReload;

    partial void OnUseMachineCompanyChanged(bool value)
    {
        // Makine firması işaretsizse → makine şubesi de anlamsız (şube firmaya bağlı) → kapat.
        if (!value) UseMachineBranch = false;
        else if (HasMachineCompany) SelectedCompany = Companies.FirstOrDefault(c => c.Id == DesktopServices.MachineCompanyId);
        if (_suppressBranchReload) return;
        _ = ReloadSuperBranchesAsync();
    }

    partial void OnSelectedCompanyChanged(ServerAuthClient.LoginCompany? value)
    {
        if (_suppressBranchReload) return;
        _ = ReloadSuperBranchesAsync();
    }

    public LoginViewModel()
    {
        // Çıkış sonrası: son giren kullanıcı adını login ekranına doldur (Beni Hatırla işaretli varsayılan).
        var last = RememberMeService.GetLastUsername();
        if (!string.IsNullOrWhiteSpace(last)) Username = last;
    }

    /// <summary>Başarılı girişte oturumla çağrılır (App pencereyi değiştirir).</summary>
    public Action<SessionContext>? OnLoggedIn { get; set; }

    // ── ADIM 1: kimlik doğrulama ──
    [RelayCommand]
    private async System.Threading.Tasks.Task Continue()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Kullanıcı adı ve parola gerekli.";
            return;
        }
        IsBusy = true;
        try
        {
            // KAYNAK-OTORİTE web: internet varsa parola web ile doğrulanır (yerele yazılır); yoksa yerelden.
            var srv = await ServerAuthClient.AuthenticateAsync(Username.Trim(), Password);
            if (srv.State == ServerAuthClient.AuthState.WrongPassword)
            {
                Error = "Kullanıcı adı veya parola hatalı.";
                return;
            }

            LoginResult result;
            if (srv.State == ServerAuthClient.AuthState.Ok)
                result = DesktopServices.Auth.Login(srv.CompanyId!, Username.Trim(), Password);
            else
            {
                var companyId = DesktopServices.ResolveCompanyId();
                result = DesktopServices.Auth.Login(companyId, Username.Trim(), Password);
                if (result.Locked)
                {
                    Error = $"Çok fazla hatalı deneme. {result.SecondsRemaining} sn sonra tekrar deneyin.";
                    return;
                }
            }
            if (!result.Success || result.Session is null)
            {
                Error = srv.State == ServerAuthClient.AuthState.Offline
                    ? "Çevrimdışısınız ve bu kullanıcı bu makinede daha önce giriş yapmamış. İnternete bağlanıp giriş yapın."
                    : (result.Error ?? "Kullanıcı adı veya parola hatalı.");
                return;
            }

            _authedSession = result.Session;
            _authedCompanyId = result.Session.CompanyId;
            _online = srv.State == ServerAuthClient.AuthState.Ok;
            CompanyName = ResolveCompanyName(_authedCompanyId!);

            // GÜVENLİK: yeni kullanıcı / admin-reset → önce kendi şifresini belirlemek zorunda (Adım 4).
            if (result.MustChangePassword)
            {
                if (!_online)
                {
                    Error = "İlk giriş şifrenizi belirlemek için internet bağlantısı gerekli. Bağlanıp tekrar deneyin.";
                    return;
                }
                NewPassword = ""; NewPassword2 = "";
                Step = 4;
                return;
            }

            // ── Makine kapısı + makinenin (admin-atanmış) şubesi (çevrimiçi kayıt/heartbeat; çevrimdışı önbellek) ──
            // Süper admin makine kısıtlarından muaftır.
            MachineGate.MachineCheck mc = await MachineGate.CheckAsync(_authedSession.CompanyId);
            if (!_authedSession.IsSuperAdmin && !mc.Allowed) { Error = mc.Reason; return; }

            bool canAll = result.Session.CanViewAllBranches || result.Session.IsSuperAdmin || result.Session.IsCompanyAdmin;

            // ── SÜPER ADMIN: hiçbir koşul engellemez. Adım 2'de "makine firması/şubesi ile giriş" VEYA firma+şube seç. ──
            if (_authedSession.IsSuperAdmin)
            {
                await SetupSuperAdminStep2Async();
                Step = 2;
                return;
            }

            // #5b: Kullanıcıya şube tanımlı değilse (ve Tüm Şubeler yetkisi yoksa) giriş YOK.
            var (userBranchId, _) = DesktopServices.LoadUserBranch(_authedSession.UserId);
            if (string.IsNullOrEmpty(userBranchId) && !canAll)
            {
                Error = "Kullanıcınıza şube tanımlanmamış. Web'den yöneticinizin size şube ataması gerekir.";
                return;
            }

            bool machineHasBranch = !string.IsNullOrEmpty(DesktopServices.MachineBranchId);
            _firstMachineSetup = false;

            if (!machineHasBranch)
            {
                // İLK KURULUM: makine şubesi henüz yok → ilk giriş yapan kullanıcı, onaylarsa kendi şubesini makineye
                // tanımlar (aşağıda FinalizeLoginAsync'te onay + sunucu ataması). Çevrimdışıysa yapılamaz (sunucu gerekli).
                if (!_online)
                {
                    Error = "Bu makine ilk kez kuruluyor; makine şubesini tanımlamak için internet bağlantısı gerekli. Bağlanıp tekrar deneyin.";
                    return;
                }
                _firstMachineSetup = true;
            }
            else if (!_online)
            {
                // Makine şubesi var + çevrimdışı → makine şubesine OTOMATİK giriş (şube seçimi yok).
                await FinalizeLoginAsync(DesktopServices.MachineBranchId, DesktopServices.MachineBranchName, isAllBranches: false, warnOnDifferent: false);
                return;
            }

            // Çevrimiçi: şube seçimine geç. Varsayılan = kullanıcının kendi şubesi (varsa). İlk kurulumda seçilen şube
            // (onay sonrası) makinenin şubesi olur.
            await LoadBranchesForUserAsync(_authedCompanyId!, canAll);
            SelectedBranch = Branches.FirstOrDefault(b => b.Id == userBranchId) ?? Branches.FirstOrDefault(b => b.Id == DesktopServices.MachineBranchId);
            BranchPassword = "";
            Step = 2; // şube seçimine geç
        }
        catch (Exception ex) { Error = "Giriş hatası: " + ex.Message; }
        finally { IsBusy = false; }
    }

    // Adım 2'den kimlik adımına dön
    [RelayCommand]
    private void Back()
    {
        Error = null; Step = 1;
        _authedSession = null; _authedCompanyId = null;
        Branches.Clear(); SelectedBranch = null; BranchPassword = "";
        IsSuperAdminMode = false; Companies.Clear(); SelectedCompany = null;
        _firstMachineSetup = false;
        NewPassword = ""; NewPassword2 = "";
    }

    // ── ADIM 4: İLK GİRİŞ şifre belirleme (yeni kullanıcı / admin-reset) ──
    [RelayCommand]
    private async System.Threading.Tasks.Task SetPassword()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 4) { Error = "Yeni şifre en az 4 karakter olmalı."; return; }
        if (NewPassword != NewPassword2) { Error = "Şifreler eşleşmiyor."; return; }
        IsBusy = true;
        try
        {
            // Web KAYNAK-OTORİTE: önce sunucuda değiştir (must_change sıfırlanır), sonra yerel kopya güncellenir.
            var ok = await ServerAuthClient.ChangeInitialPasswordAsync(NewPassword);
            if (!ok) { Error = "Şifre sunucuda belirlenemedi. İnternet bağlantınızı kontrol edip tekrar deneyin."; return; }
            if (_authedSession is not null)
                try { DesktopServices.Users.ChangeOwnPassword(_authedSession, NewPassword); } catch { }
            // Yeni şifreyle normal akışı baştan çalıştır (must_change artık 0 → şube/giriş adımına iner).
            Password = NewPassword;
            NewPassword = ""; NewPassword2 = "";
            Step = 1;
            await ContinueCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { Error = "Şifre belirleme hatası: " + ex.Message; }
        finally { IsBusy = false; }
    }

    // ── ADIM 2: şube seçimi + giriş tamamlama (yalnız çevrimiçi / süper admin) ──
    [RelayCommand]
    private async System.Threading.Tasks.Task Login()
    {
        if (_authedSession is null) { Back(); return; }
        Error = null;

        // SÜPER ADMIN: hiçbir koşul engellemez (şube seçilmemiş olsa bile → Tüm Şubeler).
        if (IsSuperAdminMode)
        {
            IsBusy = true;
            try { await SuperAdminLoginAsync(); }
            catch (Exception ex) { Error = "Giriş hatası: " + ex.Message; }
            finally { IsBusy = false; }
            return;
        }

        // Şube seçimi ZORUNLU ("Tüm Şubeler" de geçerli bir seçimdir).
        if (SelectedBranch is null) { Error = "Lütfen giriş yapılacak şubeyi seçin."; return; }
        IsBusy = true;
        try
        {
            // Gerçek şube + şifre varsa ONLINE doğrula. L2: seçilen şube makinenin şubesiyse şifre İSTENMEZ.
            if (SelectedBranch.HasPassword && !SelectedIsMachineBranch)
            {
                var ok = await ServerAuthClient.VerifyBranchAsync(_authedCompanyId ?? "", SelectedBranch.Id, BranchPassword);
                if (ok == false) { Error = "Şube şifresi hatalı."; return; }
            }
            bool isAllBranches = SelectedBranch.Id == BranchConstants.AllBranchesId;
            if (isAllBranches && !(_authedSession.CanViewAllBranches || _authedSession.IsSuperAdmin || _authedSession.IsCompanyAdmin))
            {
                Error = "Bu kullanıcının Tüm Şubeler yetkisi yok."; return;
            }
            var workingBranchId = isAllBranches ? null : SelectedBranch.Id;
            var workingBranchName = isAllBranches ? "Tüm Şubeler" : SelectedBranch.Name;
            if (!await FinalizeLoginAsync(workingBranchId, workingBranchName, isAllBranches, warnOnDifferent: true))
                return; // kullanıcı farklı-şube uyarısında iptal etti
        }
        catch (Exception ex) { Error = "Giriş hatası: " + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Oturumu tamamlar. <paramref name="workingBranchId"/> = kaydın yazılacağı ÇALIŞMA şubesi (null = Tüm Şubeler).
    /// Ana ekranda MAKİNE şubesi gösterilir; işlemler çalışma şubesiyle damgalanır. Çalışma şubesi makine
    /// şubesinden farklıysa (yalnız çevrimiçi seçimde) uyarı verilir. Dönen false = kullanıcı iptal etti.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> FinalizeLoginAsync(string? workingBranchId, string? workingBranchName, bool isAllBranches, bool warnOnDifferent)
    {
        if (_authedSession is null) return false;

        // İLK KURULUM: makine şubesi henüz yok → seçilen şube, ONAY sonrası makineye tanımlanır (sunucu). "Tüm Şubeler"
        // ilk kurulumda makine şubesi olamaz (belirli bir şube gerekir) — kullanıcıdan gerçek şube seçmesi istenir.
        if (_firstMachineSetup)
        {
            if (workingBranchId is null)
            {
                Error = "İlk kurulumda makine için belirli bir şube seçin (\"Tüm Şubeler\" olamaz).";
                return false;
            }
            PlayWarningSound();
            var confirm = await ConfirmService.AskAsync(
                $"Bu bilgisayar İLK KEZ kuruluyor.\n\nBu makine \"{CompanyName}\" firması, \"{workingBranchName}\" şubesi için " +
                "tanımlanacaktır. Bundan sonra bu makinede yapılan işlemler bu şubeye ait olur (yönetici daha sonra web'den değiştirebilir).\n\n" +
                "Onaylıyor musunuz?",
                "İlk Kurulum — Makine Şubesi", "Onayla ve Kur", "İptal", danger: false);
            if (!confirm) return false;
            var assigned = await ServerAuthClient.SelfAssignMachineBranchAsync(Environment.MachineName, workingBranchId);
            if (assigned == true)
            {
                DesktopServices.MachineBranchId = workingBranchId;
                DesktopServices.MachineBranchName = workingBranchName;
            }
            // assigned false/null: bu arada admin/başka makine atamış olabilir ya da erişilemedi → yine de girişe devam
            _firstMachineSetup = false;
        }

        // Farklı şube uyarısı: çalışma şubesi, makinenin (admin-atanmış) şubesinden farklıysa uyar.
        if (warnOnDifferent && workingBranchId is not null && !string.IsNullOrEmpty(DesktopServices.MachineBranchId)
            && workingBranchId != DesktopServices.MachineBranchId)
        {
            var machineName = DesktopServices.MachineBranchName ?? "makine şubesi";
            PlayWarningSound();
            var proceed = await ConfirmService.AskAsync(
                $"Bu makine \"{machineName}\" şubesine tanımlıdır.\n\n" +
                $"Şu an \"{workingBranchName}\" şubesi ile giriş yapıyorsunuz. Girdiğiniz tüm kayıtlar " +
                $"\"{workingBranchName}\" şubesine yazılacak; MAKİNE şubesine (\"{machineName}\") YAZILMAYACAKTIR.\n\n" +
                "Devam etmek istiyor musunuz?",
                "Farklı Şube Girişi", "Devam et ve giriş yap", "İptal", danger: true);
            if (!proceed) return false;
        }

        // ADR-085 — MAKİNE TANIMI SIFIRLAMA ALGILAMA: her şeyden ÖNCE (firma bağımsız — makine adı firmalar
        // arası bir anahtardır). Bu makine sıfırlandıysa hangi firmaya ait olduğu artık belirsizdir; bu
        // firmanın verisiyle DEVAM ETMEK YANLIŞ olur. Girişi iptal eder ve login ekranına döner.
        if (await HandleMachineResetAsync()) return false;

        // ADR-083 — KALICI SİLME ALGILAMA: her şeyden ÖNCE. Firma sunucuda kalıcı silindiyse bu makinenin
        // yerel verisi temizlenir ve login'e dönülür. Kuyruk/push'tan ÖNCE olmalı; aksi halde makine
        // silinmiş firmanın kayıtlarını sunucuya geri gönderip veriyi diriltir.
        if (await HandleCompanyPurgeAsync(_authedSession.CompanyId)) return false;

        // ADR-084 — YEREL SIFIRLAMA ALGILAMA: purge kontrolünden hemen sonra, kuyruk/push'tan ÖNCE (aynı
        // sebep: aksi halde temizlenmeden önceki eski veri sunucuya geri gönderilebilir). Bu, GİRİŞİ
        // ENGELLEMEZ — yalnız bir kerelik yerel temizlik yapıp normal akışa (yeniden indirme) devam eder.
        await HandleCompanyLocalResetAsync(_authedSession.CompanyId);

        // Şubeler sunucu-otoriteli → her girişte yerel kopyayı sunucuyla aynala (silinenler yerelde de düşer).
        await MirrorServerBranchesLocalAsync(_authedSession.CompanyId);
        // FİRMA: önce çevrimdışı kuyruk sunucuya işlenir, sonra sunucunun listesi yerele aynalanır.
        // Firma en üst ebeveyn tanımdır — diğer senkronlardan ÖNCE oturmalı (yoksa FK/tenant hatası).
        await CompanySyncService.FlushThenSyncAsync();

        DesktopServices.CurrentBranchId = workingBranchId;
        DesktopServices.CurrentBranchName = isAllBranches ? "Tüm Şubeler" : workingBranchName;
        DesktopServices.CurrentAllBranches = isAllBranches;
        _authedSession.OperatingBranchId = workingBranchId;
        DesktopServices.Session = _authedSession;

        // SENKRON SIRASI (hataya düşmesin diye kesin sıra — önce tanımlar, sonra kayıtlar):
        //   1) FİRMA kuyruğu   → yukarıda tamamlandı (en üst ebeveyn)
        //   2) TANIMLAR/lookup → şube, birim, kategori… (iş kayıtlarının FK ebeveynleri)
        //   3) İŞ VERİSİ       → push (BusinessSyncService.Tables zaten FK-güvenli sırada), sonra pull
        // Paralel çalıştırılırsa iş kaydı, ebeveyn tanımı gelmeden gidip hata verebilir → SIRAYLA await edilir.
        var u = Username.Trim(); var p = Password;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await CompanySyncService.TryFlushAsync();   // arada biriken firma işlemi kalmasın
            await LookupSyncService.PullAsync(u, p);    // 2) tanımlar
            await BusinessSyncPushService.PushAsync();  // 3) iş verisi: önce gönder
            await BusinessSyncPullService.PullAsync();  //    sonra diğer makinelerinkini çek
        });
        RememberMeService.SaveLastUsername(Username.Trim());           // çıkış sonrası prefill
        if (RememberMe) RememberMeService.Save(_authedSession);
        else RememberMeService.Clear();
        OnLoggedIn?.Invoke(_authedSession);
        return true;
    }

    /// <summary>
    /// ADR-085 — Bu MAKİNE için sunucuda bekleyen bir "tanım sıfırlama" isteği varsa VE bu makine onu daha
    /// önce uygulamadıysa: yerel makine-firma/şube önbelleğini temizler ve GİRİŞİ İPTAL EDER (login ekranına
    /// döner). ADR-084'ten (firma yerel sıfırlama) FARKI budur: o girişe İZİN VERİP devam eder, bu DURDURUR
    /// — çünkü sıfırlama sonrası makinenin hangi firmaya ait olduğu belirsizdir; bir sonraki giriş yapan
    /// kullanıcı makineyi kendi firması/şubesiyle yeniden tanımlar.
    ///
    /// Çevrimdışıysa ya da hiç istek yoksa (null) HİÇBİR ŞEY yapılmaz (fail-safe, ADR-083/084 ile aynı ilke).
    /// </summary>
    private async System.Threading.Tasks.Task<bool> HandleMachineResetAsync()
    {
        var machineName = Environment.MachineName;
        long? serverAt;
        try { serverAt = await ServerAuthClient.GetMachineResetRequestedAtAsync(machineName); }
        catch { return false; }
        if (serverAt is null) return false;                                    // istek yok / erişilemedi → dokunma

        var localAt = MachineResetLocalService.GetAppliedAt(machineName);
        if (localAt is not null && localAt.Value >= serverAt.Value) return false;   // bu istek zaten uygulanmış

        DesktopServices.MachineCompanyId = null; DesktopServices.MachineCompanyName = null;
        DesktopServices.MachineBranchId = null; DesktopServices.MachineBranchName = null;
        MachineGate.ClearCache();
        MachineResetLocalService.MarkApplied(machineName, serverAt.Value, _authedSession?.UserId ?? "system");

        PlayWarningSound();
        await ConfirmService.AskAsync(
            "Bu makinenin firma/şube tanımı süper admin tarafından sıfırlanmıştır.\n\n" +
            "Güvenlik için oturum kapatıldı — lütfen yeniden giriş yapın. İlk giriş yapan kullanıcı bu makineyi " +
            "kendi firması/şubesiyle yeniden tanımlayacaktır.",
            "Makine Tanımı Sıfırlandı", "Tamam", "");
        Back();
        return true;
    }

    /// <summary>
    /// ADR-083 — Eşitleme adımı: firma sunucuda KALICI silindiyse yerel veriyi temizler ve login'e döner.
    /// Dönen: true = silindi ve temizlendi (giriş İPTAL), false = devam edilebilir.
    ///
    /// Çevrimdışıysa (null) HİÇBİR ŞEY yapılmaz — "cevap alamadım" diye yerel veri silinmez (fail-safe).
    /// Silme yalnız sunucu açıkça "silindi" dediğinde uygulanır.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> HandleCompanyPurgeAsync(string companyId)
    {
        bool? purged;
        try { purged = await ServerAuthClient.IsCompanyPurgedAsync(); }
        catch { return false; }              // erişilemedi → dokunma
        if (purged != true) return false;    // false ya da null (çevrimdışı) → dokunma

        Error = "Firma kalıcı silinmiş — bu makinedeki kayıtlar temizleniyor…";
        int rows = 0;
        try { rows = LocalPurgeService.PurgeLocalCompany(companyId); }
        catch (Exception ex)
        {
            await ConfirmService.AskAsync(
                $"Firmanız sunucuda kalıcı olarak silinmiş, ancak bu makinedeki kayıtlar temizlenemedi:\n\n{ex.Message}\n\n" +
                "Uygulamayı yeniden başlatın; sorun sürerse yöneticinize bildirin.",
                "Yerel Veri Temizlenemedi", "Tamam", "");
            return true;   // yine de giriş yaptırma — firma sunucuda yok
        }

        // "Beni hatırla" kaydı da düşer: silinen firmaya otomatik giriş denenmesin.
        try { RememberMeService.Clear(); } catch { }
        DesktopServices.Session = null;

        await ConfirmService.AskAsync(
            $"Firmanız sunucuda KALICI olarak silinmiştir.\n\n" +
            $"Bu makinedeki {rows} kayıt temizlendi. Bu firmayla artık giriş yapılamaz.\n\n" +
            "Bilginiz dışında olduğunu düşünüyorsanız yöneticinize başvurun.",
            "Firma Silindi", "Tamam", "");
        Error = null;
        return true;   // login ekranında kal
    }

    /// <summary>
    /// ADR-084 — Sunucuda bu firma için bekleyen bir "yerel sıfırlama" isteği varsa VE bu makine onu daha
    /// önce uygulamadıysa: yerel kayıtları bir kez temizler ve sunucudakiyle eşitler. GİRİŞİ ENGELLEMEZ —
    /// wipe sonrası aşağıdaki normal senkron adımları (mirror/pull) sıfırdan yeniden doldurur.
    ///
    /// Çevrimdışıysa ya da hiç istek yoksa (null) HİÇBİR ŞEY yapılmaz (fail-safe, ADR-083 ile aynı ilke).
    /// </summary>
    private async System.Threading.Tasks.Task HandleCompanyLocalResetAsync(string companyId)
    {
        long? serverAt;
        try { serverAt = await ServerAuthClient.GetLocalResetRequestedAtAsync(); }
        catch { return; }
        if (serverAt is null) return;                                  // istek yok / erişilemedi → dokunma

        var localAt = LocalResetService.GetAppliedAt(companyId);
        if (localAt is not null && localAt.Value >= serverAt.Value) return;   // bu istek zaten uygulanmış

        try { LocalPurgeService.PurgeLocalCompany(companyId); }
        catch { return; }                                              // temizlenemedi → işaretleme, sonraki girişte tekrar denenir
        LocalResetService.MarkApplied(companyId, serverAt.Value, _authedSession?.UserId ?? "system");
    }

    // ── SÜPER ADMIN Adım 2 kurulumu + giriş ──
    private async System.Threading.Tasks.Task SetupSuperAdminStep2Async()
    {
        // Kurulumda özellikleri toplu ata; her atamanın ayrı şube yüklemesini tetiklemesini ENGELLE (çiftlenme önlenir).
        _suppressBranchReload = true;
        IsSuperAdminMode = true;

        // Firma listesini ÖNCE yükle; "makine firması" geçerliliğini bu listeye göre belirle.
        Companies.Clear();
        var online = await ServerAuthClient.GetLoginCompaniesAsync();
        if (online is not null) foreach (var c in online) Companies.Add(c);
        else LoadLocalCompanies(); // çevrimdışı → yerel firmalar

        // MAKİNE FİRMASI SİLİNMİŞSE (geçerli firma listesinde yok) makine firması/şubesi olarak SUNMA →
        // silinmiş firma "Makine firması ile giriş" olarak çıkmaz, ona giriş yapılamaz; süper admin geçerli
        // firma seçer. (Liste hiç yüklenemediyse — Count==0 — dokunma; geçici hatada makine firmasını kaybetme.)
        if (Companies.Count > 0 && !string.IsNullOrEmpty(DesktopServices.MachineCompanyId)
            && !Companies.Any(c => c.Id == DesktopServices.MachineCompanyId))
        {
            DesktopServices.MachineCompanyId = null;
            DesktopServices.MachineCompanyName = null;
            DesktopServices.MachineBranchId = null;   // firma geçersizse şube de anlamsız
            DesktopServices.MachineBranchName = null;
        }

        UseMachineCompany = HasMachineCompany;  // makine firması varsa (ve geçerliyse) varsayılan işaretli
        UseMachineBranch = HasMachineBranch;    // makine şubesi varsa varsayılan işaretli
        foreach (var n in new[] { nameof(HasMachineCompany), nameof(HasMachineBranch), nameof(MachineCompanyLabel), nameof(MachineBranchLabel), nameof(ShowCompanyPicker), nameof(ShowSuperBranchPicker) })
            OnPropertyChanged(n);

        SelectedCompany = Companies.FirstOrDefault(c => c.Id == DesktopServices.MachineCompanyId)
            ?? Companies.FirstOrDefault(c => c.Id == _authedCompanyId)
            ?? Companies.FirstOrDefault();

        _suppressBranchReload = false;
        await ReloadSuperBranchesAsync(); // TEK yükleme
    }

    /// <summary>Süper admin: etkin firmanın (makine firması ya da seçilen) şubelerini yükler. Eşzamanlı çağrıya karşı korumalı.</summary>
    private bool _reloadingSuperBranches;
    private async System.Threading.Tasks.Task ReloadSuperBranchesAsync()
    {
        if (!IsSuperAdminMode) return;
        if (_reloadingSuperBranches) return; // eşzamanlı ikinci yükleme → çiftlenmeyi önle
        _reloadingSuperBranches = true;
        try
        {
            var companyId = UseMachineCompany ? DesktopServices.MachineCompanyId : SelectedCompany?.Id;
            if (string.IsNullOrEmpty(companyId)) { Branches.Clear(); OnPropertyChanged(nameof(HasBranches)); return; }
            await LoadBranchesForUserAsync(companyId, canViewAllBranches: true); // süper admin → Tüm Şubeler daima
            SelectedBranch = Branches.FirstOrDefault(b => b.Id == DesktopServices.MachineBranchId) ?? Branches.FirstOrDefault();
        }
        finally { _reloadingSuperBranches = false; }
    }

    private void LoadLocalCompanies()
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM companies WHERE is_deleted=0 ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) Companies.Add(new ServerAuthClient.LoginCompany(r.GetString(0), r.GetString(1)));
        }
        catch { }
    }

    private async System.Threading.Tasks.Task SuperAdminLoginAsync()
    {
        // Firma çöz: makine firması (işaretliyse) → yoksa seçilen → yoksa süper adminin kendi firması (login DAİMA mümkün).
        var companyId = (UseMachineCompany ? DesktopServices.MachineCompanyId : SelectedCompany?.Id) ?? _authedCompanyId!;
        var companyName = Companies.FirstOrDefault(c => c.Id == companyId)?.Name ?? ResolveCompanyName(companyId);

        // Şube çöz: makine şubesi (işaretliyse) → yoksa seçilen → yoksa Tüm Şubeler (engelleme yok).
        string? branchId; string? branchName; bool isAll;
        if (UseMachineBranch && !string.IsNullOrEmpty(DesktopServices.MachineBranchId))
        { branchId = DesktopServices.MachineBranchId; branchName = DesktopServices.MachineBranchName; isAll = false; }
        else if (SelectedBranch is not null && SelectedBranch.Id != BranchConstants.AllBranchesId)
        { branchId = SelectedBranch.Id; branchName = SelectedBranch.Name; isAll = false; }
        else
        { branchId = null; branchName = "Tüm Şubeler"; isAll = true; }

        // Seçilen firma süper adminin kendi firması değilse: firmayı + şubelerini YERELE yaz + çapraz-firma oturumu kur.
        if (!string.Equals(companyId, _authedCompanyId, StringComparison.Ordinal))
        {
            await UpsertCompanyAndBranchesLocalAsync(companyId, companyName);
            var cross = DesktopServices.Auth.CreateSessionForUser(companyId, _authedSession!.UserId);
            if (cross is null) { Error = "Seçilen firma bağlamında oturum kurulamadı (internet gerekebilir)."; return; }
            _authedSession = cross;
            _authedCompanyId = companyId;
        }
        CompanyName = companyName;
        DesktopServices.MachineBranchName ??= null; // (ana ekran makine şubesini gösterir; değişmez)

        await FinalizeLoginAsync(branchId, branchName, isAll, warnOnDifferent: false);
    }

    /// <summary>Süper adminin seçtiği (kendi olmayan) firmayı + şubelerini yerel DB'ye yazar → çapraz-firma oturumu +
    /// ekranlar için firma/şube mevcut olur. Operasyonel veri senkronu ayrı (bu yalnız firma+şube tanımları).</summary>
    private async System.Threading.Tasks.Task UpsertCompanyAndBranchesLocalAsync(string companyId, string companyName)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($id,$n,$now,$now,1,0) " +
                                "ON CONFLICT(id) DO UPDATE SET name=$n, is_deleted=0, updated_at=$now;";
                c.Parameters.AddWithValue("$id", companyId);
                c.Parameters.AddWithValue("$n", companyName);
                c.Parameters.AddWithValue("$now", now);
                c.ExecuteNonQuery();
            }
        }
        catch { }

        await MirrorServerBranchesLocalAsync(companyId);
    }

    /// <summary>
    /// ŞUBELER SUNUCU-OTORİTELİDİR (web'den yönetilir; iş senkronuna dahil değil). Bu metot sunucudaki şube
    /// listesini yerel DB'ye AYNALAR: gelenler upsert edilir, sunucuda ARTIK OLMAYANLAR yerelde pasife alınır.
    /// Aksi halde web'de silinen şube masaüstünde tüm şube alanlarında görünmeye devam ediyordu.
    /// Çevrimdışıysa hiçbir şey yapılmaz (yerelde olanla devam).
    /// </summary>
    private static async System.Threading.Tasks.Task MirrorServerBranchesLocalAsync(string companyId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var online = await ServerAuthClient.GetLoginBranchesAsync(companyId);
            if (online is null) return; // çevrimdışı → yerelde zaten olanla devam
            using var conn = DesktopServices.Factory.Create();
            var serverIds = new System.Collections.Generic.List<string>();
            foreach (var b in online)
            {
                if (b.Id == BranchConstants.AllBranchesId) continue;
                serverIds.Add(b.Id);
                using var c = conn.CreateCommand();
                c.CommandText = "INSERT INTO branches(id,company_id,name,kind,code,created_at,updated_at,version,is_deleted) " +
                                "VALUES($id,$c,$n,'branch',$code,$now,$now,1,0) " +
                                "ON CONFLICT(id) DO UPDATE SET company_id=$c, name=$n, code=$code, is_deleted=0, updated_at=$now;";
                c.Parameters.AddWithValue("$id", b.Id);
                c.Parameters.AddWithValue("$c", companyId);
                c.Parameters.AddWithValue("$n", b.Name);
                c.Parameters.AddWithValue("$code", (object?)b.Code ?? System.DBNull.Value);
                c.Parameters.AddWithValue("$now", now);
                c.ExecuteNonQuery();
            }

            // ŞUBELER SUNUCU-OTORİTELİ: sunucunun listesinde ARTIK OLMAYAN yerel şubeler SİLİNMİŞ demektir →
            // yerelde de pasife al. Aksi halde web'de silinen şube masaüstünde tüm şube alanlarında görünmeye
            // devam ediyordu (yalnız upsert yapılıyor, silme yansıtılmıyordu).
            using (var del = conn.CreateCommand())
            {
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < serverIds.Count; i++)
                {
                    var p = "$k" + i;
                    names.Add(p);
                    del.Parameters.AddWithValue(p, serverIds[i]);
                }
                del.CommandText =
                    "UPDATE branches SET is_deleted=1, updated_at=$now WHERE company_id=$c AND is_deleted=0" +
                    (names.Count > 0 ? " AND id NOT IN (" + string.Join(",", names) + ")" : "") + ";";
                del.Parameters.AddWithValue("$c", companyId);
                del.Parameters.AddWithValue("$now", now);
                del.ExecuteNonQuery();
            }
        }
        catch { }
    }

    private async System.Threading.Tasks.Task LoadBranchesForUserAsync(string companyId, bool canViewAllBranches)
    {
        Branches.Clear();
        // "Tüm Şubeler" seçeneği YALNIZ yetkili kullanıcıya gösterilir.
        if (canViewAllBranches)
            Branches.Add(new ServerAuthClient.LoginBranch(BranchConstants.AllBranchesId, "🌐 Tüm Şubeler", null, false));
        var online = await ServerAuthClient.GetLoginBranchesAsync(companyId);
        if (online is not null) foreach (var b in online) Branches.Add(b);
        else LoadLocalBranches(companyId); // çevrimdışı → yerel DB (şifre bilgisi olmadan)
        OnPropertyChanged(nameof(HasBranches));
        OnPropertyChanged(nameof(ShowBranchPassword));
    }

    private void LoadLocalBranches(string companyId)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, code FROM branches WHERE company_id=$c AND is_deleted=0 ORDER BY name;";
            cmd.Parameters.AddWithValue("$c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                Branches.Add(new ServerAuthClient.LoginBranch(r.GetString(0), r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2), false)); // offline: şifre kontrolü yapılamaz
        }
        catch { }
    }

    private static string ResolveCompanyName(string companyId)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM companies WHERE id=$c;";
            cmd.Parameters.AddWithValue("$c", companyId);
            return cmd.ExecuteScalar() as string ?? "";
        }
        catch { return ""; }
    }
}
