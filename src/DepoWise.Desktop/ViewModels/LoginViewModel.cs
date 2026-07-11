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

    // Adım: 1 = kimlik, 2 = şube seçimi
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    private int _step = 1;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;

    public string AppName => DesktopServices.Branding.AppName;
    [ObservableProperty] private string _companyName = "";

    // Çevrimiçi mi giriş yapıldı (ADIM 1'de belirlenir): çevrimdışıysa makine şubesine otomatik giriş yapılır.
    private bool _online;

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

            // #5a: Makineye şube tanımlı değilse giriş YOK.
            if (string.IsNullOrEmpty(DesktopServices.MachineBranchId))
            {
                Error = "Bu makineye şube tanımlanmamış. Web'den yöneticinizin (admin) bu makineye şube ataması gerekir.";
                return;
            }
            // #5b: Kullanıcıya şube tanımlı değilse (ve Tüm Şubeler yetkisi yoksa) giriş YOK.
            var (userBranchId, _) = DesktopServices.LoadUserBranch(_authedSession.UserId);
            if (string.IsNullOrEmpty(userBranchId) && !canAll)
            {
                Error = "Kullanıcınıza şube tanımlanmamış. Web'den yöneticinizin size şube ataması gerekir.";
                return;
            }

            // #2 (offline): internet yoksa makinenin şubesine OTOMATİK giriş (şube seçimi yok).
            if (!_online)
            {
                await FinalizeLoginAsync(DesktopServices.MachineBranchId, DesktopServices.MachineBranchName, isAllBranches: false, warnOnDifferent: false);
                return;
            }

            // Çevrimiçi normal kullanıcı: şube seçimine geç. Varsayılan = kullanıcının kendi şubesi (varsa).
            await LoadBranchesForUserAsync(_authedCompanyId!, canAll);
            var (ubId, _2) = DesktopServices.LoadUserBranch(_authedSession.UserId);
            SelectedBranch = Branches.FirstOrDefault(b => b.Id == ubId) ?? Branches.FirstOrDefault(b => b.Id == DesktopServices.MachineBranchId);
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

        DesktopServices.CurrentBranchId = workingBranchId;
        DesktopServices.CurrentBranchName = isAllBranches ? "Tüm Şubeler" : workingBranchName;
        DesktopServices.CurrentAllBranches = isAllBranches;
        _authedSession.OperatingBranchId = workingBranchId;
        DesktopServices.Session = _authedSession;

        _ = LookupSyncService.PullAsync(Username.Trim(), Password);   // tanım senkronu
        _ = BusinessSyncPushService.PushAsync();                       // iş verisi push (web görünürlüğü)
        RememberMeService.SaveLastUsername(Username.Trim());           // çıkış sonrası prefill
        if (RememberMe) RememberMeService.Save(_authedSession);
        else RememberMeService.Clear();
        OnLoggedIn?.Invoke(_authedSession);
        return true;
    }

    // ── SÜPER ADMIN Adım 2 kurulumu + giriş ──
    private async System.Threading.Tasks.Task SetupSuperAdminStep2Async()
    {
        // Kurulumda özellikleri toplu ata; her atamanın ayrı şube yüklemesini tetiklemesini ENGELLE (çiftlenme önlenir).
        _suppressBranchReload = true;
        IsSuperAdminMode = true;
        UseMachineCompany = HasMachineCompany;  // makine firması varsa varsayılan işaretli
        UseMachineBranch = HasMachineBranch;    // makine şubesi varsa varsayılan işaretli
        foreach (var n in new[] { nameof(HasMachineCompany), nameof(HasMachineBranch), nameof(MachineCompanyLabel), nameof(MachineBranchLabel), nameof(ShowCompanyPicker), nameof(ShowSuperBranchPicker) })
            OnPropertyChanged(n);

        Companies.Clear();
        var online = await ServerAuthClient.GetLoginCompaniesAsync();
        if (online is not null) foreach (var c in online) Companies.Add(c);
        else LoadLocalCompanies(); // çevrimdışı → yerel firmalar

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

        try
        {
            var online = await ServerAuthClient.GetLoginBranchesAsync(companyId);
            if (online is null) return; // çevrimdışı → yerelde zaten olanla devam
            using var conn = DesktopServices.Factory.Create();
            foreach (var b in online)
            {
                if (b.Id == BranchConstants.AllBranchesId) continue;
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
