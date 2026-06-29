using System;

namespace DepoWise.Application.Security;

/// <summary>
/// Geliştirici modu (oturum içi, KALICI DEĞİL). Aktifken kullanıcı Süper Admin yetkilerine sahip olur.
/// Çıkışta ve uygulama yeniden başında daima KAPALI (güvenlik). Yalnız geliştirici kodu ile açılır.
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

    /// <summary>Mod değişince UI tazelensin (menü + buton görünürlüğü).</summary>
    public static event Action? Changed;
}
