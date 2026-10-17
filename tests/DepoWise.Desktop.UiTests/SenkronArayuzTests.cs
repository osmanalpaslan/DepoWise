using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DepoWise.Desktop.Views;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Desktop.UiTests;

/// <summary>
/// ═══ AR-GE B1 (2026-10-17) — SENKRON ARAYÜZÜ ═══
/// UIT17: "Gönderilemeyen Kayıtlar" penceresi (M-15 + K-22 b) — red kartı "Neden" + "Yeniden Gönder" düğmesiyle; kod
///        bildirimi "Bilgi" kartı, "Yeniden Gönder" düğmesi GİZLİ; durum satırı iki türü ayrı sayar; "Listeden Kaldır"
///        kartı listeden düşürür. Görüntü artifacts/ui-ekran/gonderilemeyen-kayitlar.png.
/// </summary>
public class SenkronArayuzTests
{
    private static readonly object Kilit = new();
    private static bool _hazir;

    private static void Ortam()
    {
        lock (Kilit)
        {
            if (_hazir) return;
            if (DesktopServices.Factory is null)
            {
                Environment.SetEnvironmentVariable("DEPOWISE_ENVIRONMENT", "UiTest_" + Guid.NewGuid().ToString("N")[..8]);
                DesktopServices.Initialize(DesktopBootstrap.Run());
            }
            _hazir = true;
        }
    }

    private static void Goruntu(Window w, string ad)
    {
        try
        {
            var klasor = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-ekran");
            Directory.CreateDirectory(klasor);
            w.CaptureRenderedFrame()?.Save(Path.Combine(klasor, ad + ".png"));
        }
        catch { /* görüntü yalnız gözle kontrol içindir */ }
    }

    private static IEnumerable<T> Gorunen<T>(Control kok) where T : Control
        => kok.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible);

    [AvaloniaFact]
    public void UIT17_Gonderilemeyen_Kayitlar_Penceresi()
    {
        Ortam();
        var co = "UISYNC";
        var svc = new GonderimRedleriService(DesktopServices.Factory);
        svc.Isle(co, """{ "tables": {} }""",
            new[] { new RejectedRow("materials", "olmayan-1", "materials: kayıt kimliği başka bir firmaya ait (atlandı).", true) },
            new[] { new BusinessSyncService.RenamedRow("materials", "olmayan-2", "code", "MLZ-100", "MLZ-100-2") });

        var w = GonderilemeyenlerWindow.Olustur(co);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Goruntu(w, "gonderilemeyen-kayitlar");

        var metinler = Gorunen<SelectableTextBlock>(w).Select(t => t.Text ?? "").ToList();
        Assert.Contains(metinler, t => t.StartsWith("Neden: kayıt kimliği başka bir firmaya ait", StringComparison.Ordinal));
        Assert.Contains(metinler, t => t.StartsWith("Bilgi:", StringComparison.Ordinal) && t.Contains("MLZ-100-2"));
        Assert.Contains(metinler, t => t.Contains("1 kayıt gönderilemedi") && t.Contains("1 kaydın kodu değiştirildi"));
        // Kayıtlar bu bilgisayarda yok → iki kartta da "Yeniden Gönder" gizli; "Listeden Kaldır" iki kartta da var.
        var dugmeler = Gorunen<Button>(w).Select(b => b.Content as string ?? "").ToList();
        Assert.DoesNotContain("Yeniden Gönder", dugmeler);
        Assert.Equal(2, dugmeler.Count(d => d == "Listeden Kaldır"));

        var kaldir = Gorunen<Button>(w).First(b => (b.Content as string) == "Listeden Kaldır");
        kaldir.Command!.Execute(kaldir.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Gorunen<Button>(w), b => (b.Content as string) == "Listeden Kaldır");
        w.Close();
    }
}
