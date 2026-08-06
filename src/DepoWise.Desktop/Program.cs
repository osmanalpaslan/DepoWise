using Avalonia;
using System;
using System.Globalization;

namespace DepoWise.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Tarih/takvim Türkçe görünsün (DatePicker ay adları "Ocak…", gün/ay/yıl sırası TR) — kullanıcı bildirimi
        // 2026-08-05. AMA sayı biçimi INVARIANT (nokta) bırakılır → mevcut sayı girişi/gösterimi (1.5) DEĞİŞMEZ,
        // veri katmanı zaten invariant (Money.Parse/Serialize). Yalnız tarih düzelir, sayılar bozulmaz.
        var tr = (CultureInfo)new CultureInfo("tr-TR").Clone();
        tr.NumberFormat = CultureInfo.InvariantCulture.NumberFormat;
        CultureInfo.DefaultThreadCurrentCulture = tr;
        CultureInfo.DefaultThreadCurrentUICulture = tr;
        CultureInfo.CurrentCulture = tr;
        CultureInfo.CurrentUICulture = tr;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
