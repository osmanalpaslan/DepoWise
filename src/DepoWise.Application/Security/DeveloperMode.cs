using System;

namespace DepoWise.Application.Security;

/// <summary>
/// Geliştirici modu (oturum içi, KALICI DEĞİL). Aktifken kullanıcı Süper Admin yetkilerine sahip olur.
/// Çıkışta ve uygulama yeniden başında daima KAPALI (güvenlik).
///
/// ⭐ SEC-03 (denetim 2026-08-25) — <b>ARTIK YALNIZ GERÇEK SÜPER ADMİN AÇABİLİR.</b>
/// Önceden tek kapı <see cref="Code"/> idi ve kodu doğrulayan yerde <b>rol kontrolü hiç yoktu</b>:
/// masaüstünde <i>Ayarlar › Geliştirici Modu</i> ekranını açabilen (yalnız <c>settings</c> görüntüleme
/// yetkisi yeten) herhangi bir kullanıcı kodu girip o oturumda <b>süper admin</b> gibi davranabiliyordu.
/// Kod kaynak kodda sabit ve depo herkese açık olduğu için "kimse bilmez" bir varsayım da yoktu.
/// </summary>
public static class DeveloperMode
{
    public const string Code = "621875";

    private static bool _active;
    public static bool IsActive
    {
        get => _active;
        set { if (_active == value) return; _active = value; Changed?.Invoke(); }
    }

    /// <summary>
    /// SEC-03 KAPISI — bu oturum geliştirici modunu açabilir mi?
    ///
    /// ⚠️ <b>Bilerek <see cref="SessionContext.IsSuperAdmin"/> (ham rol) kullanılır;
    /// <see cref="AccessControl.IsAdmin"/> KULLANILMAZ</b> — o metot <see cref="IsActive"/>'i de sayar,
    /// yani mod bir kez açıldığında kapı kendi kendini açık tutardı (döngüsel yetki).
    ///
    /// Kısıtlı süper admin de HAYIR alır: bu yetki devredilemez (ADR-083'teki kalıcı silme ile aynı ilke).
    /// </summary>
    public static bool CanActivate(SessionContext? s) => s is not null && s.IsSuperAdmin;

    /// <summary>
    /// Geliştirici modunu açmayı dener. <b>TEK etkinleştirme yolu budur</b> — arayüzler kodu kendileri
    /// karşılaştırıp bayrağı doğrudan set etmez (kapı atlanmış olurdu).
    /// Döner: açıldıysa <c>true</c>. Başarısız denemede bayrağa DOKUNULMAZ.
    /// </summary>
    public static bool TryActivate(SessionContext? s, string? code)
    {
        if (!CanActivate(s)) return false;
        if (!string.Equals(code?.Trim(), Code, StringComparison.Ordinal)) return false;
        IsActive = true;
        return true;
    }

    /// <summary>Mod değişince UI tazelensin (menü + buton görünürlüğü).</summary>
    public static event Action? Changed;
}
