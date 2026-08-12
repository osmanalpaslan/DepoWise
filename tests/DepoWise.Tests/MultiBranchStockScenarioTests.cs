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
/// ÇOK ŞUBELİ STOK — UÇTAN UCA SENARYO PAKETİ (kullanıcı isteği 2026-08-12).
///
/// <b>AMAÇ:</b> gerçek veriye geçmeden önce "aynı malzemenin AYNI ANDA birden çok şubede stoğu olabilir"
/// modelinin uçtan uca doğru çalıştığını kanıtlamak:
/// <code>
/// MALZEME ├─ ANKARA GENEL MERKEZ: 10 ├─ DÜZCE: 5 ├─ KARAMAN: 0 ├─ NEVŞEHİR: 2 └─ TEST ŞANTİYE: 7
/// </code>
///
/// <b>MEVCUT TESTLERDEN FARKI:</b> <see cref="StockLocationTests"/> ayrışmayı, <see cref="StockDistributeTests"/>
/// dağıtım kapısını, <see cref="StockConcurrencyTests"/> CAS'i tek tek doğrular. Bu dosya bunları GERÇEK
/// KULLANIM DİZİSİ hâlinde birleştirir ve her adımdan sonra <b>MUHASEBE EŞİTLİĞİNİ</b> zorunlu kılar:
/// <code>firma toplamı = ATANMAMIŞ + ANKARA + DÜZCE + KARAMAN + NEVŞEHİR + TEST ŞANTİYE</code>
/// Ayrıca eşitlik yalnız <c>stock_balances</c> içinde değil, <b>HAREKET DEFTERİNE</b> karşı da doğrulanır
/// (bakiye türetilmiş veridir; defterden kopması sessiz veri bozulmasıdır).
///
/// ⚠️ Bu testler "geçsin diye" gevşetilmemelidir. Buradaki bir kırılma, kullanıcıya yanlış şubede
/// yanlış stok göstermek demektir.
///
/// İZOLASYON: her test kendi geçici SQLite dosyasını kurar. PRODUCTION'A BAĞLANMAZ.
/// </summary>
public class MultiBranchStockScenarioTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    // Kullanıcının gerçek şube adları (canlıdaki 5 hedefle birebir).
    private readonly string _ankara, _duzce, _karaman, _nevsehir, _santiye;
    private const string Atanmamis = StockBalanceWriter.Unassigned;   // ""

    public MultiBranchStockScenarioTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mbs_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        // "Tüm Şubeler" oturumu: her çağrıda şube AÇIKÇA verilir (sessiz varsayım yok).
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA GENEL MERKEZ"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
        _karaman = branches.Create(_admin, new NewBranch("KARAMAN"));
        _nevsehir = branches.Create(_admin, new NewBranch("NEVŞEHİR"));
        _santiye = branches.Create(_admin, new NewBranch("TEST ŞANTİYE"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // ORTAK DOĞRULAYICILAR — her senaryo bunları kullanır (tek yerde tanımlı)
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Veritabanındaki HAM bakiye satırları (lokasyon → miktar). Servisin hesabı değil,
    /// diske GERÇEKTEN yazılan değer okunur.</summary>
    private Dictionary<string, decimal> Rows(string materialId)
    {
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT location_id, quantity FROM stock_balances WHERE company_id='A' AND material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = Money.Parse(r.GetString(1));
        return map;
    }

    private decimal At(string materialId, string loc) => Rows(materialId).TryGetValue(loc, out var q) ? q : 0m;

    /// <summary>Hareket defterinden (malzeme × lokasyon) bakiyesi. Bakiye tablosundan BAĞIMSIZ ikinci kaynak.</summary>
    private Dictionary<string, decimal> LedgerRows(string materialId)
    {
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(branch_id,''), direction, quantity
FROM stock_movements WHERE company_id='A' AND material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var loc = r.GetString(0);
            map.TryGetValue(loc, out var cur);
            map[loc] = cur + r.GetInt64(1) * Money.Parse(r.GetString(2));
        }
        return map;
    }

    /// <summary>
    /// ⭐ MUHASEBE EŞİTLİĞİ (kullanıcı şartı 10) — üç kaynak birden tutmalı:
    ///   1) firma toplamı = ATANMAMIŞ + 5 şube (bakiye tablosu)
    ///   2) servis toplamı (<see cref="StockService.GetBalance"/>) = aynı sayı
    ///   3) her lokasyonun bakiyesi = o lokasyonun HAREKET DEFTERİ toplamı
    /// Beklenen kırılım verildiyse ayrıca satır satır karşılaştırılır.
    /// </summary>
    private void Muhasebe(string materialId, decimal beklenenToplam, params (string Loc, decimal Qty)[] beklenen)
    {
        var rows = Rows(materialId);
        var toplam = rows.Values.Sum();

        // 1) Kova toplamı = beklenen firma toplamı
        Assert.Equal(beklenenToplam, toplam);

        // 2) Servisin firma-geneli toplamı aynı olmalı (ekran/rapor bu yolu kullanır)
        Assert.Equal(beklenenToplam, _stock.GetBalance(_admin, materialId));

        // 3) Bakiye ↔ hareket defteri kopmamalı (bakiye türetilmiş veridir)
        var ledger = LedgerRows(materialId);
        foreach (var loc in rows.Keys.Union(ledger.Keys))
        {
            ledger.TryGetValue(loc, out var defter);
            rows.TryGetValue(loc, out var bakiye);
            Assert.Equal(defter, bakiye);
        }

        // 4) Beklenen kırılım (verildiyse)
        foreach (var (loc, qty) in beklenen)
        {
            rows.TryGetValue(loc, out var actual);
            Assert.Equal(qty, actual);
        }

        // 5) Kırılımın kendisi de toplamı vermeli — beş şube + atanmamış dışında kova OLMAMALI
        var bilinen = new[] { Atanmamis, _ankara, _duzce, _karaman, _nevsehir, _santiye };
        Assert.All(rows.Keys, k => Assert.Contains(k, bilinen));
        Assert.Equal(beklenenToplam, bilinen.Sum(b => rows.TryGetValue(b, out var q) ? q : 0m));
    }

    private long Say(string table)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE company_id='A';";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 1 — BAŞLANGIÇ DAĞITIMI: 24 atanmamış → 3 şubeye bölünür
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — 24 adet ATANMAMIŞ stok üç şubeye dağıtılır: ayrı bakiye satırları oluşur,
    /// FİRMA TOPLAMI DEĞİŞMEZ (dağıtım stok yaratmaz/yok etmez).</summary>
    [Fact]
    public void S01_Baslangic_Dagitimi_Uc_Subeye_Bolunur_Firma_Toplami_Degismez()
    {
        var m = Mat("MB-01");
        _opening.RecordOpening(_admin, m, 24m, Op());              // branchId YOK → ATANMAMIŞ
        Muhasebe(m, 24m, (Atanmamis, 24m));

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 8m) }, _duzce, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 6m) }, _karaman, Op());

        // Aynı malzeme → ÜÇ ayrı bakiye satırı + tükenen ATANMAMIŞ kovası (satır 0 olarak kalır)
        Muhasebe(m, 24m, (_ankara, 10m), (_duzce, 8m), (_karaman, 6m), (Atanmamis, 0m));
        Assert.Equal(4, Rows(m).Count);   // 3 şube + sıfırlanmış atanmamış satırı
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 2 — KISMİ DAĞITIM
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>2 — Atanmamış stoğun yalnız bir bölümü aktarılır; KALAN ATANMAMIŞ'ta durur.</summary>
    [Fact]
    public void S02_Kismi_Dagitim_Kalan_Atanmamista_Kalir()
    {
        var m = Mat("MB-02");
        _opening.RecordOpening(_admin, m, 24m, Op());

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 9m) }, _nevsehir, Op());

        Muhasebe(m, 24m, (_nevsehir, 9m), (Atanmamis, 15m));
        // Dağıtım ekranı kalanı göstermeli (0 olsaydı listeden düşerdi)
        var kalan = _stock.ListUnassigned(_admin).Single(x => x.MaterialId == m);
        Assert.Equal(15m, kalan.Quantity);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 3 — AYNI MALZEME, DÖRT LOKASYON
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>3 — Ankara 10 / Düzce 5 / Karaman 4 / Nevşehir 2 — dördü AYNI ANDA yaşar.</summary>
    [Fact]
    public void S03_Ayni_Malzeme_Dort_Lokasyonda_Ayni_Anda_Durur()
    {
        var m = Mat("MB-03");
        _opening.RecordOpening(_admin, m, 21m, Op());

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 4m) }, _karaman, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 2m) }, _nevsehir, Op());

        Muhasebe(m, 21m, (_ankara, 10m), (_duzce, 5m), (_karaman, 4m), (_nevsehir, 2m), (Atanmamis, 0m));

        // Ekran/API sözleşmesi: kırılım ADLARIYLA gelir ve toplamı korur
        var kirilim = _stock.GetLocationBalances(_admin, m);
        Assert.Equal(21m, kirilim.Sum(x => x.Quantity));
        Assert.Equal(10m, kirilim.Single(x => x.LocationName == "ANKARA GENEL MERKEZ").Quantity);
        Assert.Equal(2m, kirilim.Single(x => x.LocationName == "NEVŞEHİR").Quantity);
        // ATANMAMIŞ daima EN SONDA gösterilir (kullanıcı önce gerçek depolarını görsün)
        Assert.Equal("", kirilim[^1].LocationId);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 4 — ŞUBE STOĞUNUN TÜKENMESİ (tam 0)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>4 — Düzce'deki 5'in tamamı çıkılır: Düzce TAM 0 olur, DİĞER ŞUBELER HİÇ ETKİLENMEZ,
    /// firma toplamı tam 5 azalır. Sıfır satır SİLİNMEZ (kayıt geçmişi korunur).</summary>
    [Fact]
    public void S04_Sube_Stogu_Tam_Sifira_Duser_Digerleri_Etkilenmez()
    {
        var m = Mat("MB-04");
        _opening.RecordOpening(_admin, m, 22m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 7m) }, _santiye, Op());
        Muhasebe(m, 22m, (_ankara, 10m), (_duzce, 5m), (_santiye, 7m));

        _stock.IssueOut(_admin, new[] { new StockLine(m, 5m) }, Op(), branchId: _duzce);

        Muhasebe(m, 17m, (_ankara, 10m), (_duzce, 0m), (_santiye, 7m));

        // Sıfır bakiyeli satır DURUR (silinmez) — geçmiş ve raporlanabilirlik için
        Assert.True(Rows(m).ContainsKey(_duzce));
        // ...ve kırılımda 0 olarak GÖRÜNÜR (gizlenmez → kullanıcı "burada yok" bilgisini görür)
        Assert.Equal(0m, _stock.GetLocationBalances(_admin, m).Single(x => x.LocationName == "DÜZCE").Quantity);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 5 — NEGATİF STOK KORUMASI (kısmi yazma OLMAMALI)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>5 — Düzce 0 iken 1 adet çıkış REDDEDİLİR; belge/hareket/bakiye HİÇBİRİ yazılmaz.</summary>
    [Fact]
    public void S05_Sifir_Stoklu_Subeden_Cikis_Reddedilir_Kismi_Yazma_Kalmaz()
    {
        var m = Mat("MB-05");
        _opening.RecordOpening(_admin, m, 15m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());
        _stock.IssueOut(_admin, new[] { new StockLine(m, 5m) }, Op(), branchId: _duzce);   // Düzce → 0

        var belgeOnce = Say("stock_documents");
        var hareketOnce = Say("stock_movements");

        var ex = Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 1m) }, Op(), branchId: _duzce));
        Assert.Contains("Bu şubede yeterli stok yok", ex.Message);

        // Rollback kanıtı: hiçbir satır eklenmedi, bakiyeler değişmedi
        Assert.Equal(belgeOnce, Say("stock_documents"));
        Assert.Equal(hareketOnce, Say("stock_movements"));
        Muhasebe(m, 10m, (_ankara, 10m), (_duzce, 0m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 6 & 7 — ŞUBELER ARASI (TAM / KISMİ) TRANSFER
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>6 — Ankara 10 / Düzce 0 iken Ankara→Düzce 3: Ankara 7, Düzce 3, FİRMA TOPLAMI SABİT.</summary>
    [Fact]
    public void S06_Subeler_Arasi_Transfer_Toplami_Degistirmez()
    {
        var m = Mat("MB-06");
        _opening.RecordOpening(_admin, m, 10m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());

        _stock.Transfer(_admin, m, 3m, _ankara, _duzce, Op());

        Muhasebe(m, 10m, (_ankara, 7m), (_duzce, 3m));   // toplam AYNI
    }

    /// <summary>7 — Kısmi transfer: Ankara'daki 10'un yalnız 4'ü gider, 6 KALIR.</summary>
    [Fact]
    public void S07_Kismi_Sube_Transferi_Kalani_Kaynakta_Birakir()
    {
        var m = Mat("MB-07");
        _opening.RecordOpening(_admin, m, 10m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());

        _stock.Transfer(_admin, m, 4m, _ankara, _nevsehir, Op());

        Muhasebe(m, 10m, (_ankara, 6m), (_nevsehir, 4m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 8 — ÇOK LOKASYONLU MALZEMEDE TEK ŞUBEDEN ÇIKIŞ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>8 — Ankara 6 / Düzce 4 / Karaman 3 iken Düzce'den 2 çıkış → Düzce 2;
    /// ANKARA VE KARAMAN KESİNLİKLE DEĞİŞMEZ (çapraz sızıntı yok).</summary>
    [Fact]
    public void S08_Tek_Subeden_Cikis_Diger_Subeleri_Etkilemez()
    {
        var m = Mat("MB-08");
        _opening.RecordOpening(_admin, m, 13m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 6m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 4m) }, _duzce, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 3m) }, _karaman, Op());

        _stock.IssueOut(_admin, new[] { new StockLine(m, 2m) }, Op(), branchId: _duzce);

        Muhasebe(m, 11m, (_ankara, 6m), (_duzce, 2m), (_karaman, 3m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 9 — ŞUBE BAZLI RAPORLAMA
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>9 — Rapor iki modda da doğru: depo seçilmezse malzeme başına TEK satır (firma toplamı),
    /// depo seçilirse yalnız o deponun kalemleri. Satır ÇOĞALMAZ, toplam KOPMAZ.</summary>
    [Fact]
    public void S09_Sube_Bazli_Raporlama_Satir_Cogaltmaz_Toplam_Kopmaz()
    {
        var m = Mat("MB-09");
        _opening.RecordOpening(_admin, m, 24m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 2m) }, _nevsehir, Op());
        // 7 ATANMAMIŞ'ta kalır

        // (a) FİRMA GENELİ: dört lokasyona rağmen TEK satır ve toplam 24
        var genel = _reports.StockStatus(_admin, new ReportRequest(true));
        var satir = genel.Rows.Where(r => (string)r[0]! == "MB-09").ToList();
        Assert.Single(satir);
        Assert.Equal(24m, Money.Parse((string)satir[0][2]!));

        // (b) TEK DEPO: yalnız o deponun miktarı
        var duzceRap = _reports.StockStatus(_admin, new ReportRequest(true, LocationIds: new[] { _duzce }));
        Assert.Equal(5m, Money.Parse((string)duzceRap.Rows.Single(r => (string)r[0]! == "MB-09")[3]!));

        // (c) ÇOK DEPO: malzeme depo sayısı kadar satır + toplamları doğru
        var coklu = _reports.StockStatus(_admin,
            new ReportRequest(true, LocationIds: new[] { _ankara, _duzce, _nevsehir }));
        var kalemler = coklu.Rows.Where(r => (string)r[0]! == "MB-09").ToList();
        Assert.Equal(3, kalemler.Count);
        Assert.Equal(17m, kalemler.Sum(r => Money.Parse((string)r[3]!)));

        // (d) ATANMAMIŞ ("") geçerli bir seçimdir ve açıklayıcı etiketle gösterilir
        var atan = _reports.StockStatus(_admin, new ReportRequest(true, LocationIds: new[] { "" }));
        var aSatir = atan.Rows.Single(r => (string)r[0]! == "MB-09");
        Assert.Equal(7m, Money.Parse((string)aSatir[3]!));
        Assert.Equal("Atanmamış (depo girilmemiş)", (string)aSatir[2]!);

        // (e) TÜM lokasyonlar seçilirse rapor toplamı = firma toplamı (kopma yok)
        var hepsi = _reports.StockStatus(_admin,
            new ReportRequest(true, LocationIds: new[] { _ankara, _duzce, _karaman, _nevsehir, _santiye, "" }));
        Assert.Equal(24m, hepsi.Rows.Where(r => (string)r[0]! == "MB-09").Sum(r => Money.Parse((string)r[3]!)));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 10 — MUHASEBE EŞİTLİĞİ: UZUN İŞLEM DİZİSİ BOYUNCA HER ADIMDA
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>10 — Giriş / dağıtım / transfer / çıkış / sayım karışık bir dizide çalıştırılır ve
    /// HER ADIMDAN SONRA muhasebe eşitliği + defter tutarlılığı yeniden doğrulanır.</summary>
    [Fact]
    public void S10_Uzun_Islem_Dizisinde_Muhasebe_Esitligi_Her_Adimda_Korunur()
    {
        var m = Mat("MB-10");

        _opening.RecordOpening(_admin, m, 24m, Op());
        Muhasebe(m, 24m, (Atanmamis, 24m));

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        Muhasebe(m, 24m, (_ankara, 10m), (Atanmamis, 14m));

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());
        Muhasebe(m, 24m, (_ankara, 10m), (_duzce, 5m), (Atanmamis, 9m));

        _stock.Transfer(_admin, m, 3m, _ankara, _karaman, Op());          // toplam SABİT
        Muhasebe(m, 24m, (_ankara, 7m), (_duzce, 5m), (_karaman, 3m), (Atanmamis, 9m));

        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 6m) }, Op(), branchId: _nevsehir);   // +6
        Muhasebe(m, 30m, (_nevsehir, 6m));

        _stock.IssueOut(_admin, new[] { new StockLine(m, 4m) }, Op(), branchId: _duzce);       // −4
        Muhasebe(m, 26m, (_duzce, 1m));

        // Sayım: Karaman'da 3 yerine 5 sayıldı → +2 fark hareketi (yalnız O lokasyona)
        _stock.Count(_admin, new[] { new CountLine(m, 5m) }, "Yıl sonu sayımı", Op(), branchId: _karaman);
        Muhasebe(m, 28m, (_ankara, 7m), (_duzce, 1m), (_karaman, 5m), (_nevsehir, 6m), (Atanmamis, 9m));

        // Son kontrol: defterden yeniden hesaplama AYNI kırılımı üretmeli (bakiye ↔ defter kopmadı)
        var oncesi = Rows(m);
        _stock.RecomputeBalances("A");
        Assert.Equal(oncesi, Rows(m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 11 — TRANSACTION / ATOMİKLİK
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>11 — Çok satırlı bir dağıtımda BİR satır kasıtlı olarak geçersiz kılınır (yetersiz stok):
    /// işlemin TAMAMI geri alınır — geçerli satırlar bile yazılmaz.</summary>
    [Fact]
    public void S11_Cok_Satirli_Islemde_Tek_Gecersiz_Satir_Tumunu_Geri_Alir()
    {
        var a = Mat("MB-11A"); var b = Mat("MB-11B"); var c = Mat("MB-11C");
        _opening.RecordOpening(_admin, a, 10m, Op());
        _opening.RecordOpening(_admin, b, 10m, Op());
        _opening.RecordOpening(_admin, c, 1m, Op());       // ← yetersiz kalacak satır

        var belgeOnce = Say("stock_documents");
        var hareketOnce = Say("stock_movements");

        Assert.Throws<NegativeStockException>(() => _stock.DistributeUnassigned(_admin, new[]
        {
            new StockLine(a, 5m),      // geçerli
            new StockLine(b, 5m),      // geçerli
            new StockLine(c, 5m),      // GEÇERSİZ → tamamı geri alınmalı
        }, _ankara, Op()));

        Assert.Equal(belgeOnce, Say("stock_documents"));
        Assert.Equal(hareketOnce, Say("stock_movements"));
        Muhasebe(a, 10m, (Atanmamis, 10m), (_ankara, 0m));
        Muhasebe(b, 10m, (Atanmamis, 10m), (_ankara, 0m));
        Muhasebe(c, 1m, (Atanmamis, 1m));

        // ÇOK MALZEMELİ TRANSFERDE de aynı kural
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(a, 10m), new StockLine(b, 10m) }, _ankara, Op());
        Assert.Throws<NegativeStockException>(() => _stock.Transfer(_admin,
            new[] { new StockLine(a, 5m), new StockLine(b, 99m) }, _ankara, _duzce, Op()));
        Muhasebe(a, 10m, (_ankara, 10m), (_duzce, 0m));
        Muhasebe(b, 10m, (_ankara, 10m), (_duzce, 0m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 12 — IDEMPOTENCY
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>12 — Aynı <c>operation_id</c> ile ikinci çağrı YENİ belge/hareket ÜRETMEZ ve stoğu
    /// İKİNCİ KEZ DÜŞÜRMEZ; ilk belgeyi aynen döndürür (ağ tekrarı / çift tıklama koruması).</summary>
    [Fact]
    public void S12_Ayni_OperationId_Ikinci_Kez_Stok_Dusurmez()
    {
        var m = Mat("MB-12");
        _opening.RecordOpening(_admin, m, 20m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());

        var op = Op();
        var ilk = _stock.Transfer(_admin, m, 4m, _ankara, _duzce, op);
        var beklenen = Rows(m);
        var belgeOnce = Say("stock_documents");
        var hareketOnce = Say("stock_movements");

        var ikinci = _stock.Transfer(_admin, m, 4m, _ankara, _duzce, op);   // AYNI operation id

        Assert.Equal(ilk.DocumentId, ikinci.DocumentId);
        Assert.Equal(ilk.DocNo, ikinci.DocNo);
        Assert.Equal(belgeOnce, Say("stock_documents"));
        Assert.Equal(hareketOnce, Say("stock_movements"));
        Assert.Equal(beklenen, Rows(m));
        Muhasebe(m, 20m, (_ankara, 6m), (_duzce, 4m), (Atanmamis, 10m));

        // Dağıtım yolunda da aynı garanti
        var op2 = Op();
        var d1 = _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 3m) }, _karaman, op2);
        var d2 = _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 3m) }, _karaman, op2);
        Assert.Equal(d1.DocumentId, d2.DocumentId);
        Muhasebe(m, 20m, (_karaman, 3m), (Atanmamis, 7m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 13 — EŞZAMANLILIK (oversell / negatif düşme yasak)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>13 — Aynı şube stoğuna (10) EŞZAMANLI iki çıkış (7 + 7): en fazla BİRİ başarılı olur,
    /// bakiye ASLA negatife düşmez, oversell oluşmaz, defter tutarlı kalır.</summary>
    [Fact]
    public void S13_Eszamanli_Iki_Cikis_Oversell_Uretmez_Negatife_Dusmez()
    {
        var m = Mat("MB-13");
        _opening.RecordOpening(_admin, m, 10m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());

        int basarili = 0, reddedilen = 0;
        void Cikis()
        {
            try
            {
                _stock.IssueOut(_admin, new[] { new StockLine(m, 7m) }, Op(), branchId: _ankara);
                Interlocked.Increment(ref basarili);
            }
            // Yetersizlik (iş kuralı) VEYA yoğunluk (yarış hakkı bitti) → ikisi de "yazılmadı" demektir.
            catch (Exception e) when (e is NegativeStockException or StockBusyException)
            {
                Interlocked.Increment(ref reddedilen);
            }
        }
        Parallel.Invoke(Cikis, Cikis);

        Assert.Equal(1, basarili);        // ikisi birden ASLA geçemez
        Assert.Equal(1, reddedilen);
        Assert.Equal(3m, At(m, _ankara)); // 10 − 7 = 3 (asla −4 değil)
        Muhasebe(m, 3m, (_ankara, 3m), (Atanmamis, 0m));
    }

    /// <summary>13b — Eşzamanlı dağıtımlar AYNI atanmamış kovadan çift harcayamaz.</summary>
    [Fact]
    public void S13b_Eszamanli_Dagitim_Atanmamis_Kovayi_Cift_Harcamaz()
    {
        var m = Mat("MB-13B");
        _opening.RecordOpening(_admin, m, 10m, Op());

        int ok = 0;
        void Dagit(string hedef)
        {
            try
            {
                _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 7m) }, hedef, Op());
                Interlocked.Increment(ref ok);
            }
            catch (Exception e) when (e is NegativeStockException or StockBusyException) { }
        }
        Parallel.Invoke(() => Dagit(_ankara), () => Dagit(_duzce));

        Assert.Equal(1, ok);
        Assert.Equal(3m, At(m, Atanmamis));
        Muhasebe(m, 10m);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 14 — SIFIR STOK DAVRANIŞI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>14 — Sıfıra düşen kova: dağıtım listesinde GÖRÜNMEZ, kırılımda 0 olarak GÖRÜNÜR,
    /// o kovadan yeni çıkış/transfer YAPILAMAZ ama o kovaya GİRİŞ yapılabilir (yeniden dolar).</summary>
    [Fact]
    public void S14_Sifira_Dusen_Kova_Dogru_Davranir()
    {
        var m = Mat("MB-14");
        _opening.RecordOpening(_admin, m, 5m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());

        // (a) ATANMAMIŞ tükendi → dağıtım listesinde YOK (0 satır gösterilmez)
        Assert.DoesNotContain(_stock.ListUnassigned(_admin), x => x.MaterialId == m);

        // (b) Sıfır kovadan transfer YAPILAMAZ
        Assert.Throws<NegativeStockException>(() => _stock.Transfer(_admin, m, 1m, _ankara, _duzce, Op()));

        // (c) Sıfır kova kırılımda 0 olarak GÖRÜNÜR (gizlenmez)
        _stock.IssueOut(_admin, new[] { new StockLine(m, 5m) }, Op(), branchId: _duzce);
        Assert.Equal(0m, _stock.GetBalanceAt(_admin, m, _duzce));
        Assert.Contains(_stock.GetLocationBalances(_admin, m), x => x.LocationId == _duzce && x.Quantity == 0m);

        // (d) Sıfır kovaya GİRİŞ serbest → yeniden dolar
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 8m) }, Op(), branchId: _duzce);
        Muhasebe(m, 8m, (_duzce, 8m));

        // (e) Sayım listesi hiç stok görmemiş kovayı 0 gösterir (firma toplamını DEĞİL)
        var sayim = _stock.GetCountSheet(_admin, _karaman).Single(x => x.MaterialId == m);
        Assert.Equal(0m, sayim.Quantity);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 15 — ATANMAMIŞ KAYNAK YALNIZ DAĞITIM MEKANİZMASIYLA BOŞALIR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>15 — ATANMAMIŞ stok, normal transfer kapısından ÇIKARILAMAZ (kaynak boş olamaz) ve
    /// hedef olarak da SEÇİLEMEZ; yalnız <see cref="StockService.DistributeUnassigned"/> ile aktarılır.</summary>
    [Fact]
    public void S15_Atanmamis_Kaynak_Yalniz_Dagitim_Kapisindan_Bosalir()
    {
        var m = Mat("MB-15");
        _opening.RecordOpening(_admin, m, 12m, Op());

        // (a) Normal transferde kaynak BOŞ olamaz
        Assert.Throws<ArgumentException>(() => _stock.Transfer(_admin, m, 5m, "", _ankara, Op()));
        // (b) ATANMAMIŞ hedef olamaz
        Assert.Throws<ArgumentException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, "", Op()));
        // (c) ⭐ REGRESYON KİLİDİ (STK-MB bulgusu 2026-08-12): şubeye dağıtıldıktan sonra "Atanmamış"a
        //     GERİ DÖNÜŞ YOLU YOKTUR. Transfer'in hedefi boş bırakılarak stok sessizce belirsiz kovaya
        //     itilemez — bu kapı eksikti, eklendi. API ve masaüstü zaten hedefi zorunlu tutuyordu.
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _ankara, Op());
        var bosHedef = Assert.Throws<ArgumentException>(() => _stock.Transfer(_admin, m, 5m, _ankara, "", Op()));
        Assert.Contains("Atanmamış", bosHedef.Message);
        Assert.Throws<ArgumentException>(() => _stock.Transfer(_admin, m, 5m, _ankara, null!, Op()));

        Muhasebe(m, 12m, (_ankara, 5m), (Atanmamis, 7m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 16 — SİLİNMİŞ MALZEME (mevcut davranışın TESPİTİ — kod değiştirilmez)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>16 — SİLME KAPISI (MLZ-01): stok bakiyesi olan VEYA hiç hareket görmüş malzeme silinemez.
    /// Kapı, firma geneli toplamı kullanır → BAŞKA DEPODA malı olan malzeme "stoğu yok" sanılıp silinemez.
    /// Kırılımı sıfırdan farklı ama NETİ 0 olan malzeme de hareket geçmişi nedeniyle korunur.</summary>
    [Fact]
    public void S16_Stogu_Veya_Hareketi_Olan_Malzeme_Silinemez()
    {
        // (a) Çok lokasyonlu POZİTİF stok → silme ENGELLENİR ve gerekçede stok geçer
        var m1 = Mat("MB-16A");
        _opening.RecordOpening(_admin, m1, 9m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m1, 4m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m1, 5m) }, _duzce, Op());
        var ex = Assert.Throws<InvalidOperationException>(() => _materials.Delete(_admin, m1));
        Assert.Contains("stokta", ex.Message, StringComparison.OrdinalIgnoreCase);

        // (b) NET TOPLAM 0, ama kırılım sıfırdan FARKLI: Ankara +5, Nevşehir −5
        //     (devralınan negatif stok gerçekte olabiliyor — ADR-086). Bakiye kapısı burada susar
        //     ama HAREKET kapısı devreye girer → malzeme yine silinemez (doğru davranış).
        var m2 = Mat("MB-16B");
        _opening.RecordOpening(_admin, m2, 5m, Op(), branchId: _ankara);
        _opening.RecordOpening(_admin, m2, -5m, Op(), branchId: _nevsehir);
        Assert.Equal(0m, _stock.GetBalance(_admin, m2));
        Assert.Equal(5m, At(m2, _ankara));
        Assert.Equal(-5m, At(m2, _nevsehir));
        var ex2 = Assert.Throws<InvalidOperationException>(() => _materials.Delete(_admin, m2));
        Assert.Contains("stok hareketi", ex2.Message);

        // (c) Hiç kullanılmamış malzeme serbestçe silinir (kapı gereksiz yere kilitlemiyor)
        _materials.Delete(_admin, Mat("MB-16C"));
    }

    /// <summary>16b — DEVRALINAN VERİ: MLZ-01 kapısından ÖNCE silinmiş, ama bir depoda hâlâ bakiyesi olan
    /// malzeme (canlıdaki "TEST" kaydının durumu). Kapı bugün bunu üretemez; bu test var olan veriyle
    /// ne olduğunu KİLİTLER: <b>veri kaybolmaz</b> (bakiye satırı ve defter durur) ama malzeme
    /// listelerde/raporlarda GÖRÜNMEZ → o stok kullanıcı arayüzünden ERİŞİLEMEZ hâle gelir.</summary>
    [Fact]
    public void S16b_Devralinan_Silinmis_Malzemenin_Bakiyesi_Kaybolmaz_Ama_Erisilemez()
    {
        var m = Mat("MB-16D");
        _opening.RecordOpening(_admin, m, 2m, Op(), branchId: _ankara);

        // Eski veriyi taklit et: kapıyı ATLAYARAK doğrudan is_deleted=1 (migration öncesi kayıtlar böyle)
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE materials SET is_deleted=1 WHERE id=@m;";
            cmd.AddWithValue("@m", m);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        // VERİ DURUYOR: bakiye satırı ve hareket defteri bozulmadı
        Assert.Equal(2m, At(m, _ankara));
        Assert.Equal(2m, _stock.GetBalance(_admin, m));
        Assert.Equal(2m, LedgerRows(m)[_ankara]);

        // ERİŞİLEMİYOR: grid, dağıtım listesi ve depo raporu bu stoğu GÖSTERMEZ
        Assert.Empty(_materials.SearchGrid(_admin, new MaterialGridFilter(Code: "MB-16D"), 1, 50).Items);
        Assert.DoesNotContain(_stock.ListUnassigned(_admin), x => x.MaterialId == m);
        var rap = _reports.StockStatus(_admin, new ReportRequest(true, LocationIds: new[] { _ankara }));
        Assert.DoesNotContain(rap.Rows, r => (string)r[0]! == "MB-16D");

        // ⚠️ Bu yüzden depo raporunun TOPLAMI ile ham bakiye toplamı AYRIŞIR — kullanıcı farkı göremez.
        //    (Rapora "H" maddesi olarak yazıldı; kod burada bilinçli olarak DEĞİŞTİRİLMEDİ.)
        var hamAnkara = ToplamLokasyon(_ankara);
        var raporAnkara = rap.Rows.Sum(r => Money.Parse((string)r[3]!));
        Assert.Equal(2m, hamAnkara - raporAnkara);
    }

    /// <summary>Bir lokasyondaki HAM bakiye toplamı (silinmiş malzemeler DAHİL).</summary>
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
    // 17 — ŞUBEYE BAĞLI KULLANICI (masaüstü ana kullanım biçimi)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>17 — Şubesine bağlı (masaüstünde login şubesi olan) kullanıcı yalnız KENDİ şubesinden
    /// çıkış/transfer yapabilir; başka şubeden denerse 403. Kendi şubesi otomatik kaynak olur.</summary>
    [Fact]
    public void S17_Subeye_Bagli_Kullanici_Yalniz_Kendi_Subesinden_Cikarir()
    {
        var m = Mat("MB-17");
        _opening.RecordOpening(_admin, m, 20m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _duzce, Op());

        // Düzce'ye bağlı oturum (masaüstü login şubesi). OperatingBranchId oturum kurulduktan SONRA
        // atanır (AuthService de böyle yapar) → burada da aynı yol kullanılır.
        var duzceli = new SessionContext(_admin.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        {
            OperatingBranchId = _duzce,
        };

        // (a) Kendi şubesinden çıkış serbest
        _stock.IssueOut(duzceli, new[] { new StockLine(m, 2m) }, Op(), branchId: _duzce);
        Muhasebe(m, 18m, (_duzce, 8m), (_ankara, 10m));

        // (b) BAŞKA şubeden çıkış 403
        Assert.Throws<ForbiddenException>(() =>
            _stock.IssueOut(duzceli, new[] { new StockLine(m, 1m) }, Op(), branchId: _ankara));

        // (c) Şube belirtilmezse KENDİ şubesine yazılır (rastgele şube seçilmez)
        _stock.IssueOut(duzceli, new[] { new StockLine(m, 1m) }, Op());
        Muhasebe(m, 17m, (_duzce, 7m), (_ankara, 10m));

        // (d) Transferde kaynak kendi şubesidir; başka kaynak reddedilir
        Assert.Throws<ForbiddenException>(() => _stock.Transfer(duzceli, m, 1m, _ankara, _karaman, Op()));
        _stock.Transfer(duzceli, m, 3m, _duzce, _karaman, Op());
        Muhasebe(m, 17m, (_duzce, 4m), (_karaman, 3m), (_ankara, 10m));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 18 — TEK LOKASYON VARSAYIMI AVI: LİSTE / TOPLU OKUMA ÇOĞALTMA TESTİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>18 — "Bir malzemenin yalnız tek lokasyonda stoğu olabilir" varsayımı KALMADI:
    /// beş lokasyonlu malzeme listede TEK satır olarak ve DOĞRU toplamla görünür; toplu bakiye
    /// okuması malzemeyi tekrarlamaz; ondalıklı toplam kayan noktaya düşmez.</summary>
    [Fact]
    public void S18_Bes_Lokasyonlu_Malzeme_Listede_Tek_Satir_Ve_Dogru_Toplam()
    {
        var m = Mat("MB-18");
        _opening.RecordOpening(_admin, m, 24m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 10m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 5m) }, _duzce, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 2m) }, _nevsehir, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(m, 7m) }, _santiye, Op());
        // KARAMAN = 0 (hiç dağıtılmadı), ATANMAMIŞ = 0 (tükendi)

        // Kullanıcının verdiği hedef tablo birebir
        Muhasebe(m, 24m, (_ankara, 10m), (_duzce, 5m), (_karaman, 0m), (_nevsehir, 2m), (_santiye, 7m),
            (Atanmamis, 0m));

        // (a) Malzeme LİSTE GRID'i (kullanıcının gördüğü ekran): dört dolu kovaya rağmen TEK satır,
        //     stok 24. Grid AYRI bir toplama yolu kullanır (SQL alt sorgusu) → çift kontrol.
        var grid = _materials.SearchGrid(_admin, new MaterialGridFilter(Code: "MB-18"), 1, 50);
        var satirlar = grid.Items.Where(x => x.Code == "MB-18").ToList();
        Assert.Single(satirlar);
        Assert.Equal(24m, satirlar[0].Stock);
        Assert.Equal("Yeterli", satirlar[0].Status);   // durum firma toplamına göre (kova sayısına değil)

        // (a2) Malzeme KARTI (detay) da aynı toplamı göstermeli
        Assert.Equal(24m, _materials.GetDetail(_admin, m).Stock);

        // (b) TOPLU bakiye okuma: sözlükte tek anahtar, doğru toplam
        var toplu = _stock.GetBalances(_admin, new[] { m });
        Assert.Single(toplu);
        Assert.Equal(24m, toplu[m]);

        // (c) ONDALIK: iki lokasyona bölünmüş kesirli miktar float hatası üretmemeli
        var d = Mat("MB-18D");
        _opening.RecordOpening(_admin, d, 0.3m, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(d, 0.1m) }, _ankara, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(d, 0.2m) }, _duzce, Op());
        Assert.Equal(0.3m, _stock.GetBalance(_admin, d));
        var dGrid = _materials.SearchGrid(_admin, new MaterialGridFilter(Code: "MB-18D"), 1, 50)
            .Items.Single(x => x.Code == "MB-18D");
        Assert.Equal(0.3m, dGrid.Stock);         // liste AYRI (SQL) toplama yolunu kullanır
        Assert.Equal(0.3m, _materials.GetDetail(_admin, d).Stock);
    }

    /// <summary>18b — Aynı anda ÇOK MALZEME × ÇOK LOKASYON: 3 malzeme × 5 lokasyon = 15 kova.
    /// Hiçbir kova bir diğerine sızmaz; her malzemenin toplamı bağımsız doğrudur.</summary>
    [Fact]
    public void S18b_Coklu_Malzeme_Coklu_Lokasyon_Capraz_Sizinti_Yok()
    {
        var loc = new[] { _ankara, _duzce, _karaman, _nevsehir, _santiye };
        var mats = new[] { Mat("MB-18B1"), Mat("MB-18B2"), Mat("MB-18B3") };
        // m0 → 1,2,3,4,5 (15) · m1 → 2,4,6,8,10 (30) · m2 → 3,6,9,12,15 (45)
        for (int i = 0; i < mats.Length; i++)
        {
            var toplam = Enumerable.Range(1, 5).Sum(k => k * (i + 1));
            _opening.RecordOpening(_admin, mats[i], toplam, Op());
            for (int j = 0; j < loc.Length; j++)
                _stock.DistributeUnassigned(_admin, new[] { new StockLine(mats[i], (j + 1) * (i + 1)) }, loc[j], Op());
        }

        for (int i = 0; i < mats.Length; i++)
        {
            var beklenen = loc.Select((l, j) => (l, (decimal)((j + 1) * (i + 1)))).ToArray();
            Muhasebe(mats[i], Enumerable.Range(1, 5).Sum(k => k * (i + 1)), beklenen);
        }

        // Bir malzemenin bir kovasından çıkış YALNIZ onu etkiler (15 kovadan 14'ü sabit)
        var oncesi = mats.ToDictionary(x => x, Rows);
        _stock.IssueOut(_admin, new[] { new StockLine(mats[1], 3m) }, Op(), branchId: _karaman);
        Assert.Equal(oncesi[mats[0]], Rows(mats[0]));
        Assert.Equal(oncesi[mats[2]], Rows(mats[2]));
        Assert.Equal(3m, At(mats[1], _karaman));   // 6 − 3
        Muhasebe(mats[1], 27m);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 19 — DAĞITIM LİSTESİ SESSİZCE KESİLMEZ (H-1 · 2026-08-12 düzeltildi)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>19 — Ekranların kullandığı yol (<see cref="StockService.ListUnassignedPage"/>) 500'lük
    /// sınırın ötesindeki kalemleri de getirir ve kaç kayıt olduğunu AÇIKÇA söyler. Dar bir pencere
    /// zorlansa bile kesilme artık SESSİZ değildir.
    /// Ayrıntılı sınır/arama/çok-tur senaryoları: <see cref="UnassignedListLimitTests"/>.</summary>
    [Fact]
    public void S19_Dagitim_Listesi_Sessizce_Kesilmez()
    {
        const int adet = 520;
        for (int i = 0; i < adet; i++)
            _opening.RecordOpening(_admin, Mat($"MB-19-{i:D4}"), 1m, Op());

        // Ekranların kullandığı yol: tamamı + sayım bilgisi
        var page = _stock.ListUnassignedPage(_admin);
        Assert.Equal(adet, page.Items.Count);
        Assert.Equal(adet, page.TotalCount);
        Assert.False(page.Truncated);
        Assert.Equal("520 kayıt bulundu.", page.CountText);
        Assert.Equal(520m, ToplamLokasyon(Atanmamis));        // ekran = gerçek

        // Pencere bilerek daraltılırsa kesilme OLUR ama artık kullanıcıya SÖYLENİR
        var dar = _stock.ListUnassignedPage(_admin, null, 500);
        Assert.True(dar.Truncated);
        Assert.Equal(20, dar.HiddenCount);
        Assert.Contains("20 kayıt ekranda değil", dar.CountText);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
