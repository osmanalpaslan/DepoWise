using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DepoWise.Application.Security;
using DepoWise.Desktop.ViewModels;
using DepoWise.Desktop.Views;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.UiTests;

/// <summary>
/// ⭐ 2026-10-12 — YENİ KAYIT EKRANLARI GÖRSEL TARAMASI (kullanıcı talebi: TALEP_2026-10-12_gorsel.md).
///
/// <para><b>Ne yapar:</b> öncelikli ekranları "yeni kayıt" durumunda, iki SABİT içerik genişliğinde (1126 px = 1366 px
/// ekranda tam açık pencere − sol menü; 940 px = varsayılan 1180 px pencere − sol menü) ve sabit tohum veriyle çizer;
/// <c>artifacts/ui-ekran/gorsel/&lt;ekran&gt;-&lt;genişlik&gt;.png</c> kaydeder ve OTOMATİK ölçer:
/// (1) pencere dışına taşan görünür kontrol, (2) tek satırlık metnin kutusuna sığmayıp kesilmesi,
/// (3) aynı panelde üst üste binen form alanları. Bulgular <c>gorsel/bulgular.txt</c>'ye yazılır.</para>
///
/// <para><b>Bilinen fark (canlı ↔ görünmez ekran):</b> ekran burada kabuksuz (sol menü/üst bar yok) ve sabit genişlikte
/// çizilir; yazı tipi Inter (uygulamadaki ile aynı aile ama sistem yazı tipi ipucu/ClearType yok), DPI 96. Görüntü canlı
/// uygulamanın BİREBİR kopyası değildir; yerleşim/taşma için güvenilirdir, piksel inceliği için değildir.</para>
///
/// <para><b>Sınır:</b> testler yalnız <c>UiTest_*</c> ortamındaki tohum veriyi kullanır; hiçbir kayıt KAYDEDİLMEZ
/// (formlar yalnız açılır). Taşma ölçümü şimdilik RAPOR amaçlıdır; düzeltilen ekranlar için ayrıca kilitlenir.</para>
/// </summary>
public class GorselTaramaTests
{
    private static readonly object Kilit = new();
    private static SessionContext? _s;
    private static readonly int[] Genislikler = { 1126, 940 };
    private const int Yukseklik = 1200;   // uzun formun tamamı görünsün (canlıda kaydırılır)

    private static SessionContext Ortam()
    {
        lock (Kilit)
        {
            if (_s is not null) return _s;
            if (DesktopServices.Factory is null)
            {
                Environment.SetEnvironmentVariable("DEPOWISE_ENVIRONMENT", "UiTest_" + Guid.NewGuid().ToString("N")[..8]);
                DesktopServices.Initialize(DesktopBootstrap.Run());
            }
            var uid = DesktopServices.Users.EnsureInitialAdmin("UI", "ui.gorsel", "ui123456", RoleKeys.CompanyAdmin);
            _s = new SessionContext(uid, "UI", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
            // Sabit, güvenli tohum: seçim kutularında gerçekçi (uzun Türkçe) değerler olsun.
            DesktopServices.Branches.Create(_s, new NewBranch("Merkez Şantiye Deposu (Gölbaşı)"));
            DesktopServices.Materials.Create(_s, new NewMaterial("GRS-001", "Hidrolik Yağ Filtresi — Ağır İş Makinesi"));
            DesktopServices.Personnel.Create(_s, new NewPersonnel("Şükrü Öztürkoğlu", null, null, null));
            // ARAÇ TOHUMU YOK: test ortamı sınıflar arasında ortak; UIT10 araç listesini birebir sayar ("SRK-01").
            // Burada araç eklemek koşu sırasına göre UIT10'u düşürüyordu (2026-10-12, kararsız hata).
            return _s;
        }
    }

    private static string Klasor()
    {
        var k = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-ekran", "gorsel"));
        Directory.CreateDirectory(k);
        return k;
    }

    // ── Otomatik ölçümler ─────────────────────────────────────────────────────────────────────────────
    private static IEnumerable<T> Gorunen<T>(Visual kok) where T : Control
        => kok.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0);

    private static Rect PencereKonumu(Control c, Window w)
        => c.TransformToVisual(w) is { } m ? new Rect(c.Bounds.Size).TransformToAABB(m) : default;

    private static bool YatayKaydirmaIcinde(Control c)
        => c.GetVisualAncestors().OfType<ScrollViewer>().Any(s => s.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                                                                 && s.Extent.Width > s.Viewport.Width + 1);

    public static List<string> Olc(Window w)
    {
        var bulgu = new List<string>();
        var genislik = w.ClientSize.Width;
        // 1) pencere dışına taşan etkileşimli/metin kontrolleri
        foreach (var c in Gorunen<Control>(w).Where(c => c is TextBox or ComboBox or Button or NumericUpDown or CheckBox or TextBlock or Controls.LookupBox))
        {
            var r = PencereKonumu(c, w);
            if (r.Right > genislik + 1 && !YatayKaydirmaIcinde(c))
                bulgu.Add($"TAŞMA: {Ad(c)} sağ kenar {r.Right:0} > {genislik:0}");
        }
        // 2) tek satırlık metin kesilmesi (sarma yok, kutu metinden dar)
        foreach (var t in Gorunen<TextBlock>(w).Where(t => t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap && !string.IsNullOrWhiteSpace(t.Text)))
        {
            var olcu = new TextBlock { Text = t.Text, FontSize = t.FontSize, FontFamily = t.FontFamily, FontWeight = t.FontWeight, FontStyle = t.FontStyle, LetterSpacing = t.LetterSpacing };
            olcu.Measure(Size.Infinity);
            if (olcu.DesiredSize.Width > t.Bounds.Width + 2 && t.Bounds.Width < 2000)
                bulgu.Add($"KESİK METİN: \"{Kisalt(t.Text!)}\" ({olcu.DesiredSize.Width:0} px gerek, {t.Bounds.Width:0} px var)");
        }
        // 3) aynı panelde üst üste binen form alanları
        foreach (var grup in Gorunen<Controls.FormField>(w).GroupBy(f => f.GetVisualParent()))
        {
            var liste = grup.ToList();
            for (int i = 0; i < liste.Count; i++)
                for (int j = i + 1; j < liste.Count; j++)
                {
                    var a = PencereKonumu(liste[i], w); var b = PencereKonumu(liste[j], w);
                    var kes = a.Intersect(b);
                    if (kes.Width > 2 && kes.Height > 2)
                        bulgu.Add($"ÇAKIŞMA: \"{liste[i].Label}\" ↔ \"{liste[j].Label}\"");
                }
        }
        // 4) BOŞ ALAN VARKEN İÇ KAYDIRMA (kullanıcı bulgusu 2026-10-12): bir form kaydırıcısı kayıyor ama pencerenin
        //    altında hiçbir şeyin çizilmediği geniş boşluk var → form sabit bir kutuya sıkıştırılmış demektir.
        var yukseklik = w.ClientSize.Height;
        // En alttaki ÇİZİLEN içerik: yaprak kontrollerin alt kenarı, içinde bulunduğu kaydırıcının görünen alanıyla kırpılır
        // (kaydırıcıda aşağıda kalan, görünmeyen alanlar "dolu" sayılmasın).
        double KirpikAlt(Control c)
        {
            var alt = PencereKonumu(c, w).Bottom;
            foreach (var sv in c.GetVisualAncestors().OfType<ScrollViewer>())
                alt = Math.Min(alt, PencereKonumu(sv, w).Bottom);
            return Math.Min(alt, yukseklik);
        }
        // YALNIZ gerçek içerik sayılır (yazı, giriş kutusu, düğme, resim, ikon). Pencere şablonunun tam boy, boş
        // arka plan katmanları "dolu" sayılırsa ekran hiç boş görünmez ve denetim körleşir (ilk sürümde öyleydi).
        var enAlt = Gorunen<Control>(w).Where(c => c is TextBlock or TextBox or Button or ComboBox or CheckBox
                                                    or Controls.LookupBox or Image or PathIcon)
                                         .Select(KirpikAlt).DefaultIfEmpty(0).Max();
        foreach (var sv in Gorunen<ScrollViewer>(w).Where(s => s.Extent.Height > s.Viewport.Height + 1 && s.Viewport.Height > 50))
        {
            if (yukseklik - enAlt > 120)
                bulgu.Add($"SIKIŞIK FORM: kaydırıcı {sv.Viewport.Height:0}/{sv.Extent.Height:0} px gösteriyor, altta {yukseklik - enAlt:0} px boş");
        }
        // 5) ULAŞILAMAYAN ALAN: pencerenin altından taşan etkileşimli kontrol, dikey kayan bir kaydırıcının içinde değilse
        foreach (var c in Gorunen<Control>(w).Where(c => c is TextBox or ComboBox or Button or CheckBox or Controls.LookupBox))
        {
            var r = PencereKonumu(c, w);
            var kayar = c.GetVisualAncestors().OfType<ScrollViewer>().Any(s => s.Extent.Height > s.Viewport.Height + 1);
            if (r.Top > yukseklik + 1 && !kayar)
                bulgu.Add($"ULAŞILAMAZ: {Ad(c)} pencere altında ({r.Top:0} > {yukseklik:0})");
        }
        return bulgu.Distinct().ToList();
    }

    private static string Ad(Control c) => c switch
    {
        TextBlock t => $"metin \"{Kisalt(t.Text ?? "")}\"",
        Button b => $"düğme \"{Kisalt(b.Content?.ToString() ?? "")}\"",
        _ => (c.FindAncestorOfType<Controls.FormField>()?.Label is { } l ? $"alan \"{l}\" ({c.GetType().Name})" : c.GetType().Name),
    };
    private static string Kisalt(string s) => s.Length > 40 ? s[..40] + "…" : s;

    /// <summary>Ekranı her iki genişlikte çizer, görüntüyü kaydeder, bulguları döndürür (dosyaya da yazar).</summary>
    public static Dictionary<string, List<string>> Tara(string ad, Func<SessionContext, (object vm, Control view)> kur)
    {
        var s = Ortam();
        var sonuc = new Dictionary<string, List<string>>();
        // (genişlik, yükseklik, dosya eki): uzun boy = formun tamamı; 1126×760 = gerçek dizüstü içerik alanı
        // (form sığmaz → kaydırma / boşluk denetimi burada anlam kazanır).
        foreach (var (g, h, ek) in Genislikler.Select(g => (g, Yukseklik, $"{g}")).Append((1126, 760, "1126x760")))
        {
            var (vm, view) = kur(s);
            view.DataContext = vm;
            var w = new Window { Width = g, Height = h, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();   // oran bağlaması (Bounds.Height) ikinci yerleşim turunda oturur
            try { w.CaptureRenderedFrame()?.Save(Path.Combine(Klasor(), $"{ad}-{ek}.png")); } catch { }
            sonuc[ek] = Olc(w);
            w.Close();
        }
        var satirlar = sonuc.SelectMany(kv => kv.Value.Select(b => $"{ad}\t{kv.Key}\t{b}")).ToList();
        lock (Kilit)
        {
            var dosya = Path.Combine(Klasor(), "bulgular.txt");
            var eski = File.Exists(dosya) ? File.ReadAllLines(dosya).Where(l => !l.StartsWith(ad + "\t")).ToList() : new List<string>();
            File.WriteAllLines(dosya, eski.Concat(satirlar.Count > 0 ? satirlar : new[] { $"{ad}\t-\t(bulgu yok)" }));
        }
        return sonuc;
    }

    // ── Öncelikli ekranlar (yeni kayıt durumu) ───────────────────────────────────────────────────────
    [AvaloniaFact] public void G01_Bakim_Yeni() => Tara("bakim-yeni", s => { var vm = new MaintenanceViewModel(s, 1); vm.ToggleMntAddCommand.Execute(null); return (vm, new MaintenanceView()); });
    [AvaloniaFact] public void G02_Depo_Giris_Yeni() => Tara("depo-giris-yeni", s => { var vm = new StockEntryViewModel(s); return (vm, new StockEntryView()); });
    [AvaloniaFact] public void G03_Depo_Cikis_Yeni() => Tara("depo-cikis-yeni", s => { var vm = new StockEntryViewModel(s) { SelectedKind = "Depo Çıkışı" }; return (vm, new StockEntryView()); });
    [AvaloniaFact] public void G04_Arac_Yeni() => Tara("arac-yeni", s => { var vm = new VehiclesViewModel(s); vm.ToggleAddCommand.Execute(null); return (vm, new VehiclesView()); });
    [AvaloniaFact] public void G05_Yakit_Dagitim_Yeni() => Tara("yakit-dagitim-yeni", s => { var vm = new FuelViewModel(s, 0); vm.ToggleDistCommand.Execute(null); return (vm, new FuelView()); });
    [AvaloniaFact] public void G06_Gunluk_Faaliyet_Yeni() => Tara("gunluk-faaliyet-yeni", s => { var vm = new DailyActivityViewModel(s); vm.NewRecordCommand.Execute(null); return (vm, new DailyActivityView()); });
    [AvaloniaFact] public void G07_Cari_Yeni() => Tara("cari-yeni", s => { var vm = new PartiesViewModel(s); vm.NewPartyCommand.Execute(null); return (vm, new PartiesView()); });
    [AvaloniaFact] public void G08_Kasa_Banka_Hesap_Yeni() => Tara("kasa-banka-hesap-yeni", s => { var vm = new FinanceViewModel(s); vm.NewAccountCommand.Execute(null); Dispatcher.UIThread.RunJobs(); return (vm, new FinanceView()); });

    // ── Diğer sık kullanılan yeni kayıt ekranları (menü "Malzeme Yeni Kayıt" = openAdd ile aynı ekran) ──
    [AvaloniaFact] public void G09_Malzeme_Yeni() => Tara("malzeme-yeni", s => (new MaterialsViewModel(s, openAdd: true), new MaterialsView()));
    [AvaloniaFact] public void G10_Personel_Yeni() => Tara("personel-yeni", s => { var vm = new PersonnelViewModel(s); vm.NewPersonnelCommand.Execute(null); return (vm, new PersonnelView()); });
    [AvaloniaFact] public void G11_Talep_Yeni() => Tara("talep-yeni", s => { var vm = new RequestsViewModel(s); vm.NewRequestCommand.Execute(null); return (vm, new RequestsView()); });
    [AvaloniaFact] public void G12_Is_Emri_Yeni() => Tara("is-emri-yeni", s => { var vm = new WorkOrdersViewModel(s); vm.NewOrderCommand.Execute(null); return (vm, new WorkOrdersView()); });

    // ── Kalan yeni kayıt ekranları (2026-10-12, 2. aşama). Sunucudan liste çeken ekranlar test ortamında oturum
    //    anahtarı olmadığı için istek ATMAZ; ayrıca serverurl.txt erişilemez yerel adrese yönlendirir (csproj).
    [AvaloniaFact] public void G13_Duyuru_Yeni() => Tara("duyuru-yeni", s => { var vm = new AnnouncementsViewModel(s); vm.NewAnnouncementCommand.Execute(null); return (vm, new AnnouncementsView()); });
    [AvaloniaFact] public void G14_Sube_Yeni() => Tara("sube-yeni", s => { var vm = new BranchesViewModel(s); vm.NewBranchCommand.Execute(null); return (vm, new BranchesView()); });
    [AvaloniaFact] public void G15_Takvim_Yeni() => Tara("takvim-yeni", s => { var vm = new CalendarViewModel(s); vm.NewEventCommand.Execute(null); return (vm, new CalendarView()); });
    [AvaloniaFact] public void G16_Firma_Yeni() => Tara("firma-yeni", s => { var vm = new CompaniesViewModel(s); vm.NewCompanyCommand.Execute(null); return (vm, new CompaniesView()); });
    [AvaloniaFact] public void G17_Maliyet_Merkezi_Yeni() => Tara("maliyet-merkezi-yeni", s => { var vm = new CostCentersViewModel(s); vm.NewCenterCommand.Execute(null); return (vm, new CostCentersView()); });
    [AvaloniaFact] public void G18_Belge_Yeni() => Tara("belge-yeni", s => { var vm = new DocumentsViewModel(s); vm.NewDocumentCommand.Execute(null); return (vm, new DocumentsView()); });
    [AvaloniaFact] public void G19_Ekipman_Yeni() => Tara("ekipman-yeni", s => { var vm = new EquipmentViewModel(s); vm.NewEquipmentCommand.Execute(null); return (vm, new EquipmentView()); });
    [AvaloniaFact] public void G20_Denetim_Yeni() => Tara("denetim-yeni", s => { var vm = new InspectionViewModel(s); vm.NewRecordCommand.Execute(null); return (vm, new InspectionView()); });
    [AvaloniaFact] public void G21_Proje_Yeni() => Tara("proje-yeni", s => { var vm = new ProjectsViewModel(s); vm.NewProjectCommand.Execute(null); return (vm, new ProjectsView()); });
    [AvaloniaFact] public void G22_Satin_Alma_Yeni() => Tara("satin-alma-yeni", s => { var vm = new PurchasingViewModel(s); vm.NewOrderCommand.Execute(null); return (vm, new PurchasingView()); });
    [AvaloniaFact] public void G23_Surumler() => Tara("surumler", s => (new ReleasesViewModel(s), new ReleasesView()));
    [AvaloniaFact] public void G24_Malzeme_Sablonu_Yeni() => Tara("malzeme-sablonu-yeni", s => { var vm = new MaterialTemplatesViewModel(s); vm.ToggleAddCommand.Execute(null); return (vm, new MaterialTemplatesView()); });
    [AvaloniaFact] public void G25_Arac_Sablonu_Yeni() => Tara("arac-sablonu-yeni", s => { var vm = new VehicleTemplatesViewModel(s); vm.ToggleAddCommand.Execute(null); return (vm, new VehicleTemplatesView()); });
    [AvaloniaFact] public void G26_Fatura_Yeni() => Tara("fatura-yeni", s => { var vm = new InvoicesViewModel(s); vm.NewInvoiceFormCommand.Execute(null); return (vm, new InvoicesView()); });
    [AvaloniaFact] public void G27_Odeme_Yeni() => Tara("odeme-yeni", s => { var vm = new PaymentsViewModel(s); vm.NewEntryCommand.Execute(null); return (vm, new PaymentsView()); });
    [AvaloniaFact] public void G28_Zimmet() => Tara("zimmet", s => (new AssignmentsViewModel(s), new AssignmentsView()));
    [AvaloniaFact] public void G29_Kullanici_Yeni() => Tara("kullanici-yeni", s => { var vm = new UsersViewModel(s); vm.NewUserCommand.Execute(null); return (vm, new UsersView()); });
    [AvaloniaFact] public void G30_Sayim() => Tara("sayim", s => (new StockCountViewModel(s), new StockCountView()));
}
