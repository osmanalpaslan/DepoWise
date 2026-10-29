using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ iOS TARZI TASARIM (2026-10-28, kullanıcı kararı: "tasarımı tamamen sana bırakıyorum … bazı butonları sağ tık içine
/// taşıyabilirsin") — masaüstü kaynak sözleşmeleri ═══
///  IOS-SAG1 — liste ekranlarında satır SAĞ TIK menüsü var; "Düzenle" içerir; her öğe ekrandaki karşılık düğmeyle AYNI komutu
///             ve AYNI görünürlük (yetki) koşulunu taşır; seçili kayıt yoksa öğeler pasif
///  IOS-SAG2 — sağ tık da satırı seçer (menü doğru kayda uygulansın); orta tuş vb. seçmez
/// </summary>
public class IosTasarimTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Ekran(string ad) => File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views", ad + ".axaml"));

    public static IEnumerable<object[]> SagTikEkranlari() => new[]
    {
        "VehiclesView", "MaterialsView", "EquipmentView", "PersonnelView", "WorkOrdersView", "MaterialTemplatesView",
        "VehicleTemplatesView", "DocumentsView", "ProjectsView", "CostCentersView", "BranchesView", "AnnouncementsView", "CalendarView",
        "RequestsView", "FuelView",
    }.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(SagTikEkranlari))]
    public void IOSSAG1_Liste_Sag_Tik_Menusu_Ekrandaki_Dugmeyle_Ayni(string ekran)
    {
        var x = Ekran(ekran);
        var bas = x.IndexOf("<ContextMenu Classes=\"SatirMenusu\">", StringComparison.Ordinal);
        Assert.True(bas > 0, ekran + ": satır sağ tık menüsü yok");
        var menu = x[bas..x.IndexOf("</ContextMenu>", bas, StringComparison.Ordinal)];
        var ogeler = Regex.Matches(menu, @"<MenuItem Header=""([^""]+)"" Command=""\{Binding (\w+)\}""(?: IsVisible=""([^""]+)"")? IsEnabled=""([^""]+)""/>");
        Assert.Contains(ogeler, m => m.Groups[1].Value == "Düzenle");
        foreach (Match o in ogeler)
        {
            var komut = o.Groups[2].Value;
            Assert.Contains("{Binding Selected", o.Groups[4].Value);           // seçim yoksa pasif (ya da düğmenin kendi etkinlik koşulu)
            // Ekrandaki karşılık düğme (menü dışında) aynı komutu taşır; görünürlük koşulu varsa menüde de AYNISI.
            var disari = x[..bas] + x[x.IndexOf("</ContextMenu>", bas, StringComparison.Ordinal)..];
            var dugme = Regex.Match(disari, @"<(?:Button|MenuItem)\b[^>]*Command=""\{Binding " + komut + @"\}""[^>]*>");
            Assert.True(dugme.Success, $"{ekran}: {komut} için ekranda düğme yok");
            var kosul = Regex.Match(dugme.Value, @"\sIsVisible=""([^""]+)""");
            if (kosul.Success) Assert.Equal(kosul.Groups[1].Value, o.Groups[3].Value);
        }
        // Yıkıcı işlem (Sil) varsa en sonda ve ayıraçla ayrılmış
        var silIndex = menu.IndexOf("Header=\"Sil\"", StringComparison.Ordinal);
        if (silIndex > 0)
        {
            Assert.True(menu.LastIndexOf("<Separator/>", silIndex, StringComparison.Ordinal) > 0, ekran + ": Sil ayıraçla ayrılmalı");
            Assert.Equal(-1, menu.IndexOf("<MenuItem", silIndex, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IOSSAG2_Sag_Tik_Satiri_Secer()
    {
        var k = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Controls", "TableRowSelect.cs"));
        Assert.Contains("if (!tus.IsLeftButtonPressed && !tus.IsRightButtonPressed) return;", k);
        Assert.DoesNotContain("if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;", k);
    }

    /// <summary>IOS-YEN1 — "Yenile" yazılı düğme kalmadı; hepsi simgeli kare düğme, ekran okuyucu adı "Yenile" ve ipucu "Yenile (F5)".</summary>
    [Fact]
    public void IOSYEN1_Yenile_Simgeli_Ve_Erisilebilir()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        int adet = 0;
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var x = File.ReadAllText(f);
            Assert.DoesNotContain("Content=\"Yenile\"", x);
            foreach (Match m in Regex.Matches(x, @"<Button\s[^>]*YenileDugme[^>]*>"))
            {
                adet++;
                Assert.Contains("AutomationProperties.Name=\"Yenile\"", m.Value);
                Assert.Contains("ToolTip.Tip=\"Yenile (F5)\"", m.Value);
            }
        }
        Assert.True(adet >= 30, "simgeli yenile düğmesi: " + adet);
    }

    /// <summary>IOS-YIK1 — yıkıcı işlem düğmesi ("Sil", "İptal Et", "Kaydı İptal Et") sıradan gri (Secondary) stilde olmaz: kırmızı
    /// (Danger) ya da menüde. Tablo satırı içindeki sade (Ghost) satır işlemleri bilinçli olarak kapsam dışı.</summary>
    [Fact]
    public void IOSYIK1_Yikici_Dugme_Gri_Degil()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        var ihlal = new List<string>();
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), "<Button Classes=\"Secondary\"[^>]*Content=\"(Sil|İptal Et|Kaydı İptal Et)\""))
                ihlal.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
        Assert.True(ihlal.Count == 0, "gri yıkıcı düğme: " + string.Join(", ", ihlal));
    }

    /// <summary>IOS-HIZ1 — sabit genişlikli form alanı (FormField Width=…) DİKEY bir StackPanel içindeyse hizası açıkça belirtilir;
    /// yoksa Avalonia onu ortalar ve alan diğerlerinden sağa kayar (2026-10-29 görsel taramada Kullanıcı "Not", Bakım "depo").</summary>
    [Fact]
    public void IOSHIZ1_Sabit_Genislikli_Alan_Dikey_Sutunda_Kaymaz()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        var ihlal = new List<string>();
        var etiket = new Regex(@"<!--[\s\S]*?-->|<(/?)([A-Za-z_][\w:.]*)\b([^>]*?)(/?)>");
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var yigin = new List<(string Ad, string Nit)>();
            foreach (Match m in etiket.Matches(File.ReadAllText(f)))
            {
                if (m.Value.StartsWith("<!--")) continue;
                var ad = m.Groups[2].Value; var nit = m.Groups[3].Value;
                if (m.Groups[1].Value == "/")
                {
                    while (yigin.Count > 0) { var son = yigin[^1]; yigin.RemoveAt(yigin.Count - 1); if (son.Ad == ad) break; }
                    continue;
                }
                if (ad == "ctrl:FormField" && Regex.IsMatch(nit, @"\sWidth=""\d+""") && !nit.Contains("HorizontalAlignment=") && !nit.Contains("Grid.Column=")
                    && yigin.Count > 0 && yigin[^1].Ad == "StackPanel" && !yigin[^1].Nit.Contains("Orientation=\"Horizontal\""))
                    ihlal.Add(Path.GetFileName(f) + ": " + Regex.Match(nit, @"Label=""([^""]*)""").Groups[1].Value);
                if (m.Groups[4].Value != "/") yigin.Add((ad, nit));
            }
        }
        Assert.True(ihlal.Count == 0, "dikey sütunda hizasız sabit genişlikli alan: " + string.Join(", ", ihlal));
    }

    /// <summary>IOS-BOS1 — madde 1 (2026-10-29): ana eylemi olan ekranlarda "kayıt yok" paneli aynı ana eylemi (metin + komut → aynı
    /// yetki) gösterir; kullanıcı boş ekranda ne yapacağını görür.</summary>
    [Fact]
    public void IOSBOS1_Bos_Ekranda_Ana_Eylem()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        int adet = 0; var ihlal = new List<string>();
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var x = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(x, @"<ctrl:StatePanel\s[^>]*Mode=""Empty""[^>]*>"))
            {
                var once = x[..m.Index];
                var tb = Regex.Matches(once, @"<ctrl:Toolbar\s[^>]*>").LastOrDefault();
                if (tb is null) continue;
                var komut = Regex.Match(tb.Value, "PrimaryActionCommand=\"([^\"]+)\"");
                var metin = Regex.Match(tb.Value, "PrimaryActionText=\"([^\"]+)\"");
                if (!komut.Success || !metin.Success) continue;
                adet++;
                if (!m.Value.Contains($"ActionCommand=\"{komut.Groups[1].Value}\"") || !m.Value.Contains($"ActionText=\"{metin.Groups[1].Value}\""))
                    ihlal.Add(Path.GetFileName(f));
            }
        }
        Assert.True(adet >= 20, "ana eylemli boş panel: " + adet);
        Assert.True(ihlal.Count == 0, "boş panelde ana eylem eksik/farklı: " + string.Join(", ", ihlal));
    }

    /// <summary>IOS-HAP1 — madde 4 (2026-10-29): liste üstü hazır filtre hapları. Durum açılır listesi olan ekranlarda hap sırası
    /// (ListBox.HapSecici, aynı bağlantı); Araçlar/Malzemeler'de Durum hapları; eski durum açılır listesi kalmadı.</summary>
    [Fact]
    public void IOSHAP1_Hazir_Filtre_Haplari()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        string Oku(string ad) => File.ReadAllText(Path.Combine(dizin, ad));
        foreach (var ad in new[] { "EquipmentView.axaml", "ProjectsView.axaml", "PurchasingView.axaml", "WorkOrdersView.axaml" })
        {
            Assert.Contains("ItemsSource=\"{Binding FilterStatusOptions}\" SelectedItem=\"{Binding FilterStatus}\"", Oku(ad));
            Assert.Matches("<ListBox Classes=\"HapSecici\" ItemsSource=\"\\{Binding FilterStatusOptions\\}\"", Oku(ad));
            Assert.DoesNotMatch("<ComboBox[^>]*FilterStatusOptions", Oku(ad));
        }
        Assert.Matches("<ListBox Classes=\"HapSecici\" ItemsSource=\"\\{Binding StatusFilters\\}\"", Oku("InvoicesView.axaml"));
        foreach (var ad in new[] { "VehiclesView.axaml", "MaterialsView.axaml" })
            Assert.Contains("<ListBox Classes=\"HapSecici\" ItemsSource=\"{Binding DurumHaplari}\" SelectedItem=\"{Binding SeciliDurumHapi}\"", Oku(ad));
        var ios = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Themes", "Ios.axaml"));
        Assert.Contains("<Style Selector=\"ListBox.HapSecici ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter\">", ios);
        // Malzeme hapları servisin ürettiği durum metinleriyle birebir (yoksa hap hiçbir şey bulamaz)
        var servis = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Infrastructure", "Materials", "MaterialService.cs"));
        foreach (var d in new[] { "Stok Yok", "Düşük Stok", "Yeterli" }) Assert.Contains("'" + d + "'", servis);
    }

    /// <summary>IOS-HRK1 — madde 5 (2026-10-29, skill alpnex-arayuz-hareket): iOS temasındaki TÜM geçişler 250 ms altında; basılınca
    /// küçülme yalnız düğme/hap/kartta; tablo satırına (ListBox.Table) iOS temasında hareket eklenmez.</summary>
    [Fact]
    public void IOSHRK1_Yumusak_Gecisler_Sinirli()
    {
        var ios = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Themes", "Ios.axaml"));
        var sureler = Regex.Matches(ios, @"Duration=""0:0:(\d+(?:\.\d+)?)""").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.True(sureler.Count >= 3, "geçiş sayısı: " + sureler.Count);
        Assert.All(sureler, s => Assert.True(s <= 0.25, "250 ms üstü geçiş: " + s));
        Assert.Contains("<Style Selector=\"Button.Cip:pressed, Button.IosEylem:pressed, ListBox.HapSecici ListBoxItem:pressed, Button.Primary:pressed, Button.Secondary:pressed\">", ios);
        Assert.DoesNotContain("ListBox.Table", ios);
    }

    /// <summary>IOS-FORM1 — 2. tur madde 3 (2026-10-29): yeni kayıt formları iOS gruplu kutularda (IosFormGrup + IosFormBaslik);
    /// eski düz bölüm başlığı (FormBolum) kalmadı; grup içindeki yan yana alanlar sarılır (WrapPanel) — dar pencerede kutudan taşmaz.</summary>
    [Fact]
    public void IOSFORM1_Formlar_Gruplu()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        var formlar = new[] { "Materials", "Equipment", "Personnel", "WorkOrders", "Purchasing", "Documents", "Projects", "Inspection",
            "Users", "MaterialTemplates", "VehicleTemplates", "Requests", "DailyActivity", "Parties", "Announcements", "Calendar",
            "CostCenters", "Branches", "Companies", "Maintenance", "Finance", "Vehicles",
            "Payments", "Invoices", "Fuel", "StockEntry", "Assignments" };   // 3c: yapısı farklı formlar
        foreach (var ad in formlar)
        {
            var x = File.ReadAllText(Path.Combine(dizin, ad + "View.axaml"));
            Assert.Contains("<Border Classes=\"IosFormGrup\">", x);
            Assert.Contains("Classes=\"IosFormBaslik\"", x);
            Assert.DoesNotContain("Classes=\"FormBolum\"", x);
        }
        // 3b: büyük formlarda Yeni Araç'taki sağ sütun (Önizleme + Kontrol listesi); grup içinde sabit geniş (≥560 px) alan yok
        foreach (var ad in new[] { "Materials", "Equipment", "Personnel", "WorkOrders", "Purchasing" })
        {
            var x = File.ReadAllText(Path.Combine(dizin, ad + "View.axaml"));
            Assert.Contains("<TextBlock Text=\"Önizleme\"", x);
            Assert.Contains("<TextBlock Text=\"Kontrol listesi\"", x);
            Assert.Contains("Classes=\"KontrolIsaret\"", x);
        }
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
            Assert.DoesNotMatch(@"<ctrl:FormField HorizontalAlignment=""Left""[^>]*Width=""(5[6-9]\d|[6-9]\d\d)""", File.ReadAllText(f));
        // grup içi yan yana alanlar sarılır (dar pencerede kutudan taşmaz)
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\" ItemSpacing=\"14\" LineSpacing=\"12\">",
            File.ReadAllText(Path.Combine(dizin, "EquipmentView.axaml")));
    }

    /// <summary>IOS-PANEL1 — 2026-10-29 (kullanıcı isteği): Araç + Malzeme bilgi paneli 420 px, BilgiPaneli sınıfı; veri alanları
    /// ZEMİNSİZ özellik satırlarında (eski SurfaceElevated gri kutular yok); kaydırma alanında sağ boşluk (çubuk içerikle çakışmaz).</summary>
    [Fact]
    public void IOSPANEL1_Bilgi_Paneli_Zeminsiz_Genis()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        foreach (var (ad, enAz) in new[] { ("MaterialsView", 7), ("VehiclesView", 10) })
        {
            var x = File.ReadAllText(Path.Combine(dizin, ad + ".axaml"));
            Assert.Contains("ColumnDefinitions=\"*,420\" IsVisible=\"{Binding !ShowAdd}\"", x);
            Assert.Contains("Classes=\"Panel BilgiPaneli\"", x);
            var panel = x[x.IndexOf("Classes=\"Panel BilgiPaneli\"", StringComparison.Ordinal)..];
            Assert.True(Regex.Matches(panel, "<Border Classes=\"OzellikSatir\">").Count >= enAz, ad + " özellik satırı");
            Assert.DoesNotContain("<Border Background=\"{DynamicResource SurfaceElevatedBrush}\" CornerRadius=\"12\" Padding=\"14,", panel);
        }
        var ios = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Themes", "Ios.axaml"));
        Assert.Contains("<Style Selector=\"Border.BilgiPaneli ScrollViewer\">\n        <Setter Property=\"Padding\" Value=\"0,0,14,0\"/>", ios.Replace("\r\n", "\n"));
        Assert.Contains("<Style Selector=\"Border.OzellikSatir\">\n        <Setter Property=\"Background\" Value=\"Transparent\"/>", ios.Replace("\r\n", "\n"));
    }

    /// <summary>IOS-MENU1 — 2. tur madde 5 (2026-10-29): seyrek işlemler (Excel'e Aktar, Yazdır) görünür düğme DEĞİL — "•••" ve sağ tık
    /// menüsünde (Asgari Stok ve Raporlar hariç: orada ana işlem). Sağ tık menüsünde silme/iptal (tehlike) EN ALTTA. Menü stili iOS.</summary>
    [Fact]
    public void IOSMENU1_Sade_Cubuk_Menuler()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        var haric = new[] { "AsgariStokView.axaml", "ReportsView.axaml", "ComponentGalleryView.axaml" };
        int menude = 0;
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var x = File.ReadAllText(f);
            if (haric.Contains(Path.GetFileName(f))) continue;
            Assert.DoesNotMatch(@"<Button[^>]*Content=""(Excel'e Aktar|Yazdır)""", x);
            menude += Regex.Matches(x, @"<MenuItem Header=""Excel'e Aktar""").Count;
            foreach (Match m in Regex.Matches(x, @"<ContextMenu Classes=""SatirMenusu"">(.*?)</ContextMenu>", RegexOptions.Singleline))
            {
                var ogeler = Regex.Matches(m.Groups[1].Value, "<MenuItem[^>]*>").Select(o => o.Value).ToList();
                var ilkTehlike = ogeler.FindIndex(o => o.Contains("Classes=\"tehlike\""));
                if (ilkTehlike >= 0) Assert.All(ogeler.Skip(ilkTehlike), o => Assert.Contains("tehlike", o));
            }
        }
        Assert.True(menude >= 30, "menüdeki Excel öğesi: " + menude);
        var ios = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Themes", "Ios.axaml"));
        Assert.Contains("<Style Selector=\"MenuItem.tehlike\">", ios);
        Assert.Contains("<Style Selector=\"ContextMenu, MenuFlyoutPresenter\">", ios);
    }
}
