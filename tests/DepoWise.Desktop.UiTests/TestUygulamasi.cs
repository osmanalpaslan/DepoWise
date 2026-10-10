using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(DepoWise.Desktop.UiTests.TestUygulamasi))]

namespace DepoWise.Desktop.UiTests;

/// <summary>
/// Görünmez ekran (Avalonia Headless) için uygulama kurulumu. GERÇEK <see cref="App"/> sınıfı yüklenir →
/// uygulamanın kendi stil ve renk dosyaları (Components.axaml, Palette.axaml …) testte de geçerlidir.
/// App, headless ortamda veritabanı/giriş penceresi AÇMAZ (yalnız klasik masaüstü yaşam döngüsünde açar).
/// Uygulamada temel tema (Fluent/Semi) çalışma anında eklendiği için burada Fluent elle eklenir.
/// </summary>
public static class TestUygulamasi
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
        .WithInterFont()
        .AfterSetup(_ =>
        {
            if (Avalonia.Application.Current is { } app && !app.Styles.OfType<FluentTheme>().Any())
                app.Styles.Insert(0, new FluentTheme());
        });
}
