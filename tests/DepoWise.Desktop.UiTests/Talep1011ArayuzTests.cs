using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Desktop.ViewModels;
using DepoWise.Desktop.Views;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop.UiTests;

/// <summary>
/// ⭐ 2026-10-11 — kullanıcı talebi (TALEP_2026-10-11.md) ekranlarının GÖRÜNMEZ EKRANDA (gerçek görünüm + ayrı yerel
/// test veritabanı) doğrulanması. Gerçek veriye dokunulmaz (DEPOWISE_ENVIRONMENT=UiTest_*).
/// UIT13: Zimmet — "+ Listeye Ekle" ile iki kalem eklenir, kalem tablosu çizilir, × ile çıkarılır.
/// UIT14: Raporlar → Depo Çıkışları — Teslim Alan filtresi görünür; malzeme ÇOKLU seçilir (çipler); rapor çalışır ve
///        2. sütun "Teslim Alan" olur.
/// UIT15: Güncelleme Notları ekranı — not kartı ve "Kullandığınız sürüm" rozeti çizilir.
/// UIT16: "Yenilikler" penceresi — başlık ve maddeler çizilir.
/// Ekran görüntüleri artifacts/ui-ekran/*.png.
/// </summary>
public class Talep1011ArayuzTests
{
    private static readonly object Kilit = new();
    private static SessionContext? _s;
    private static string _ali = "";

    private static SessionContext Ortam()
    {
        lock (Kilit)
        {
            if (_s is not null) return _s;
            // 2026-10-11: arayüz test sınıfları AYNI test ortamını paylaşır — biri başlattıysa yeniden başlatılmaz
            // (yoksa ikinci sınıf ortamı değiştirip birincinin tohum verisini görünmez kılardı).
            if (DesktopServices.Factory is null)
            {
                Environment.SetEnvironmentVariable("DEPOWISE_ENVIRONMENT", "UiTest_" + Guid.NewGuid().ToString("N")[..8]);
                DesktopServices.Initialize(DesktopBootstrap.Run());
            }
            var uid = DesktopServices.Users.EnsureInitialAdmin("UI", "ui.admin1011", "ui123456", RoleKeys.CompanyAdmin);
            _s = new SessionContext(uid, "UI", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
            var depo = DesktopServices.Branches.Create(_s, new NewBranch("UI Depo 1011"));
            var m1 = DesktopServices.Materials.Create(_s, new NewMaterial("ZMT-A", "Yağ Filtresi"));
            var m2 = DesktopServices.Materials.Create(_s, new NewMaterial("ZMT-B", "Hava Filtresi"));
            DesktopServices.Equipment.Create(_s, new NewEquipment("ZMT-EKP", "Jeneratör"));
            _ali = DesktopServices.Personnel.Create(_s, new NewPersonnel("Ali UiTest", null, null, null));
            DesktopServices.Stock.ReceiveIn(_s, new[] { new StockLine(m1, 50m, 10m), new StockLine(m2, 50m, 20m) }, "ui-1011-acilis", depo);
            DesktopServices.Stock.IssueOut(_s, new[] { new StockLine(m1, 2m, 10m) }, "ui-1011-cikis", depo, personnelId: _ali);
            return _s;
        }
    }

    private static Window Ac(Control icerik, int w = 1500, int h = 950)
    {
        var pencere = new Window { Width = w, Height = h, Content = icerik };
        pencere.Show();
        Dispatcher.UIThread.RunJobs();
        return pencere;
    }

    private static void Goruntu(Window w, string ad)
    {
        try
        {
            var klasor = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-ekran");
            Directory.CreateDirectory(klasor);
            w.CaptureRenderedFrame()?.Save(Path.Combine(klasor, ad + ".png"));
        }
        catch { }
    }

    private static IEnumerable<T> Gorunen<T>(Control kok) where T : Control
        => kok.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible);

    /// <summary>Zimmet kalem tablosu başlığı. Sütun-çizgisi stili başlığı "Teslim Edilecek Kalem" biçiminde çizer → Türkçe
    /// büyük/küçük harf duyarsız karşılaştırma (İ/i doğru eşleşsin diye tr-TR).</summary>
    private static bool BaslikGorunur(Control kok)
        => Gorunen<TextBlock>(kok).Any(t => string.Compare(t.Text, "TESLİM EDİLECEK KALEM",
            System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), System.Globalization.CompareOptions.IgnoreCase) == 0);
    private static bool MetinGorunur(Control kok, string metin)
        => Gorunen<TextBlock>(kok).Any(t => t.Text == metin);

    [AvaloniaFact]
    public void UIT13_Zimmet_Toplu_Teslim_Listesi()
    {
        var s = Ortam();
        var vm = new AssignmentsViewModel(s);
        var view = new AssignmentsView { DataContext = vm };
        var w = Ac(view);
        Assert.True(vm.IsIssue);
        Assert.Contains(Gorunen<Button>(view), b => (b.Content as string) == "+ Listeye Ekle");
        Assert.False(BaslikGorunur(view));                                     // liste boşken tablo yok

        vm.FormAsset = vm.AssetOptions.First(a => a.Name.StartsWith("ZMT-A"));
        vm.FormQuantity = 2m;
        vm.AddLineCommand.Execute(null);
        vm.FormAsset = vm.AssetOptions.First(a => a.Name.StartsWith("ZMT-B"));
        vm.AddLineCommand.Execute(null);
        vm.FormAsset = vm.AssetOptions.First(a => a.Name.StartsWith("ZMT-A"));   // aynı malzeme → miktar artar
        vm.FormQuantity = 3m;
        vm.AddLineCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Lines.Count);
        Assert.Equal(5m, vm.Lines.Single(l => l.Label.StartsWith("ZMT-A")).Quantity);
        Assert.Null(vm.FormAsset);                                             // kutu bir sonraki kalem için boşaldı
        Goruntu(w, "zimmet-toplu-teslim");   // önce görüntü: kontrol düşerse de ekranda ne çizildiği görülebilsin
        // Başlık sütun-çizgisi stiliyle "Teslim Edilecek Kalem" biçiminde çizilir → büyük/küçük harfe duyarsız.
        Assert.True(BaslikGorunur(view));

        vm.RemoveLineCommand.Execute(vm.Lines[1]);
        Assert.Single(vm.Lines);
        vm.FormOp = "İade";                                                    // başka işlem → liste temizlenir
        Assert.Empty(vm.Lines);
        Assert.False(vm.IsIssue);
        w.Close();
    }

    [AvaloniaFact]
    public void UIT14_Depo_Cikislari_Raporu_Filtreler()
    {
        var s = Ortam();
        var vm = new ReportsViewModel(s);
        var view = new ReportsView { DataContext = vm };
        var w = Ac(view);
        vm.SelectedReport = ReportCatalog.ByKey("stock-issues")!;
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ShowRecipient);
        Assert.True(vm.ShowMaterial);
        Assert.Contains(vm.Recipients, r => r.Name == "Ali UiTest");
        vm.MaterialSearch = "ZMT";
        Dispatcher.UIThread.RunJobs();
        vm.PickMaterialCommand.Execute(vm.MaterialResults.First(m => m.Code == "ZMT-A"));
        vm.MaterialSearch = "ZMT";
        Dispatcher.UIThread.RunJobs();
        vm.PickMaterialCommand.Execute(vm.MaterialResults.First(m => m.Code == "ZMT-B"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, vm.PickedMaterials.Count);
        Assert.Equal(2, Gorunen<Border>(view).Count(b => b.Classes.Contains("Chip")));

        vm.Recipients.Single(r => r.Name == "Ali UiTest").IsChecked = true;
        vm.RunCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Goruntu(w, "rapor-depo-cikislari");
        w.Close();
    }

    [AvaloniaFact]
    public void UIT15_Guncelleme_Notlari_Ekrani()
    {
        var s = Ortam();
        var ilk = GuncellemeNotlari.Tumu.First(n => n.MasaustuSurumu is not null);
        var vm = new UpdateNotesViewModel(s, ilk.MasaustuSurumu);
        var view = new UpdateNotesView { DataContext = vm };
        var w = Ac(view, 1200, 900);
        Assert.False(vm.Bos);
        Assert.Contains(Gorunen<SelectableTextBlock>(view), t => t.Text == ilk.Baslik);
        Assert.True(MetinGorunur(view, "Kullandığınız sürüm"));
        // web'e ait madde masaüstü ekranında GÖRÜNMEZ
        foreach (var m in ilk.Maddeler.Where(m => m.Platform == GuncellemeNotlari.Web))
            Assert.DoesNotContain(Gorunen<SelectableTextBlock>(view), t => t.Text == m.Metin);
        Goruntu(w, "guncelleme-notlari");
        w.Close();
    }

    [AvaloniaFact]
    public void UIT16_Yenilikler_Penceresi()
    {
        Ortam();
        var not = GuncellemeNotlari.Tumu.First(n => n.MasaustuSurumu is not null);
        var pencere = new GuncellemeNotuWindow(not);
        pencere.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Gorunen<SelectableTextBlock>(pencere), t => t.Text == "Yenilikler: " + not.Baslik);
        Assert.Equal(not.MaddelerIcin(web: false).Count(),
            pencere.GetVisualDescendants().OfType<ItemsControl>().First(i => i.Name == "Liste").ItemCount);
        Goruntu(pencere, "yenilikler-penceresi");
        pencere.Close();
    }
}
