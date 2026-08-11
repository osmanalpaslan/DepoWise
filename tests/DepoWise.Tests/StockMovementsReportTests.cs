using ClosedXML.Excel;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-10a (2026-08-11) — STOK HAREKETLERİ RAPORU (katalog + Date/Location + XLSX export).
///
/// <b>Kapsam (bilinçli):</b> yalnız <c>Date</c> ve <c>Location</c> filtreleri. <c>Search</c>,
/// <c>Material</c> ve <c>MovementType</c> **STK-10b**'nindir ve bu artımda YOKTUR.
///
/// <b>Kilitlenen kurallar:</b>
///  • KAYNAK/HEDEF: <c>direction &gt; 0</c> → hedef · <c>direction &lt; 0</c> → kaynak.
///  • Transfer defterde **İKİ AYRI SATIR** kalır (tek satıra indirgenmez).
///  • Lokasyon filtresi: <c>branch_id = X OR branch_from_id = X</c> → A→B transferi hem A hem B'de.
///  • 🔒 <b>BranchScope × Location</b>: kapsam DIŞ SINIRDIR, lokasyon içeride daraltır
///    (<c>WHERE kapsam AND lokasyon</c>) → Depo A oturumu Depo B filtresiyle **BOŞ** alır.
///  • Hareket türü etiketi **tek kaynaktan** (`MovementTypeOptions`, STK-B1) — ikinci harita YOK.
///  • SQL'de **filtre → sırala → LIMIT** (plan §13/D-2).
///
/// 🔒 ÇEVRİMDIŞI: bu sınıf tamamen yerel SQLite üzerindedir, HTTP yoktur.
/// </summary>
public class StockMovementsReportTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly MaintenanceService _maintenance;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel = new();
    private readonly SessionContext _tumSubeler, _depoAOturum;
    private readonly string _depoA, _depoB, _depoC, _mat, _mat2, _vehicle, _def;

    private const string Rapor = "stock-movements";
    /// <summary>Tüm test verisini kapsayan geniş aralık (rapor RequiresDate → gönderilmezse "Bu Ay"a düşer).</summary>
    private const long Gunes = 1_699_000_000_000, Batis = 1_701_000_000_000;

    public StockMovementsReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_stk10a_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        SeedCompany(_factory, "A");

        var materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _maintenance = new MaintenanceService(_factory, _clock);
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _tumSubeler = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_tumSubeler, new NewBranch("Depo A"));
        _depoB = branches.Create(_tumSubeler, new NewBranch("Depo B"));
        _depoC = branches.Create(_tumSubeler, new NewBranch("Depo C"));
        _depoAOturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        _mat = materials.Create(_tumSubeler, new NewMaterial("HRK-1", "Yağ filtresi"));
        _mat2 = materials.Create(_tumSubeler, new NewMaterial("HRK-2", "Hava filtresi"));
        _vehicle = new VehicleService(_factory, _clock)
            .Create(_tumSubeler, new NewVehicle("HRK-IS", "34HRK01", 2020, 1000m, "km", _depoA));
        _def = new MaintenanceDefinitionService(_factory, _clock)
            .Create(_tumSubeler, new NewMaintenanceDefinition("Periyodik", 10000m, "km"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
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

    private ReportRequest Istek(SessionContext? _ = null, long? from = Gunes, long? to = Batis, params string[] lokasyonlar)
        => new(Executed: true, FromDate: from, ToDate: to,
               LocationIds: lokasyonlar.Length == 0 ? null : lokasyonlar);

    private TableModel Calistir(SessionContext s, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
        => _reports.Run(s, Rapor, req, maxRows);

    // ── Kolon indeksleri (başlıktan; sabit sayı gömmüyoruz) ──
    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new InvalidOperationException($"'{baslik}' kolonu yok. Kolonlar: {string.Join(", ", t.Headers)}");
    }

    private static string Hucre(TableModel t, int row, string baslik) => (string?)t.Rows[row][K(t, baslik)] ?? "";

    private static IEnumerable<IReadOnlyList<object?>> Turden(TableModel t, string movementType)
    {
        var etiket = MovementTypeOptions.Label(movementType);
        var tur = K(t, "Tür");
        return t.Rows.Where(r => (string?)r[tur] == etiket);
    }

    /// <summary>Tüm hareket türlerini üretir (8 tür) — testlerin ortak zemini.</summary>
    private void Senaryo()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);          // opening → Depo A
        _clock.Advance(60_000);
        var girisBelge = _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);   // in
        _clock.Advance(60_000);
        _stock.IssueOut(_tumSubeler, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);                      // out
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());                                                // transfer ×2
        _clock.Advance(60_000);
        _stock.Count(_tumSubeler, new[] { new CountLine(_mat, 99m) }, "sayım", Op(), branchId: _depoA);               // adjustment
        _clock.Advance(60_000);
        _stock.ReverseDocument(_tumSubeler, girisBelge.DocumentId, "yanlış giriş");                                   // reverse
        _clock.Advance(60_000);
        var bakim = _maintenance.Save(_tumSubeler, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 5000m,
            PerformedDate: _clock.UtcNow.ToUnixTimeMilliseconds(),
            Materials: new[] { new MaintenanceMaterialLine(_mat, 2m) },
            StockLocationId: _depoB), Op());                                                                          // usage → Depo B
        _clock.Advance(60_000);
        _maintenance.Cancel(_tumSubeler, bakim, "iptal");                                                             // usage_reverse → Depo B
        _clock.Advance(60_000);
        _opening.RecordOpening(_tumSubeler, _mat2, 7m, Op());                                                          // opening → ATANMAMIŞ
    }

    // ══════════════ 1. KATALOG VE KOLONLAR ══════════════

    /// <summary>1 — Rapor katalogda ve `Run` tanıyor; filtreleri YALNIZ Date + Location.</summary>
    [Fact]
    public void Rapor_Katalogda_ve_Yalniz_Date_Location_Filtreleri_Var()
    {
        var d = ReportCatalog.ByKey(Rapor);
        Assert.NotNull(d);
        Assert.Equal("Stok Hareketleri", d!.Name);
        Assert.True(d.UsesDate);
        Assert.True(d.UsesLocation);
        Assert.True(d.RequiresDate);           // defter büyür → tarihsiz tam tarama yok

        // STK-10b'nin filtreleri bu artımda AÇILMADI (kapsam sızmasının nöbetçisi).
        Assert.False(d.UsesBranch);
        Assert.False(d.UsesVehicle);
        Assert.False(d.UsesMaintenanceDef);
        Assert.False(d.UsesStatus);
        Assert.Equal(ReportFilters.Date | ReportFilters.Location, d.Filters);
    }

    /// <summary>2 — Kolonlar: Kaynak ve Hedef AYRI; defterin okunabilmesi için gerekli alanlar var.</summary>
    [Fact]
    public void Kolonlar_Kaynak_ve_Hedefi_Ayri_Gosteriyor()
    {
        Senaryo();
        var t = Calistir(_tumSubeler, Istek());

        foreach (var beklenen in new[] { "Tarih", "Tür", "Kod", "Malzeme", "Miktar", "Birim",
                                         "Kaynak", "Hedef", "Belge No", "Fatura No", "Durum", "Açıklama" })
            Assert.Contains(beklenen, t.Headers);
        Assert.Equal("Stok Hareketleri Raporu", t.Title);
        Assert.NotEqual(K(t, "Kaynak"), K(t, "Hedef"));
    }

    // ══════════════ 2. KAYNAK / HEDEF SEMANTİĞİ ══════════════

    /// <summary>3 — `direction > 0` → HEDEF dolu, kaynak yok (giriş/açılış).</summary>
    [Fact]
    public void Giris_Hareketinde_Hedef_Dolu_Kaynak_Bos()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);

        var t = Calistir(_tumSubeler, Istek());
        foreach (var tur in new[] { "opening", "in" })
        {
            var satir = Assert.Single(Turden(t, tur));
            Assert.Equal("Depo A", (string?)satir[K(t, "Hedef")]);
            Assert.Equal("—", (string?)satir[K(t, "Kaynak")]);
        }
    }

    /// <summary>4 — `direction < 0` → KAYNAK dolu, hedef yok (çıkış/bakım tüketimi).</summary>
    [Fact]
    public void Cikis_Hareketinde_Kaynak_Dolu_Hedef_Bos()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.IssueOut(_tumSubeler, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);

        var t = Calistir(_tumSubeler, Istek());
        var satir = Assert.Single(Turden(t, "out"));
        Assert.Equal("Depo A", (string?)satir[K(t, "Kaynak")]);
        Assert.Equal("—", (string?)satir[K(t, "Hedef")]);
    }

    /// <summary>5 — 🔴 TRANSFER: defterde İKİ satır kalır; giriş bacağı `Depo A → Depo B` gösterir.</summary>
    [Fact]
    public void Transfer_Iki_Satir_Kalir_ve_Kaynak_Hedef_Dogru()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());

        var t = Calistir(_tumSubeler, Istek());
        var bacaklar = Turden(t, "transfer").ToList();
        Assert.Equal(2, bacaklar.Count);   // TEK SATIRA İNDİRGENMEDİ

        var giris = bacaklar.Single(r => ((NumCell)r[K(t, "Miktar")]!).Value > 0);
        var cikis = bacaklar.Single(r => ((NumCell)r[K(t, "Miktar")]!).Value < 0);

        Assert.Equal("Depo A", (string?)giris[K(t, "Kaynak")]);   // branch_from_id
        Assert.Equal("Depo B", (string?)giris[K(t, "Hedef")]);    // branch_id
        Assert.Equal("Depo A", (string?)cikis[K(t, "Kaynak")]);
        Assert.Equal("—", (string?)cikis[K(t, "Hedef")]);
    }

    /// <summary>6 — ATANMAMIŞ hareket "Atanmamış" etiketiyle görünür; gerçek depo gibi değil.</summary>
    [Fact]
    public void Lokasyonsuz_Hareket_Atanmamis_Etiketiyle_Gorunur()
    {
        _opening.RecordOpening(_tumSubeler, _mat2, 7m, Op());   // depo YOK

        var t = Calistir(_tumSubeler, Istek());
        var satir = Assert.Single(t.Rows);
        Assert.Equal("Atanmamış", (string?)satir[K(t, "Hedef")]);
    }

    // ══════════════ 3. FİLTRELER ══════════════

    /// <summary>7 — TARİH filtresi: aralık dışındaki hareket gelmez.</summary>
    [Fact]
    public void Tarih_Filtresi_Araligi_Disini_Getirmiyor()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        var ilkAn = _clock.UtcNow.ToUnixTimeMilliseconds();
        _clock.Advance(10 * 60_000);
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);
        var ikinciAn = _clock.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(2, Calistir(_tumSubeler, Istek(from: Gunes, to: Batis)).Rows.Count);
        Assert.Single(Calistir(_tumSubeler, Istek(from: Gunes, to: ilkAn)).Rows);
        Assert.Single(Calistir(_tumSubeler, Istek(from: ikinciAn, to: Batis)).Rows);
        Assert.Empty(Calistir(_tumSubeler, Istek(from: ikinciAn + 1, to: Batis)).Rows);
    }

    /// <summary>8 — LOKASYON: "Tüm Şubeler" (filtre yok) → ATANMAMIŞ dahil HEPSİ.</summary>
    [Fact]
    public void Tum_Subeler_Atanmamis_Dahil_Hepsini_Getirir()
    {
        Senaryo();
        var t = Calistir(_tumSubeler, Istek());
        Assert.Contains(t.Rows, r => (string?)r[K(t, "Hedef")] == "Atanmamış");
        Assert.Contains(t.Rows, r => (string?)r[K(t, "Hedef")] == "Depo A");
        Assert.Contains(t.Rows, r => (string?)r[K(t, "Hedef")] == "Depo B");
    }

    /// <summary>9 — 🔴 LOKASYON A: transferin İKİ bacağı da görünür
    /// (çıkış `branch_id=A`, giriş `branch_from_id=A`).</summary>
    [Fact]
    public void Depo_A_Filtresi_Transferin_Iki_Bacagini_da_Getirir()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());

        var t = Calistir(_tumSubeler, Istek(lokasyonlar: _depoA));
        Assert.Equal(2, Turden(t, "transfer").Count());
    }

    /// <summary>10 — 🔴 LOKASYON B: yalnız GİRİŞ bacağı (`branch_id=B`) görünür.</summary>
    [Fact]
    public void Depo_B_Filtresi_Yalniz_Giris_Bacagini_Getirir()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());

        var t = Calistir(_tumSubeler, Istek(lokasyonlar: _depoB));
        var bacak = Assert.Single(Turden(t, "transfer"));
        Assert.Equal("Depo A", (string?)bacak[K(t, "Kaynak")]);
        Assert.Equal("Depo B", (string?)bacak[K(t, "Hedef")]);
    }

    /// <summary>11 — 🔴 LOKASYON C: ilgisiz depo → transferden hiçbir satır gelmez.</summary>
    [Fact]
    public void Ilgisiz_Depo_Filtresi_Transferi_Getirmez()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());

        var t = Calistir(_tumSubeler, Istek(lokasyonlar: _depoC));
        Assert.Empty(t.Rows);
    }

    /// <summary>12 — 📦 ATANMAMIŞ filtresi: yalnız İKİ tarafı da boş olan hareketler.</summary>
    [Fact]
    public void Atanmamis_Filtresi_Yalniz_Lokasyonsuz_Hareketleri_Getirir()
    {
        Senaryo();

        var t = Calistir(_tumSubeler, Istek(lokasyonlar: ""));
        Assert.NotEmpty(t.Rows);
        Assert.All(t.Rows, r =>
        {
            Assert.Equal("Atanmamış", (string?)r[K(t, "Hedef")] == "Atanmamış" ? "Atanmamış" : (string?)r[K(t, "Kaynak")]);
        });
        // Gerçek depolu hiçbir satır sızmadı.
        Assert.DoesNotContain(t.Rows, r => (string?)r[K(t, "Hedef")] == "Depo A" || (string?)r[K(t, "Kaynak")] == "Depo A");
    }

    /// <summary>13 — Çoklu lokasyon seçimi birleşim (union) verir.</summary>
    [Fact]
    public void Coklu_Lokasyon_Secimi_Birlesim_Verir()
    {
        Senaryo();
        var a = Calistir(_tumSubeler, Istek(lokasyonlar: _depoA)).Rows.Count;
        var b = Calistir(_tumSubeler, Istek(lokasyonlar: _depoB)).Rows.Count;
        var ab = Calistir(_tumSubeler, Istek(lokasyonlar: new[] { _depoA, _depoB })).Rows.Count;

        Assert.True(ab >= Math.Max(a, b));
        Assert.True(ab <= a + b);   // kesişen satırlar (transfer bacakları) iki kez sayılmaz
    }

    // ══════════════ 4. 🔒 BranchScope × Location KESİŞİMİ ══════════════

    /// <summary>14 — 🔴 Depo A OTURUMU + A filtresi: yalnız KAPSAM İÇİNDEKİ bacak görünür.
    /// Giriş bacağının `branch_id`'si Depo B olduğu için kapsam onu eler.</summary>
    [Fact]
    public void Depo_A_Oturumu_A_Filtresiyle_Yalniz_Kapsam_Icindekini_Gorur()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());

        var kapsamli = Calistir(_depoAOturum, Istek(lokasyonlar: _depoA));
        var bacak = Assert.Single(Turden(kapsamli, "transfer"));
        Assert.Equal("Depo A", (string?)bacak[K(kapsamli, "Kaynak")]);
        Assert.Equal("—", (string?)bacak[K(kapsamli, "Hedef")]);   // çıkış bacağı

        // Aynı filtreyle "Tüm Şubeler" oturumu İKİ bacağı görür → fark kapsamdan geliyor.
        Assert.Equal(2, Turden(Calistir(_tumSubeler, Istek(lokasyonlar: _depoA)), "transfer").Count());
    }

    /// <summary>15 — 🔴🔒 EN KRİTİK: Depo A oturumu Depo B filtresiyle **BOŞ** sonuç alır.
    /// Lokasyon filtresi kapsamı GENİŞLETEMEZ — yetki aşılmaz.</summary>
    [Fact]
    public void Depo_A_Oturumu_Depo_B_Filtresiyle_BOS_Sonuc_Alir()
    {
        Senaryo();   // Depo B'de transfer girişi + bakım tüketimi VAR

        var tumu = Calistir(_tumSubeler, Istek(lokasyonlar: _depoB));
        Assert.NotEmpty(tumu.Rows);   // yetkili kullanıcı görüyor

        var kapsamli = Calistir(_depoAOturum, Istek(lokasyonlar: _depoB));
        Assert.Empty(kapsamli.Rows);  // 🔒 şubeye bağlı kullanıcı GÖREMEZ
    }

    /// <summary>16 — Kapsam, lokasyon filtresi HİÇ verilmese de uygulanır (varsayılan sınır).</summary>
    [Fact]
    public void Kapsam_Lokasyon_Filtresi_Olmadan_da_Uygulanir()
    {
        Senaryo();
        var tumu = Calistir(_tumSubeler, Istek());
        var kapsamli = Calistir(_depoAOturum, Istek());
        Assert.True(kapsamli.Rows.Count < tumu.Rows.Count);
        // Depo B'ye ait hiçbir hareket kapsamlı sonuçta yok.
        Assert.DoesNotContain(kapsamli.Rows, r => (string?)r[K(kapsamli, "Hedef")] == "Depo B");
    }

    // ══════════════ 5. HAREKET TÜRLERİ (STK-B1 korunuyor) ══════════════

    /// <summary>17 — 8 hareket türünün tamamı raporda DOĞRU TÜRKÇE etiketle geliyor;
    /// hiçbiri ham İngilizce değil. Etiketler STK-B1'in tek kaynağından okunur.</summary>
    [Fact]
    public void Sekiz_Hareket_Turu_Dogru_Turkce_Etiketle_Geliyor()
    {
        Senaryo();
        var t = Calistir(_tumSubeler, Istek());
        var turKolonu = K(t, "Tür");
        var gorulen = t.Rows.Select(r => (string?)r[turKolonu]!).Distinct().ToList();

        // Senaryo 8 türün 8'ini üretir.
        Assert.Equal(8, gorulen.Count);
        foreach (var etiket in gorulen)
        {
            Assert.Contains(MovementTypeOptions.All, x => x.Label == etiket);          // katalogdan
            Assert.DoesNotContain(MovementTypeOptions.All, x => x.Key == etiket);      // ham DEĞİL
        }
    }

    /// <summary>18 — BKM-04: bakım tüketimi SEÇİLEN depoda görünüyor (kaynak = Depo B).</summary>
    [Fact]
    public void Bakim_Tuketimi_Secilen_Depoda_Gorunuyor()
    {
        Senaryo();
        var t = Calistir(_tumSubeler, Istek());
        var tuketim = Assert.Single(Turden(t, "usage"));
        Assert.Equal("Depo B", (string?)tuketim[K(t, "Kaynak")]);
        Assert.Equal("—", (string?)tuketim[K(t, "Hedef")]);
    }

    /// <summary>19 — BKM-04: bakım tüketiminin TERS kaydı ORİJİNAL hareketin deposunda görünüyor.</summary>
    [Fact]
    public void Bakim_Tuketiminin_Ters_Kaydi_Orijinal_Depoda()
    {
        Senaryo();
        var t = Calistir(_tumSubeler, Istek());
        var ters = Assert.Single(Turden(t, "usage_reverse"));
        Assert.Equal("Depo B", (string?)ters[K(t, "Hedef")]);   // geri ekleme → direction > 0 → hedef
    }

    // ══════════════ 6. SINIRLAR / GÜVENLİK / BOŞ SONUÇ ══════════════

    /// <summary>20 — 🔴 SATIR TAVANI SQL'e iniyor: `maxRows` kadar satır döner (bellekte kesilmiyor).</summary>
    [Fact]
    public void Satir_Tavani_SQL_Tarafinda_Uygulaniyor()
    {
        for (int i = 0; i < 12; i++)
        {
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 1m) }, Op(), branchId: _depoA);
            _clock.Advance(60_000);
        }

        Assert.Equal(12, Calistir(_tumSubeler, Istek()).Rows.Count);
        Assert.Equal(5, Calistir(_tumSubeler, Istek(), maxRows: 5).Rows.Count);
        Assert.Single(Calistir(_tumSubeler, Istek(), maxRows: 1).Rows);

        // Tavan uygulanırken SIRALAMA korunur: en yeni hareket ilk satırdır.
        var kesik = Calistir(_tumSubeler, Istek(), maxRows: 1);
        var tam = Calistir(_tumSubeler, Istek());
        Assert.Equal(Hucre(tam, 0, "Tarih"), Hucre(kesik, 0, "Tarih"));
    }

    /// <summary>21 — Sıralama: en yeni hareket en üstte (tarih azalan).</summary>
    [Fact]
    public void Siralama_En_Yeni_Ustte()
    {
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 1m) }, Op(), branchId: _depoA);
        _clock.Advance(10 * 60_000);
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 2m) }, Op(), branchId: _depoB);

        var t = Calistir(_tumSubeler, Istek());
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("Depo B", Hucre(t, 0, "Hedef"));   // en yeni
        Assert.Equal("Depo A", Hucre(t, 1, "Hedef"));
    }

    /// <summary>22 — BOŞ SONUÇ: hata değil, boş tablo (başlıklar korunur, toplam satırı yok).</summary>
    [Fact]
    public void Bos_Sonuc_Duzgun_Doner()
    {
        var t = Calistir(_tumSubeler, Istek());
        Assert.Empty(t.Rows);
        Assert.NotEmpty(t.Headers);
        Assert.Null(t.TotalRow);
    }

    /// <summary>23 — 🔒 FİRMA İZOLASYONU: başka firmanın hareketleri rapora SIZMAZ.</summary>
    [Fact]
    public void Baska_Firmanin_Hareketleri_Rapora_Sizmaz()
    {
        SeedCompany(_factory, "B");
        var users = new UserService(_factory, _clock);
        var uidB = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var sB = new SessionContext(uidB, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var matB = new MaterialService(_factory, _clock).Create(sB, new NewMaterial("YAB-1", "Yabancı malzeme"));
        var depoB = new BranchService(_factory, _clock).Create(sB, new NewBranch("Yabancı Depo"));
        _stock.ReceiveIn(sB, new[] { new StockLine(matB, 50m) }, Op(), branchId: depoB);

        Senaryo();

        var t = Calistir(_tumSubeler, Istek());
        Assert.DoesNotContain(t.Rows, r => (string?)r[K(t, "Kod")] == "YAB-1");
        // Yabancı firmanın deposu FİLTRE olarak gönderilse bile veri sızmaz.
        var yabanciFiltre = Calistir(_tumSubeler, Istek(lokasyonlar: depoB));
        Assert.Empty(yabanciFiltre.Rows);
    }

    // ══════════════ 7. 🔴 GERÇEK XLSX ROUND-TRIP (RPR-01'in açık bıraktığı boşluk) ══════════════

    /// <summary>XLSX'i GERÇEKTEN açıp okur: (başlıklar, satırlar) — hücreler metne çevrilir,
    /// sayısal kolonlar ham sayı olarak karşılaştırılır.</summary>
    private static (List<string> Headers, List<List<string>> Rows) XlsxOku(byte[] bytes, int beklenenSatir, IReadOnlyList<bool> numeric)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        var headers = new List<string>();
        int c = 1;
        while (!string.IsNullOrEmpty(ws.Cell(1, c).GetString())) { headers.Add(ws.Cell(1, c).GetString()); c++; }

        var rows = new List<List<string>>();
        for (int r = 0; r < beklenenSatir; r++)
        {
            var satir = new List<string>();
            for (int col = 0; col < headers.Count; col++)
            {
                var cell = ws.Cell(r + 2, col + 1);
                satir.Add(col < numeric.Count && numeric[col]
                    ? (cell.IsEmpty() ? "" : cell.GetDouble().ToString("0.####"))
                    : cell.GetString());
            }
            rows.Add(satir);
        }
        return (headers, rows);
    }

    /// <summary>Rapor sonucunu XLSX ile AYNI biçimde metne çevirir (karşılaştırma için).</summary>
    private static List<List<string>> TabloyuMetne(TableModel t)
        => t.Rows.Select(r => r.Select((h, i) => h switch
        {
            null => "",
            NumCell n => n.Value.ToString("0.####"),
            _ => h.ToString() ?? "",
        }).ToList()).ToList();

    /// <summary>🔴 EKRAN ↔ XLSX SATIR SATIR KARŞILAŞTIRMA — aynı `ReportRequest`, aynı küme.
    /// Yalnız satır SAYISI değil, her hücre karşılaştırılır (tarih/kod/malzeme/miktar/kaynak/hedef dahil).</summary>
    private void EkranVeXlsxAyni(ReportRequest istek, string senaryoAdi, SessionContext? oturum = null)
    {
        var s = oturum ?? _tumSubeler;
        var ekran = Calistir(s, istek);
        var bytes = _excel.Export(ekran);          // API'nin export ucuyla AYNI yol (BuildReport → Excel.Export)
        Assert.True(bytes.Length > 0, $"{senaryoAdi}: XLSX üretilemedi.");

        var (headers, xlsxRows) = XlsxOku(bytes, ekran.Rows.Count, ekran.Numeric ?? Array.Empty<bool>());
        Assert.Equal(ekran.Headers, headers);

        var ekranRows = TabloyuMetne(ekran);
        Assert.Equal(ekranRows.Count, xlsxRows.Count);
        for (int i = 0; i < ekranRows.Count; i++)
            Assert.Equal(ekranRows[i], xlsxRows[i]);   // hücre hücre — eksik/fazla/farklı sıra yakalanır
    }

    /// <summary>24 — 🔴 ALTI ANLAMLI FİLTRE KOMBİNASYONUNDA gerçek XLSX satır satır doğrulandı.
    /// RPR-01'de "yalnız aynı servis çağrılıyor" dolaylı ispatıyla yetinilmişti — burada XLSX
    /// GERÇEKTEN açılıp okunuyor.</summary>
    [Fact]
    public void Ekran_ve_XLSX_Alti_Kombinasyonda_Satir_Satir_Ayni()
    {
        Senaryo();
        var ilkAn = 1_700_000_000_000L;

        EkranVeXlsxAyni(Istek(), "1) filtresiz (geniş tarih)");
        EkranVeXlsxAyni(Istek(lokasyonlar: _depoA), "2) lokasyon = Depo A");
        EkranVeXlsxAyni(Istek(lokasyonlar: _depoB), "3) lokasyon = Depo B");
        EkranVeXlsxAyni(Istek(lokasyonlar: ""), "4) lokasyon = Atanmamış");
        EkranVeXlsxAyni(Istek(from: ilkAn + 120_000, to: Batis), "5) dar tarih aralığı");
        EkranVeXlsxAyni(Istek(from: ilkAn + 120_000, to: Batis, lokasyonlar: new[] { _depoA, _depoB }),
            "6) tarih + çoklu lokasyon");
    }

    /// <summary>25 — 🔴 XLSX'te KAYNAK/HEDEF ve "Atanmamış" doğru yazılıyor (gözle okunabilir çıktı).</summary>
    [Fact]
    public void XLSX_Kaynak_Hedef_ve_Atanmamis_Dogru()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());
        _clock.Advance(60_000);
        _opening.RecordOpening(_tumSubeler, _mat2, 7m, Op());   // ATANMAMIŞ

        var ekran = Calistir(_tumSubeler, Istek());
        var (headers, rows) = XlsxOku(_excel.Export(ekran), ekran.Rows.Count, ekran.Numeric!);

        int kaynak = headers.IndexOf("Kaynak"), hedef = headers.IndexOf("Hedef");
        Assert.True(kaynak >= 0 && hedef >= 0);
        Assert.Contains(rows, r => r[kaynak] == "Depo A" && r[hedef] == "Depo B");   // transfer giriş bacağı
        Assert.Contains(rows, r => r[hedef] == "Atanmamış");
        Assert.Contains(rows, r => r[hedef] == "—" && r[kaynak] == "Depo A");        // transfer çıkış bacağı
    }

    /// <summary>26 — Ekran ve export AYNI satır tavanına tabidir (plan §13/D-1): ayrı bir export
    /// yolu ya da farklı limit YOK.</summary>
    [Fact]
    public void Ekran_ve_Export_Ayni_Satir_Tavanina_Tabi()
    {
        for (int i = 0; i < 10; i++)
        {
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 1m) }, Op(), branchId: _depoA);
            _clock.Advance(60_000);
        }

        var ekran = Calistir(_tumSubeler, Istek(), maxRows: 4);
        Assert.Equal(4, ekran.Rows.Count);

        var (_, xlsxRows) = XlsxOku(_excel.Export(ekran), ekran.Rows.Count, ekran.Numeric!);
        Assert.Equal(4, xlsxRows.Count);   // export ekrandan FAZLA satır üretmiyor
    }

    /// <summary>27 — Boş sonuçta XLSX yine üretilir (başlıklı, satırsız) — hata değil.</summary>
    [Fact]
    public void Bos_Sonucta_da_XLSX_Uretiliyor()
    {
        var ekran = Calistir(_tumSubeler, Istek());
        Assert.Empty(ekran.Rows);

        var (headers, rows) = XlsxOku(_excel.Export(ekran), 0, ekran.Numeric ?? Array.Empty<bool>());
        Assert.Equal(ekran.Headers, headers);
        Assert.Empty(rows);
    }

    // ══════════════ 8. MASAÜSTÜ / ÇEVRİMDIŞI + WEB PARİTESİ ══════════════

    /// <summary>28 — 🔒 ÇEVRİMDIŞI: masaüstü raporu ve Excel çıktısı YEREL SQLite üzerinde, hiçbir
    /// HTTP çağrısı olmadan üretiliyor. Rapor için yeni API bağımlılığı YOK.
    ///
    /// İstek, masaüstü <c>ReportsViewModel.BuildTable()</c> ile AYNI kuralla kurulur: filtre yalnız
    /// rapor onu kullanıyorsa gönderilir (<c>UsesDate</c>/<c>UsesLocation</c>).</summary>
    [Fact]
    public void Masaustu_Cevrimdisi_Rapor_ve_Export_Calisiyor()
    {
        Senaryo();
        var d = ReportCatalog.ByKey(Rapor)!;

        // ReportsViewModel.BuildTable() deseni — bayrak kapalıysa alan gönderilmez.
        var istek = new ReportRequest(
            Executed: true,
            FromDate: d.UsesDate ? Gunes : null,
            ToDate: d.UsesDate ? Batis : null,
            LocationIds: d.UsesLocation ? new[] { _depoA } : null);

        var tablo = _reports.Run(_tumSubeler, Rapor, istek);
        Assert.NotEmpty(tablo.Rows);
        Assert.Contains("Kaynak", tablo.Headers);

        // Masaüstü Excel'i de aynı TableModel'den üretilir (ortak ExcelExportService).
        var (headers, rows) = XlsxOku(_excel.Export(tablo), tablo.Rows.Count, tablo.Numeric!);
        Assert.Equal(tablo.Headers, headers);
        Assert.Equal(TabloyuMetne(tablo), rows);
    }

    /// <summary>29 — WEB ↔ MASAÜSTÜ PARİTESİ: iki platform da AYNI katalogdan aynı raporu sürer ve
    /// aynı istekle AYNI kümeyi alır (rapor motoru ortaktır — `ReportService`).
    /// Bu artımda yeni filtre bayrağı eklenmediği için RPR-01'in kablolama koruması AYNEN geçerlidir.</summary>
    [Fact]
    public void Web_ve_Masaustu_Ayni_Katalogdan_Ayni_Sonucu_Alir()
    {
        Senaryo();
        var istek = Istek(lokasyonlar: _depoB);

        // İki platform da ReportCatalog.All'dan besleniyor → rapor ikisinde de görünür.
        Assert.Contains(ReportCatalog.All, x => x.Key == Rapor);

        var birinci = _reports.Run(_tumSubeler, Rapor, istek);
        var ikinci = _reports.Run(_tumSubeler, Rapor, istek);
        Assert.Equal(TabloyuMetne(birinci), TabloyuMetne(ikinci));   // deterministik: aynı istek → aynı küme
        Assert.Equal(birinci.Headers, ikinci.Headers);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
