using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-06 (FAZ C, 2026-08-11) — STOK RAPORLARINDA LOKASYON BOYUTU.
///
/// Kapsam: <b>Stok Durumu</b> (firma toplamı ↔ depo kırılımı) ve <b>Stok Sayım</b> (sayılan depo).
/// Rapor katmanı Web ve masaüstünde ORTAKTIR (tek <see cref="ReportService"/>) → buradaki her sonuç
/// iki platformda da aynıdır; export de aynı metodu çağırdığı için filtreyi otomatik alır.
///
/// ⚠️ İKİ KAVRAM AYRIDIR: <b>Tüm Şubeler</b> = firmanın tüm depolarının toplamı (Atanmamış DAHİL) ·
/// <b>Atanmamış</b> = yalnız <c>location_id=""</c>, geçmişte deposu girilmemiş stok (gerçek depo değil).
/// </summary>
public class StockReportLocationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _depoA, _depoB, _mat, _mat2;

    public StockReportLocationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rpt_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        SeedCompany(_factory, "A");

        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_admin, new NewBranch("Depo A"));
        _depoB = branches.Create(_admin, new NewBranch("Depo B"));
        _mat = _materials.Create(_admin, new NewMaterial("RPT-1", "Rapor malzemesi 1"));
        _mat2 = _materials.Create(_admin, new NewMaterial("RPT-2", "Rapor malzemesi 2"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private ReportRequest Req(params string[] locations)
        => new(Executed: true, LocationIds: locations.Length == 0 ? null : locations);

    /// <summary>Depo A: 10 · Depo B: 5 · Atanmamış: 3 → firma toplamı 18.</summary>
    private void SeedThreeLocations()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 3m) }, Op());   // lokasyonsuz (geçmiş)
    }

    private static decimal QtyOf(TableModel t, string code, int qtyCol)
        => t.Rows.Where(r => (string?)r[0] == code).Sum(r => Money.Parse((string?)r[qtyCol]));

    // ── 1-3. Firma toplamı · tek lokasyon · çok lokasyon ──────────────────────────────────

    /// <summary>1 — FİRMA GENELİ (filtre boş): malzeme başına TEK satır, tüm depoların toplamı.
    /// Bu, STK-06 öncesi davranışın BİREBİR aynısıdır (regresyon yok).</summary>
    [Fact]
    public void Firma_Geneli_Rapor_Malzeme_Basina_Tek_Satir_ve_Toplam()
    {
        SeedThreeLocations();

        var t = _reports.StockStatus(_admin, Req());

        Assert.Equal(new[] { "Kod", "Malzeme", "Stok", "Min Stok" }, t.Headers);
        var rows = t.Rows.Where(r => (string?)r[0] == "RPT-1").ToList();
        Assert.Single(rows);                                   // satır ÇOĞALMADI
        Assert.Equal(18m, Money.Parse((string?)rows[0][2]));   // 10 + 5 + 3 (Atanmamış DAHİL)
        Assert.Equal(2, t.Rows.Count);                         // 2 malzeme → 2 satır
    }

    /// <summary>2 — TEK LOKASYON: yalnız o deponun miktarı + "Depo / Şantiye" kolonu.</summary>
    [Fact]
    public void Tek_Lokasyon_Raporu_Yalniz_O_Deponun_Miktarini_Doner()
    {
        SeedThreeLocations();

        var t = _reports.StockStatus(_admin, Req(_depoA));

        Assert.Contains("Depo / Şantiye", t.Headers);
        var row = Assert.Single(t.Rows.Where(r => (string?)r[0] == "RPT-1").ToList());
        Assert.Equal("Depo A", (string?)row[2]);
        Assert.Equal(10m, Money.Parse((string?)row[3]));
    }

    /// <summary>3 + 6 — ÇOK LOKASYON: her depo AYRI satır; seçili depoların toplamı doğru ve
    /// LOKASYON TOPLAMI = FİRMA TOPLAMI invariantı (üç lokasyon birlikte seçilince) sağlanır.</summary>
    [Fact]
    public void Lokasyon_Toplami_Firma_Toplamina_Esit()
    {
        SeedThreeLocations();

        var hepsi = _reports.StockStatus(_admin, Req(_depoA, _depoB, ""));
        var toplam = QtyOf(hepsi, "RPT-1", 3);

        Assert.Equal(3, hepsi.Rows.Count(r => (string?)r[0] == "RPT-1"));   // üç ayrı satır
        Assert.Equal(18m, toplam);
        Assert.Equal(QtyOf(_reports.StockStatus(_admin, Req()), "RPT-1", 2), toplam);   // = firma toplamı
        Assert.Equal(_stock.GetBalance(_admin, _mat), toplam);                          // = servis toplamı
    }

    // ── 4-5. ATANMAMIŞ ────────────────────────────────────────────────────────────────────

    /// <summary>4 + 5 — ATANMAMIŞ doğru gösteriliyor ama GERÇEK DEPO GİBİ DEĞİL:
    /// adı açıklayıcıdır ("Atanmamış (depo girilmemiş)"), gerçek şube adı değildir.</summary>
    [Fact]
    public void ATANMAMIS_Gosteriliyor_Ama_Gercek_Depo_Gibi_Degil()
    {
        SeedThreeLocations();

        var t = _reports.StockStatus(_admin, Req(""));

        var row = Assert.Single(t.Rows.Where(r => (string?)r[0] == "RPT-1").ToList());
        Assert.Equal(3m, Money.Parse((string?)row[3]));
        Assert.Contains("Atanmamış", (string?)row[2]);
        Assert.NotEqual("Depo A", (string?)row[2]);
        Assert.NotEqual("Depo B", (string?)row[2]);
    }

    /// <summary>7 — BOŞ SONUÇ: hiç stoğu olmayan depo seçilince rapor boş döner (satır uydurulmaz).</summary>
    [Fact]
    public void Stogu_Olmayan_Depo_Bos_Rapor_Doner()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);

        var t = _reports.StockStatus(_admin, Req(_depoB));
        Assert.Empty(t.Rows);
        Assert.Null(t.TotalRow);
    }

    /// <summary>8 — TOPLAM SATIRI: lokasyon modunda rapor altına toplam yazılır ve satırların
    /// C# decimal toplamıyla BİREBİR aynıdır (float yuvarlaması yok).</summary>
    [Fact]
    public void Toplam_Satiri_Satirlarin_Decimal_Toplamiyla_Ayni()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 0.1m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat2, 0.2m) }, Op(), branchId: _depoA);

        var t = _reports.StockStatus(_admin, Req(_depoA));
        Assert.NotNull(t.TotalRow);
        Assert.Equal(0.3m, Money.Parse((string?)t.TotalRow![3]));   // 0.1 + 0.2 = TAM 0.3
    }

    // ── 9. Açılış stoğu ───────────────────────────────────────────────────────────────────

    /// <summary>9 — AÇILIŞ STOĞU raporda doğru depoda görünür.</summary>
    [Fact]
    public void Acilis_Stogu_Raporda_Kendi_Deposunda_Gorunur()
    {
        _opening.RecordOpening(_admin, _mat, 25m, Op(), branchId: _depoB);

        var t = _reports.StockStatus(_admin, Req(_depoB));
        var row = Assert.Single(t.Rows.Where(r => (string?)r[0] == "RPT-1").ToList());
        Assert.Equal("Depo B", (string?)row[2]);
        Assert.Equal(25m, Money.Parse((string?)row[3]));
    }

    // ── 10-12. Stok Sayım raporu ──────────────────────────────────────────────────────────

    /// <summary>10 — SAYIM RAPORU artık SAYILAN DEPOYU gösteriyor ve "Sistem" sütunu firma toplamı
    /// DEĞİL, o deponun miktarıdır. Depo A=10, Depo B=5 iken A'da 12 sayılırsa: Sistem 10, Fark +2.</summary>
    [Fact]
    public void Sayim_Raporu_Sayilan_Depoyu_ve_O_Deponun_Sistemini_Gosterir()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);
        _stock.Count(_admin, new[] { new CountLine(_mat, 12m) }, "yıl sonu", Op(), branchId: _depoA);

        var t = _reports.StockCount(_admin, new ReportRequest(Executed: true));

        Assert.Contains("Sayılan Depo", t.Headers);
        var row = Assert.Single(t.Rows);
        Assert.Equal("Depo A", (string?)row[1]);
        Assert.Equal(10d, (double)row[4]!);    // Sistem = SAYILAN DEPONUN miktarı (15 DEĞİL)
        Assert.Equal(12d, (double)row[5]!);    // Sayılan
        Assert.Equal(2d, (double)row[6]!);     // Fark = +2
        Assert.Equal("Fazla", (string?)row[7]);
    }

    /// <summary>11 — FARKLI DEPOLARDAKİ SAYIMLAR BİRBİRİNE KARIŞMAZ: iki sayım ayrı satırlar,
    /// her biri kendi deposunun sistemiyle karşılaştırılmış.</summary>
    [Fact]
    public void Farkli_Depolarin_Sayimlari_Birbirine_Karismaz()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);
        _stock.Count(_admin, new[] { new CountLine(_mat, 12m) }, "A sayımı", Op(), branchId: _depoA);
        _stock.Count(_admin, new[] { new CountLine(_mat, 4m) }, "B sayımı", Op(), branchId: _depoB);

        var t = _reports.StockCount(_admin, new ReportRequest(Executed: true));
        Assert.Equal(2, t.Rows.Count);

        var a = Assert.Single(t.Rows.Where(r => (string?)r[1] == "Depo A").ToList());
        var b = Assert.Single(t.Rows.Where(r => (string?)r[1] == "Depo B").ToList());
        Assert.Equal(10d, (double)a[4]!); Assert.Equal(2d, (double)a[6]!);    // 12 − 10
        Assert.Equal(5d, (double)b[4]!); Assert.Equal(-1d, (double)b[6]!);    // 4 − 5
    }

    /// <summary>12 — SAYIM RAPORU LOKASYON FİLTRESİ: yalnız seçilen depodaki sayımlar listelenir.</summary>
    [Fact]
    public void Sayim_Raporu_Lokasyon_Filtresi_Calisir()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);
        _stock.Count(_admin, new[] { new CountLine(_mat, 12m) }, "A", Op(), branchId: _depoA);
        _stock.Count(_admin, new[] { new CountLine(_mat, 4m) }, "B", Op(), branchId: _depoB);

        var t = _reports.StockCount(_admin, new ReportRequest(Executed: true, LocationIds: new[] { _depoA }));
        var row = Assert.Single(t.Rows);
        Assert.Equal("Depo A", (string?)row[1]);
    }

    // ── 13. Yetki / kapsam ────────────────────────────────────────────────────────────────

    /// <summary>13 — BAŞKA FİRMANIN stoğu raporda GÖRÜNMEZ (lokasyon kimliği verilse bile).
    /// Sorgu firmaya kilitlidir; yabancı lokasyon seçimi boş sonuç verir, veri sızmaz.</summary>
    [Fact]
    public void Baska_Firmanin_Deposu_Raporda_Gorunmez()
    {
        SeedCompany(_factory, "B");
        var users = new UserService(_factory, _clock);
        var bUid = users.EnsureInitialAdmin("B", "b_admin", "admin123", RoleKeys.CompanyAdmin);
        var bOturum = new SessionContext(bUid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var bDepo = new BranchService(_factory, _clock).Create(bOturum, new NewBranch("B Deposu"));
        var bMat = _materials.Create(bOturum, new NewMaterial("B-1", "B malzemesi"));
        new StockService(_factory, _clock).ReceiveIn(bOturum, new[] { new StockLine(bMat, 99m) }, Op(), branchId: bDepo);

        SeedThreeLocations();

        // A firmasının kullanıcısı B'nin deposunu seçse bile hiçbir şey göremez.
        Assert.Empty(_reports.StockStatus(_admin, Req(bDepo)).Rows);
        // Firma geneli raporda da B'nin malzemesi YOK.
        Assert.DoesNotContain(_reports.StockStatus(_admin, Req()).Rows, r => (string?)r[0] == "B-1");
    }

    // ── 14. Çevrimdışı → senkron → aynı rapor ─────────────────────────────────────────────

    /// <summary>14 — ÇEVRİMDIŞI MASAÜSTÜ ↔ SUNUCU PARİTESİ: çevrimdışı yazılan hareketler senkron
    /// sonrası sunucuda AYNI raporu üretir (rapor katmanı ortak, veri defterden kuruluyor).</summary>
    [Fact]
    public void Cevrimdisi_Rapor_Senkron_Sonrasi_Sunucu_Raporuyla_Ayni()
    {
        SeedThreeLocations();
        _stock.Transfer(_admin, _mat, 2m, _depoA, _depoB, Op());

        var yerel = _reports.StockStatus(_admin, Req(_depoA, _depoB, ""));

        var srvPath = Path.Combine(Path.GetTempPath(), "dw_rpt_srv_" + Guid.NewGuid().ToString("N") + ".db");
        var server = new SqliteConnectionFactory(srvPath);
        try
        {
            new MigrationRunner(server).Run();
            SeedCompany(server, "A");
            using (var doc = JsonDocument.Parse(new BusinessSyncService(_factory, _clock).BuildSnapshot("A")))
                new BusinessSyncService(server, _clock).Apply("A", doc.RootElement);
            new StockService(server, _clock).RecomputeBalances("A");   // sunucu-otoriteli: defterden

            var sunucu = new ReportService(server).StockStatus(_admin, Req(_depoA, _depoB, ""));

            Assert.Equal(yerel.Rows.Count, sunucu.Rows.Count);
            Assert.Equal(QtyOf(yerel, "RPT-1", 3), QtyOf(sunucu, "RPT-1", 3));
            Assert.Equal(Money.Parse((string?)yerel.TotalRow![3]), Money.Parse((string?)sunucu.TotalRow![3]));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(srvPath); } catch { }
        }
    }

    // ── 15-16. Katalog sözleşmesi + regresyon ─────────────────────────────────────────────

    /// <summary>15 — KATALOG: lokasyon filtresi YALNIZ stok raporlarında açık. Başka raporlara
    /// körlemesine eklenmedi; `Branch` (kaydı işleyen şube) bayrağı ile karıştırılmadı.
    ///
    /// ⚠️ BEKLENEN LİSTE GÜNCELLENDİ (STK-10a, 2026-08-11): <c>stock-movements</c> eklendi.
    /// Bu bir GEVŞETME DEĞİLDİR — liste hâlâ TAM EŞLEŞME ile sınanıyor ve kalan 10 raporun lokasyon
    /// filtresi olmadığını kanıtlamaya devam ediyor. Yeni rapor lokasyon filtresini <b>bilinçli</b>
    /// kullanır: hareket defteri depo bazlıdır (STK-06 K-2 kararının aynı gerekçesi).</summary>
    [Fact]
    public void Lokasyon_Filtresi_Yalniz_Stok_Raporlarinda_Acik()
    {
        var lokasyonlu = ReportCatalog.All.Where(d => d.UsesLocation).Select(d => d.Key).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "stock", "stock-count", "stock-movements" }, lokasyonlu);

        // Branch ve Location AYRI bayraklardır: stok raporlarında Branch AÇILMADI.
        Assert.False(ReportCatalog.ByKey("stock")!.UsesBranch);
        Assert.False(ReportCatalog.ByKey("stock-count")!.UsesBranch);
        Assert.False(ReportCatalog.ByKey("stock-movements")!.UsesBranch);

        // STK-10b-1: hareket türü filtresi YALNIZ hareket raporunda açıldı (körlemesine yayılmadı).
        var turluler = ReportCatalog.All.Where(d => d.UsesMovementType).Select(d => d.Key).ToList();
        Assert.Equal(new[] { "stock-movements" }, turluler);

        // STK-10b-2: arama filtresi de YALNIZ hareket raporunda açıldı.
        var aramalilar = ReportCatalog.All.Where(d => d.UsesSearch).Select(d => d.Key).ToList();
        Assert.Equal(new[] { "stock-movements" }, aramalilar);

        // STK-10b-3: malzeme filtresi de YALNIZ hareket raporunda açıldı (körlemesine yayılmadı).
        var malzemeliler = ReportCatalog.All.Where(d => d.UsesMaterial).Select(d => d.Key).ToList();
        Assert.Equal(new[] { "stock-movements" }, malzemeliler);

        // SIRADAKİ bayrak (8192) HENÜZ AÇILMADI — kapsam sızmasının nöbetçisi (gevşetilmedi, kaydırıldı).
        Assert.All(ReportCatalog.All, d => Assert.False(d.Filters.HasFlag((ReportFilters)8192)));
    }

    /// <summary>16 — REGRESYON: lokasyon boyutu diğer stok kullanan raporları bozmadı.
    /// Malzeme yönetici raporları hâlâ FİRMA TOPLAMI veriyor ve satır çoğaltmıyor.</summary>
    [Fact]
    public void Malzeme_Yonetici_Raporlari_Bozulmadi()
    {
        SeedThreeLocations();

        var nonTemplate = _reports.MaterialsNonTemplate(_admin, new ReportRequest(Executed: true));
        var rows = nonTemplate.Rows.Where(r => (string?)r[0] == "RPT-1").ToList();
        Assert.Single(rows);                                    // satır ÇOĞALMADI
        Assert.Equal(18m, Money.Parse((string?)rows[0][3]));    // firma toplamı (Atanmamış dahil)
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
