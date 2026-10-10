using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DepoWise.Application.Security;
using DepoWise.Desktop.ViewModels;
using DepoWise.Desktop.Views;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.UiTests;

/// <summary>
/// ⭐ 2026-10-10 — YENİ EKRANLARIN GÖRÜNMEZ EKRANDA (gerçek görünüm + gerçek yerel veritabanı) DOĞRULANMASI.
/// Veritabanı AYRI bir test ortamındadır (%LOCALAPPDATA%\DepoWise\Data\UiTest_*) — gerçek veriye dokunulmaz.
/// UIT10: Kiralık Araçlar ekranı yalnız kiralıkları listeler, kira kolonları ve kira formu görünür;
///        Araç Listesi kiralıkları göstermez ve kira formu orada GÖRÜNMEZ.
/// UIT11: Talep Formu — aynı kaleme iki araç seçilince iki çip çizilir; arama sonucunda malzeme KODU görünür.
/// UIT12: Yakıt Özeti — ay kartı çizilir, bu ay açık gelir; haftalar ÇUBUK, günler KUTUCUK olarak ayrı çizilir.
/// Ekran görüntüleri artifacts/ui-ekran/*.png (gözle kontrol için).
/// </summary>
public class YeniEkranlarArayuzTests
{
    private static readonly object Kilit = new();
    private static SessionContext? _s;

    private static SessionContext Ortam()
    {
        lock (Kilit)
        {
            if (_s is not null) return _s;
            Environment.SetEnvironmentVariable("DEPOWISE_ENVIRONMENT", "UiTest_" + Guid.NewGuid().ToString("N")[..8]);
            var boot = DesktopBootstrap.Run();
            DesktopServices.Initialize(boot);
            var uid = DesktopServices.Users.EnsureInitialAdmin("UI", "ui.admin", "ui123456", RoleKeys.CompanyAdmin);
            _s = new SessionContext(uid, "UI", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
            Tohumla(_s);
            return _s;
        }
    }

    private static long Gun(DateTime d) => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static void Tohumla(SessionContext s)
    {
        var v = DesktopServices.Vehicles;
        var sirket = v.Create(s, new NewVehicle("SRK-01", "34 SRK 01", CurrentMeter: 1000m));
        var eski = v.Create(s, new NewVehicle("KRL-01", Rental: new RentalInfo("Kiracı A.Ş.", Gun(DateTime.Today.AddDays(-40)))));
        v.Create(s, new NewVehicle("KRL-02", "06 KRL 02", Rental: new RentalInfo("Kiracı A.Ş.", Gun(DateTime.Today.AddDays(-3)),
            ReplacedVehicleId: eski, ReplacementReason: "Motor arızası")));
        DesktopServices.Materials.Create(s, new NewMaterial("FLT-01", "Yağ Filtresi"));

        var f = DesktopServices.Fuel;
        f.AddDepotEntry(s, new NewDepotEntry(5000m, 40m, "TRY", EntryDate: Gun(DateTime.Today.AddDays(-30))), Guid.NewGuid().ToString("N"));
        var bugun = DateTime.Today;
        decimal sayac = 1000m;
        foreach (var (gun, litre) in new[] { (1, 120m), (2, 40m), (3, 80m), (8, 200m), (9, 30m) })
        {
            if (gun > bugun.Day) continue;
            sayac += 50;
            f.Distribute(s, new NewDistribution(sirket, litre, sayac, DistributionDate: Gun(new DateTime(bugun.Year, bugun.Month, gun))), Guid.NewGuid().ToString("N"));
        }
        sayac += 50;
        f.Distribute(s, new NewDistribution(sirket, 60m, sayac, DistributionDate: Gun(bugun)), Guid.NewGuid().ToString("N"));
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
        catch { /* görüntü yalnız gözle kontrol içindir; testin sonucunu etkilemez */ }
    }

    private static IEnumerable<T> Gorunen<T>(Control kok) where T : Control
        => kok.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible);

    [AvaloniaFact]
    public void UIT10_Kiralik_Araclar_Ekrani()
    {
        var s = Ortam();
        var vm = new VehiclesViewModel(s, kiralik: true);
        var view = new VehiclesView { DataContext = vm };
        var w = Ac(view);

        Assert.Equal(new[] { "KRL-01", "KRL-02" }, vm.Items.Select(x => x.Code).OrderBy(x => x));
        Assert.Contains(Gorunen<SortHeader>(view), h => h.Text == "KİRALAYAN FİRMA");   // kira kolonu başlığı görünür
        Assert.Contains(Gorunen<TextBlock>(view), t => t.Text == "Kiracı A.Ş.");
        Assert.Equal("KRL-01", vm.Items.Single(x => x.Code == "KRL-02").ReplacedVehicle);
        Assert.Equal("passive", vm.Items.Single(x => x.Code == "KRL-01").Status);   // değişimle pasif
        Goruntu(w, "kiralik-liste");

        vm.ToggleAddCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Gorunen<TextBlock>(view), t => t.Text == "Kira Bilgisi");
        Assert.Contains(Gorunen<CheckBox>(view), c => (c.Content as string ?? "").Contains("YERİNE geldi"));
        Goruntu(w, "kiralik-form");
        w.Close();

        // Araç Listesi: kiralıklar YOK, kira formu YOK.
        var vm2 = new VehiclesViewModel(s);
        var view2 = new VehiclesView { DataContext = vm2 };
        var w2 = Ac(view2);
        Assert.Equal(new[] { "SRK-01" }, vm2.Items.Select(x => x.Code));
        vm2.ToggleAddCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Gorunen<TextBlock>(view2), t => t.Text == "Kira Bilgisi");
        w2.Close();
    }

    [AvaloniaFact]
    public void UIT11_Talep_Formu_Coklu_Arac_ve_Malzeme_Kodu()
    {
        var s = Ortam();
        var vm = new RequestsViewModel(s);
        var view = new RequestsView { DataContext = vm };
        var w = Ac(view);
        vm.NewRequestCommand.Execute(null);
        vm.MaterialSearch = "FLT";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Gorunen<TextBlock>(view), t => t.Text == "FLT-01");          // arama sonucunda KOD

        var araclar = vm.Vehicles.Where(x => x.InternalCode is "SRK-01" or "KRL-02").ToList();
        Assert.Contains(araclar, a => a.Display.EndsWith("(Kiralık)"));
        foreach (var a in araclar) { vm.NewItemVehicle = a; Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(2, vm.NewItemVehicles.Count);
        Assert.Null(vm.NewItemVehicle);                                              // kutu bir sonraki seçim için boşaldı
        Assert.Equal(2, Gorunen<Border>(view).Count(b => b.Classes.Contains("Chip")));

        vm.PickMaterialCommand.Execute(vm.MaterialResults.First());
        vm.NewItemQty = 4;
        vm.AddItemCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var kalem = Assert.Single(vm.FormItems);
        Assert.Equal(2, kalem.VehicleIds.Count);
        Assert.Contains(view.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == "FLT-01");   // kalem tablosunda KOD sütunu
        Goruntu(w, "talep-formu");
        w.Close();
    }

    [AvaloniaFact]
    public void UIT12_Yakit_Ozeti_Haftalar_Cubuk_Gunler_Kutucuk()
    {
        var s = Ortam();
        var vm = new FuelViewModel(s, 2);
        var view = new FuelView { DataContext = vm };
        var w = Ac(view, 1400, 1100);

        var buAy = Assert.Single(vm.MonthlySummary, m => m.S.IsCurrentMonth);
        Assert.True(buAy.IsExpanded);                                            // bu ay açık gelir
        Assert.NotEmpty(Gorunen<Border>(view).Where(b => b.Classes.Contains("FuelMonth")));
        Assert.NotEmpty(Gorunen<ProgressBar>(view).Where(p => p.Classes.Contains("FuelWeek")));   // haftalar = çubuk
        Assert.Equal(buAy.S.Days.Count, Gorunen<StackPanel>(view).Count(p => p.Classes.Contains("FuelDay")));   // günler = kutucuk
        Goruntu(w, "yakit-ozeti");

        buAy.IsExpanded = false;                                                 // kapatınca ayrıntı gizlenir
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Gorunen<ProgressBar>(view).Where(p => p.Classes.Contains("FuelWeek")));
        w.Close();
    }
}
