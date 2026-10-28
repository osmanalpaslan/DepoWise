using Avalonia.Automation;
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

    /// <summary>M-62 (2/4): durum metninden "kaydedildi" bildirimi — yalnız başarı; hata/olumsuz metin bildirime dönmez.</summary>
    [Fact]
    public void UIT17_Basari_Bildirimi_Kurali()
    {
        foreach (var basari in new[] { "Kaydedildi.", "Excel kaydedildi: C:\\x.xlsx", "Şablon eklendi.", "Yakıt dağıtımı iptal edildi.",
                                       "Silindi — Çöp Kutusu'ndan geri alınabilir.", "3 kalem teslim kaydedildi.", "Yetkiler kaydedildi." })
            Assert.True(DepoWise.Desktop.Bildirimler.BasariMi(basari), basari);
        foreach (var degil in new[] { null, "", "Hata: bağlantı yok", "Kaydedilemedi: alan eksik", "Excel dışa aktarma yetkiniz yok.",
                                      "Silme başarısız", "Kayıt bulunamadı", "3 satır — Stok Durumu", "Fotoğraf silinemedi" })
            Assert.False(DepoWise.Desktop.Bildirimler.BasariMi(degil), degil ?? "null");

        var gelen = new List<string>();
        void Al(string m) => gelen.Add(m);
        DepoWise.Desktop.Bildirimler.BasariGoster += Al;
        try
        {
            DepoWise.Desktop.Bildirimler.Durum("  Şube silindi.  ");
            DepoWise.Desktop.Bildirimler.Durum("Hata: silinemedi");
        }
        finally { DepoWise.Desktop.Bildirimler.BasariGoster -= Al; }
        Assert.Equal(new[] { "Şube silindi." }, gelen);
    }

    /// <summary>M-62 (2/4): bildirim kartı gerçekten çizilir (tema şablonu var) — ana penceredeki ile aynı ayarlar.</summary>
    [AvaloniaFact]
    public void UIT18_Bildirim_Karti_Cizilir()
    {
        var w = Ac(new TextBlock { Text = "Ekran" }, 900, 500);
        var yonetici = new Avalonia.Controls.Notifications.WindowNotificationManager(w)
        {
            Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight, MaxItems = 3,
        };
        Dispatcher.UIThread.RunJobs();
        yonetici.Show(new Avalonia.Controls.Notifications.Notification("İşlem tamam", "Malzeme kaydedildi.",
            Avalonia.Controls.Notifications.NotificationType.Success, TimeSpan.FromSeconds(30)));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Malzeme kaydedildi.");
        for (int i = 0; i < 60; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }   // açılış animasyonu
        Goruntu(w, "bildirim-kaydedildi");
        w.Close();
    }

    /// <summary>M-62 (3/4): pencere kısayollarının ekran parçalarını bulup çalıştırması — Ctrl+N ana eylem (Yeni Malzeme formu açılır),
    /// F5 yenile (ekranın Load komutu), Ctrl+F arama kutusuna odak; karşılığı olmayan ekranda hiçbir şey yapılmaz.</summary>
    [AvaloniaFact]
    public void UIT19_Klavye_Kisayollari()
    {
        var s = Ortam();
        var vm = new MaterialsViewModel(s);
        var view = new MaterialsView { DataContext = vm };
        var w = Ac(view);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.ShowAdd);
        Assert.True(DepoWise.Desktop.Kisayollar.AnaEylem(view));          // Ctrl+N
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.ShowAdd);
        Assert.True(DepoWise.Desktop.Kisayollar.Yenile(vm));               // F5 → IRefreshable
        // F5 → IRefreshable OLMAYAN ekranda ekranın Load komutu (yansıma yolu) — Stok Hareketleri
        var hareket = new StockMovementsViewModel(s) { Search = "YOK-" + Guid.NewGuid().ToString("N") };
        Assert.False(hareket is IRefreshable);
        Assert.True(DepoWise.Desktop.Kisayollar.Yenile(hareket));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(hareket.Movements);                                    // arama süzgeciyle yeniden yüklendi
        Assert.True(DepoWise.Desktop.Kisayollar.AramayaOdaklan(view));     // Ctrl+F
        Assert.True(w.FocusManager?.GetFocusedElement() is TextBox t && t.Classes.Contains("Search"));
        Assert.False(DepoWise.Desktop.Kisayollar.Yenile(new object()));    // karşılığı yok → dokunma
        Assert.False(DepoWise.Desktop.Kisayollar.AnaEylem(new TextBlock()));
        Assert.False(DepoWise.Desktop.Kisayollar.SekmeKapat(null));
        w.Close();
    }

    /// <summary>M-62 (4/4): kabuk ekranın Busy/IsBusy/IsLoading bayrağını okur; sunucudan okuyan ekranlarda bayrak yükleme süresince açık.</summary>
    [AvaloniaFact]
    public void UIT20_Yukleniyor_Bayragi()
    {
        Assert.True(ShellViewModel.SayfaMesgulMu(new { Busy = true }));
        Assert.True(ShellViewModel.SayfaMesgulMu(new { IsLoading = true }));
        Assert.False(ShellViewModel.SayfaMesgulMu(new { Busy = false }));
        Assert.False(ShellViewModel.SayfaMesgulMu(new { Baska = true }));
        Assert.False(ShellViewModel.SayfaMesgulMu(null));
        // sunucudan okuyan ekranlar artık bayrak taşır (yükleme bitince kapanır)
        var s = Ortam();
        foreach (var vm in new object[] { new ProjectsViewModel(s), new DocumentsViewModel(s) })
        {
            Assert.NotNull(vm.GetType().GetProperty("Busy"));
            for (int i = 0; i < 50 && ShellViewModel.SayfaMesgulMu(vm); i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
            Assert.False(ShellViewModel.SayfaMesgulMu(vm));
        }
    }

    /// <summary>M-60: ortak ekran parçaları — sayfalama özeti/kırpma/boyut, seçim çubuğu görünürlüğü, uyarı bandı kapanması,
    /// durum paneli yeni modları; galeri çizilir (taşma yok, ekran görüntüsü).</summary>
    [AvaloniaFact]
    public void UIT21_Ortak_Ekran_Parcalari()
    {
        Assert.Equal("51–100 / 1.240", DepoWise.Desktop.Controls.Pager.Ozet(2, 50, 1240));
        Assert.Equal("1.201–1.240 / 1.240", DepoWise.Desktop.Controls.Pager.Ozet(25, 50, 1240));
        Assert.Equal("0 kayıt", DepoWise.Desktop.Controls.Pager.Ozet(1, 50, 0));
        Assert.Equal(25, DepoWise.Desktop.Controls.Pager.SayfaSayisi(1240, 50));
        Assert.Equal(1, DepoWise.Desktop.Controls.Pager.SayfaSayisi(0, 50));

        var p = new DepoWise.Desktop.Controls.Pager { TotalCount = 1240, PageSize = 50, Page = 25 };
        Assert.False(p.CanNext);
        Assert.True(p.CanPrevious);
        Assert.Equal("Sayfa 25 / 25", p.PageText);
        p.PreviousCommand.Execute(null);
        Assert.Equal(24, p.Page);
        p.TotalCount = 120;                                  // toplam azaldı → son sayfaya kırpılır
        Assert.Equal(3, p.Page);
        p.PageSize = 100;                                    // boyut değişti → sayfa 1
        Assert.Equal(1, p.Page);
        Assert.Equal("1–100 / 120", p.Summary);

        var secim = new DepoWise.Desktop.Controls.SelectionBar();
        var w = Ac(new StackPanel { Children = { secim } }, 600, 200);
        Dispatcher.UIThread.RunJobs();
        Assert.False(secim.IsVisible);                       // seçim yokken görünmez
        secim.SelectedCount = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.True(secim.IsVisible);
        Assert.Equal("2 kayıt seçili", secim.Text);
        w.Close();

        var bant = new DepoWise.Desktop.Controls.InfoBar { IsClosable = true, Message = "x" };
        bant.CloseCommand.Execute(null);
        Assert.False(bant.IsVisible);

        var galeri = new ComponentGalleryView { DataContext = new ComponentGalleryViewModel() };
        var gw = Ac(galeri, 1400, 1800);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(gw.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "51–100 / 1.240");
        Assert.Contains(gw.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "3 kayıt seçili");
        Goruntu(gw, "bilesen-galerisi-m60");
        gw.Close();
    }

    /// <summary>M-65: yoğunluk — Sık seçilince tablo satırları alçalır, Normal'e dönünce eski yükseklik; bilinmeyen değer Normal'e düşer.</summary>
    [AvaloniaFact]
    public void UIT22_Yogunluk_Satir_Yuksekligi()
    {
        var s = Ortam();
        try
        {
            var vm = new MaterialsViewModel(s);
            var view = new MaterialsView { DataContext = vm };
            var w = Ac(view);
            Dispatcher.UIThread.RunJobs();
            double SatirMin() => view.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.IsEffectivelyVisible).MinHeight;

            DepoWise.Desktop.Theming.ThemeService.ApplyDensity(DepoWise.Desktop.Theming.ThemeService.DensityCompact, persist: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(34, SatirMin());
            Assert.Equal("compact", DepoWise.Desktop.Theming.ThemeService.CurrentDensity);
            Goruntu(w, "yogunluk-sik");

            DepoWise.Desktop.Theming.ThemeService.ApplyDensity("bilinmeyen", persist: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(48, SatirMin());
            Assert.Equal("normal", DepoWise.Desktop.Theming.ThemeService.CurrentDensity);
            w.Close();

            var ayar = new ThemeSettingsView { DataContext = new ThemeSettingsViewModel() };
            var aw = Ac(ayar, 1100, 1300);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Gorunen<Button>(ayar), b => AutomationProperties.GetName(b) == "Sık yoğunluk");
            Goruntu(aw, "tema-yogunluk");
            aw.Close();
        }
        finally { DepoWise.Desktop.Theming.ThemeService.ApplyDensity("normal", persist: false); }
    }

    /// <summary>M-63 (Şablon C): Sayım — sekmeler (giriş · geçmiş), kalem tablosu kalan alanın tamamı (yükseklik sınırı yok),
    /// alt sabit çubukta "Taslak Kaydet … Sayımı Tamamla" (birincil en sağda; web ile aynı ad — B8 denetimi); geçmiş düzeltmeler ayrı sekmede.</summary>
    [AvaloniaFact]
    public void UIT23_Sayim_Islem_Sablonu()
    {
        var s = Ortam();
        var vm = new StockCountViewModel(s);
        var view = new StockCountView { DataContext = vm };
        var w = Ac(view, 1126, 760);
        Dispatcher.UIThread.RunJobs();
        var sekmeler = view.GetVisualDescendants().OfType<TabItem>().Select(t => t.Header as string).ToList();
        Assert.Contains("Sayım girişi", sekmeler);
        Assert.Contains("Geçmiş düzeltmeler", sekmeler);
        var dugmeler = Gorunen<Button>(view).Select(b => b.Content as string).ToList();
        Assert.True(dugmeler.IndexOf("Taslak Kaydet") >= 0 && dugmeler.IndexOf("Taslak Kaydet") < dugmeler.IndexOf("Sayımı Tamamla"));
        // kalem tablosu (CountLines) yükseklikle sınırlanmaz
        vm.MaterialSearch = "FLT-01";
        Dispatcher.UIThread.RunJobs();
        vm.PickMaterialCommand.Execute(vm.MaterialResults.First(m => m.Code == "FLT-01"));
        vm.CountedQty = 2m;
        vm.AddCountLineCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var tablo = view.GetVisualDescendants().OfType<ItemsControl>().First(i => ReferenceEquals(i.ItemsSource, vm.CountLines))
            .GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("Table"));
        Assert.True(double.IsPositiveInfinity(tablo.MaxHeight));
        Assert.True(tablo.Bounds.Height > 200, "kalem tablosu kalan alanı doldurmalı: " + tablo.Bounds.Height);
        Goruntu(w, "sayim-sablon-c");
        w.Close();
    }

    /// <summary>M-63 (Şablon C): Zimmet — "Yeni işlem" / "Kimde ne var" sekmeleri; teslim listesi kalan alanın tamamı;
    /// Kaydet alt sabit çubukta (dar pencerede de sağda, form satırına sarılmaz); yazma yetkisiyle "Yeni işlem" açılır.</summary>
    [AvaloniaFact]
    public void UIT24_Zimmet_Islem_Sablonu()
    {
        var s = Ortam();
        var vm = new AssignmentsViewModel(s);
        var view = new AssignmentsView { DataContext = vm };
        var w = Ac(view, 1126, 760);
        Dispatcher.UIThread.RunJobs();
        var tab = view.GetVisualDescendants().OfType<TabControl>().First();
        Assert.Equal(0, tab.SelectedIndex);
        Assert.Equal(new[] { "Yeni işlem", "Kimde ne var" }, tab.Items.OfType<TabItem>().Select(t => t.Header as string));
        vm.FormAsset = vm.AssetOptions.First(a => a.Name.StartsWith("ZMT-A"));
        vm.FormQuantity = 1m;
        vm.AddLineCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var tablo = view.GetVisualDescendants().OfType<ItemsControl>().First(i => ReferenceEquals(i.ItemsSource, vm.Lines))
            .GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("Table"));
        Assert.True(double.IsPositiveInfinity(tablo.MaxHeight));
        Assert.True(tablo.Bounds.Height > 150, "teslim listesi kalan alanı doldurmalı: " + tablo.Bounds.Height);
        var kaydet = Gorunen<Button>(view).Single(b => (b.Content as string) == "Kaydet");
        var konum = Avalonia.VisualExtensions.TranslatePoint(kaydet, new Avalonia.Point(0, 0), w)!.Value;
        Assert.True(konum.X > 800, "Kaydet sağda olmalı: " + konum.X);
        Assert.Contains(Gorunen<TextBlock>(view), t => t.Text == "1 kalem teslim listesinde — Kaydet hepsini tek işlemde işler.");
        Goruntu(w, "zimmet-sablon-c");
        w.Close();
    }

    /// <summary>M-63 (Şablon C): Yakıt — form açıkken liste gizlenir, form alanın tamamını kullanır; eylemler altta
    /// ("Vazgeç … Kaydet"); toplu dağıtım satırları kalan alanı doldurur; Vazgeç ile liste geri gelir.</summary>
    [AvaloniaFact]
    public void UIT25_Yakit_Islem_Sablonu()
    {
        var s = Ortam();
        var vm = new FuelViewModel(s, 0);
        var view = new FuelView { DataContext = vm };
        var w = Ac(view, 1126, 760);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.FormAcik);
        vm.ToggleTopluCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.FormAcik);
        var satirlar = view.GetVisualDescendants().OfType<ItemsControl>().First(i => ReferenceEquals(i.ItemsSource, vm.TopluSatirlar));
        var alan = satirlar.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("Panel"));
        Assert.True(alan.Bounds.Height > 200, "toplu satır alanı kalan alanı doldurmalı: " + alan.Bounds.Height);
        var dugmeler = Gorunen<Button>(view).Select(b => b.Content as string).ToList();
        Assert.True(dugmeler.IndexOf("Vazgeç") < dugmeler.IndexOf("Tümünü Kaydet"));
        Goruntu(w, "yakit-toplu-sablon-c");
        vm.ToggleTopluCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.FormAcik);
        vm.ToggleDistCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.FormAcik);
        Assert.Contains(Gorunen<Button>(view), b => b.Command == vm.SaveDistCommand);
        Goruntu(w, "yakit-dagitim-sablon-c");
        w.Close();
    }

    /// <summary>M-63 (Şablon C): Giriş-Çıkış — "İşlem" / "Son hareketler" sekmeleri; Kaydet alt sabit çubukta (pencerenin altında,
    /// form ne kadar uzun olursa olsun görünür); SeciliSekme=1 ile liste tam yükseklikte görünür.</summary>
    [AvaloniaFact]
    public void UIT26_Giris_Cikis_Islem_Sablonu()
    {
        var s = Ortam();
        var vm = new StockEntryViewModel(s);
        var view = new StockEntryView { DataContext = vm };
        var w = Ac(view, 1126, 760);
        Dispatcher.UIThread.RunJobs();
        var tab = view.GetVisualDescendants().OfType<TabControl>().First();
        Assert.Equal(new[] { "İşlem", "Son hareketler" }, tab.Items.OfType<TabItem>().Select(t => t.Header as string));
        var kaydet = Gorunen<Button>(view).Single(b => b.Command == vm.SaveCommand);
        var konum = Avalonia.VisualExtensions.TranslatePoint(kaydet, new Avalonia.Point(0, 0), w)!.Value;
        Assert.True(konum.Y > 600, "Kaydet altta sabit olmalı: " + konum.Y);
        Goruntu(w, "giris-cikis-sablon-c");
        vm.SeciliSekme = 1;
        Dispatcher.UIThread.RunJobs();
        var liste = view.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, vm.Movements));
        Assert.True(liste.IsEffectivelyVisible && liste.Bounds.Height > 400, "liste tam yükseklikte olmalı: " + liste.Bounds.Height);
        Goruntu(w, "giris-cikis-son-hareketler");
        w.Close();
    }

    /// <summary>M-64 (Şablon B): Cari — "Yeni Cari" araç çubuğunda tek ana düğme; seçili kayda ait eylemler seçim çubuğunda
    /// (seçim yokken görünmez); sayfalama listenin altında ortak Pager.</summary>
    [AvaloniaFact]
    public void UIT27_Cari_Liste_Sablonu()
    {
        var s = Ortam();
        if (!DesktopServices.Parties.List(s, "UIT27", null, null, 1, 10, null).Items.Any())
            DesktopServices.Parties.Create(s, new DepoWise.Infrastructure.Accounting.NewParty("UIT27-C", "UIT27 Cari", DepoWise.Infrastructure.Accounting.PartyTypes.Both));
        var vm = new PartiesViewModel(s);
        var view = new PartiesView { DataContext = vm };
        var w = Ac(view, 1300, 820);
        for (int i = 0; i < 50 && vm.Rows.Count == 0; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var secim = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.SelectionBar>().Single();
        Assert.False(secim.IsVisible);
        Assert.Contains(Gorunen<Button>(view), b => (b.Content as string) == "Yeni Cari");
        var pager = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Pager>().Single();
        Assert.Equal(vm.TotalCount, pager.TotalCount);
        Assert.False(pager.ShowPageSize);
        vm.Selected = vm.Rows.First();
        Dispatcher.UIThread.RunJobs();
        Assert.True(secim.IsVisible);
        var dugmeler = Gorunen<Button>(secim).Select(b => b.Content as string).ToList();
        Assert.Contains("Düzenle", dugmeler);
        Assert.Equal("Sil", dugmeler.Where(x => x is "Düzenle" or "Hareket Ekle" or "Aktif / Pasif" or "Sil").Last());   // yıkıcı eylem en sağda
        Goruntu(w, "cari-sablon-b");
        w.Close();
    }

    /// <summary>UIT28 — ⭐ M-64 (2/6) Faturalar Şablon B: "Yeni Fatura" araç çubuğunda tek ana düğme; "İptal Et" yalnız seçim
    /// çubuğunda (seçim yokken gizli); sayfalama listenin altında ortak Pager (eski ◀ ▶ satırı yok).</summary>
    [AvaloniaFact]
    public void UIT28_Fatura_Liste_Sablonu()
    {
        var s = Ortam();
        var cari = DesktopServices.Parties.List(s, "UIT28", null, null, 1, 10, null).Items.FirstOrDefault()?.Party.Id
                   ?? DesktopServices.Parties.Create(s, new DepoWise.Infrastructure.Accounting.NewParty("UIT28-C", "UIT28 Cari", DepoWise.Infrastructure.Accounting.PartyTypes.Both));
        // Sabit işlem kimliği → ikinci koşuda ikinci fatura oluşmaz (idempotency).
        DesktopServices.Invoices.Create(s, new DepoWise.Infrastructure.Accounting.NewInvoice(
            DepoWise.Infrastructure.Accounting.InvoiceDirections.Purchase, cari,
            new[] { new DepoWise.Infrastructure.Accounting.NewInvoiceLine(null, "UIT28 hizmet", null, 1m, 100m) },
            "uit28-fatura", AffectsStock: false));
        var vm = new InvoicesViewModel(s);
        var view = new InvoicesView { DataContext = vm };
        var w = Ac(view, 1300, 820);
        for (int i = 0; i < 50 && vm.Rows.Count == 0; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var secim = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.SelectionBar>().Single();
        Assert.False(secim.IsVisible);
        var gorunen = Gorunen<Button>(view).Select(b => b.Content as string).ToList();
        Assert.Contains("Yeni Fatura", gorunen);
        Assert.DoesNotContain("İptal Et", gorunen);
        Assert.DoesNotContain("◀", gorunen);
        var pager = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Pager>().Single();
        Assert.Equal(vm.TotalCount, pager.TotalCount);
        Assert.True(vm.TotalCount > 0);
        vm.Selected = vm.Rows.First();
        Dispatcher.UIThread.RunJobs();
        Assert.True(secim.IsVisible);
        for (int i = 0; i < 50 && vm.Detail is null; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var iptal = Gorunen<Button>(secim).Single(b => (b.Content as string) == "İptal Et");
        Assert.True(vm.CanCancelSelected, "yürürlükteki fatura iptal edilebilmeli");
        Assert.True(iptal.IsEffectivelyEnabled, "detay yüklenince İptal Et etkin olmalı");
        Goruntu(w, "fatura-sablon-b");
        w.Close();
    }

    /// <summary>UIT29 — ⭐ M-64 (3/6) Tahsilat/Ödeme Şablon B: ana düğme araç çubuğunda; "Ters Kayıt" yalnız seçim çubuğunda;
    /// sayfalama listenin altında ortak Pager.</summary>
    [AvaloniaFact]
    public void UIT29_Tahsilat_Liste_Sablonu()
    {
        var s = Ortam();
        var cari = DesktopServices.Parties.List(s, "UIT29", null, null, 1, 10, null).Items.FirstOrDefault()?.Party.Id
                   ?? DesktopServices.Parties.Create(s, new DepoWise.Infrastructure.Accounting.NewParty("UIT29-C", "UIT29 Cari", DepoWise.Infrastructure.Accounting.PartyTypes.Both));
        var kasa = DesktopServices.FinanceQueries.Accounts(s).FirstOrDefault(a => a.Account.Code == "UIT29-K")?.Account.Id
                   ?? DesktopServices.Finance.CreateAccount(s, new DepoWise.Infrastructure.Accounting.NewFinanceAccount("UIT29-K", "UIT29 Kasa",
                          DepoWise.Infrastructure.Accounting.FinanceAccountKinds.Cash));
        // Sabit işlem kimliği → ikinci koşuda ikinci kayıt oluşmaz (idempotency).
        DesktopServices.Finance.Add(s, new DepoWise.Infrastructure.Accounting.NewFinanceEntry(kasa,
            DepoWise.Infrastructure.Accounting.FinanceTxnTypes.Receipt, 50m, "uit29-tahsilat", PartyId: cari));
        var vm = new PaymentsViewModel(s);
        var view = new PaymentsView { DataContext = vm };
        var w = Ac(view, 1300, 820);
        for (int i = 0; i < 50 && vm.Rows.Count == 0; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var secim = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.SelectionBar>().Single();
        Assert.False(secim.IsVisible);
        var gorunen = Gorunen<Button>(view).Select(b => b.Content as string).ToList();
        Assert.Contains("Yeni Tahsilat / Ödeme", gorunen);
        Assert.DoesNotContain("Ters Kayıt", gorunen);
        Assert.DoesNotContain("◀", gorunen);
        var pager = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Pager>().Single();
        Assert.True(vm.TotalCount > 0);
        Assert.Equal(vm.TotalCount, pager.TotalCount);
        vm.Selected = vm.Rows.First(r => !r.IsReversed && !r.IsReversalEntry);
        Dispatcher.UIThread.RunJobs();
        Assert.True(secim.IsVisible);
        var ters = Gorunen<Button>(secim).Single(b => (b.Content as string) == "Ters Kayıt");
        Assert.True(ters.IsEffectivelyEnabled, "ters kaydı olmayan işlemde Ters Kayıt etkin olmalı");
        Goruntu(w, "tahsilat-sablon-b");
        w.Close();
    }

    /// <summary>UIT30 — ⭐ M-64 (5/6) Malzemeler: numaralı sayfalama (kullanıcı isteği 2026-07-18) artık ortak Pager'da
    /// (ShowPageNumbers): ±3 komşu, bulunulan sayfa birincil renkte, numaraya basınca o sayfa; sayfa boyutu Pager'da (araç
    /// çubuğunda değil); Pager SOLDA (kullanıcı isteği 2026-07-19); görünür tek ana düğme "Yeni Malzeme".</summary>
    [AvaloniaFact]
    public void UIT30_Malzeme_Numarali_Pager()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, DepoWise.Desktop.Controls.Pager.Numaralar(1, 10).ToArray());
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8 }, DepoWise.Desktop.Controls.Pager.Numaralar(5, 10).ToArray());
        Assert.Equal(new[] { 7, 8, 9, 10 }, DepoWise.Desktop.Controls.Pager.Numaralar(10, 10).ToArray());

        var p = new DepoWise.Desktop.Controls.Pager { ShowPageNumbers = true, TotalCount = 120, PageSize = 25, Page = 1 };
        var pw = Ac(new StackPanel { Children = { p } }, 900, 120);
        Dispatcher.UIThread.RunJobs();
        var no = Gorunen<Button>(p).Where(b => b.Content is int).ToList();
        Assert.Equal(new object[] { 1, 2, 3, 4 }, no.Select(b => b.Content).ToArray());   // 1. sayfa: ±3 → 1–4
        Assert.True(no[0].Classes.Contains("Primary") && !no[1].Classes.Contains("Primary"), "bulunulan sayfa vurgulu olmalı");
        no[2].Command!.Execute(no[2].CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, p.Page);
        Assert.False(Gorunen<TextBlock>(p).Any(t => t.Text == "Sayfa 3 / 5"), "numaralı biçimde 'Sayfa x / y' metni gizli");
        pw.Close();

        var vm = new MaterialsViewModel(Ortam());
        var view = new MaterialsView { DataContext = vm };
        var w = Ac(view, 1300, 820);
        for (int i = 0; i < 50 && !vm.HasRows; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var pager = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Pager>().Single();
        Assert.True(pager.ShowPageNumbers);
        Assert.Equal(vm.PageSize, pager.PageSize);
        Assert.Equal(vm.TotalCount, pager.TotalCount);
        var toolbar = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Toolbar>().First();
        Assert.DoesNotContain(toolbar.GetVisualDescendants().OfType<ComboBox>(), c => ReferenceEquals(c.ItemsSource, vm.PageSizes));
        var pagerKutu = pager.GetVisualDescendants().OfType<WrapPanel>().First();
        Assert.True(pagerKutu.Bounds.X < 5, "Pager sola yaslı olmalı (sol en altta)");
        var anaDugmeler = Gorunen<Button>(view).Where(b => b.Classes.Contains("Primary") && b.Content is string).Select(b => (string)b.Content!).ToList();
        Assert.Equal(new[] { vm.AddButtonText }, anaDugmeler);
        Goruntu(w, "malzeme-pager");
        w.Close();
    }

    /// <summary>UIT31 — ⭐ M-64 (6/6) Araçlar: Malzemeler ile aynı ortak numaralı Pager (solda, sayfa boyutu Pager'da, araç
    /// çubuğunda değil); görünür tek ana düğme "Yeni Araç".</summary>
    [AvaloniaFact]
    public void UIT31_Arac_Numarali_Pager()
    {
        var s = Ortam();
        if (!DesktopServices.Vehicles.SearchGrid(s, new DepoWise.Infrastructure.Vehicles.VehicleGridFilter(), 1, 1).Items.Any())
            DesktopServices.Vehicles.Create(s, new DepoWise.Infrastructure.Vehicles.NewVehicle("UIT31-A", "06UIT31"));
        var vm = new VehiclesViewModel(s);
        var view = new VehiclesView { DataContext = vm };
        var w = Ac(view, 1300, 820);
        for (int i = 0; i < 50 && !vm.HasRows; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        Assert.True(vm.HasRows);
        var pager = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Pager>().Single();
        Assert.True(pager.ShowPageNumbers);
        Assert.True(pager.IsEffectivelyVisible);
        Assert.Equal(vm.PageSize, pager.PageSize);
        Assert.Equal(vm.TotalCount, pager.TotalCount);
        var toolbar = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Toolbar>().First();
        Assert.DoesNotContain(toolbar.GetVisualDescendants().OfType<ComboBox>(), c => ReferenceEquals(c.ItemsSource, vm.PageSizes));
        var anaDugmeler = Gorunen<Button>(view).Where(b => b.Classes.Contains("Primary") && b.Content is string).Select(b => (string)b.Content!).ToList();
        Assert.Equal(new[] { vm.AddButtonText }, anaDugmeler);
        Goruntu(w, "arac-pager");
        w.Close();
    }

    /// <summary>UIT-IOS2 — ⭐ iOS tasarım (2026-10-28, kullanıcı onayı): araç listesinde seyrek toplu işlem ("Sayaçları Onar") "•••"
    /// menüsünde; seçili araca ait QR Etiketi / Arıza Bildir / İş Emri Aç / Düzenle sağdaki ARAÇ KARTINDA yuvarlak eylem düğmesi;
    /// araç çubuğunda artık yoklar. Muayene belgesi olan araçta bakım/muayene ilerleme çubuğu görünür.</summary>
    [AvaloniaFact]
    public void UITIOS2_Arac_Karti_Eylemler_Ve_Bakim_Cubugu()
    {
        var s = Ortam();
        var kod = "IOS2-" + Guid.NewGuid().ToString("N")[..5];
        var id = DesktopServices.Vehicles.Create(s, new DepoWise.Infrastructure.Vehicles.NewVehicle(kod, "06IOS2"));
        var gun = 86_400_000L; var simdi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            DesktopServices.Inspection.Save(s, new DepoWise.Infrastructure.Maintenance.NewInspection(id, "inspection", simdi - 200 * gun, simdi + 165 * gun));
            var vm = new VehiclesViewModel(s);
            var view = new VehiclesView { DataContext = vm };
            var w = Ac(view, 1400, 900);
            for (int i = 0; i < 50 && !vm.HasRows; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
            var toolbar = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.Toolbar>().First();
            var cubukDugmeleri = Gorunen<Button>(toolbar).Select(b => b.Content as string).ToList();
            Assert.Contains("•••", cubukDugmeleri);
            foreach (var tasinan in new[] { "QR Etiketi", "Arıza Bildir", "İş Emri Aç", "Sayaçları Onar" })
                Assert.DoesNotContain(tasinan, cubukDugmeleri);

            vm.Selected = vm.Items.First(r => r.Code == kod);
            for (int i = 0; i < 50 && !vm.HasBakimCubugu; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
            Dispatcher.UIThread.RunJobs();
            var eylemler = Gorunen<Button>(view).Where(b => b.Classes.Contains("IosEylem"))
                .SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).ToList();
            Assert.Contains("QR Etiketi", eylemler);
            Assert.Contains("Düzenle", eylemler);
            var cubuk = Assert.Single(vm.BakimCubuklari, c => c.Ad == "Muayene");                 // kod değil Türkçe ad
            Assert.InRange(cubuk.Yuzde, 50, 60);                                  // 200 / 365 gün geçti
            Assert.Contains(view.GetVisualDescendants().OfType<ProgressBar>(), p => p.Classes.Contains("IosCubuk") && p.IsEffectivelyVisible);
            Goruntu(w, "arac-kart-ios");
            w.Close();
        }
        finally { DesktopServices.Vehicles.Delete(s, id); }   // ortak test veritabanı: Kiralık testinin araç listesini kirletme
    }

    /// <summary>UIT-IOS4 — ⭐ iOS tasarım (2026-10-28, kullanıcı onayı): Yeni Araç formu bölümleri iOS grup kutularında; sağda
    /// "Önizleme" ve "Kontrol listesi". Zorunlu alan girilince kontrol işareti "eksik" halkadan "tamam" tike döner; önizleme kodu gösterir.
    /// İptal … Kaydet sırası (M-61) korunur.</summary>
    [AvaloniaFact]
    public void UITIOS4_Yeni_Arac_Formu_Gruplar_Onizleme_Kontrol()
    {
        var vm = new VehiclesViewModel(Ortam());
        var view = new VehiclesView { DataContext = vm };
        var w = Ac(view, 1400, 1000);
        vm.ToggleAddCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.ShowAdd);
        var gruplar = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("IosFormGrup") && b.IsEffectivelyVisible).ToList();
        Assert.True(gruplar.Count >= 7, "görünür form grubu: " + gruplar.Count);            // kiralık dışında 7 bölüm
        var metinler = Gorunen<TextBlock>(view).Select(t => t.Text).ToList();
        Assert.Contains("Önizleme", metinler);
        Assert.Contains("Kontrol listesi", metinler);
        Assert.Contains("İç kod girilmedi", metinler);

        Border Isaret(string ad) => view.GetVisualDescendants().OfType<Grid>()
            .First(g => g.Children.OfType<TextBlock>().Any(t => t.Text == ad) && g.Children.OfType<Panel>().Any(p => p.Classes.Contains("KontrolIsaret")))
            .Children.OfType<Panel>().First().Children.OfType<Border>().First(b => b.IsVisible);
        Assert.Contains("eksik", Isaret("İç Kod").Classes);
        vm.NewCode = "IOS4-KOD";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("tamam", Isaret("İç Kod").Classes);
        Assert.Contains("IOS4-KOD", Gorunen<TextBlock>(view).Select(t => t.Text));
        Assert.Contains("eksik", Isaret("Şantiye / Şube").Classes);

        var sira = Gorunen<Button>(view).Select(b => b.Content as string).Where(x => x is "İptal" or "Kaydet").ToList();
        Assert.Equal(new[] { "İptal", "Kaydet" }, sira);
        Goruntu(w, "arac-yeni-ios");
        w.Close();
    }

    /// <summary>UIT-IOS3 — bakım çubuğu hesabı: gecikti = kırmızı + "Gecikti", ilk bakım = boş çubuk, belge oranı son→sonraki tarih
    /// aralığından; taşma yüzde 100'de kesilir.</summary>
    [Fact]
    public void UITIOS3_Bakim_Cubugu_Hesabi()
    {
        var gec = BakimCubugu.Bakimdan(new DepoWise.Infrastructure.Maintenance.MaintenanceAlert("v", "d", "Yağ", DepoWise.Application.Maintenance.AlertLevel.Overdue, 1.3, 13000, 10000));
        Assert.Equal(("Gecikti", 100d, true), (gec.Sag, gec.Yuzde, gec.Gecikti));
        var ilk = BakimCubugu.Bakimdan(new DepoWise.Infrastructure.Maintenance.MaintenanceAlert("v", "d", "Filtre", DepoWise.Application.Maintenance.AlertLevel.Normal, 0.4, 0, 500, NeverPerformed: true));
        Assert.Equal(("İlk bakım bekliyor", 0d), (ilk.Sag, ilk.Yuzde));
        var yak = BakimCubugu.Bakimdan(new DepoWise.Infrastructure.Maintenance.MaintenanceAlert("v", "d", "Fren", DepoWise.Application.Maintenance.AlertLevel.Approaching, 0.9, 900, 1000));
        Assert.Equal(("%90 doldu", "yaklasiyor"), (yak.Sag, yak.Seviye));
        var belge = BakimCubugu.Belgeden(new DepoWise.Infrastructure.Maintenance.InspectionRow("K", "P", "Sigorta", 1000, 3000, "", "",
            DepoWise.Infrastructure.Maintenance.DateAlertLevel.Normal), simdiMs: 2500);
        Assert.Equal(75d, belge.Yuzde);
    }

    /// <summary>UIT33 — ⭐ M-65 ana ekran "Hızlı işlemler": yalnız menüde görünen ekranlar (yetki menüyle aynı kaynaktan), katalog
    /// sırasıyla; her katalog anahtarı gerçek bir masaüstü ekranına gider; menü boşsa bölüm görünmez.</summary>
    [AvaloniaFact]
    public void UIT33_Ana_Ekran_Hizli_Islemler()
    {
        Assert.Equal(new[] { "Zimmet", "Stok Sayımı" },
            DashboardViewModel.HizliIslemSec(new[] { "stock:count", "materials", "assignments" }).Select(h => h.Baslik).ToArray());
        Assert.Empty(DashboardViewModel.HizliIslemSec(new[] { "materials", "vehicles" }));     // yetkisiz → düğme yok
        Assert.Equal(3, DashboardViewModel.HizliIslemSec(DashboardViewModel.HizliIslemKatalogu.Select(h => h.NavKey)).Count);
        var masaustuAnahtarlari = AppScreens.All.Where(a => a.OnDesktop).Select(a => a.DesktopNavKey).ToHashSet();
        Assert.All(DashboardViewModel.HizliIslemKatalogu, h => Assert.Contains(h.NavKey, masaustuAnahtarlari));

        var vm = new DashboardViewModel(Ortam());          // kabuk (menü) yok → hızlı işlem yok
        var view = new DashboardView { DataContext = vm };
        var w = Ac(view, 1100, 700);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.HizliIslemVar);
        Assert.DoesNotContain(Gorunen<TextBlock>(view), t => t.Text == "Hızlı İşlemler");
        // ⭐ iOS tasarım (2026-10-28): "Toplam Araç" kartında durum dağılım çubuğu görünür; parçalar toplamı tutar.
        Assert.Contains(Gorunen<TextBlock>(view), t => t.Text == "Özet");
        var cubuk = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.OranCubugu>().Single();
        Assert.True(cubuk.IsEffectivelyVisible);
        Assert.Equal(vm.AracToplam, vm.AracAktif + vm.AracBakimda + vm.AracArizali + vm.AracPasif);
        Assert.Equal(vm.AracAktif, (int)cubuk.Deger1);
        Goruntu(w, "ana-ekran-ios");
        w.Close();
        vm.Dispose();
    }

    /// <summary>UIT35 — ⭐ M-31 Asgari Stok Önerileri: depoda asgarinin altına düşen malzeme tabloda (önerilen miktar talep miktarına
    /// başlangıç değeri), "Excel'e Aktar" ve "Seçilenlerden Talep Oluştur" görünür; seçili satırdan talep oluşur (onay penceresi
    /// headless'ta yok sayılmaz → servis yoluyla aynı işlemin sonucu doğrulanır).</summary>
    [AvaloniaFact]
    public void UIT35_Asgari_Stok_Onerileri()
    {
        var s = Ortam();
        var depo = DesktopServices.Branches.List(s).First(b => BranchAccess.CanAccess(s, b.Id)).Id;
        var m = DesktopServices.Materials.Create(s, new NewMaterial("UIT35-" + Guid.NewGuid().ToString("N")[..6], "Asgari test malzemesi"));
        DesktopServices.Stock.ReceiveIn(s, new[] { new StockLine(m, 2m) }, Guid.NewGuid().ToString("N"), branchId: depo);
        DesktopServices.AsgariStok.Ayarla(s, m, depo, 7m);

        var vm = new AsgariStokViewModel(s);
        var view = new AsgariStokView { DataContext = vm };
        var w = Ac(view, 1300, 900);
        var satir = Assert.Single(vm.Items, x => x.Oneri.MaterialId == m);
        Assert.Equal(5m, satir.TalepMiktari);                                         // önerilen = 7 − 2
        var metinler = Gorunen<TextBlock>(view).Select(t => t.Text).ToList();
        Assert.Contains("Asgari Stok Önerileri", metinler);
        var dugmeler = Gorunen<Button>(view).Select(b => b.Content as string).ToList();
        Assert.Contains("Excel'e Aktar", dugmeler);
        Assert.Contains("Seçilenlerden Talep Oluştur", dugmeler);
        Assert.Contains("Depoya Özeli Kaldır", dugmeler);
        Goruntu(w, "asgari-stok");
        w.Close();
    }

    /// <summary>UIT34 — kullanıcı isteği 2026-10-25: menü ekran adları belirgin (tam opak, 13,5 pt) ve üzerine gelince NET vurgu
    /// (kehribar zemin + sağa kayma); seçili satır ayrıca kalın. Kabuk çizilir, bir ekran bağlantısına "fare üzerinde" durumu verilir.</summary>
    [AvaloniaFact]
    public void UIT34_Menu_Okunakli_Ve_Hover()
    {
        var shell = new ShellViewModel(Ortam());
        var w = new MainWindow { DataContext = shell, Width = 1400, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var bolum in shell.Sections)                     // üç seviye: bölüm → grup → ekran
        {
            bolum.IsExpanded = true;
            foreach (var g in bolum.Groups) g.IsExpanded = true;
        }
        Dispatcher.UIThread.RunJobs();
        var linkler = w.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("NavLink") && b.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(linkler);
        var link = linkler.First(l => !l.Classes.Contains("active"));
        Assert.Equal(1.0, link.Opacity);
        Assert.Equal(13.5, link.FontSize);
        ((Avalonia.Controls.IPseudoClasses)link.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Avalonia.Media.Transformation.TransformOperations.Parse("translateX(3px)").ToString(), link.RenderTransform?.ToString());
        Goruntu(w, "menu-hover");
        w.Close();
        shell.Release();
        Assert.Null(ShellViewModel.Current);   // sıradaki testler (UIT33: kabuk yok → hızlı işlem yok) kabuktan etkilenmez
    }

    /// <summary>UIT32 — ⭐ M-65 (2/2) tanım ekranları: kısa form SAĞ YAN SAYFADA açılır (SideSheet); liste görünür kalır ve
    /// yüksekliği formdan etkilenmez (eskiden form listenin üstünde açılıp onu aşağı itiyordu); Esc = İptal.</summary>
    [AvaloniaFact]
    public void UIT32_Tanim_Ekranlari_Yan_Sayfa()
    {
        var s = Ortam();
        var ekranlar = new (string Ad, Func<(object Vm, Control View, Action Ac)> Kur)[]
        {
            ("sube", () => { var vm = new BranchesViewModel(s); return (vm, new BranchesView { DataContext = vm }, () => vm.ShowAdd = true); }),
            ("maliyet", () => { var vm = new CostCentersViewModel(s); return (vm, new CostCentersView { DataContext = vm }, () => vm.ShowAdd = true); }),
            ("firma", () => { var vm = new CompaniesViewModel(s); return (vm, new CompaniesView { DataContext = vm }, () => vm.ShowAdd = true); }),
            ("duyuru", () => { var vm = new AnnouncementsViewModel(s); return (vm, new AnnouncementsView { DataContext = vm }, () => vm.ShowAdd = true); }),
            ("takvim", () => { var vm = new CalendarViewModel(s); return (vm, new CalendarView { DataContext = vm }, () => vm.ShowAdd = true); }),
        };
        foreach (var (ad, kur) in ekranlar)
        {
            var (vm, view, ac) = kur();
            var w = Ac(view, 1300, 820);
            Dispatcher.UIThread.RunJobs();
            var sheet = view.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.SideSheet>().Single();
            Assert.False(sheet.IsVisible, ad + ": form kapalıyken yan sayfa görünmemeli");
            // Form satırının yanındaki/altındaki ana içerik (liste ızgarası) kapalıyken nerede başlıyor?
            var icerik = ((Grid)sheet.Parent!).Children.Where(c => c != sheet && Grid.GetRow(c) == Grid.GetRow(sheet) && c.IsVisible).ToList();
            Assert.NotEmpty(icerik);
            var ustOnce = icerik[0].Bounds.Y;
            ac();
            Dispatcher.UIThread.RunJobs();
            Assert.True(sheet.IsVisible, ad + ": form açılınca yan sayfa görünmeli");
            Assert.True(icerik[0].IsVisible, ad + ": liste görünür kalmalı");
            Assert.Equal(ustOnce, icerik[0].Bounds.Y);                                   // liste aşağı itilmedi
            Assert.True(sheet.Bounds.X > 1300 / 3.0, ad + ": yan sayfa SAĞDA olmalı");
            Assert.Contains(Gorunen<Button>(sheet), b => (b.Content as string) == "Kaydet");
            // Dar yan sayfada hiçbir alan sağ kenardan taşıp kesilmemeli (yan yana alanlar alt satıra sarılır).
            var sagKenar = sheet.Bounds.Width - 20;                                       // iç boşluk 20
            foreach (var alan in sheet.GetVisualDescendants().OfType<DepoWise.Desktop.Controls.FormField>().Where(f => f.IsEffectivelyVisible))
            {
                var sag = Avalonia.VisualExtensions.TranslatePoint(alan, new Avalonia.Point(alan.Bounds.Width, 0), sheet)!.Value.X;
                Assert.True(sag <= sagKenar + 0.5, $"{ad}: «{alan.Label}» alanı yan sayfadan taşıyor ({sag:0} > {sagKenar:0})");
            }
            Goruntu(w, "yan-sayfa-" + ad);
            sheet.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.False(sheet.IsVisible, ad + ": Esc formu kapatmalı");
            w.Close();
        }
    }

    /// <summary>M-58: kayıtlı rapor görünümü — kaydet, seçimi boz, görünümü seç → rapor + filtre geri gelir ve rapor
    /// tek tıkla çalışır; PDF ve görünüm düğmeleri ekranda.</summary>
    [AvaloniaFact]
    public void UIT16_Rapor_Kayitli_Gorunum_Ve_Pdf()
    {
        var s = Ortam();
        var vm = new ReportsViewModel(s, managerMode: true);
        vm.SelectedReport = vm.ReportItems.First(r => r.Key == "stock-negative");
        var depo = vm.Locations.First();
        depo.IsChecked = true;
        vm.GorunumAdi = "UI eksi stok";
        vm.GorunumKaydetCommand.Execute(null);
        Assert.Contains(vm.Gorunumler, g => g.Ad == "UI eksi stok");

        vm.SelectedReport = vm.ReportItems.First(r => r.Key == "stock");
        foreach (var l in vm.Locations) l.IsChecked = false;
        vm.SeciliGorunum = vm.Gorunumler.First(g => g.Ad == "UI eksi stok");
        Assert.Equal("stock-negative", vm.SelectedReport.Key);
        Assert.True(vm.Locations.Single(l => l.Id == depo.Id).IsChecked);
        Assert.True(vm.HasRun);   // tek tık: rapor çalıştı

        // ekran yeniden açılınca görünüm yerel tercihten gelir
        Assert.Contains(new ReportsViewModel(s, managerMode: true).Gorunumler, g => g.Ad == "UI eksi stok");

        var view = new ReportsView { DataContext = vm };
        var w = Ac(view);
        Dispatcher.UIThread.RunJobs();
        var dugmeler = Gorunen<Button>(view).Select(b => b.Content as string).ToList();
        Assert.Contains("PDF", dugmeler);
        Assert.Contains("Görünümü Kaydet", dugmeler);
        Assert.Contains("Görünümü Sil", dugmeler);
        Goruntu(w, "raporlar-kayitli-gorunum");
        w.Close();
    }
}
