namespace DepoWise.Web.Services;

/// <summary>Oturum durumu (JWT + kullanıcı). Tarayıcıda saklanır (ProtectedLocalStorage) → sayfa geçişi/yenilemede korunur.</summary>
public sealed class AuthState
{
    public string? Token { get; private set; }
    public string? UserId { get; private set; }
    public string? CompanyId { get; private set; }
    public string? BranchId { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    /// <summary>Firma adı (görünen). GUID yerine üst bar/ana ekranda gösterilir. Boşsa CompanyId'ye düşülebilir.</summary>
    public string? CompanyName { get; private set; }
    /// <summary>Giriş yapan kullanıcının görünen adı/kullanıcı adı (üst bar).</summary>
    public string? UserName { get; private set; }
    /// <summary>Üst bar/etiketlerde gösterilecek firma metni — ad varsa ad, yoksa id.</summary>
    public string CompanyDisplay => string.IsNullOrWhiteSpace(CompanyName) ? (CompanyId ?? "") : CompanyName!;

    /// <summary>Tarayıcı deposundan yükleme denendi mi (guard erken yönlendirmesin diye).</summary>
    public bool Loaded { get; set; }

    // Kullanıcının görebileceği modüller + yetkileri (masaüstüyle aynı; menü + buton görünürlüğü buna göre).
    private IReadOnlyList<MenuModule> _modules = Array.Empty<MenuModule>();
    public IReadOnlyList<MenuModule> Modules => _modules;
    /// <summary>Kullanıcı Admin veya Süper Admin mi (menü yanıtından). Admin-only alan görünürlüğü için (#5).</summary>
    public bool IsAdmin { get; private set; }
    /// <summary>Kısıtlı Süper Admin rolü (menü yanıtından). "Yedek Yönetimi" gibi süper+kısıtlı-süper-only ekranlar için.</summary>
    public bool IsRestrictedSuperAdmin { get; private set; }
    /// <summary>
    /// DEN-F1 (denetim 2026-08-18) — ÖZEL BUTON YETKİSİ. Web'de bu kavram HİÇ YOKTU: <c>/api/me/menu</c>
    /// yalnız modülleri döndürüyordu ve burada buton desteği bulunmuyordu. Masaüstü 6 yerde
    /// <c>AccessControl.CanUseButton</c> kontrolü yaparken web, kullanıcının yetkisi olmayan butonu
    /// GÖSTERİYOR, kullanıcı tıklayıp hata alıyordu (CLAUDE.md §5: UI ≡ API).
    /// ⚠️ Güvenlik açığı DEĞİLDİ — sunucu tarafı zaten fail-closed (<c>RequireButton</c>); bu, arayüzü
    /// sunucuyla hizalar. Sunucu <c>CanUseButton</c> sonucunu gönderdiği için admin bypass burada da geçerlidir.
    /// </summary>
    private IReadOnlySet<string> _buttons = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Kullanıcı bu özel butonu kullanabilir mi (deny-by-default; süper admin daima).</summary>
    public bool CanButton(string key) => IsSuperAdmin || _buttons.Contains(key);

    /// <summary>
    /// ⭐ FAZ 4.16 — PERSONELE KULLANICI BAĞLAMA. Admin bypass'ı VEYA açıkça verilmiş
    /// «Personele Kullanıcı Bağlama» yetkisi. Sunucudaki <c>UserService.RequireLinkPermission</c>
    /// ile AYNI kural — arayüz burada ikinci bir yetki mantığı kurmaz, aynı soruyu sorar.
    /// </summary>
    public bool CanLinkUser => IsAdmin || CanButton("btn-link-user");

    // ═══ FAZ 4.6 (kullanıcı isteği 2026-09-06) — SATIR İÇİ "+" FİRMA AYARI ═══════════════════════
    // Firma hangi sabit tanımların yanında "+" çıkacağını seçer. Ayar oturumda bir kez okunur;
    // kayıt yoksa AÇIK sayılır (bugünkü davranış). Gerçek kapı SUNUCUDADIR (LookupService).
    private IReadOnlyDictionary<string, bool>? _lookupPlus;

    public void SetLookupPlus(IReadOnlyDictionary<string, bool> harita) { _lookupPlus = harita; Changed?.Invoke(); }

    /// <summary>Bu tanım için satır içi "+" açık mı? (Bilinmiyorsa AÇIK — kullanıcı kilitlenmez.)</summary>
    public bool LookupPlusAcik(string table)
        => _lookupPlus is null || !_lookupPlus.TryGetValue(table, out var acik) || acik;

    public void SetModules(IReadOnlyList<MenuModule> m, bool isAdmin = false, bool isRestrictedSuperAdmin = false,
        IReadOnlyList<string>? buttons = null)
    {
        _modules = m;
        IsAdmin = isAdmin || IsSuperAdmin;
        IsRestrictedSuperAdmin = isRestrictedSuperAdmin;
        _buttons = buttons is null ? new HashSet<string>(StringComparer.Ordinal)
                                   : new HashSet<string>(buttons, StringComparer.Ordinal);
        Changed?.Invoke();
    }

    // ═══ G5 — EKRAN PLATFORM GÖRÜNÜRLÜĞÜ (2026-08-12) ═══════════════════════════════════════
    // ERİŞİM = PLATFORM_AKTİF && YETKİ_VAR. Bu bölüm YALNIZ platform tarafını taşır; yetki
    // yukarıdaki CanView/CanCreate/... ile AYRI kalır ve hiçbir zaman birbirinin yerine geçmez.
    // Süper admin platform kapısından MUAF DEĞİLDİR: platform kapalıysa ekran web'de açılmaz
    // (kapatma kararını zaten süper admin verir; kendi kararını sessizce delmemeli).
    private HashSet<string> _webClosedScreens = new(StringComparer.Ordinal);

    /// <summary>Sunucudan gelen etkin harita: WEB'de KAPALI olan ekran anahtarları.</summary>
    public void SetScreenVisibility(IEnumerable<string> webClosedScreenKeys)
    { _webClosedScreens = new HashSet<string>(webClosedScreenKeys, StringComparer.Ordinal); Changed?.Invoke(); }

    /// <summary>Bu ekran WEB platformunda açık mı? (yetki AYRICA kontrol edilir)</summary>
    public bool PlatformOpen(string screenKey) => !_webClosedScreens.Contains(screenKey);

    // ═══ MNU — MENÜ DÜZENİ (2026-08-18) ═════════════════════════════════════════════════════
    // Ekranın menüdeki ADI · ÜST MENÜSÜ · SIRASI. Platform ve yetkiden AYRI durur: düzen hiçbir
    // erişim kararı vermez. Boş küme = katalog varsayılanı → menü bugünküyle birebir aynı çizilir.
    private DepoWise.Application.Security.MenuLayoutSet _menuLayout =
        DepoWise.Application.Security.MenuLayoutSet.Empty;

    /// <summary>Firmanın menü düzeni (sunucudan platform bilgisiyle AYNI istekte gelir).</summary>
    public DepoWise.Application.Security.MenuLayoutSet MenuLayout => _menuLayout;

    public void SetMenuLayout(DepoWise.Application.Security.MenuLayoutSet set)
    { _menuLayout = set; Changed?.Invoke(); }

    /// <summary>Route'un bağlı olduğu ekran web'de açık mı? Katalogda olmayan route platform
    /// yönetimi dışındadır → true (yetki yine de ayrıca çalışır).</summary>
    public bool PlatformOpenForRoute(string route)
    {
        var sc = DepoWise.Application.Security.AppScreens.ByWebRoute(route);
        return sc is null || PlatformOpen(sc.Key);
    }

    public bool CanView(string key) => IsSuperAdmin || _modules.Any(x => x.Key == key);
    public bool CanCreate(string key) => IsSuperAdmin || (_modules.FirstOrDefault(x => x.Key == key)?.Create ?? false);
    public bool CanEdit(string key) => IsSuperAdmin || (_modules.FirstOrDefault(x => x.Key == key)?.Edit ?? false);
    public bool CanDelete(string key) => IsSuperAdmin || (_modules.FirstOrDefault(x => x.Key == key)?.Delete ?? false);

    public event Action? Changed;

    public void SignIn(string token, string userId, string companyId, bool isSuperAdmin, string? branchId = null,
        string? companyName = null, string? userName = null)
    {
        Token = token; UserId = userId; CompanyId = companyId; IsSuperAdmin = isSuperAdmin; BranchId = branchId;
        CompanyName = companyName; UserName = userName;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Token = UserId = CompanyId = BranchId = CompanyName = UserName = null; IsSuperAdmin = false; IsAdmin = false;
        IsRestrictedSuperAdmin = false;
        _modules = Array.Empty<MenuModule>();
        Changed?.Invoke();
    }
}
