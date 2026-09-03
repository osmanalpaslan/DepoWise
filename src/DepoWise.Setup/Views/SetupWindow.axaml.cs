using System.Diagnostics;
using System.Reflection;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using DepoWise.Application.Setup;

namespace DepoWise.Setup.Views;

/// <summary>
/// Kurulum penceresi. <b>İş mantığı burada DEĞİLDİR</b> (<c>.claude/rules/desktop.md</c>):
/// akış <see cref="SetupRunner"/> içindedir; bu dosya yalnız ekranı çizer ve kullanıcı eylemlerini
/// iletir.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly string _server;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly SetupRunner _runner;
    private SetupManifest? _manifest;
    private CancellationTokenSource? _iptal;
    private string _logPath = "";

    private static readonly SetupStep[] Adimlar =
    {
        SetupStep.SistemKontrolu, SetupStep.Indirme, SetupStep.Dogrulama,
        SetupStep.Kurulum, SetupStep.SonKontroller
    };

    private static string AdimAdi(SetupStep s) => s switch
    {
        SetupStep.SistemKontrolu => "Sistem kontrolü",
        SetupStep.Indirme => "Paket indiriliyor",
        SetupStep.Dogrulama => "Paket doğrulanıyor",
        SetupStep.Kurulum => "Alpnex kuruluyor",
        _ => "Son kontroller",
    };

    public SetupWindow()
    {
        InitializeComponent();   // ad alanlarini ATAR (AvaloniaXamlLoader.Load tek basina atamaz)

        _server = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ServerUrl")?.Value?.TrimEnd('/')
            ?? "https://depowise-erp.fly.dev";
        _runner = new SetupRunner(_server, _http);

        _logPath = Path.Combine(Path.GetTempPath(), "alpnex-kurulum.log");

        KlasorKutusu.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "app");

        GozatBtn.Click += async (_, _) => await KlasorSecAsync();
        KurBtn.Click += async (_, _) => await KurAsync();
        IptalBtn.Click += (_, _) => _iptal?.Cancel();
        TekrarDeneBtn.Click += async (_, _) => await BaslatAsync();
        HataKapatBtn.Click += (_, _) => Close();
        BaslatBtn.Click += (_, _) => { UygulamayiBaslat(); Close(); };

        Opened += async (_, _) => await AcilisAsync();
    }

    // ── Ekran geçişleri ────────────────────────────────────────────────────────────────────

    private async Task GosterAsync(Border hedef, params Border[] digerleri)
    {
        foreach (var d in digerleri) { d.Opacity = 0; d.IsVisible = false; }
        hedef.IsVisible = true;
        hedef.Opacity = 0;
        await Task.Delay(16);          // düzen yerleşsin ki geçiş görünsün
        hedef.Opacity = 1;             // stildeki 180 ms solma devreye girer
    }

    /// <summary>Açılış: kısa marka anı (≤700 ms), sonra kendiliğinden hazırlığa geçer.</summary>
    private async Task AcilisAsync()
    {
        // Avalonia'nın kendi GEÇİŞ (Transitions) mekanizması kullanılır; Animation.RunAsync bir
        // Visual ister, ScaleTransform'a uygulanamaz (denendi → InvalidCastException).
        EkranAcilis.IsVisible = true;
        EkranAcilis.Opacity = 0;
        AcilisLogo.RenderTransform = TransformOperations.Parse("scale(0.96)");

        await Task.Delay(16);                                   // başlangıç değeri yerleşsin
        EkranAcilis.Opacity = 1;                                // 'sayfa' sınıfı: 180 ms solma
        AcilisLogo.RenderTransform = TransformOperations.Parse("scale(1)");   // 420 ms ölçek

        await Task.Delay(620);         // toplam ≈700 ms — kullanıcıyı bekletmez
        await BaslatAsync();
    }

    // ── Akış ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Kurulum tanımını alır ve ön-koşulları gösterir.</summary>
    private async Task BaslatAsync()
    {
        await GosterAsync(EkranHazirlik, EkranAcilis, EkranIlerleme, EkranHata, EkranBitti);
        OnKosulListe.Children.Clear();
        KurBtn.IsEnabled = false;
        HazirlikAltBaslik.Text = "Kurulum için sisteminiz kontrol ediliyor…";

        try
        {
            _manifest = await _runner.GetManifestAsync(CancellationToken.None);
            SurumEtiketi.Text = $"Sürüm {_manifest.Application.Version}  ·  {_server}";

            var sonuc = _runner.CheckPrerequisites(_manifest, KlasorKutusu.Text ?? "", networkKnownGood: true);
            foreach (var (r, i) in sonuc.Select((r, i) => (r, i)))
            {
                OnKosulListe.Children.Add(OnKosulSatiri(r));
                await Task.Delay(60);   // sırayla belirme — toplam < 400 ms
            }

            if (SetupPrerequisites.FirstBlocker(sonuc) is { } engel)
            {
                HazirlikAltBaslik.Text = "Kuruluma devam edilemiyor.";
                HataGoster(engel.Detail ?? engel.Label, "ON_KOSUL:" + engel.Id, kalici: false);
                return;
            }

            HazirlikAltBaslik.Text = "Sisteminiz hazır. Kuruluma başlayabilirsiniz.";
            KurBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            HataGoster(KullaniciMesaji(ex), HataKoduAl(ex), kalici: false);
        }
    }

    private Control OnKosulSatiri(PrerequisiteResult r)
    {
        var simge = new Avalonia.Controls.Shapes.Path
        {
            Data = (Geometry)this.FindResource(r.Ok ? "SimgeOnay" : "SimgeUyari")!,
            VerticalAlignment = VerticalAlignment.Center,
        };
        simge.Classes.Add("simge");
        simge.Classes.Add(r.Ok ? "tamam" : "hata");

        var metin = new TextBlock { Text = r.Label, VerticalAlignment = VerticalAlignment.Center };
        metin.Classes.Add("govde");
        if (r.Ok) metin.Foreground = (IBrush)this.FindResource("MetinBrush")!;

        var satir = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        satir.Children.Add(simge);
        satir.Children.Add(metin);
        return satir;
    }

    private async Task KurAsync()
    {
        if (_manifest is null) return;
        var klasor = string.IsNullOrWhiteSpace(KlasorKutusu.Text)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "app")
            : KlasorKutusu.Text!.Trim();

        _iptal = new CancellationTokenSource();
        await GosterAsync(EkranIlerleme, EkranHazirlik, EkranAcilis, EkranHata, EkranBitti);
        AdimListeKur();

        var progress = new Progress<SetupState>(s => Dispatcher.UIThread.Post(() => DurumCiz(s)));

        try
        {
            await _runner.InstallAsync(_manifest, klasor, progress, _iptal.Token);

            BittiAciklama.Text = $"Sürüm {_manifest.Application.Version} kuruldu. " +
                                 "Masaüstündeki kısayoldan da açabilirsiniz.";
            await GosterAsync(EkranBitti, EkranIlerleme, EkranHazirlik, EkranAcilis, EkranHata);
        }
        catch (OperationCanceledException)
        {
            HataGoster("Kurulum iptal edildi. Bilgisayarınızda kalıcı bir değişiklik yapılmadı.",
                "IPTAL", kalici: false);
        }
        catch (Exception ex)
        {
            Logla(ex);
            HataGoster(KullaniciMesaji(ex), HataKoduAl(ex), kalici: true);
        }
        finally
        {
            _iptal?.Dispose();
            _iptal = null;
        }
    }

    // ── Çizim ──────────────────────────────────────────────────────────────────────────────

    private readonly Dictionary<SetupStep, (Avalonia.Controls.Shapes.Path Icon, TextBlock Text)> _adimGorsel = new();

    private void AdimListeKur()
    {
        AdimListe.Children.Clear();
        _adimGorsel.Clear();
        foreach (var a in Adimlar)
        {
            var simge = new Avalonia.Controls.Shapes.Path
            {
                Data = (Geometry)this.FindResource("SimgeOnay")!,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.35,
            };
            simge.Classes.Add("simge");

            var metin = new TextBlock { Text = AdimAdi(a), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.5 };
            metin.Classes.Add("govde");

            var satir = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            satir.Children.Add(simge);
            satir.Children.Add(metin);
            AdimListe.Children.Add(satir);
            _adimGorsel[a] = (simge, metin);
        }
        // Sistem kontrolü hazırlık ekranında zaten yapıldı → tamam işaretle.
        AdimIsaretle(SetupStep.SistemKontrolu, tamam: true);
    }

    private void AdimIsaretle(SetupStep adim, bool tamam)
    {
        if (!_adimGorsel.TryGetValue(adim, out var g)) return;
        g.Icon.Classes.Remove("aktif");
        g.Icon.Classes.Remove("tamam");
        g.Icon.Classes.Add(tamam ? "tamam" : "aktif");
        g.Icon.Opacity = 1;
        g.Text.Opacity = 1;
        g.Text.Foreground = (IBrush)this.FindResource("MetinBrush")!;
    }

    private void DurumCiz(SetupState s)
    {
        Ilerleme.Value = s.Percent;
        IlerlemeYuzde.Text = $"%{s.Percent}";
        IlerlemeDurum.Text = s.Message;
        IlerlemeDetay.Text = s.Detail ?? "";

        // Bu adıma gelindiyse öncekiler tamamlanmıştır.
        var idx = Array.IndexOf(Adimlar, s.Step);
        for (var i = 0; i < idx; i++) AdimIsaretle(Adimlar[i], tamam: true);
        AdimIsaretle(s.Step, tamam: false);
        if (s.Step == SetupStep.SonKontroller && s.Percent >= 100) AdimIsaretle(s.Step, tamam: true);
    }

    private void HataGoster(string mesaj, string kod, bool kalici)
    {
        HataMesaji.Text = mesaj;
        HataKodu.Text = "Hata kodu: " + kod;
        HataLogYolu.Text = kalici ? "Günlük dosyası: " + _logPath : "";
        HataGuvence.Text = "Bilgisayarınızda kalıcı bir değişiklik yapılmadı.";
        EkranAcilis.IsVisible = EkranHazirlik.IsVisible = EkranIlerleme.IsVisible = EkranBitti.IsVisible = false;
        EkranHata.IsVisible = true;
    }

    private static string KullaniciMesaji(Exception ex) => ex switch
    {
        SetupVerificationException sv => sv.Message,
        HttpRequestException => "Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edip tekrar deneyin.",
        TaskCanceledException => "İşlem zaman aşımına uğradı. Tekrar deneyin.",
        UnauthorizedAccessException => "Kurulum klasörüne yazma izni yok. Farklı bir klasör seçin.",
        IOException => "Dosya yazılamadı. Diskte yeterli alan olduğundan emin olup tekrar deneyin.",
        _ => "Beklenmeyen bir sorun oluştu. Tekrar deneyin.",
    };

    private static string HataKoduAl(Exception ex)
        => ex is SetupVerificationException sv ? sv.Code : ex.GetType().Name;

    /// <summary>Teknik günlük. <b>Gizli veri yazılmaz</b> — parola/jeton/bağlantı dizesi asla.</summary>
    private void Logla(Exception ex)
    {
        try
        {
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {HataKoduAl(ex)}: {ex.Message}{Environment.NewLine}");
        }
        catch { }
    }

    // ── Kullanıcı eylemleri ────────────────────────────────────────────────────────────────

    private async Task KlasorSecAsync()
    {
        var klasorler = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Kurulum klasörü seçin",
            AllowMultiple = false,
        });
        if (klasorler.Count > 0 && klasorler[0].TryGetLocalPath() is { } yol)
            KlasorKutusu.Text = Path.Combine(yol, "Alpnex");
    }

    private void UygulamayiBaslat()
    {
        try
        {
            var exe = Path.Combine(KlasorKutusu.Text ?? "", "DepoWise.Desktop.exe");
            if (File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch { }
    }
}
