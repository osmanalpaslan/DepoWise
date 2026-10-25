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
            // 2026-10-11: arayüz test sınıfları AYNI test ortamını paylaşır — biri başlattıysa yeniden başlatılmaz
            // (yoksa ikinci sınıf ortamı değiştirip birincinin tohum verisini görünmez kılardı).
            if (DesktopServices.Factory is null)
            {
                Environment.SetEnvironmentVariable("DEPOWISE_ENVIRONMENT", "UiTest_" + Guid.NewGuid().ToString("N")[..8]);
                DesktopServices.Initialize(DesktopBootstrap.Run());
            }
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

    /// <summary>Kullanıcı isteği 2026-10-24: Sayım Girişi'nde malzeme aranırken sonuçlarda KOD — AD görünür; "Listeye Ekle" sonrası
    /// listedeki satırda da kod ve ad birlikte görünür.</summary>
    [AvaloniaFact]
    public void UIT13_Sayim_Arama_ve_Liste_Kod_Ad()
    {
        var s = Ortam();
        var vm = new StockCountViewModel(s);
        var view = new StockCountView { DataContext = vm };
        var w = Ac(view);
        vm.MaterialSearch = "Yağ Filtresi";   // ADLA arama
        Dispatcher.UIThread.RunJobs();
        var sonuc = Gorunen<Button>(view).Select(b => b.Content as string).Where(x => x is not null).ToList();
        Assert.Contains("FLT-01 — Yağ Filtresi", sonuc);
        Assert.DoesNotContain("Yağ Filtresi", sonuc);   // yalnız ad görünmez
        Goruntu(w, "sayim-arama-kod-ad");

        vm.PickMaterialCommand.Execute(vm.MaterialResults.First(m => m.Code == "FLT-01"));
        Assert.Equal("FLT-01 — Yağ Filtresi", vm.MaterialSearch);   // seçilen malzeme kutuda da KOD — AD
        vm.CountedQty = 3m;
        vm.AddCountLineCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var metinler = Gorunen<TextBlock>(view).Select(t => t.Text).ToList();
        Assert.Contains("FLT-01", metinler);        // listede kod kolonu
        Assert.Contains("Yağ Filtresi", metinler);  // ve ad kolonu
        Goruntu(w, "sayim-liste-kod-ad");
        w.Close();
    }

    /// <summary>Kullanıcı isteği 2026-10-24: Giriş-Çıkış "Yeni Kayıt"ta da "Listeye Ekle" sepeti görünür; mevcut malzeme miktar ve
    /// birim fiyatıyla listeye girer; aynı malzeme tekrar eklenince miktar toplanır; tür değişince sepet boşalır.</summary>
    [AvaloniaFact]
    public void UIT14_Yeni_Kayit_Coklu_Malzeme_Sepeti()
    {
        var s = Ortam();
        var vm = new StockEntryViewModel(s);
        var view = new StockEntryView { DataContext = vm };
        var w = Ac(view);
        Assert.Equal("Yeni Kayıt", vm.SelectedKind);
        Assert.True(vm.ShowExitLines);
        Assert.Contains(Gorunen<Button>(view), b => (b.Content as string) == "+ Listeye Ekle");

        vm.MaterialSearch = "FLT-01";
        Dispatcher.UIThread.RunJobs();
        vm.PickMaterialCommand.Execute(vm.MaterialResults.First(m => m.Code == "FLT-01"));
        vm.Quantity = 4m; vm.UnitPrice = 25m;
        vm.AddExitLineCommand.Execute(null);
        vm.MaterialSearch = "FLT-01";
        Dispatcher.UIThread.RunJobs();
        vm.PickMaterialCommand.Execute(vm.MaterialResults.First(m => m.Code == "FLT-01"));
        vm.Quantity = 6m; vm.UnitPrice = 35m;
        vm.AddExitLineCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var satir = Assert.Single(vm.ExitLines);
        Assert.Equal(10m, satir.Quantity);
        Assert.Equal(31m, satir.UnitPrice);   // (4·25 + 6·35) / 10
        Assert.Contains(Gorunen<TextBlock>(view), t => t.Text == "FLT-01 — Yağ Filtresi");
        Goruntu(w, "giris-yeni-kayit-sepet");

        vm.SelectedKind = "Depo Çıkışı";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.ExitLines);   // giriş listesi çıkışa taşınmaz
        w.Close();
    }

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
    /// <summary>⭐ M-56 2/2: stok hareketi satırında "Kaynağa Git" çizilir; kaynak YEREL veritabanından (SQLite lehçesi) bulunur ve
    /// hedef ekranlar derin bağlantıyla kaydı açar (iş emri, talep); bakım "Uyarılar" kısayolu doğru sekmede açılır.</summary>
    [AvaloniaFact]
    public void UIT15_Hareketten_Kaynaga_Gitme()
    {
        var s = Ortam();
        var mat = DesktopServices.Materials.Create(s, new NewMaterial("KYN-01", "Kaynak Malzemesi"));
        var wo = DesktopServices.WorkOrders.Create(s, new DepoWise.Infrastructure.WorkOrders.NewWorkOrder("UI-IE-15", "Kaynak işi"));
        var belge = DesktopServices.Stock.ReceiveIn(s, new[] { new StockLine(mat, 3m) }, Guid.NewGuid().ToString("N")).DocumentId;
        using (var c = DesktopServices.Factory!.Create())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO work_order_links(id, work_order_id, company_id, entity_type, entity_id, created_at, updated_at) " +
                              $"VALUES('{Guid.NewGuid():N}','{wo}','{s.CompanyId}','stock_document','{belge}',1,1);";
            cmd.ExecuteNonQuery();
        }

        var vm = new StockMovementsViewModel(s) { Search = "KYN-01" };
        vm.LoadCommand.Execute(null);
        var view = new StockMovementsView { DataContext = vm };
        var w = Ac(view);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Gorunen<Button>(view), b => (b.Content as string) == "Kaynağa Git");
        Goruntu(w, "stok-hareketleri-kaynak");
        w.Close();

        var satir = vm.Movements.Single(m => m.DocumentId == belge);
        var k = DesktopServices.Stock.KaynakBul(s, satir.DocumentId, satir.MovementType, satir.Note)!;
        Assert.Equal(("is_emri", "work_orders", wo), (k.Tur, k.EkranKey, k.HedefId));
        var ie = new WorkOrdersViewModel(s);
        ie.OpenEntity(k.HedefId);
        Assert.Equal(wo, ie.Selected?.Id);

        var talep = DesktopServices.Requests.Create(s, new DepoWise.Infrastructure.Requests.NewRequest(
            new[] { new DepoWise.Infrastructure.Requests.RequestItemInput(mat, 1m) }));
        var tv = new RequestsViewModel(s) { Search = "olmayan" };
        tv.OpenEntity(talep.Id);
        Assert.Equal(talep.Id, tv.Selected?.Id);   // arama süzgeci temizlenip kayıt seçildi

        Assert.Equal(3, new MaintenanceViewModel(s, 3).SelectedTab);
    }
}
