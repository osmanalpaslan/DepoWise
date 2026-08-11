using System.Reflection;
using System.Text;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// RPR-01 (2026-08-11) — WEB ↔ MASAÜSTÜ RAPOR FİLTRE PARİTESİ.
///
/// <b>Kapatılan risk.</b> Rapor VERİSİ iki platformda ortaktır (tek <see cref="ReportService"/>), ama
/// filtre ARAYÜZLERİ ayrı ayrı ELLE yazılır: Web'de <c>Reports.razor</c> içinde bir <c>@if</c> bloğu,
/// masaüstünde <c>ReportsViewModel</c> içinde bir <c>ShowXxx</c> + <c>ReportsView.axaml</c> içinde bir
/// <c>IsVisible</c> bloğu. Kataloğa yeni bir filtre eklendiğinde bunlardan biri unutulursa hiçbir şey
/// patlamaz — filtre o platformda sessizce YOKTUR ve kullanıcı farklı sonuç görür. STK-06'da tam olarak
/// bu risk görüldü (elle önlendi); bu dosya onu KALICI olarak yakalar.
///
/// <b>Yöntem.</b> Tek doğru kaynak <see cref="ReportFilters"/> enum'udur. Her bayrak için aşağıdaki
/// <see cref="Map"/> tablosunda bir satır bulunmak ZORUNDADIR; satır yoksa test kırılır. Her satır,
/// bayrağın 4 katmandaki (Application · API · Web · Masaüstü) bağlantılarını doğrular.
///
/// <b>Neden metin taraması?</b> Test projesi Web ve Desktop projelerine referans VERMEZ (Razor/Avalonia
/// derlenmez). Bu bilinçlidir: parite uğruna ortak bir UI katmanı kurmak RPR-01'in kapsamı değildir
/// (talimat §11). Bunun yerine iki arayüzün KAYNAK METNİ okunur — üretim kodu hiç değişmez.
///
/// ⚠️ Bu dosya görsel (piksel) eşitlik iddiasında DEĞİLDİR. Doğruladığı şey: filtrenin iki tarafta da
/// VAR olduğu, doğru katalog bayrağına BAĞLI olduğu, etiketinin bulunduğu ve değerin hem sorgu hem
/// export gövdesine AKTARILDIĞI.
/// </summary>
public class ReportFilterParityTests
{
    // ── Kaynak dosyalar ───────────────────────────────────────────────────────────────────
    private static readonly string Root = FindRepoRoot();
    private static readonly string WebRazorPath = Path.Combine(Root, "src", "DepoWise.Web", "Components", "Pages", "Reports.razor");
    private static readonly string DesktopVmPath = Path.Combine(Root, "src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs");
    private static readonly string DesktopXamlPath = Path.Combine(Root, "src", "DepoWise.Desktop", "Views", "ReportsView.axaml");
    private static readonly string ApiPath = Path.Combine(Root, "src", "DepoWise.Api", "Program.cs");

    private static readonly string WebRazor = ReadSource(WebRazorPath);
    private static readonly string DesktopVm = ReadSource(DesktopVmPath);
    private static readonly string DesktopXaml = ReadSource(DesktopXamlPath);
    private static readonly string ApiSource = ReadSource(ApiPath);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DepoWise.sln"))) dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                "DepoWise.sln bulunamadı — parite testi arayüz kaynak dosyalarını okuyamıyor. " +
                "Bu test kaynak ağacından çalıştırılmalıdır (atlanmaz: eksik kaynak = doğrulanmamış parite).");
        return dir.FullName;
    }

    /// <summary>Kaynak dosya okunamıyorsa test SESSİZCE GEÇMEZ — açık hata verir (talimat §10).</summary>
    private static string ReadSource(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Parite testi için gereken arayüz kaynağı bulunamadı: {path}", path);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    // ── Kablolama tablosu — HER ReportFilters bayrağı için BİR satır ───────────────────────

    /// <param name="Flag">Katalog bayrağı (tek doğru kaynak).</param>
    /// <param name="DesktopShow">Masaüstü görünürlük özelliği. İsimlendirme TEK TİP DEĞİLDİR
    /// (Branch → <c>ShowBranchSelect</c>, Vehicle → <c>ShowVehicleSelect</c>) — bu yüzden tabloda açıkça yazılır.</param>
    /// <param name="RequestProps"><see cref="ReportRequest"/> alan(lar)ı. Date iki alan kullanır; Status
    /// <c>Statuses</c> adını taşır — kalanlar <c>{Bayrak}Ids</c>.</param>
    /// <param name="LabelToken">İki arayüzde de bulunması gereken kullanıcıya-dönük etiket parçası.
    /// Birebir aynı etiket ZORUNLU DEĞİLDİR (piksel paritesi hedef değil); ortak, ayırt edici parça aranır.</param>
    internal sealed record Wiring(ReportFilters Flag, string DesktopShow, string[] RequestProps, string LabelToken)
    {
        public string DescriptorProperty => "Uses" + Flag;
        public string ApiCatalogField => "uses" + Flag;
    }

    internal static readonly IReadOnlyList<Wiring> Map = new[]
    {
        new Wiring(ReportFilters.Date,           "ShowDate",           new[] { "FromDate", "ToDate" }, "Başlangıç"),
        new Wiring(ReportFilters.Branch,         "ShowBranchSelect",   new[] { "BranchIds" },          "Şube / Şantiye"),
        new Wiring(ReportFilters.Vehicle,        "ShowVehicleSelect",  new[] { "VehicleIds" },         "Araç ara"),
        new Wiring(ReportFilters.VehicleType,    "ShowVehicleType",    new[] { "VehicleTypeIds" },     "Araç Türü"),
        new Wiring(ReportFilters.MaintenanceDef, "ShowMaintenanceDef", new[] { "MaintenanceDefIds" },  "Bakım Tanımı"),
        new Wiring(ReportFilters.Technician,     "ShowTechnician",     new[] { "TechnicianIds" },      "Teknisyen"),
        new Wiring(ReportFilters.Supplier,       "ShowSupplier",       new[] { "SupplierIds" },        "Tedarikçi"),
        new Wiring(ReportFilters.Requester,      "ShowRequester",      new[] { "RequesterIds" },       "Talep Eden"),
        new Wiring(ReportFilters.Status,         "ShowStatus",         new[] { "Statuses" },           "Durum"),
        // STK-06 — stok lokasyonu. Branch ile AYNI ŞEY DEĞİLDİR (kaydı işleyen şube ≠ stoğun fiziksel yeri).
        new Wiring(ReportFilters.Location,       "ShowLocation",       new[] { "LocationIds" },        "Depo / Şantiye"),
        // STK-10b-1 — stok hareket türü. Seçenekler SABİT listedir ve TEK kaynaktan gelir
        // (MovementTypeOptions, STK-B1); Web bu dosyayı derlediği için /api/reports/scope'a alan eklenmedi.
        new Wiring(ReportFilters.MovementType,   "ShowMovementType",   new[] { "MovementTypes" },      "Hareket Türü"),
        // STK-10b-2 (ADR-104) — serbest metin arama. TEK alanı SKALER olan filtre (`string?`, liste değil):
        // tarama alan ADINA göre çalıştığı için bu fark ek kural GEREKTİRMEZ.
        // ⚠️ Etiket parçası bilinçli olarak UZUN: yalnız "Ara" yazsaydık "Araç"/"Araç ara" metinlerine
        // takılıp blok silinse bile testi geçirirdi (RPR-01'in daha önce yakaladığı zayıflığın aynısı).
        new Wiring(ReportFilters.Search,         "ShowSearch",         new[] { "SearchText" },         "Ara (kod, malzeme, not, belge)"),
        // STK-10b-3 — MALZEME. Seçenekler ÖNCEDEN YÜKLENMEZ: iki platform da kendi MEVCUT malzeme arama
        // desenini kullanır (web: /api/materials?search=… · masaüstü: yerel Materials.List(term)). Bu
        // yüzden /api/reports/scope'a malzeme listesi eklenmedi — parite "aynı ID sözleşmesi" üzerinden
        // kurulur (MaterialIds), aynı seçenek listesi üzerinden DEĞİL.
        new Wiring(ReportFilters.Material,       "ShowMaterial",       new[] { "MaterialIds" },        "Malzeme (kod/ad ile ara)"),
    };

    internal static Wiring? WiringFor(string flagName) => Map.FirstOrDefault(w => w.Flag.ToString() == flagName);

    /// <summary>Yeni filtre eklendiğinde dokunulması gereken yerlerin listesi — test kırıldığında
    /// geliştiriciye/Claude'a doğrudan yol gösterir (bu, RPR-01'in asıl ürünüdür).</summary>
    internal static string Checklist(Wiring w) =>
        $"\n\nYENİ FİLTRE İÇİN DOKUNULACAK YERLER ({w.Flag}):\n" +
        $"  1) ReportCatalog.cs      → ReportFilters.{w.Flag} + ReportDescriptor.{w.DescriptorProperty}\n" +
        $"  2) ReportModels.cs       → ReportRequest.{string.Join(" / ", w.RequestProps)}\n" +
        $"  3) Api/Program.cs        → katalog yanıtında {w.ApiCatalogField} + ReportReqDto alanı + SORGU ve EXPORT uçlarında aktarım\n" +
        $"  4) Web/Reports.razor     → @if (_sel?.{w.DescriptorProperty} == true) bloğu + CatItem alanı + SORGU ve EXPORT gövdeleri\n" +
        $"  5) Desktop/ReportsViewModel.cs → {w.DesktopShow} özelliği + [NotifyPropertyChangedFor] + BuildTable() aktarımı\n" +
        $"  6) Desktop/ReportsView.axaml   → IsVisible=\"{{Binding {w.DesktopShow}}}\" bloğu + etiket\n";

    // ── Saf tarayıcı — negatif ispatta doktorlanmış metinle de çağrılabilsin diye parametrik ──

    /// <summary>Bir filtrenin 4 katmandaki bağlantılarını tarar; bulunan EKSİKLERİ döndürür (boş liste = tam).
    /// Metinler parametre olduğu için, testin gerçekten yakaladığını kanıtlamak üzere kasten bozulmuş
    /// metinlerle de çağrılabilir — üretim koduna sahte hata bırakmaya gerek kalmaz (talimat §9).</summary>
    internal static IReadOnlyList<string> Scan(Wiring w, string razor, string vm, string xaml, string api)
    {
        var gaps = new List<string>();

        // ── Katman 1: Application (sözleşme) ──
        if (typeof(ReportDescriptor).GetProperty(w.DescriptorProperty, BindingFlags.Public | BindingFlags.Instance) is null)
            gaps.Add($"APPLICATION: ReportDescriptor.{w.DescriptorProperty} yok.");
        foreach (var p in w.RequestProps)
            if (typeof(ReportRequest).GetProperty(p, BindingFlags.Public | BindingFlags.Instance) is null)
                gaps.Add($"APPLICATION: ReportRequest.{p} yok.");

        // ── Katman 2: API ──
        if (!api.Contains($"{w.ApiCatalogField} = d.{w.DescriptorProperty}", StringComparison.Ordinal))
            gaps.Add($"API: /api/reports/catalog yanıtında '{w.ApiCatalogField}' alanı yok → Web filtreyi hiç göremez.");
        foreach (var p in w.RequestProps)
            if (Count(api, $"d.{p}") < 2)
                gaps.Add($"API: '{p}' alanı SORGU ve EXPORT uçlarının ikisinde birden aktarılmıyor " +
                         $"(bulunan: {Count(api, $"d.{p}")}) → export filtreyi yok sayar.");

        // ── Katman 3: Web (Reports.razor) ──
        // ⚠️ Token '@if (' ile başlar: bayrak adı istek gövdelerinde de geçtiği için, yalnız
        // "_sel?.UsesX == true" aramak EKRAN BLOĞU silinse bile yanlışlıkla geçerdi (negatif ispat #4 yakaladı).
        if (!razor.Contains($"@if (_sel?.{w.DescriptorProperty} ==", StringComparison.Ordinal))
            gaps.Add($"WEB: ekranda filtre bloğu yok — '@if (_sel?.{w.DescriptorProperty} == ...)' bulunamadı.");
        if (!razor.Contains($"Bool(e, \"{w.ApiCatalogField}\")", StringComparison.Ordinal))
            gaps.Add($"WEB: katalog okumasında '{w.ApiCatalogField}' alanı okunmuyor → bayrak her zaman false kalır.");
        if (!razor.Contains(w.LabelToken, StringComparison.Ordinal))
            gaps.Add($"WEB: kullanıcıya dönük etiket yok ('{w.LabelToken}' bulunamadı).");
        foreach (var p in w.RequestProps)
        {
            var field = Camel(p);
            if (Count(razor, $"{field} = ") < 2)
                gaps.Add($"WEB: '{field}' SORGU ve EXPORT gövdelerinin ikisinde birden gönderilmiyor " +
                         $"(bulunan: {Count(razor, $"{field} = ")}) → ekran filtreli, Excel filtresiz olur.");
        }

        // ── Katman 4: Masaüstü (ReportsViewModel + ReportsView.axaml) ──
        if (!vm.Contains($"public bool {w.DesktopShow} ", StringComparison.Ordinal))
            gaps.Add($"MASAÜSTÜ: görünürlük özelliği yok — 'public bool {w.DesktopShow}' bulunamadı.");
        if (!vm.Contains($"SelectedReport?.{w.DescriptorProperty} ==", StringComparison.Ordinal))
            gaps.Add($"MASAÜSTÜ: {w.DesktopShow} katalog bayrağına bağlı değil " +
                     $"('SelectedReport?.{w.DescriptorProperty} ==' bulunamadı) → filtre yanlış raporda görünür.");
        if (!vm.Contains($"NotifyPropertyChangedFor(nameof({w.DesktopShow}))", StringComparison.Ordinal))
            gaps.Add($"MASAÜSTÜ: rapor değişince {w.DesktopShow} tazelenmiyor " +
                     "([NotifyPropertyChangedFor] eksik) → filtre ekranda takılı kalır.");
        // notify + tanım + BuildTable aktarımı = en az 3 geçiş.
        if (Count(vm, w.DesktopShow) < 3)
            gaps.Add($"MASAÜSTÜ: {w.DesktopShow} rapor isteğine (BuildTable) aktarılmıyor " +
                     $"(kaynakta {Count(vm, w.DesktopShow)} geçiş; en az 3 beklenir) → seçim yok sayılır.");
        if (!xaml.Contains($"IsVisible=\"{{Binding {w.DesktopShow}}}\"", StringComparison.Ordinal))
            gaps.Add($"MASAÜSTÜ: ekranda filtre bloğu yok — 'IsVisible=\"{{Binding {w.DesktopShow}}}\"' bulunamadı.");
        if (!xaml.Contains(w.LabelToken, StringComparison.Ordinal))
            gaps.Add($"MASAÜSTÜ: kullanıcıya dönük etiket yok ('{w.LabelToken}' bulunamadı).");

        return gaps;
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>PascalCase → camelCase (C# alan adı → JSON alan adı).</summary>
    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    // ══════════════════ 1. ANA PARİTE KAPISI ══════════════════

    /// <summary>1 — Kataloğa eklenen HER filtre bayrağının bir kablolama satırı olmalı. Yeni bayrak
    /// eklenip bu tablo güncellenmezse test burada kırılır → filtre sessizce tek platformda kalamaz.</summary>
    [Fact]
    public void Her_Filtre_Bayragi_Kablolama_Tablosunda_Kayitli()
    {
        var eksik = Enum.GetNames(typeof(ReportFilters))
            .Where(n => n != nameof(ReportFilters.None))
            .Where(n => WiringFor(n) is null)
            .ToList();

        Assert.True(eksik.Count == 0,
            "Kataloğa yeni filtre bayrağı eklenmiş ama parite tablosuna (ReportFilterParityTests.Map) " +
            $"satır eklenmemiş: {string.Join(", ", eksik)}.\n" +
            "Bu satır eklenmeden filtrenin Web ve masaüstünde gerçekten var olduğu DOĞRULANAMAZ.");

        // Tabloda karşılığı olmayan (silinmiş) bayrak da kalmasın.
        foreach (var w in Map)
            Assert.True(Enum.IsDefined(typeof(ReportFilters), w.Flag), $"Parite tablosunda artık var olmayan bayrak: {w.Flag}");
    }

    /// <summary>2 — ASIL TEST: her filtre 4 katmanda da bağlı mı? Yalnız Web'e ya da yalnız masaüstüne
    /// eklenen bir filtre burada kırılır.</summary>
    [Fact]
    public void Tum_Filtreler_Web_ve_Masaustunde_Eksiksiz_Bagli()
    {
        var rapor = new StringBuilder();
        foreach (var w in Map)
        {
            var gaps = Scan(w, WebRazor, DesktopVm, DesktopXaml, ApiSource);
            if (gaps.Count == 0) continue;
            rapor.AppendLine($"── {w.Flag} ──");
            foreach (var g in gaps) rapor.AppendLine("   • " + g);
            rapor.Append(Checklist(w));
        }
        Assert.True(rapor.Length == 0, "WEB ↔ MASAÜSTÜ FİLTRE PARİTESİ BOZUK:\n" + rapor);
    }

    /// <summary>3 — Katalogdaki hiçbir rapor, parite tablosunda karşılığı olmayan bir filtre kullanamaz.</summary>
    [Fact]
    public void Katalogdaki_Raporlar_Yalniz_Bagli_Filtreleri_Kullanir()
    {
        foreach (var d in ReportCatalog.All)
            foreach (var w in Map)
                if (d.Filters.HasFlag(w.Flag) && w.Flag != ReportFilters.None)
                    Assert.True(Scan(w, WebRazor, DesktopVm, DesktopXaml, ApiSource).Count == 0,
                        $"'{d.Name}' raporu {w.Flag} filtresini kullanıyor ama filtre her platformda bağlı değil.");
    }

    // ══════════════════ 2. TESTİN GERÇEKTEN YAKALADIĞININ İSPATI ══════════════════
    // Üretim koduna sahte hata bırakılmaz: gerçek kaynak metni KOPYA üzerinde bozulur (talimat §9).

    private static string ReplaceFirst(string src, string find, string with)
    {
        var i = src.IndexOf(find, StringComparison.Ordinal);
        Assert.True(i >= 0, $"Negatif ispat kurulamadı — kaynakta '{find}' bulunamadı.");
        return src[..i] + with + src[(i + find.Length)..];
    }

    /// <summary>4 — Filtre YALNIZ masaüstüne eklenmiş (Web bloğu yok) → test kırılmalı.</summary>
    [Fact]
    public void Eksik_WEB_Filtre_Blogu_Yakalaniyor()
    {
        var w = WiringFor(nameof(ReportFilters.Location))!;
        var bozuk = ReplaceFirst(WebRazor, "@if (_sel?.UsesLocation ==", "@if (_sel?.UsesHicbirSey ==");

        var gaps = Scan(w, bozuk, DesktopVm, DesktopXaml, ApiSource);
        Assert.Contains(gaps, g => g.StartsWith("WEB:", StringComparison.Ordinal));
        // Masaüstü sağlam olduğu için oradan şikâyet gelmemeli (test doğru yeri gösteriyor).
        Assert.DoesNotContain(gaps, g => g.StartsWith("MASAÜSTÜ:", StringComparison.Ordinal));
    }

    /// <summary>5 — Filtre ekranda var ama EXPORT gövdesinde gönderilmiyor → test kırılmalı.
    /// Bu, "ekran filtreli / Excel filtresiz" sessiz hatasının tam karşılığıdır.</summary>
    [Fact]
    public void Web_Export_Govdesinde_Eksik_Filtre_Yakalaniyor()
    {
        var w = WiringFor(nameof(ReportFilters.Location))!;
        // İki geçişten birini bozarsak (sorgu VEYA export) sayaç 1'e düşer.
        var bozuk = ReplaceFirst(WebRazor, "locationIds = ", "locationIdsKullanilmiyor = ");

        var gaps = Scan(w, bozuk, DesktopVm, DesktopXaml, ApiSource);
        Assert.Contains(gaps, g => g.Contains("SORGU ve EXPORT gövdelerinin", StringComparison.Ordinal));
    }

    /// <summary>6 — Filtre YALNIZ Web'e eklenmiş (masaüstü ekran bloğu yok) → test kırılmalı.</summary>
    [Fact]
    public void Eksik_MASAUSTU_Ekran_Blogu_Yakalaniyor()
    {
        var w = WiringFor(nameof(ReportFilters.Location))!;
        var bozuk = ReplaceFirst(DesktopXaml, "IsVisible=\"{Binding ShowLocation}\"", "IsVisible=\"{Binding ShowBranchSelect}\"");

        var gaps = Scan(w, WebRazor, DesktopVm, bozuk, ApiSource);
        Assert.Contains(gaps, g => g.StartsWith("MASAÜSTÜ:", StringComparison.Ordinal));
        Assert.DoesNotContain(gaps, g => g.StartsWith("WEB:", StringComparison.Ordinal));
    }

    /// <summary>7 — Masaüstünde görünürlük özelliği var ama rapor değişince tazelenmiyor
    /// ([NotifyPropertyChangedFor] unutulmuş) → filtre yanlış raporda ekranda kalır. Yakalanmalı.</summary>
    [Fact]
    public void Masaustunde_Tazelenmeyen_Filtre_Yakalaniyor()
    {
        var w = WiringFor(nameof(ReportFilters.Location))!;
        var bozuk = ReplaceFirst(DesktopVm, "NotifyPropertyChangedFor(nameof(ShowLocation))", "NotifyPropertyChangedFor(nameof(ShowChart))");

        var gaps = Scan(w, WebRazor, bozuk, DesktopXaml, ApiSource);
        Assert.Contains(gaps, g => g.Contains("tazelenmiyor", StringComparison.Ordinal));
    }

    /// <summary>8 — API katalog yanıtından alan düşerse Web filtreyi HİÇ göremez → yakalanmalı.</summary>
    [Fact]
    public void Eksik_API_Katalog_Alani_Yakalaniyor()
    {
        var w = WiringFor(nameof(ReportFilters.Location))!;
        var bozuk = ReplaceFirst(ApiSource, "usesLocation = d.UsesLocation", "usesBaskaSey = d.UsesLocation");

        var gaps = Scan(w, WebRazor, DesktopVm, DesktopXaml, bozuk);
        Assert.Contains(gaps, g => g.StartsWith("API:", StringComparison.Ordinal));
    }

    /// <summary>9 — Kablolama satırı olmayan yeni bir bayrak eklenirse arama boş döner ve
    /// <see cref="Her_Filtre_Bayragi_Kablolama_Tablosunda_Kayitli"/> kırılır.</summary>
    [Fact]
    public void Kablolama_Satiri_Olmayan_Bayrak_Bulunamaz()
    {
        Assert.Null(WiringFor("DepoRafi"));                       // henüz var olmayan örnek bayrak
        Assert.NotNull(WiringFor(nameof(ReportFilters.Location))); // var olan bayrak bulunur
    }

    // ══════════════════ 3. EXPORT PARİTESİ ══════════════════

    /// <summary>10 — Sorgu ve export uçları AYNI rapor isteğini kurmalı. İkisi ayrışırsa Excel çıktısı
    /// ekrandan farklı olur (sessiz hata). Metin düzeyinde birebir karşılaştırılır.</summary>
    [Fact]
    public void Sorgu_ve_Export_Uclari_AYNI_Rapor_Istegini_Kurar()
    {
        var satirlar = ApiSource.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("var req = new DepoWise.Application.Reports.ReportRequest(", StringComparison.Ordinal))
            .Select(l => l.Split("//")[0].Trim())   // açıklama satırı farkı önemsiz
            .ToList();

        Assert.True(satirlar.Count == 2,
            $"/api/reports ve /api/reports/{{type}}/export uçlarında tam 2 ReportRequest kurulumu beklenir; bulunan: {satirlar.Count}.");
        Assert.Equal(satirlar[0], satirlar[1]);
    }

    // ══════════════════ 4. SEÇENEK / VARSAYILAN PARİTESİ ══════════════════

    /// <summary>11 — "📦 Atanmamış" seçeneği İKİ arayüzde de sunulmalı. Yalnız birinde olursa aynı
    /// kullanıcı Web'de görebildiği stoğu masaüstünde göremez. ⚠️ "Tüm Şubeler" ≠ "Atanmamış":
    /// ilki filtresiz toplam (Atanmamış dahil), ikincisi yalnız <c>location_id=""</c>.</summary>
    [Fact]
    public void Lokasyon_Filtresi_ATANMAMIS_Secenegini_IKI_Arayuzde_de_Sunuyor()
    {
        Assert.Contains("📦 Atanmamış", WebRazor, StringComparison.Ordinal);
        Assert.Contains("📦 Atanmamış", DesktopVm, StringComparison.Ordinal);

        // Boş seçim = Tüm Şubeler; iki arayüz de bunu kullanıcıya söylüyor (kavram karışmasın).
        Assert.Contains("Tüm Şubeler", WebRazor, StringComparison.Ordinal);
        Assert.Contains("boş=tümü", DesktopXaml, StringComparison.Ordinal);
    }

    /// <summary>12 — Talep durumları TEK kaynaktan (RequestStatusOptions): sunucu Web'e bu listeyi
    /// gönderir, masaüstü aynı sabiti doğrudan kullanır → iki platform aynı seçenekleri gösterir.</summary>
    [Fact]
    public void Durum_Secenekleri_TEK_Kaynaktan_Geliyor()
    {
        Assert.Contains("RequestStatusOptions.All", ApiSource, StringComparison.Ordinal);
        Assert.Contains("RequestStatusOptions.All", DesktopVm, StringComparison.Ordinal);
        Assert.Contains("_requestStatuses", WebRazor, StringComparison.Ordinal);   // Web sunucudan okur
        Assert.Equal(5, RequestStatusOptions.All.Count);
    }

    /// <summary>13 — Tarih varsayılanı (Bu Ay) İKİ arayüzde de uygulanır; sunucu da aynı varsayılana düşer.
    /// Biri unutulursa aynı rapor iki platformda farklı aralık gösterir.</summary>
    [Fact]
    public void Tarih_Varsayilani_IKI_Arayuzde_de_Uygulaniyor()
    {
        Assert.Contains("ApplyDateDefault", WebRazor, StringComparison.Ordinal);
        Assert.Contains("ApplyDateDefault", DesktopVm, StringComparison.Ordinal);
        Assert.Contains("RequiresDate: true", DesktopVm, StringComparison.Ordinal);
        Assert.Contains("RequiresDate: true", WebRazor, StringComparison.Ordinal);

        var (from, to) = ReportCatalog.CurrentMonthRange();
        Assert.True(from < to);
    }

    /// <summary>14 — Arayüzlerde BAŞIBOŞ filtre bloğu kalmasın: iki arayüzde geçen her <c>Uses…</c>
    /// bayrağının katalogda ve parite tablosunda karşılığı olmalı. Böylece "Web'e elle bir filtre
    /// eklendi ama kataloğa/masaüstüne hiç girmedi" durumu da yakalanır (ters yönlü parite kaybı).
    ///
    /// ⚠️ Rapor ANAHTARININ arayüzde kullanılması yasak DEĞİLDİR — masaüstü grafik türünü
    /// (<c>BuildChart</c>) rapora göre seçer ve bu meşrudur. Yasak olan, FİLTRE GÖRÜNÜRLÜĞÜNÜ
    /// anahtara bağlamaktır; onu yukarıdaki <see cref="Scan"/> zaten katalog bayrağına zorluyor.</summary>
    [Fact]
    public void Arayuzlerdeki_Tum_Filtre_Bayraklari_Katalogda_Tanimli()
    {
        var bilinen = Map.Select(w => w.DescriptorProperty).ToHashSet(StringComparer.Ordinal);

        foreach (var (kaynak, ad) in new[] { (WebRazor, "Web/Reports.razor"), (DesktopVm, "Desktop/ReportsViewModel.cs") })
        {
            var bulunan = System.Text.RegularExpressions.Regex.Matches(kaynak, @"\bUses[A-Z][A-Za-z]*")
                .Select(m => m.Value).Distinct().ToList();

            Assert.NotEmpty(bulunan);   // hiç bulunmuyorsa tarama bozulmuştur, sessizce geçmesin
            var yabanci = bulunan.Where(x => !bilinen.Contains(x)).ToList();
            Assert.True(yabanci.Count == 0,
                $"{ad} içinde katalogda karşılığı olmayan filtre bayrağı var: {string.Join(", ", yabanci)}.\n" +
                "Bu bayrak ya ReportFilters'a ve parite tablosuna eklenmeli, ya da arayüzden kaldırılmalı — " +
                "aksi hâlde filtre yalnız tek platformda yaşar.");
        }
    }
}

/// <summary>
/// RPR-01 — DAVRANIŞ TARAFI: filtre semantiği ve çevrimdışı çalışma korunuyor mu?
///
/// Yukarıdaki sınıf filtrenin VAR olduğunu kanıtlar; bu sınıf ne YAPTIĞINI kilitler. Rapor motoru
/// Web ve masaüstünde ORTAK olduğu için buradaki her sonuç iki platformda da aynıdır — testler
/// masaüstünün çevrimdışı yolunu (yerel SQLite, HTTP YOK) koşturur.
/// </summary>
public class ReportFilterBehaviourParityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _depoA, _depoB, _mat;

    public ReportFilterBehaviourParityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rpr01_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }

        var materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_admin, new NewBranch("Depo A"));
        _depoB = branches.Create(_admin, new NewBranch("Depo B"));
        _mat = materials.Create(_admin, new NewMaterial("RPR-1", "Parite malzemesi"));

        // Depo A: 10 · Depo B: 4 · Atanmamış: 6 → firma toplamı 20.
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 6m) }, Op());   // geçmiş: depo girilmemiş
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Sabit saatin çevresini kapsayan tarih aralığı. Tarih kullanan raporlarda iki arayüz de
    /// alanı ön-doldurup gönderir; gönderilmezse ortak katman "Bu Ay"a düşer (test saati o ay değil).</summary>
    private const long GunBasi = 1_699_900_000_000, GunSonu = 1_700_100_000_000;

    /// <summary>Masaüstü <c>ReportsViewModel.BuildTable()</c> ile AYNI kural: bir filtre yalnız rapor onu
    /// kullanıyorsa gönderilir, hiçbir depo işaretli değilse null (= Tüm Şubeler).</summary>
    private static ReportRequest MasaustuIstegi(ReportDescriptor rapor, params string[] isaretliDepolar)
        => new(Executed: true,
               FromDate: rapor.UsesDate ? GunBasi : null,
               ToDate: rapor.UsesDate ? GunSonu : null,
               LocationIds: rapor.UsesLocation && isaretliDepolar.Length > 0 ? isaretliDepolar : null);

    private static decimal Toplam(TableModel t, int kolon)
        => t.Rows.Sum(r => Money.Parse((string?)r[kolon]));

    private static int KolonIndeksi(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        return -1;
    }

    /// <summary>15 — STK-06 semantiği korunuyor: Tüm Şubeler (boş) ≠ tek depo ≠ Atanmamış.
    /// Üçü de AYRI sonuç verir ve depo kırılımlarının toplamı firma toplamına eşittir.</summary>
    [Fact]
    public void Lokasyon_Filtresi_Uc_Anlami_Ayri_Tutuyor()
    {
        var rapor = ReportCatalog.ByKey("stock")!;

        var tumu = _reports.Run(_admin, "stock", MasaustuIstegi(rapor));
        Assert.Equal(20m, Toplam(tumu, KolonIndeksi(tumu, "Stok")));
        Assert.Equal(-1, KolonIndeksi(tumu, "Depo / Şantiye"));   // filtresizken kırılım kolonu YOK

        var depoA = _reports.Run(_admin, "stock", MasaustuIstegi(rapor, _depoA));
        Assert.Equal(10m, Toplam(depoA, KolonIndeksi(depoA, "Stok")));
        Assert.NotEqual(-1, KolonIndeksi(depoA, "Depo / Şantiye"));

        // "" = ATANMAMIŞ — gerçek bir depo DEĞİLDİR, "Tüm Şubeler" ile aynı şey de değildir.
        var atanmamis = _reports.Run(_admin, "stock", MasaustuIstegi(rapor, ""));
        Assert.Equal(6m, Toplam(atanmamis, KolonIndeksi(atanmamis, "Stok")));
        Assert.NotEqual(20m, Toplam(atanmamis, KolonIndeksi(atanmamis, "Stok")));

        // Kırılımların toplamı = firma toplamı (invariant).
        var hepsi = _reports.Run(_admin, "stock", MasaustuIstegi(rapor, _depoA, _depoB, ""));
        Assert.Equal(20m, Toplam(hepsi, KolonIndeksi(hepsi, "Stok")));
    }

    /// <summary>16 — Stok Sayım: "Sayılan Depo" kolonu ve lokasyon filtresi korunuyor
    /// (hangi deponun sayıldığı raporda görünmeli — STK-06 bulgusu).</summary>
    [Fact]
    public void Stok_Sayim_Sayilan_Depo_Kolonu_ve_Filtresi_Korunuyor()
    {
        var rapor = ReportCatalog.ByKey("stock-count")!;
        Assert.True(rapor.UsesLocation);

        _stock.Count(_admin, new[] { new CountLine(_mat, 9m) }, "A sayımı", Op(), branchId: _depoA);
        _stock.Count(_admin, new[] { new CountLine(_mat, 3m) }, "B sayımı", Op(), branchId: _depoB);

        var hepsi = _reports.Run(_admin, "stock-count", MasaustuIstegi(rapor));
        Assert.NotEqual(-1, KolonIndeksi(hepsi, "Sayılan Depo"));
        Assert.Equal(2, hepsi.Rows.Count);

        var yalnizA = _reports.Run(_admin, "stock-count", MasaustuIstegi(rapor, _depoA));
        Assert.Single(yalnizA.Rows);
        Assert.Contains("Depo A", (string?)yalnizA.Rows[0][KolonIndeksi(yalnizA, "Sayılan Depo")] ?? "");
    }

    /// <summary>17 — Rapor motoru filtreyi AYNI istekten aldığı için, aynı gövdeyle çalıştırılan
    /// "ekran sorgusu" ve "Excel export" birebir aynı tabloyu üretir (API'de iki uç da BuildReport
    /// çağırır — kaynak eşitliği ayrıca test #10'da kilitli).</summary>
    [Fact]
    public void Filtreli_Export_Ekrandaki_Filtreli_Sonucun_Aynisi()
    {
        var rapor = ReportCatalog.ByKey("stock")!;
        var istek = MasaustuIstegi(rapor, _depoA);

        var ekran = _reports.Run(_admin, "stock", istek);
        var export = _reports.Run(_admin, "stock", istek);   // export ucu aynı gövde + aynı BuildReport

        Assert.Equal(ekran.Headers, export.Headers);
        Assert.Equal(ekran.Rows.Count, export.Rows.Count);
        Assert.Equal(10m, Toplam(export, KolonIndeksi(export, "Stok")));
        // Filtresiz export ile karışmadığının ispatı: firma toplamı 20, filtreli 10.
        var filtresiz = _reports.Run(_admin, "stock", MasaustuIstegi(rapor));
        Assert.Equal(20m, Toplam(filtresiz, KolonIndeksi(filtresiz, "Stok")));
    }

    /// <summary>18 — ÇEVRİMDIŞI: masaüstü rapor filtreleri internet olmadan çalışır. Bu testin tamamı
    /// yerel SQLite üzerindedir — hiçbir HTTP çağrısı yoktur (<c>ApiTestHost</c> kullanılmaz).
    /// Lokasyon listesi de yerel <see cref="BranchService"/>'ten gelir.</summary>
    [Fact]
    public void Masaustu_Rapor_Filtreleri_Cevrimdisi_Calisiyor()
    {
        // 1) Filtre seçenekleri yerelden yüklenir (masaüstü LoadLocations ile aynı kaynak).
        var depolar = new BranchService(_factory, _clock).List(_admin).ToList();
        Assert.Equal(2, depolar.Count);
        Assert.Contains(depolar, b => b.Id == _depoA);

        // 2) Rapor katalogtan seçilir, filtre görünürlüğü katalog bayrağından gelir (sunucuya sorulmaz).
        var rapor = ReportCatalog.ByKey("stock")!;
        Assert.True(rapor.UsesLocation);

        // 3) Filtre uygulanır ve rapor yerel veritabanından üretilir.
        var sonuc = _reports.Run(_admin, "stock", MasaustuIstegi(rapor, _depoA));
        Assert.NotEmpty(sonuc.Rows);
        Assert.Equal(10m, Toplam(sonuc, KolonIndeksi(sonuc, "Stok")));

        // 4) Tarih filtresi kullanan rapor da çevrimdışı çalışır (varsayılan "Bu Ay" sunucuda değil,
        //    iki platformun ORTAK kullandığı katmandadır → internetsiz de uygulanır).
        var sayim = _reports.Run(_admin, "stock-count", new ReportRequest(true));
        Assert.Equal("Stok Sayım Raporu", sayim.Title);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
