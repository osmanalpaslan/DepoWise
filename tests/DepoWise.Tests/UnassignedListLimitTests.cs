using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// H-1 — DAĞITIM LİSTESİNİN SESSİZ KESİLMESİ (kullanıcı isteği 2026-08-12, STK-08 öncesi).
///
/// <b>HATA NEYDİ:</b> <c>ListUnassigned</c> varsayılan 500 satır döndürüyordu; web ve masaüstü limiti
/// yükseltmiyordu ve sonuçta kaç kaydın GİZLENDİĞİNİ kullanıcıya söyleyen hiçbir bilgi yoktu. Canlıda
/// ATANMAMIŞ'ta 677 bakiye satırı var (610 pozitif dağıtılabilir · 66 negatif · 1 silinmiş malzeme)
/// → kullanıcı dağıtımı "bitirdiğini" sanıp pozitif kalemleri gözden kaçırabilirdi.
///
/// <b>İKİNCİ (DAHA SİNSİ) HATA:</b> sıfır bakiyeli satırlar SQL'de değil, LIMIT'ten SONRA C#'ta
/// eleniyordu → sıfırlar limitten YER KAPIYORDU. Dağıtım ilerledikçe tükenen kalemler ATANMAMIŞ'ta
/// 0 satırı olarak KALDIĞI için (bilinçli davranış), ikinci turda liste sıfırlarla dolup gerçek
/// kalemleri dışarı itebilirdi. Bu, çok turlu STK-08 dağıtımını sessizce yarım bırakırdı.
///
/// Bu dosya önce hatayı YENİDEN ÜRETİR (eski davranışın simülasyonu), sonra düzeltmenin A–I
/// senaryolarında çalıştığını kanıtlar. İZOLE SQLite; PRODUCTION'A BAĞLANMAZ.
/// </summary>
public class UnassignedListLimitTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _admin;
    private readonly string _depo;

    public UnassignedListLimitTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_h1_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depo = new BranchService(_factory, _clock).Create(_admin, new NewBranch("ANKARA GENEL MERKEZ"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>ATANMAMIŞ kovasında verilen miktarda stoğu olan malzeme üretir. Miktar negatif olabilir
    /// (ADR-086 devralınan eksik stok) — gerçek veri böyle.</summary>
    private string Uret(string kod, decimal miktar)
    {
        var m = _materials.Create(_admin, new NewMaterial(kod, "Malzeme " + kod));
        if (miktar != 0m) _opening.RecordOpening(_admin, m, miktar, Op());
        return m;
    }

    /// <summary>ATANMAMIŞ kovasında bakiye satırı OLAN ama miktarı SIFIR olan malzeme
    /// (dağıtımı tamamlanmış kalem — satır silinmez, 0 olarak kalır).</summary>
    private string UretSifirlanmis(string kod)
    {
        var m = Uret(kod, 1m);
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 1m) }, _depo, Op());
        Assert.Equal(0m, _stock.GetBalanceAt(_admin, m, StockBalanceWriter.Unassigned));
        return m;
    }

    /// <summary>ESKİ DAVRANIŞIN SİMÜLASYONU: sıfır filtresi olmadan LIMIT uygulayıp sonra C#'ta elemek.
    /// Düzeltmenin gerçekten bir şeyi değiştirdiğini kanıtlamak için burada duruyor.</summary>
    private List<string> EskiDavranis(int limit)
    {
        var list = new List<string>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.code, sb.quantity
FROM stock_balances sb
JOIN materials m ON m.id = sb.material_id AND m.company_id = sb.company_id AND m.is_deleted = 0
WHERE sb.company_id='A' AND sb.location_id=''
ORDER BY m.code LIMIT @lim;";
        cmd.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (Money.Parse(r.GetString(1)) != 0m) list.Add(r.GetString(0));   // eleme LIMIT'ten SONRA
        return list;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 0 — HATANIN YENİDEN ÜRETİMİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>0a — ESKİ davranış: 520 kalemin 500'ü döner ve kaç kaydın gizlendiğine dair
    /// HİÇBİR sinyal yoktur. YENİ davranış: hepsi döner + sayım bilgisi gelir.</summary>
    [Fact]
    public void H0a_Eski_Davranis_Sessizce_Keserdi_Yeni_Davranis_Sayiyi_Soyler()
    {
        for (int i = 0; i < 520; i++) Uret($"K-{i:D4}", 1m);

        // ESKİ: 500 satır, "devamı var" bilgisi YOK
        Assert.Equal(500, EskiDavranis(500).Count);

        // YENİ: tamamı + açık sayım
        var page = _stock.ListUnassignedPage(_admin);
        Assert.Equal(520, page.Items.Count);
        Assert.Equal(520, page.TotalCount);
        Assert.False(page.Truncated);
        Assert.Equal("520 kayıt bulundu.", page.CountText);
    }

    /// <summary>0b — ⭐ ASIL SİNSİ HATA: 500 adet SIFIRLANMIŞ kalem (dağıtımı bitmiş) + 10 pozitif kalem.
    /// Eski yolda sıfırlar limiti doldurup pozitifleri dışarı iterdi → ekran <b>BOŞ</b> görünürdü ve
    /// kullanıcı "dağıtım bitti" sanırdı. Yeni yolda sıfırlar SQL'de elenir → 10 kalem görünür.</summary>
    [Fact]
    public void H0b_Sifirlanmis_Kalemler_Limitten_Yer_KAPMAZ()
    {
        // Kodlar alfabetik: sıfırlananlar "A-..." (önce), pozitifler "Z-..." (sonra) → eski yol hepsini kaçırır
        for (int i = 0; i < 500; i++) UretSifirlanmis($"A-{i:D4}");
        for (int i = 0; i < 10; i++) Uret($"Z-{i:D4}", 5m);

        // ESKİ: 500'lük pencere tamamen sıfırlarla dolar → HİÇBİR pozitif kalem görünmez
        Assert.Empty(EskiDavranis(500));

        // YENİ: sıfırlar SQL'de elendiği için 10 pozitif kalemin tamamı görünür
        var page = _stock.ListUnassignedPage(_admin, null, 500);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(10, page.TotalCount);
        Assert.Equal(10, page.DistributableCount);
        Assert.False(page.Truncated);
        Assert.All(page.Items, x => Assert.StartsWith("Z-", x.Code));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // A–D — SINIR DEĞERLERİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A/B/C/D — 499 · 500 · 501 · 676 kayıt: hiçbirinde sessiz kesilme yok, sayım doğru.</summary>
    [Theory]
    [InlineData(499)]   // A) 500'den az
    [InlineData(500)]   // B) tam 500
    [InlineData(501)]   // C) 501
    [InlineData(676)]   // D) canlıdaki satır sayısı
    public void HABCD_Sinir_Degerlerinde_Sessiz_Kesilme_Yok(int adet)
    {
        for (int i = 0; i < adet; i++) Uret($"S-{i:D4}", 1m);

        var page = _stock.ListUnassignedPage(_admin);
        Assert.Equal(adet, page.Items.Count);       // hepsi ekranda
        Assert.Equal(adet, page.TotalCount);
        Assert.Equal(adet, page.DistributableCount);
        Assert.False(page.Truncated);
        Assert.Equal(0, page.HiddenCount);
        Assert.Equal($"{adet} kayıt bulundu.", page.CountText);

        // Eski varsayılan (500) HÂLÂ desteklenir; ama artık kaçının gizlendiğini SÖYLER.
        var dar = _stock.ListUnassignedPage(_admin, null, 500);
        if (adet > 500)
        {
            Assert.True(dar.Truncated);
            Assert.Equal(adet - 500, dar.HiddenCount);
            Assert.Equal($"{adet} kayıt bulundu. 500 kayıt gösteriliyor ({adet - 500} kayıt ekranda değil).",
                dar.CountText);
        }
        else Assert.False(dar.Truncated);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // E & G — GERÇEK STK-08 DAĞILIMI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>E/G — ⭐ CANLI DAĞILIMIN BİREBİR KOPYASI: 610 pozitif + 66 negatif + 1 silinmiş = 677 satır.
    /// <b>610 pozitif kalemin TAMAMI erişilebilir olmalı.</b> Negatifler görünür ama dağıtılabilir
    /// sayılmaz; silinmiş malzeme hiç görünmez (iş kuralları GEVŞETİLMEDİ).</summary>
    [Fact]
    public void HEG_Gercek_STK08_Dagilimi_610_Pozitifin_Tamami_Erisilebilir()
    {
        var pozitifKodlar = new List<string>();
        for (int i = 0; i < 610; i++) { pozitifKodlar.Add($"P-{i:D4}"); Uret($"P-{i:D4}", 1m + i % 7); }
        for (int i = 0; i < 66; i++) Uret($"N-{i:D4}", -(1m + i % 3));

        // 1 SİLİNMİŞ malzeme: bakiyesi var ama malzeme is_deleted (canlıdaki "TEST" kaydının durumu).
        // MLZ-01 kapısı bunu bugün üretmez → devralınan veri doğrudan taklit edilir.
        var silinmis = Uret("T-TEST", 2m);
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE materials SET is_deleted=1 WHERE id=@m;";
            cmd.AddWithValue("@m", silinmis);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        // Ham gerçek: ATANMAMIŞ kovasında 677 bakiye satırı var
        Assert.Equal(677, HamSatirSayisi());

        var page = _stock.ListUnassignedPage(_admin);
        Assert.Equal(676, page.TotalCount);            // silinmiş malzeme listelenmez (iş kuralı korundu)
        Assert.Equal(676, page.Items.Count);           // hepsi ekranda
        Assert.Equal(610, page.DistributableCount);    // yalnız pozitifler dağıtılabilir
        Assert.False(page.Truncated);
        Assert.Equal(0, page.HiddenCount);
        Assert.Equal("676 kayıt bulundu.", page.CountText);

        // G) 610 pozitif kalemin TAMAMI listede — tek tek doğrula
        var gorunen = page.Items.Where(x => x.Quantity > 0).Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(610, gorunen.Count);
        Assert.All(pozitifKodlar, k => Assert.Contains(k, gorunen));

        // Negatifler görünür (kullanıcı devralınan eksiği bilmeli) ama dağıtılamaz
        Assert.Equal(66, page.Items.Count(x => x.Quantity < 0));
        // Silinmiş malzeme HİÇBİR koşulda listede yok
        Assert.DoesNotContain(page.Items, x => x.Code == "T-TEST");

        // ARAMA ile erişilebilen pozitif kalem sayısı da 610 (arama tüm kümede çalışıyor)
        var aramaPozitif = _stock.ListUnassignedPage(_admin, "P-");
        Assert.Equal(610, aramaPozitif.Items.Count);
        Assert.Equal(610, aramaPozitif.TotalCount);
        Assert.Equal(610, aramaPozitif.DistributableCount);
        Assert.False(aramaPozitif.Truncated);

        // Arama silinmiş kalemi getirmez (kod'u aransa bile)
        Assert.Empty(_stock.ListUnassignedPage(_admin, "T-TEST").Items);
    }

    private int HamSatirSayisi()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_balances WHERE company_id='A' AND location_id='';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // F — ARAMA TÜM VERİ KÜMESİNE UYGULANIR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>F — Arama, "ilk 500 içinde" değil <b>TÜM veri kümesinde</b> yapılır: alfabetik olarak
    /// 500'ün ÇOK ötesindeki bir kalem aramayla bulunur. Sayımlar da aynı aramaya göre hesaplanır.</summary>
    [Fact]
    public void HF_Arama_Ilk_500_Ile_Sinirli_Degil()
    {
        for (int i = 0; i < 600; i++) Uret($"C-{i:D4}", 1m);
        Uret("ZZZ-SON-KALEM", 42m);      // alfabetik EN SONDA → dar pencerede asla görünmez

        // Dar pencerede (500) gerçekten görünmüyor — testin anlamlı olduğunun kanıtı
        var dar = _stock.ListUnassignedPage(_admin, null, 500);
        Assert.True(dar.Truncated);
        Assert.DoesNotContain(dar.Items, x => x.Code == "ZZZ-SON-KALEM");

        // ...ama ARAMA onu bulur (SQL'de aranıyor, bellekte değil)
        var arama = _stock.ListUnassignedPage(_admin, "ZZZ-SON", 500);
        var bulunan = Assert.Single(arama.Items);
        Assert.Equal("ZZZ-SON-KALEM", bulunan.Code);
        Assert.Equal(42m, bulunan.Quantity);
        // Sayımlar da ARAMAYA göre: "601 kayıt var" değil, "1 kayıt bulundu"
        Assert.Equal(1, arama.TotalCount);
        Assert.Equal(1, arama.DistributableCount);
        Assert.False(arama.Truncated);
        Assert.Equal("1 kayıt bulundu.", arama.CountText);

        // Ada göre arama da aynı kümede çalışır (kod VEYA ad)
        Assert.Single(_stock.ListUnassignedPage(_admin, "Malzeme ZZZ-SON", 500).Items);
    }

    /// <summary>F2 — Arama, sıfırlanmış ve silinmiş kalemleri de doğru eler (liste büyüsün diye
    /// kural gevşetilmedi) ve hiç sonuç yoksa metin bunu söyler.</summary>
    [Fact]
    public void HF2_Arama_Sifir_ve_Silinmisi_Getirmez()
    {
        UretSifirlanmis("BUL-SIFIR");
        Uret("BUL-POZITIF", 3m);

        var page = _stock.ListUnassignedPage(_admin, "BUL-");
        var tek = Assert.Single(page.Items);
        Assert.Equal("BUL-POZITIF", tek.Code);
        Assert.Equal(1, page.TotalCount);

        var bos = _stock.ListUnassignedPage(_admin, "HIC-YOK");
        Assert.Empty(bos.Items);
        Assert.Equal(0, bos.TotalCount);
        Assert.False(bos.Truncated);
        Assert.Equal("Dağıtılacak atanmamış stok yok.", bos.CountText);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ÇOK TURLU DAĞITIM — H-1'in asıl amacı
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Çok turlu dağıtım: 676 kalemin tamamı, tükendikçe listeden düşerek, hiçbiri gözden
    /// kaçmadan dağıtılabiliyor mu? Her turda sayım bilgisi gerçeği söylemeli.</summary>
    [Fact]
    public void H_Cok_Turlu_Dagitim_Hicbir_Kalemi_Gozden_Kacirmaz()
    {
        for (int i = 0; i < 610; i++) Uret($"P-{i:D4}", 2m);
        for (int i = 0; i < 66; i++) Uret($"N-{i:D4}", -1m);

        var dagitilan = new HashSet<string>(StringComparer.Ordinal);
        int tur = 0;
        while (tur++ < 10)
        {
            var page = _stock.ListUnassignedPage(_admin, null, 200);   // bilerek DAR pencere
            var pozitif = page.Items.Where(x => x.Quantity > 0).ToList();
            if (pozitif.Count == 0) break;

            // Kullanıcı ekranda gördüğü kadarını dağıtır
            _stock.DistributeUnassigned(_admin,
                pozitif.Select(x => new StockLine(x.MaterialId, x.Quantity)).ToList(), _depo, Op());
            foreach (var x in pozitif) dagitilan.Add(x.Code);
        }

        // 610 pozitif kalemin TAMAMI dağıtıldı — hiçbiri limitin arkasında kalmadı
        Assert.Equal(610, dagitilan.Count);

        // Geriye yalnız 66 negatif kalır; dağıtılabilir 0 ve bu AÇIKÇA yazılır
        var son = _stock.ListUnassignedPage(_admin);
        Assert.Equal(66, son.TotalCount);
        Assert.Equal(0, son.DistributableCount);
        Assert.All(son.Items, x => Assert.True(x.Quantity < 0));
        Assert.False(son.Truncated);

        // Tüm pozitif stok hedefe taşındı; ATANMAMIŞ'ta pozitif kalmadı
        Assert.Equal(1220m, ToplamLokasyon(_depo));           // 610 × 2
        Assert.Equal(-66m, ToplamLokasyon(StockBalanceWriter.Unassigned));
    }

    private decimal ToplamLokasyon(string locationId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE company_id='A' AND location_id=@l;";
        cmd.AddWithValue("@l", locationId);
        decimal t = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read()) t += Money.Parse(r.GetString(0));
        return t;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // GERİYE UYUMLULUK + SINIRLAR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Eski <see cref="StockService.ListUnassigned"/> imzası KORUNDU (mevcut çağıranlar
    /// kırılmadı) ve varsayılanı hâlâ 500'dür; yeni sayfa metodu ise ÜST SINIRI ister.</summary>
    [Fact]
    public void H_Eski_Imza_Korundu_Varsayilanlar_Belgelendigi_Gibi()
    {
        for (int i = 0; i < 600; i++) Uret($"U-{i:D4}", 1m);

        Assert.Equal(500, StockService.DefaultUnassignedLimit);
        Assert.Equal(2000, StockService.MaxUnassignedLimit);
        Assert.Equal(500, _stock.ListUnassigned(_admin).Count);              // eski davranış aynen
        Assert.Equal(600, _stock.ListUnassignedPage(_admin).Items.Count);    // yeni yol tamamını verir

        // Üst sınır aşılamaz (kaçak büyütme yok) ve alt sınır güvenli
        Assert.Equal(2000, _stock.ListUnassignedPage(_admin, null, 99_999).Limit);
        Assert.Equal(1, _stock.ListUnassignedPage(_admin, null, 0).Limit);
    }

    /// <summary>Yetki kapısı yeni yolda da geçerli (deny-by-default gevşetilmedi).</summary>
    [Fact]
    public void H_Yetkisiz_Kullanici_Sayfayi_Da_Goremez()
    {
        var yetkisiz = new SessionContext("u2", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _stock.ListUnassignedPage(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _stock.ListUnassigned(yetkisiz));
    }

    /// <summary>Sıfır bakiye "0" dışında "0.00" gibi ÖLÇEKLİ metinlerle de yazılabilir; sayısal filtre
    /// üçünü de eler (metin karşılaştırması bunu yapamazdı).</summary>
    [Fact]
    public void H_Olcekli_Sifir_Metinleri_De_Elenir()
    {
        var m = Uret("OLCEK-01", 1m);
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE stock_balances SET quantity='0.000' WHERE company_id='A' AND material_id=@m AND location_id='';";
            cmd.AddWithValue("@m", m);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        var page = _stock.ListUnassignedPage(_admin);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // I — MASAÜSTÜ ÇAĞRI SÖZLEŞMESİ (kaynak taraması — RPR-01 deseni)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>I — Masaüstü dağıtım ekranı sayfa metodunu kullanmalı ve sayım metnini göstermelidir.
    /// Bir gün biri eski metoda dönerse ya da sabit 500 yazarsa bu test KIRILIR (sessiz gerileme yok).</summary>
    [Fact]
    public void HI_Masaustu_Sayfa_Metodunu_Kullanir_Ve_Sayimi_Gosterir()
    {
        var vm = Kaynak("src/DepoWise.Desktop/ViewModels/StockDistributeViewModel.cs");
        Assert.Contains("ListUnassignedPage", vm);
        Assert.DoesNotContain("ListUnassigned(_session", vm);   // eski, sayım taşımayan çağrı KALMADI
        Assert.Contains("CountText", vm);
        Assert.Contains("Truncated", vm);

        var view = Kaynak("src/DepoWise.Desktop/Views/StockDistributeView.axaml");
        Assert.Contains("Binding CountText", view);             // metin GERÇEKTEN ekranda
    }

    /// <summary>H — Web tarafı: API sayım bilgisini döndürmeli, sayfa da onu göstermelidir.</summary>
    [Fact]
    public void HH_Web_Ve_Api_Sayimi_Tasir_Ve_Gosterir()
    {
        var api = Kaynak("src/DepoWise.Api/Program.cs");
        Assert.Contains("ListUnassignedPage", api);
        Assert.Contains("countText", api);
        Assert.Contains("truncated", api);
        Assert.Contains("MaxUnassignedLimit", api);             // varsayılan limit üst sınıra çekildi

        var web = Kaynak("src/DepoWise.Web/Components/Pages/StockDistribute.razor");
        Assert.Contains("countText", web);
        Assert.Contains("_countText", web);
        Assert.Contains("_truncated", web);
        Assert.Contains("\"items\"", web);                      // yeni nesne yanıtı okunuyor
    }

    private static string Kaynak(string rel)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return File.ReadAllText(p);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("Kaynak dosya bulunamadı: " + rel);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
