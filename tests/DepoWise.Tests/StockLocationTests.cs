using DepoWise.Application.Common;
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
/// STK-02 (FAZ C, 2026-08-11) — DEPO/LOKASYON BAZLI STOK BAKİYESİ.
///
/// Migration064 ile <c>stock_balances</c> anahtarı <c>(company_id, material_id, location_id)</c> oldu:
/// bir malzemenin ARTIK BİRDEN ÇOK bakiye satırı olabilir. Bu dosya, o değişikliğin iki yönünü birlikte
/// kilitler:
///   • <b>Ayrışma:</b> her deponun stoğu kendi kovasında durur (transfer artık bakiyede GÖRÜNÜR).
///   • <b>Kopmama:</b> firma geneli toplam = lokasyon toplamları — liste/rapor/dashboard satır ÇOĞALTMAZ.
///
/// ⚠️ Bu testler "geçsin diye" gevşetilmemelidir: buradaki bir kırılma, kullanıcıya <b>sessizce yanlış
/// stok</b> göstermek demektir (en tehlikeli hata türü).
/// </summary>
public class StockLocationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _admin;
    private readonly string _depoA;
    private readonly string _depoB;

    public StockLocationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_loc_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        // "Tüm Şubeler" oturumu (BranchScope null) → depoyu her çağrıda AÇIKÇA veriyoruz.
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_admin, new NewBranch("Depo A"));
        _depoB = branches.Create(_admin, new NewBranch("Depo B"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code, decimal minStock = 0m)
        => _materials.Create(_admin, new NewMaterial(code, code, MinStock: minStock));

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Bakiye tablosundaki HAM satırlar — (lokasyon → metin miktar). Testin gördüğü şey,
    /// servisin hesapladığı değil, veritabanına GERÇEKTEN yazılandır.</summary>
    private Dictionary<string, string> RawRows(string materialId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT location_id, quantity FROM stock_balances WHERE company_id='A' AND material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetString(1);
        return map;
    }

    // ── 1. Ayrışma: hangi hareket hangi kovaya yazıldı ───────────────────────────────────

    /// <summary>1 — Giriş, belgenin deposuna yazılır (rastgele/ilk depoya DEĞİL).</summary>
    [Fact]
    public void Giris_BelgeninDeposuna_Yazilir()
    {
        var m = Mat("L-01");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);

        var rows = RawRows(m);
        Assert.Single(rows);
        Assert.True(rows.ContainsKey(_depoA));
        Assert.Equal(10m, Money.Parse(rows[_depoA]));
    }

    /// <summary>2 — Aynı malzeme iki depoda → İKİ ayrı bakiye satırı; ikisi birbirini EZMEZ.</summary>
    [Fact]
    public void IkiDepodakiAyniMalzeme_AyriSatirlarda_Tutulur()
    {
        var m = Mat("L-02");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);

        var rows = RawRows(m);
        Assert.Equal(2, rows.Count);
        Assert.Equal(10m, Money.Parse(rows[_depoA]));
        Assert.Equal(4m, Money.Parse(rows[_depoB]));
    }

    /// <summary>3 — Deposuz (Tüm Şubeler/idari) hareket ATANMAMIŞ ('') kovasına gider; rastgele şubeye ASLA.</summary>
    [Fact]
    public void DeposuzHareket_ATANMAMIS_Kovasina_Gider()
    {
        var m = Mat("L-03");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 7m) }, Op());   // branchId verilmedi

        var rows = RawRows(m);
        Assert.Single(rows);
        Assert.True(rows.ContainsKey(StockBalanceWriter.Unassigned));
        Assert.Equal(7m, Money.Parse(rows[StockBalanceWriter.Unassigned]));
    }

    /// <summary>4 — Açılış stoğu da kendi lokasyonuna yazılır (defterle birebir).</summary>
    [Fact]
    public void AcilisStogu_KendiLokasyonuna_Yazilir()
    {
        var m = Mat("L-04");
        _opening.RecordOpening(_admin, m, 25m, Op(), branchId: _depoB);

        var rows = RawRows(m);
        Assert.Single(rows);
        Assert.Equal(25m, Money.Parse(rows[_depoB]));
        Assert.Equal(25m, _opening.GetBalance(_admin, m));   // firma geneli toplam
    }

    // ── 2. Kopmama: toplam okuma yolları ────────────────────────────────────────────────

    /// <summary>5 — <c>GetBalance</c> FİRMA GENELİ toplamdır; <c>GetBalanceAt</c> tek lokasyondur.</summary>
    [Fact]
    public void GetBalance_FirmaGeneli_GetBalanceAt_TekLokasyon()
    {
        var m = Mat("L-05");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);

        Assert.Equal(14m, _stock.GetBalance(_admin, m));
        Assert.Equal(10m, _stock.GetBalanceAt(_admin, m, _depoA));
        Assert.Equal(4m, _stock.GetBalanceAt(_admin, m, _depoB));
        Assert.Equal(0m, _stock.GetBalanceAt(_admin, m, StockBalanceWriter.Unassigned));
    }

    /// <summary>6 — Lokasyon kırılımı toplamla KOPMAZ (Σ kırılım = genel toplam).</summary>
    [Fact]
    public void LokasyonKirilimi_Toplamiyla_Kopmaz()
    {
        var m = Mat("L-06");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 1m) }, Op());   // ATANMAMIŞ

        var byLoc = _stock.GetBalancesByLocation(_admin, m);
        Assert.Equal(3, byLoc.Count);
        Assert.Equal(_stock.GetBalance(_admin, m), byLoc.Values.Sum());
        Assert.Equal(15m, byLoc.Values.Sum());
    }

    /// <summary>7 — TOPLU okuma (<c>GetBalances</c>, N+1 önleyen tek sorgu) de lokasyonları TOPLAR;
    /// malzemeyi iki kez döndürmez.</summary>
    [Fact]
    public void TopluOkuma_LokasyonlariToplar_MalzemeyiTekrarlamaz()
    {
        var m1 = Mat("L-07a");
        var m2 = Mat("L-07b");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m1, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m1, 4m) }, Op(), branchId: _depoB);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m2, 3m) }, Op(), branchId: _depoA);

        var map = _stock.GetBalances(_admin, new[] { m1, m2 });
        Assert.Equal(2, map.Count);
        Assert.Equal(14m, map[m1]);
        Assert.Equal(3m, map[m2]);
    }

    // ── 3. Transfer artık bakiyede GÖRÜNÜR (STK-02'nin asıl amacı) ──────────────────────

    /// <summary>8 — Transfer: kaynak azalır, hedef artar, FİRMA TOPLAMI değişmez.</summary>
    [Fact]
    public void Transfer_KaynagiAzaltir_HedefiArtirir_ToplamSabit()
    {
        var m = Mat("L-08");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);

        _stock.Transfer(_admin, m, 4m, _depoA, _depoB, Op());

        Assert.Equal(6m, _stock.GetBalanceAt(_admin, m, _depoA));
        Assert.Equal(4m, _stock.GetBalanceAt(_admin, m, _depoB));
        Assert.Equal(10m, _stock.GetBalance(_admin, m));   // net toplam korunur
    }

    /// <summary>9 — Bir depodaki stok, DİĞER deponun çıkışını finanse EDEMEZ (fail-closed).</summary>
    [Fact]
    public void BaskaDepodakiStok_BuDeponunCikisini_Karsilamaz()
    {
        var m = Mat("L-09");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);

        Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 1m) }, Op(), branchId: _depoB));

        // Reddedilen çıkış HİÇBİR kovayı değiştirmemiş olmalı.
        Assert.Equal(10m, _stock.GetBalanceAt(_admin, m, _depoA));
        Assert.Equal(0m, _stock.GetBalanceAt(_admin, m, _depoB));
    }

    /// <summary>10 — Çıkış yalnız KENDİ deposunu düşürür; diğer depo etkilenmez.</summary>
    [Fact]
    public void Cikis_YalnizKendiDeposunu_Dusurur()
    {
        var m = Mat("L-10");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, Op(), branchId: _depoB);

        _stock.IssueOut(_admin, new[] { new StockLine(m, 3m) }, Op(), branchId: _depoA);

        Assert.Equal(7m, _stock.GetBalanceAt(_admin, m, _depoA));
        Assert.Equal(5m, _stock.GetBalanceAt(_admin, m, _depoB));
        Assert.Equal(12m, _stock.GetBalance(_admin, m));
    }

    /// <summary>11 — Ters kayıt (iptal) miktarı ORİJİNAL lokasyona geri verir, başka depoya değil.</summary>
    [Fact]
    public void TersKayit_OrijinalLokasyona_Geri_Verir()
    {
        var m = Mat("L-11");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        var doc = _stock.IssueOut(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoA);
        Assert.Equal(6m, _stock.GetBalanceAt(_admin, m, _depoA));

        _stock.ReverseDocument(_admin, doc.DocumentId, "hatalı çıkış");

        Assert.Equal(10m, _stock.GetBalanceAt(_admin, m, _depoA));
        Assert.Equal(0m, _stock.GetBalanceAt(_admin, m, _depoB));
        Assert.Equal(10m, _stock.GetBalance(_admin, m));
    }

    /// <summary>12 — Sayım, SAYILAN deponun bakiyesiyle karşılaştırır ("genelden oku, lokasyona yaz" olmaz).
    /// Depo B'de 5 varken Depo A 12 sayılırsa fark 12−10=+2 olmalı (12−15 = −3 DEĞİL).</summary>
    [Fact]
    public void Sayim_SayilanDeponun_Bakiyesiyle_Karsilastirir()
    {
        var m = Mat("L-12");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, Op(), branchId: _depoB);

        _stock.Count(_admin, new[] { new CountLine(m, 12m) }, "yıl sonu sayımı", Op(), branchId: _depoA);

        Assert.Equal(12m, _stock.GetBalanceAt(_admin, m, _depoA));   // sayılan değere oturur
        Assert.Equal(5m, _stock.GetBalanceAt(_admin, m, _depoB));    // diğer depo DOKUNULMAZ
        Assert.Equal(17m, _stock.GetBalance(_admin, m));
    }

    // ── 4. Yeniden hesaplama ve hassasiyet ──────────────────────────────────────────────

    /// <summary>13 — Sunucu-otoriteli yeniden hesaplama lokasyon kırılımını KORUR ve bozuk satırı düzeltir.</summary>
    [Fact]
    public void RecomputeBalances_LokasyonKirilimini_Korur()
    {
        var m = Mat("L-13");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);

        // Bakiyeyi KASTEN boz + gerçekte olmayan bir "hayalet" lokasyon satırı ekle.
        using (var conn = _factory.Create())
        {
            using var bad = conn.CreateCommand();
            bad.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m AND location_id=@l;";
            bad.AddWithValue("@m", m); bad.AddWithValue("@l", _depoA);
            bad.ExecuteNonQuery();

            using var ghost = conn.CreateCommand();
            ghost.CommandText = "INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) " +
                                "VALUES('A',@m,'hayalet-depo','77',1);";
            ghost.AddWithValue("@m", m);
            ghost.ExecuteNonQuery();
        }

        _stock.RecomputeBalances("A");

        var rows = RawRows(m);
        Assert.Equal(2, rows.Count);                                  // hayalet satır TEMİZLENDİ
        Assert.Equal(10m, Money.Parse(rows[_depoA]));
        Assert.Equal(4m, Money.Parse(rows[_depoB]));
        Assert.Equal(14m, _stock.GetBalance(_admin, m));
    }

    /// <summary>14 — ONDALIK HASSASİYET: toplama C# <c>decimal</c> ile yapılır. 0.1 + 0.2 kayan noktada
    /// 0.30000000000000004 eder; burada TAM 0.3 olmalıdır.</summary>
    [Fact]
    public void OndalikToplam_KayanNoktaya_Dusmez()
    {
        var m = Mat("L-14");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 0.1m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 0.2m) }, Op(), branchId: _depoB);
        Assert.Equal(0.3m, _stock.GetBalance(_admin, m));

        var m2 = Mat("L-14b");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m2, 10.25m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m2, 99.99m) }, Op(), branchId: _depoB);
        Assert.Equal(110.24m, _stock.GetBalance(_admin, m2));
        Assert.Equal(110.24m, _stock.GetBalances(_admin, new[] { m2 })[m2]);

        // LİSTE yolu AYRI bir toplama kullanır (SQL alt sorgusu; SqlDialect.StockTotalSubquery).
        // Kanonik metin biçimi sayesinde ondalık burada da kaymamalı — iki yol AYNI sayıyı vermeli.
        Assert.Equal(0.3m, Single(_materials.SearchGrid(_admin, new MaterialGridFilter(Code: "L-14"), 1, 50).Items
            .Where(x => x.Code == "L-14")).Stock);
        Assert.Equal(110.24m, Single(_materials.SearchGrid(_admin, new MaterialGridFilter(Code: "L-14b"), 1, 50).Items).Stock);
    }

    private static T Single<T>(IEnumerable<T> items) => Assert.Single(items);

    // ── 5. Liste / rapor / dashboard SATIR ÇOĞALTMAZ ────────────────────────────────────

    /// <summary>15 — Malzeme listesi (grid): iki depolu malzeme TEK satır, stok kolonu TOPLAM.
    /// (Düz JOIN olsaydı aynı malzeme iki kez listelenirdi — kullanıcının gördüğü en yıkıcı hata.)</summary>
    [Fact]
    public void MalzemeListesi_IkiDepoluMalzemeyi_Cogaltmaz()
    {
        var m = Mat("L-15");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);

        var grid = _materials.SearchGrid(_admin, new MaterialGridFilter(Code: "L-15"), 1, 50);
        Assert.Equal(1, grid.TotalCount);
        Assert.Equal(14m, Assert.Single(grid.Items).Stock);

        // Malzeme kartı (detay) da toplamı göstermeli.
        Assert.Equal(14m, _materials.GetDetail(_admin, m).Stock);
    }

    /// <summary>16 — Düşük stok uyarısı: iki depolu malzeme TEK kez sayılır ve eşik FİRMA TOPLAMINA
    /// göre değerlendirilir (min 12; toplam 14 → uyarı YOK; düz JOIN olsaydı 10 ve 4 ayrı ayrı eşiğin
    /// altında görünüp İKİ kez uyarı üretirdi).</summary>
    [Fact]
    public void DusukStokUyarisi_Cogaltmaz_ToplamaGoreDegerlendirir()
    {
        var m = Mat("L-16", minStock: 12m);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);

        var dashboard = new DashboardService(_factory,
            new DepoWise.Infrastructure.Maintenance.MaintenanceService(_factory, _clock),
            new DepoWise.Infrastructure.Maintenance.InspectionService(_factory, _clock));

        var dash = dashboard.GetSummary(_admin);
        Assert.DoesNotContain(dash.Alerts, a => a.Title.Contains("L-16"));
        Assert.Equal(0, dash.LowStockCount);

        // Eşiğin ALTINA düşünce TEK uyarı çıkmalı (iki depo → iki uyarı DEĞİL).
        _stock.IssueOut(_admin, new[] { new StockLine(m, 3m) }, Op(), branchId: _depoA);   // toplam 11 < 12
        var dash2 = dashboard.GetSummary(_admin);
        Assert.Equal(1, dash2.Alerts.Count(a => a.Title.Contains("L-16")));
        Assert.Equal(1, dash2.LowStockCount);   // sayı ile liste KOPMAZ
    }

    /// <summary>17 — Malzeme SİLME koruması: stok BAŞKA depodaysa da görülmeli. Eski tek-satır okuması
    /// yalnız ilk kovaya bakardı → başka depoda malı olan malzeme silinebilirdi.</summary>
    [Fact]
    public void MalzemeSilme_BaskaDepodakiStogu_Gorur()
    {
        var m = Mat("L-17");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 6m) }, Op(), branchId: _depoB);

        var ex = Assert.ThrowsAny<Exception>(() => _materials.Delete(_admin, m));
        Assert.Contains("stokta", ex.Message);
    }

    // ── 6. STK-03 — MASAÜSTÜ (ÇEVRİMDIŞI) SÖZLEŞMESİ ───────────────────────────────────────

    /// <summary>18 — MASAÜSTÜ SÖZLEŞMESİ: masaüstü stok uçlarını KULLANMAZ; bu servisi çevrimdışı,
    /// API'ye hiç uğramadan çağırır. Lokasyon sahiplik kontrolü bu yüzden API'ye değil SERVİSE kondu —
    /// aksi hâlde çevrimdışı yol korumasız kalır ve yabancı depo kimliği bakiyenin BİRİNCİL ANAHTARINA
    /// yazılırdı. Burada internet YOK; koruma yine de çalışmalı.</summary>
    [Fact]
    public void Masaustu_Cevrimdisi_Yolda_Da_Yabanci_Depo_Reddedilir()
    {
        // Başka firmanın gerçek deposu (aynı yerel veritabanında; sync ile inmiş olabilir).
        var users = new UserService(_factory, _clock);
        var otherId = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var otherAdmin = new SessionContext(otherId, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yabanciDepo = new BranchService(_factory, _clock).Create(otherAdmin, new NewBranch("B Deposu"));

        var m = Mat("L-18");
        Assert.Throws<ForbiddenException>(() =>
            _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, Op(), branchId: yabanciDepo));
        Assert.Throws<ForbiddenException>(() =>
            _opening.RecordOpening(_admin, m, 5m, Op(), branchId: yabanciDepo));

        Assert.Empty(RawRows(m));   // hiçbir bakiye satırı oluşmadı
    }

    /// <summary>19 — ÇEVRİMDIŞI → SENKRON: masaüstünde internetsiz yazılan lokasyonlu hareketler
    /// sunucuya taşındığında sunucu, hareket defterinden AYNI lokasyon kırılımını üretir.
    /// Bakiye türetilmiş veridir; sync'te ayrı bir doğruluk kaynağı olarak taşınmaz (ADR-102).
    /// Sync kodu STK-02/03'te DEĞİŞTİRİLMEDİ — bu test onun hâlâ doğru olduğunun kanıtıdır.</summary>
    [Fact]
    public void Cevrimdisi_Yazilan_Lokasyonlu_Hareketler_Sunucuda_Ayni_Kirilimi_Uretir()
    {
        var m = Mat("L-19");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _depoB);
        _stock.Transfer(_admin, m, 3m, _depoA, _depoB, Op());   // A:7 · B:7

        // "Sunucu" = ayrı veritabanı. Masaüstünün snapshot'ı uygulanır (gerçek push yolunun aynısı).
        var serverPath = Path.Combine(Path.GetTempPath(), "depowise_loc_srv_" + Guid.NewGuid().ToString("N") + ".db");
        var server = new SqliteConnectionFactory(serverPath);
        try
        {
            new MigrationRunner(server).Run();
            using (var conn = server.Create())
            using (var seed = conn.CreateCommand())
            {
                seed.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);";
                seed.ExecuteNonQuery();
            }
            var snapshot = new DepoWise.Infrastructure.Sync.BusinessSyncService(_factory, _clock).BuildSnapshot("A");
            using (var doc = System.Text.Json.JsonDocument.Parse(snapshot))
                new DepoWise.Infrastructure.Sync.BusinessSyncService(server, _clock).Apply("A", doc.RootElement);

            // Sunucu-otoriteli yeniden hesaplama: bakiye DEFTERDEN kurulur.
            new StockService(server, _clock).RecomputeBalances("A");

            var srvStock = new StockService(server, _clock);
            Assert.Equal(7m, srvStock.GetBalanceAt(_admin, m, _depoA));
            Assert.Equal(7m, srvStock.GetBalanceAt(_admin, m, _depoB));
            Assert.Equal(14m, srvStock.GetBalance(_admin, m));
            Assert.Equal(_stock.GetBalance(_admin, m), srvStock.GetBalance(_admin, m));   // iki taraf KOPMAZ
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(serverPath); } catch { }
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
