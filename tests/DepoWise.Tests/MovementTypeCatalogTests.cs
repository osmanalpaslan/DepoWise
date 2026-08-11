using System.Text;
using System.Text.RegularExpressions;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-B1 (2026-08-11) — STOK HAREKET TÜRÜ KATALOĞU / GÖSTERİM PARİTESİ.
///
/// <b>Kapatılan hata:</b> aynı <c>movement_type</c> kullanıcıya ÜÇ ayrı yerde, ÜÇ FARKLI biçimde
/// gösteriliyordu ve üçü de eksikti → <c>usage</c>, <c>usage_reverse</c> ve <c>reverse</c> kullanıcıya
/// HAM İNGİLİZCE sızıyor; <c>adjustment</c> masaüstünde "Düzeltme", web'de "Sayım Düzeltme" görünüyordu.
///
/// Bu dosya üç şeyi kalıcı olarak kilitler:
///  1. <b>Katalog kapsamı</b> — üretimin ürettiği HER tür katalogda ve Türkçe etiketli.
///  2. <b>Tek kaynak</b> — üç gösterim yüzeyi de kendi switch'ini KULLANMIYOR.
///  3. <b>Gerçek kod yolu</b> — 8 türün 8'i gerçek servislerle ÜRETİLİP ekranda ne göründüğü doğrulanıyor.
///
/// ⚠️ Kapsam: yalnız GÖSTERİM. Veritabanındaki değerler, şema, senkron ve hareket üretim mantığı
/// değişmedi (STK-B1 sınırı; STK-10'un rapor/filtre/export kısmı bu işte YAPILMADI).
/// </summary>
public class MovementTypeCatalogTests
{
    // ── Üretimde hareket ÜRETEN üç dosya (kaynak taraması bunlarda yapılır) ──────────────────
    private static readonly string Root = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DepoWise.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "DepoWise.sln bulunamadı — STK-B1 kaynak taraması yapılamıyor (sessizce atlanmaz).");
    }

    private static string Src(params string[] parts)
    {
        var p = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        if (!File.Exists(p)) throw new FileNotFoundException($"STK-B1 testi için gereken kaynak yok: {p}", p);
        return File.ReadAllText(p, Encoding.UTF8);
    }

    private static readonly string StockSvc = Src("src", "DepoWise.Infrastructure", "Materials", "StockService.cs");
    private static readonly string OpeningSvc = Src("src", "DepoWise.Infrastructure", "Materials", "OpeningStockService.cs");
    private static readonly string MaintSvc = Src("src", "DepoWise.Infrastructure", "Maintenance", "MaintenanceService.cs");
    private static readonly string WebMovements = Src("src", "DepoWise.Web", "Components", "Pages", "StockMovements.razor");
    private static readonly string WebCsproj = Src("src", "DepoWise.Web", "DepoWise.Web.csproj");

    /// <summary>Kaynak koddan doğrulanmış 8 tür — testin beklediği kanonik küme.</summary>
    private static readonly string[] Uretimdeki8 =
    {
        "opening", "in", "out", "transfer", "adjustment", "usage", "usage_reverse", "reverse",
    };

    // ══════════════ 1. KATALOG KAPSAMI ══════════════

    /// <summary>1 — Katalog, üretimde üretilebilen 8 türün TAMAMINI kapsıyor; fazlası da yok.</summary>
    [Fact]
    public void Katalog_Uretimdeki_Sekiz_Turun_Tamamini_Kapsiyor()
    {
        var katalog = MovementTypeOptions.All.Select(x => x.Key).ToList();
        Assert.Equal(8, katalog.Count);
        Assert.Equal(Uretimdeki8.OrderBy(x => x, StringComparer.Ordinal),
                     katalog.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(katalog.Count, katalog.Distinct(StringComparer.Ordinal).Count());   // kopya yok
    }

    /// <summary>2 — Her türün kullanıcıya dönük, DOLU ve anahtardan FARKLI bir Türkçe etiketi var.
    /// Etiket anahtara eşitse ham İngilizce sızıyor demektir.</summary>
    [Fact]
    public void Her_Turun_Kullaniciya_Donuk_Turkce_Etiketi_Var()
    {
        foreach (var (key, label) in MovementTypeOptions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(label), $"'{key}' türünün etiketi boş.");
            Assert.NotEqual(key, label);
            Assert.True(label.Any(char.IsLetter), $"'{key}' etiketi harf içermiyor: '{label}'");
        }
    }

    /// <summary>3 — 🔴 HAM DEĞER KAÇAĞI: 8 türün hiçbiri kullanıcıya ham İngilizce olarak dönmüyor.
    /// Özellikle STK-B1'in kapattığı üçü açıkça sınanır.</summary>
    [Theory]
    [InlineData("usage")]
    [InlineData("usage_reverse")]
    [InlineData("reverse")]
    [InlineData("opening")]
    [InlineData("in")]
    [InlineData("out")]
    [InlineData("transfer")]
    [InlineData("adjustment")]
    public void Ham_Ingilizce_Deger_Kullaniciya_Sizmiyor(string tur)
    {
        var etiket = MovementTypeOptions.Label(tur);
        Assert.NotEqual(tur, etiket);
        Assert.False(string.IsNullOrWhiteSpace(etiket));
    }

    /// <summary>4 — `count` bir <c>doc_type</c>'tır (sayım BELGESİ), <c>movement_type</c> DEĞİL.
    /// Web'de bunun için ölü bir eşleme vardı; kataloğa yanlışlıkla girmemeli.</summary>
    [Fact]
    public void count_Hareket_Turu_DEGILDIR_Katalogda_Yok()
    {
        Assert.DoesNotContain(MovementTypeOptions.All, x => x.Key == "count");
        // Sayım belgesi defterde `adjustment` hareketi üretir — kullanıcı "Sayım Düzeltme" görür.
        Assert.Equal("Sayım Düzeltme", MovementTypeOptions.Label("adjustment"));
        // Ölü dal web'den de kaldırılmış olmalı.
        Assert.DoesNotContain("\"count\" => \"Sayım\"", WebMovements, StringComparison.Ordinal);
    }

    /// <summary>5 — Birbirine benzeyen üç kavram AYRI AYRI adlandırılmış: bakım tüketimi ≠ bakım
    /// tüketimi iptali ≠ belge ters kaydı ≠ sayım düzeltmesi. Kullanıcı bunları karıştırmamalı.</summary>
    [Fact]
    public void Benzer_Kavramlar_Birbirinden_Ayirt_Edilebilir()
    {
        var usage = MovementTypeOptions.Label("usage");
        var usageReverse = MovementTypeOptions.Label("usage_reverse");
        var reverse = MovementTypeOptions.Label("reverse");
        var adjustment = MovementTypeOptions.Label("adjustment");

        Assert.NotEqual(usage, usageReverse);         // tüketim ≠ tüketimin iptali
        Assert.NotEqual(usageReverse, reverse);       // bakım iptali ≠ BELGE ters kaydı
        Assert.NotEqual(adjustment, usageReverse);    // sayım düzeltmesi ≠ bakım iptali
        Assert.NotEqual(adjustment, reverse);

        // Etiketlerin tamamı birbirinden farklı (iki tür aynı adı taşıyamaz).
        var etiketler = MovementTypeOptions.All.Select(x => x.Label).ToList();
        Assert.Equal(etiketler.Count, etiketler.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>6 — Bilinmeyen (gelecekteki) bir değer SESSİZCE GİZLENMİYOR. "Diğer" demek yeni bir
    /// türü görünmez kılardı; ham değer dönmesi fark edilir ve aşağıdaki kaynak taraması testi kırılır.</summary>
    [Fact]
    public void Bilinmeyen_Deger_Sessizce_Gizlenmiyor()
    {
        Assert.Equal("boyle_bir_tur_yok", MovementTypeOptions.Label("boyle_bir_tur_yok"));
        Assert.Equal("", MovementTypeOptions.Label(null));
        Assert.Equal("", MovementTypeOptions.Label(""));
    }

    // ══════════════ 2. KAYNAK TARAMASI — YENİ TÜR EKLENİRSE TEST KIRILIR ══════════════

    /// <summary>7 — 🔴 GELECEĞE KORUMA: üretim kodunda hareket türü olarak yazılan HER string
    /// literali katalogda olmalı. Yeni bir tür eklenip kataloğa girilmezse bu test kırılır ve
    /// hangi değer olduğunu söyler → tür sessizce etiketsiz kalamaz.
    ///
    /// Tarama, hareket üreten ÜÇ dosyadaki üç yazma şeklini kapsar:
    /// <c>ApplyLine(…, "tür", …)</c> · <c>InsertMovement(…, "tür", …)</c> ·
    /// <c>AddWithValue("@type", … "tür" …)</c> · SQL'e gömülü <c>'opening'</c>.
    ///
    /// ⚠️ SINIR (dürüst kayıt): tarama bu üç yazma şeklini tanır. Dördüncü bir
    /// <c>INSERT INTO stock_movements</c> ifadesi eklenirse tarama onu göremez — bu yüzden aşağıda
    /// ayrıca "insert ifadesi sayısı 3" testi vardır (yeni bir yazma yolu da testi kırar).</summary>
    [Fact]
    public void Uretim_Kodundaki_Her_Hareket_Turu_Katalogda_Var()
    {
        var bulunan = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kaynak in new[] { StockSvc, OpeningSvc, MaintSvc })
        {
            var tekSatir = Regex.Replace(kaynak, @"\s+", " ");   // çok satırlı çağrılar tek satıra
            foreach (var kalip in new[]
                     {
                         @"ApplyLine\([^;]{0,400}?,\s*""([a-z_]+)""\s*,",
                         @"InsertMovement\([^;]{0,400}?,\s*""([a-z_]+)""\s*,",
                         @"AddWithValue\(""@type"",[^;]{0,200}?""([a-z_]+)""",
                         @"AddWithValue\(""@type"",[^;]{0,200}?""([a-z_]+)""\s*\)",
                         @"movement_type[^;]{0,600}?VALUES\([^;]{0,300}?'([a-z_]+)'",
                     })
                foreach (Match m in Regex.Matches(tekSatir, kalip))
                    foreach (Group g in m.Groups.Cast<Group>().Skip(1))
                        if (g.Success) bulunan.Add(g.Value);
        }

        Assert.True(bulunan.Count > 0, "Kaynak taraması hiç hareket türü bulamadı — tarama bozulmuş olabilir.");

        var katalog = MovementTypeOptions.All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var katalogsuz = bulunan.Where(x => !katalog.Contains(x)).ToList();
        Assert.True(katalogsuz.Count == 0,
            "Üretim kodunda üretilen ama KATALOGDA OLMAYAN hareket türü var: " + string.Join(", ", katalogsuz) +
            "\nMovementTypeOptions.All'a Türkçe etiketiyle eklenmeli — aksi hâlde kullanıcıya ham İngilizce görünür.");

        // Taramanın gerçekten çalıştığının kanıtı: STK-B1'in kapattığı üç tür bulunmuş olmalı.
        Assert.Contains("usage", bulunan);
        Assert.Contains("usage_reverse", bulunan);
        Assert.Contains("reverse", bulunan);
    }

    /// <summary>8 — Hareket defterine yazan ifade sayısı SABİT (3). Dördüncü bir yazma yolu eklenirse
    /// bu test kırılır → yukarıdaki taramanın kör noktası açıkta kalmaz.</summary>
    [Fact]
    public void Hareket_Defterine_Yazan_Yol_Sayisi_Degismedi()
    {
        int Say(string s) => Regex.Matches(s, @"INSERT INTO stock_movements").Count;
        var toplam = Say(StockSvc) + Say(OpeningSvc) + Say(MaintSvc);
        Assert.True(toplam == 3,
            $"`stock_movements`'a yazan ifade sayısı 3 değil ({toplam}). Yeni bir yazma yolu eklendiyse " +
            "hareket türü kaynak taraması (Uretim_Kodundaki_Her_Hareket_Turu_Katalogda_Var) onu görmeyebilir — " +
            "tarama kalıbı güncellenmeli.");

        // Başka bir dosyada da yazılmıyor olmalı.
        var digerleri = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .Where(f => File.ReadAllText(f).Contains("INSERT INTO stock_movements", StringComparison.Ordinal))
            .Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "MaintenanceService.cs", "OpeningStockService.cs", "StockService.cs" }, digerleri);
    }

    // ══════════════ 3. TEK KAYNAK — ÜÇ YÜZEY DE KENDİ SWITCH'İNİ KULLANMIYOR ══════════════

    /// <summary>9 — 🔴 PARİTE: üç gösterim yüzeyi de kataloğa bağlı; hiçbiri kendi etiket switch'ini
    /// taşımıyor. Aksi hâlde biri güncellenip diğeri unutulur (STK-B1'in TAM olarak sebebi buydu).</summary>
    [Fact]
    public void Uc_Gosterim_Yuzeyi_de_TEK_KAYNAKTAN_Besleniyor()
    {
        // Masaüstü hareket listeleri (StockMovementRow.TypeText) + malzeme kartı (RecentForMaterial)
        Assert.Contains("MovementTypeOptions.Label(MovementType)", StockSvc, StringComparison.Ordinal);
        Assert.Contains("var typeText = MovementTypeOptions.Label(type);", StockSvc, StringComparison.Ordinal);
        // Web hareket listesi
        Assert.Contains("MovementTypeOptions.Label(t)", WebMovements, StringComparison.Ordinal);

        // Eski elle yazılmış etiketler hiçbir yüzeyde KALMAMALI.
        foreach (var (kaynak, ad) in new[] { (StockSvc, "StockService.cs"), (WebMovements, "StockMovements.razor") })
            foreach (var eski in new[] { "\"opening\" => \"Açılış\"", "\"in\" => \"Giriş\"", "\"transfer\" => \"Transfer\"" })
                Assert.False(kaynak.Contains(eski, StringComparison.Ordinal),
                    $"{ad} hâlâ kendi hareket türü etiket switch'ini taşıyor ('{eski}') — tek kaynak bozuldu.");
    }

    /// <summary>10 — Web ve masaüstü AYNI katalog DOSYASINI derliyor (ayna dosya tutulmuyor →
    /// iki liste ıraksayamaz). Proje bu deseni ListColumns ve RequestOperationStatus için de kullanıyor.</summary>
    [Fact]
    public void Web_ve_Masaustu_Ayni_Katalog_Dosyasini_Derliyor()
    {
        Assert.Contains(@"..\DepoWise.Application\Ui\MovementTypeOptions.cs", WebCsproj, StringComparison.Ordinal);
        // Web'de ayrı bir kopya OLMAMALI.
        var webAyna = Path.Combine(Root, "src", "DepoWise.Web", "Services", "MovementTypeOptions.cs");
        Assert.False(File.Exists(webAyna),
            "Web'de ayrı bir MovementTypeOptions kopyası var — paylaşılan dosya deseni bozulmuş, iki liste ıraksayabilir.");
    }
}

/// <summary>
/// STK-B1 — GERÇEK KOD YOLU: 8 hareket türünün 8'i gerçek servislerle ÜRETİLİR ve kullanıcının
/// ekranda ne gördüğü doğrulanır. Katalogda etiket olması yetmez; üretim yolundan geçmesi gerekir.
///
/// Tamamen yerel SQLite üzerindedir (çevrimdışı yol) — HTTP yoktur.
/// </summary>
public class MovementTypeRealPathTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly MaintenanceService _maintenance;
    private readonly SessionContext _oturum;
    private readonly string _depoA, _depoB, _mat, _vehicle, _def;

    public MovementTypeRealPathTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_stkb1_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }

        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _maintenance = new MaintenanceService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(admin, new NewBranch("Depo A"));
        _depoB = branches.Create(admin, new NewBranch("Depo B"));
        _oturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        _mat = _materials.Create(_oturum, new NewMaterial("STKB1-1", "Test malzemesi"));
        _vehicle = new VehicleService(_factory, _clock)
            .Create(_oturum, new NewVehicle("STKB1-IS", "34SB101", 2020, 1000m, "km", _depoA));
        _def = new MaintenanceDefinitionService(_factory, _clock)
            .Create(_oturum, new NewMaintenanceDefinition("Periyodik", 10000m, "km"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>8 türün 8'ini GERÇEK üretim yollarıyla oluşturur.</summary>
    private void TumTurleriUret()
    {
        _opening.RecordOpening(_oturum, _mat, 100m, Op(), branchId: _depoA);            // opening
        var girisBelge = _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);  // in
        _clock.Advance(1000);
        _stock.IssueOut(_oturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);                     // out
        _clock.Advance(1000);
        _stock.Transfer(_oturum, _mat, 10m, _depoA, _depoB, Op());                                              // transfer ×2
        _clock.Advance(1000);
        _stock.Count(_oturum, new[] { new CountLine(_mat, 90m) }, "yıl sonu sayımı", Op(), branchId: _depoA);    // adjustment
        _clock.Advance(1000);
        _stock.ReverseDocument(_oturum, girisBelge.DocumentId, "yanlış giriş");                                  // reverse
        _clock.Advance(1000);
        var bakim = _maintenance.Save(_oturum, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 5000m,
            PerformedDate: _clock.UtcNow.ToUnixTimeMilliseconds(),
            Materials: new[] { new MaintenanceMaterialLine(_mat, 2m) },
            StockLocationId: _depoA), Op());                                                                     // usage
        _clock.Advance(1000);
        _maintenance.Cancel(_oturum, bakim, "yanlış bakım");                                                     // usage_reverse
    }

    private HashSet<string> DefterdekiTurler()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT movement_type FROM stock_movements WHERE company_id='A';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    /// <summary>11 — 🔴 Gerçek üretim yolları TAM OLARAK 8 tür üretiyor; hepsi katalogda.</summary>
    [Fact]
    public void Gercek_Uretim_Yollari_Tam_Olarak_Sekiz_Tur_Uretiyor()
    {
        TumTurleriUret();

        var defterde = DefterdekiTurler();
        var katalog = MovementTypeOptions.All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(8, defterde.Count);
        Assert.True(defterde.SetEquals(katalog),
            "Defterdeki türler katalogla eşleşmiyor.\nDefterde: " + string.Join(", ", defterde.OrderBy(x => x)) +
            "\nKatalogda: " + string.Join(", ", katalog.OrderBy(x => x)));
    }

    /// <summary>12 — 🔴 HAM DEĞER KAÇAĞI (gerçek kayıtlar): hareket listesinde hiçbir satır ham
    /// İngilizce tür göstermiyor. Bu, kullanıcının GERÇEKTEN gördüğü metindir.</summary>
    [Fact]
    public void Hareket_Listesinde_Hicbir_Satir_Ham_Ingilizce_Gostermiyor()
    {
        TumTurleriUret();

        var satirlar = _stock.SearchMovements(_oturum, null, null, null, 500);
        Assert.NotEmpty(satirlar);
        foreach (var satir in satirlar)
            Assert.True(satir.TypeText != satir.MovementType,
                $"Hareket listesinde ham tür görünüyor: '{satir.MovementType}' (etiket üretilememiş).");

        // 8 türün 8'i listede temsil ediliyor ve hepsinin etiketi katalogdan geliyor.
        var gorulen = satirlar.Select(x => x.MovementType).Distinct().ToList();
        Assert.Equal(8, gorulen.Count);
        foreach (var t in gorulen)
            Assert.Equal(MovementTypeOptions.Label(t), satirlar.First(x => x.MovementType == t).TypeText);
    }

    /// <summary>13 — Malzeme kartı "Son Hareketler" paneli (İKİ platformda ortak) de ham değer
    /// göstermiyor. Eskiden burada AYRI bir switch vardı ve `usage`/`usage_reverse` ham geçiyordu.</summary>
    [Fact]
    public void Malzeme_Karti_Son_Hareketler_de_Ham_Deger_Gostermiyor()
    {
        TumTurleriUret();

        var satirlar = _stock.RecentForMaterial(_oturum, _mat, 100);
        Assert.NotEmpty(satirlar);

        var hamDegerler = MovementTypeOptions.All.Select(x => x.Key).ToList();
        foreach (var satir in satirlar)
        {
            // Label biçimi: "<Tür>" ya da "<Tür> · <Depo>" → ilk parça tür etiketidir.
            var turParcasi = satir.Label.Split('·')[0].Trim();
            Assert.False(hamDegerler.Contains(turParcasi, StringComparer.Ordinal),
                $"Malzeme kartında ham tür görünüyor: '{turParcasi}'");
            Assert.True(MovementTypeOptions.All.Any(x => x.Label == turParcasi),
                $"Malzeme kartındaki '{turParcasi}' katalogda bir etiket değil.");
        }
    }

    /// <summary>14 — 🔴 `adjustment` PARİTESİ: sayım tek bir etiket üretir ve iki yüzeyde de AYNIDIR.
    /// (Eskiden masaüstü "Düzeltme", web ve malzeme kartı "Sayım Düzeltme" diyordu.)</summary>
    [Fact]
    public void adjustment_Iki_Yuzeyde_de_Ayni_Etiketi_Gosteriyor()
    {
        _opening.RecordOpening(_oturum, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(1000);
        _stock.Count(_oturum, new[] { new CountLine(_mat, 90m) }, "sayım", Op(), branchId: _depoA);

        var hareket = _stock.SearchMovements(_oturum, null, null, null, 50)
            .First(x => x.MovementType == "adjustment");
        var kart = _stock.RecentForMaterial(_oturum, _mat, 50)
            .First(x => x.Label.StartsWith("Sayım Düzeltme", StringComparison.Ordinal));

        Assert.Equal("Sayım Düzeltme", hareket.TypeText);
        Assert.StartsWith("Sayım Düzeltme", kart.Label, StringComparison.Ordinal);
        Assert.Equal(MovementTypeOptions.Label("adjustment"), hareket.TypeText);
    }

    /// <summary>15 — 🔴 `reverse` PARİTESİ: belge ters kaydı ham görünmüyor ve iki yüzeyde AYNI.
    /// (Eskiden masaüstünde HAM "reverse", web'de "İptal (ters)", malzeme kartında "İptal" idi.)</summary>
    [Fact]
    public void reverse_Ham_Gorunmuyor_ve_Iki_Yuzeyde_Ayni()
    {
        var belge = _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);
        _clock.Advance(1000);
        _stock.ReverseDocument(_oturum, belge.DocumentId, "yanlış giriş");

        var hareket = _stock.SearchMovements(_oturum, null, null, null, 50)
            .First(x => x.MovementType == "reverse");
        Assert.NotEqual("reverse", hareket.TypeText);
        Assert.Equal(MovementTypeOptions.Label("reverse"), hareket.TypeText);

        var kart = _stock.RecentForMaterial(_oturum, _mat, 50);
        Assert.Contains(kart, x => x.Label.StartsWith(MovementTypeOptions.Label("reverse"), StringComparison.Ordinal));
        Assert.DoesNotContain(kart, x => x.Label.StartsWith("reverse", StringComparison.Ordinal));
    }

    /// <summary>16 — 🔴 `usage` ve `usage_reverse` (BKM-04 ile görünür oldular) artık Türkçe ve
    /// BİRBİRİNDEN AYIRT EDİLEBİLİR görünüyor.</summary>
    [Fact]
    public void Bakim_Tuketimi_ve_Iptali_Turkce_ve_Ayirt_Edilebilir()
    {
        _opening.RecordOpening(_oturum, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(1000);
        var bakim = _maintenance.Save(_oturum, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 5000m,
            PerformedDate: _clock.UtcNow.ToUnixTimeMilliseconds(),
            Materials: new[] { new MaintenanceMaterialLine(_mat, 2m) },
            StockLocationId: _depoA), Op());
        _clock.Advance(1000);
        _maintenance.Cancel(_oturum, bakim, "iptal");

        var satirlar = _stock.SearchMovements(_oturum, null, null, null, 50);
        var tuketim = satirlar.First(x => x.MovementType == "usage");
        var iptal = satirlar.First(x => x.MovementType == "usage_reverse");

        Assert.NotEqual("usage", tuketim.TypeText);
        Assert.NotEqual("usage_reverse", iptal.TypeText);
        Assert.NotEqual(tuketim.TypeText, iptal.TypeText);
        // Bakım iptali, BELGE ters kaydıyla da karıştırılmıyor.
        Assert.NotEqual(MovementTypeOptions.Label("reverse"), iptal.TypeText);
    }

    /// <summary>17 — Transferin İKİ bacağı da "Transfer" etiketli; defterin anlamı DEĞİŞMEDİ.
    ///
    /// ⚠️ ŞUBE KAPSAMI NOTU (bu testi yazarken doğrulandı): <c>SearchMovements</c>
    /// <c>BranchScope.Sql(s, "sm.branch_id")</c> uygular. Depo A ile giriş yapmış bir oturum
    /// transferin YALNIZ kaynak bacağını görür (hedef bacağın <c>branch_id</c>'si Depo B'dir).
    /// Bu MEVCUT ve DOĞRU davranıştır — STK-B1 kapsamında değiştirilmedi. İki bacağı da görmek için
    /// "Tüm Şubeler" oturumu gerekir. (STK-10'un lokasyon filtresi tasarlanırken bu etkileşim
    /// hesaba katılmalıdır — planına not düşüldü.)</summary>
    [Fact]
    public void Transferin_Iki_Bacagi_da_Transfer_Etiketli()
    {
        _opening.RecordOpening(_oturum, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(1000);
        _stock.Transfer(_oturum, _mat, 10m, _depoA, _depoB, Op());

        // "Tüm Şubeler" oturumu (OperatingBranchId yok) → her iki bacak da görünür.
        var tumSubeler = new SessionContext(_oturum.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var bacaklar = _stock.SearchMovements(tumSubeler, null, null, null, 50)
            .Where(x => x.MovementType == "transfer").ToList();
        Assert.Equal(2, bacaklar.Count);
        Assert.All(bacaklar, b => Assert.Equal("Transfer", b.TypeText));

        // Şube kapsamlı oturumda yalnız KAYNAK bacak görünür (mevcut davranış, kilitlendi).
        var kapsamli = _stock.SearchMovements(_oturum, null, null, null, 50)
            .Where(x => x.MovementType == "transfer").ToList();
        Assert.Single(kapsamli);
        Assert.Equal("Transfer", kapsamli[0].TypeText);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
