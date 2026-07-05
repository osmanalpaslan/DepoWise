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

    // Bu bilgisayarın "ait olduğu şube" (ilk şube girişinde yerele kaydedilir; farklı şube uyarısı buna göre).
    private const string MachineHomeBranchKey = "machine.home_branch_id";

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

    // Bu makineye tanımlı şube (ilk giriş şubesi). Seçilen şube buysa şifre SORULMAZ (L2).
    private string? _machineHomeBranchId;

    /// <summary>Seçilen şube makinenin şubesi mi? (öyleyse şube şifresi istenmez)</summary>
    public bool SelectedIsMachineBranch => SelectedBranch is not null && !string.IsNullOrEmpty(_machineHomeBranchId)
        && SelectedBranch.Id == _machineHomeBranchId;

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
            _machineHomeBranchId = DesktopServices.Settings.Get(_authedCompanyId, MachineHomeBranchKey); // L2

            // Kullanıcının KENDİ firmasının şubelerini yükle (firma listesi gösterilmez).
            await LoadBranchesForUserAsync(_authedCompanyId!, result.Session.CanViewAllBranches);
            CompanyName = ResolveCompanyName(_authedCompanyId!);
            SelectedBranch = null; BranchPassword = "";
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
    }

    // ── ADIM 2: şube seçimi + giriş tamamlama ──
    [RelayCommand]
    private async System.Threading.Tasks.Task Login()
    {
        if (_authedSession is null) { Back(); return; }
        Error = null;
        // B1: şube seçimi ZORUNLU (Tüm Şubeler de geçerli bir seçimdir).
        if (SelectedBranch is null) { Error = "Lütfen giriş yapılacak şubeyi seçin."; return; }
        IsBusy = true;
        try
        {
            // Gerçek şube seçildiyse ve şifre gerekiyorsa ONLINE doğrula (çevrimdışıysa atlanır).
            // L2: seçilen şube bu makinenin kendi şubesiyse şube şifresi İSTENMEZ (direkt giriş).
            if (SelectedBranch is not null && SelectedBranch.HasPassword && !SelectedIsMachineBranch)
            {
                var ok = await ServerAuthClient.VerifyBranchAsync(_authedCompanyId ?? "", SelectedBranch.Id, BranchPassword);
                if (ok == false) { Error = "Şube şifresi hatalı."; return; }
            }
            bool isAllBranches = SelectedBranch?.Id == BranchConstants.AllBranchesId;

            // "Tüm Şubeler" seçimi YALNIZ yetkili kullanıcıya açık.
            if (isAllBranches && !_authedSession.CanViewAllBranches)
            {
                Error = "Bu kullanıcının Tüm Şubeler yetkisi yok."; return;
            }

            var selectedBranchId = isAllBranches ? null : SelectedBranch?.Id;
            var companyKey = _authedCompanyId;

            // Farklı şube uyarısı (#2): bu bilgisayar bir şubeye tanımlıysa ve BAŞKA şube ile giriş yapılıyorsa uyar + ses.
            if (selectedBranchId is not null)
            {
                var home = DesktopServices.Settings.Get(companyKey, MachineHomeBranchKey);
                if (!string.IsNullOrEmpty(home) && home != selectedBranchId)
                {
                    var homeName = Branches.FirstOrDefault(b => b.Id == home)?.Name ?? "başka bir şube";
                    PlayWarningSound();
                    var proceed = await ConfirmService.AskAsync(
                        $"Bu bilgisayar \"{homeName}\" şubesine tanımlıdır.\n\n" +
                        $"Şu an \"{SelectedBranch?.Name}\" şubesi ile giriş yapıyorsunuz. Girdiğiniz tüm kayıtlar " +
                        $"\"{SelectedBranch?.Name}\" şubesine yazılacaktır.\n\n" +
                        $"Bu makinenin şubesi (\"{homeName}\") için işlem yapmak istiyorsanız, lütfen o şubenin " +
                        "kullanıcısı ile giriş yapın.\n\nYine de devam etmek istiyor musunuz?",
                        "Farklı Şube Girişi", "Devam et ve giriş yap", "İptal", danger: true);
                    if (!proceed) return;
                }
            }

            // Şube bağlamı erken yazılır → makine kaydı (gate + heartbeat) firma+şube ile oluşur.
            DesktopServices.CurrentBranchId = selectedBranchId;
            DesktopServices.CurrentBranchName = isAllBranches ? "Tüm Şubeler" : SelectedBranch?.Name;
            DesktopServices.CurrentAllBranches = isAllBranches;

            // Makine kapısı: kota dışı/pasif makineden giriş engellenir (süper admin hariç).
            if (!_authedSession.IsSuperAdmin)
            {
                var (allowed, gateReason) = await MachineGate.CheckAsync(_authedSession.CompanyId);
                if (!allowed) { Error = gateReason; return; }
            }

            _authedSession.OperatingBranchId = selectedBranchId;
            DesktopServices.Session = _authedSession;

            // İlk şube girişi → bu bilgisayarın "ait olduğu şube" olarak kaydet (farklı şube uyarısı buna göre).
            if (selectedBranchId is not null && string.IsNullOrEmpty(DesktopServices.Settings.Get(companyKey, MachineHomeBranchKey)))
                DesktopServices.Settings.Set(companyKey, MachineHomeBranchKey, selectedBranchId);

            _ = LookupSyncService.PullAsync(Username.Trim(), Password);   // tanım senkronu
            _ = BusinessSyncPushService.PushAsync();                       // iş verisi push (web görünürlüğü)
            RememberMeService.SaveLastUsername(Username.Trim());           // çıkış sonrası prefill
            if (RememberMe) RememberMeService.Save(_authedSession);
            else RememberMeService.Clear();
            OnLoggedIn?.Invoke(_authedSession);
        }
        catch (Exception ex) { Error = "Giriş hatası: " + ex.Message; }
        finally { IsBusy = false; }
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
