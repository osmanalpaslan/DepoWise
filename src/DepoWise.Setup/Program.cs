using Avalonia;

namespace DepoWise.Setup;

// ── Alpnex Kurulum Aracı ──
// Sunucudan kurulum tanımını alır, ön-koşulları kontrol eder, paketi indirir, SHA-256 ile
// DOĞRULAR (fail-closed), kurar, sürüm durumunu yazar (çift indirmeyi önler) ve kısayol oluşturur.
// Arayüz Avalonia'dır (uygulamayla aynı yığın); iş akışı SetupRunner içindedir.
static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
