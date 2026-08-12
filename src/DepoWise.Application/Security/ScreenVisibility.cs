namespace DepoWise.Application.Security;

/// <summary>
/// G5 — bir ekranın FİRMAYA ÖZEL platform kısıtı. <c>null</c> = o platform için kayıt yok →
/// katalog varsayılanı geçerlidir.
/// </summary>
public sealed record ScreenVisibilityOverride(string ScreenKey, bool? Desktop, bool? Web);

/// <summary>
/// ═══ G5 — PLATFORM GÖRÜNÜRLÜĞÜ ÇÖZÜMLEYİCİSİ (saf mantık, 2026-08-12) ═══
///
/// <b>ÜÇ KAVRAM AYRI DURUR</b> (birbirinin yerine geçmez):
/// <list type="bullet">
///   <item><b>Platform</b> — bu ekran bu platformda AÇIK mı? (burası)</item>
///   <item><b>Yetki</b> — kullanıcı bu modülde bu aksiyonu yapabilir mi? (<see cref="AccessControl"/>)</item>
///   <item><b>Kapsam</b> — hangi şube/veri üzerinde? (<see cref="BranchScope"/>)</item>
/// </list>
///
/// <b>ERİŞİM = PLATFORM_AKTİF &amp;&amp; YETKİ_VAR.</b> Platform görünürlüğü yetki VERMEZ ve yetkiyi
/// BYPASS ETMEZ; yalnız bir ekranı o platformda kapatabilir.
///
/// <b>⚠️ YALNIZ DARALTIR — GENİŞLETMEZ:</b> etkin platform = katalog varsayılanı <b>VE</b> firma kaydı.
/// Katalogda o platformda VAR OLMAYAN bir ekran, veritabanı kaydıyla AÇILAMAZ. Açılabilseydi menüde
/// karşılığı olmayan bir giriş belirir ve tıklandığında hiçbir yere gitmezdi (ör. yalnız web'de bulunan
/// "Kota İzleme" masaüstü menüsüne düşerdi). Bu kural, parite testlerinin verdiği garantiyi de korur.
/// </summary>
public static class ScreenVisibility
{
    /// <summary>Bir ekranın firmadaki ETKİN platformları. <paramref name="overrides"/> null/boş ise
    /// katalog varsayılanı aynen döner (migration sonrası hiçbir ekran kapanmaz).</summary>
    public static ScreenPlatform Effective(AppScreen screen,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? overrides)
    {
        var eff = screen.Platforms;
        if (overrides is null || !overrides.TryGetValue(screen.Key, out var o)) return eff;

        // Kayıt YALNIZ kapatabilir: katalogda olmayanı açmaz (bkz. sınıf açıklaması).
        if (o.Desktop == false) eff &= ~ScreenPlatform.Desktop;
        if (o.Web == false) eff &= ~ScreenPlatform.Web;
        return eff;
    }

    /// <summary>Ekran bu platformda kullanılabilir mi? (yetki BURADA kontrol EDİLMEZ)</summary>
    public static bool IsEnabled(AppScreen screen, ScreenPlatform platform,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? overrides)
        => Effective(screen, overrides).HasFlag(platform);

    /// <summary>
    /// Ekran anahtarından kontrol. <b>Bilinmeyen anahtar = KAPALI</b> (deny-by-default): katalogda
    /// olmayan bir ekran, platform yönetiminin de yetki ağacının da dışındadır; açık bırakmak
    /// sessiz bir kapı olurdu.
    /// </summary>
    public static bool IsEnabled(string screenKey, ScreenPlatform platform,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? overrides)
    {
        var screen = AppScreens.ByKey(screenKey);
        return screen is not null && IsEnabled(screen, platform, overrides);
    }

    /// <summary>
    /// ⭐ TEK KAPI — ekran açılabilir mi? Platform VE yetki birlikte değerlendirilir.
    /// Menü, route koruması, masaüstü gezinmesi ve API bu metodu kullanır → tek yerde tek kural.
    /// </summary>
    public static bool CanOpen(SessionContext session, AppScreen screen, ScreenPlatform platform,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? overrides,
        PermissionAction action = PermissionAction.View)
        => IsEnabled(screen, platform, overrides) && AccessControl.Can(session, screen.ModuleKey, action);

    /// <summary>Masaüstü gezinme anahtarından tek kapı (alt-ekran anahtarları dahil: "stock:movements").</summary>
    public static bool CanOpenDesktop(SessionContext session, string desktopNavKey,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? overrides)
    {
        var screen = AppScreens.ByDesktopNavKey(desktopNavKey);
        // Katalogda OLMAYAN gezinme hedefleri (ör. "dashboard", grup takma adları) platform yönetimi
        // dışındadır → yalnız yetki kararı verir. Bunlar menüde yer almaz, ekran içi kısayollardır.
        if (screen is null) return true;
        return CanOpen(session, screen, ScreenPlatform.Desktop, overrides);
    }

    /// <summary>Web route'undan tek kapı. Route katalogda yoksa platform yönetimi dışındadır (yetki ayrıca çalışır).</summary>
    public static bool CanOpenWeb(SessionContext session, string webRoute,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? overrides)
    {
        var screen = AppScreens.ByWebRoute(webRoute);
        if (screen is null) return true;
        return CanOpen(session, screen, ScreenPlatform.Web, overrides);
    }
}
