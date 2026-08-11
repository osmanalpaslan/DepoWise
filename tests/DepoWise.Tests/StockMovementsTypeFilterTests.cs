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
/// STK-10b-1 (2026-08-11) — STOK HAREKETLERİ RAPORU: HAREKET TÜRÜ FİLTRESİ.
///
/// <b>Kapsam:</b> YALNIZ <c>MovementType</c> filtresi. <c>Search</c> (10b-2), <c>Material</c> (10b-3)
/// ve ekran bağlantıları (10b-4) bu artımda YOKTUR.
///
/// <b>Kilitlenen kurallar:</b>
///  • 8 türün 8'i ayrı ayrı filtrelenebilir; seçilmeyen türler gelmez.
///  • Filtre KANONİK <c>movement_type</c> anahtarıyla çalışır — kullanıcıya gösterilen ETİKETLE değil.
///  • Seçenekler ve etiketler TEK kaynaktan: <see cref="MovementTypeOptions"/> (STK-B1) — ikinci harita YOK.
///  • Filtre SQL'de uygulanır (bellekte süzme yok) ve <c>BranchScope</c>'u GENİŞLETMEZ.
///  • Bilinmeyen anahtar fail-closed: veri sızdırmaz.
///  • Export ekranla AYNI kümeyi üretir — gerçek XLSX ile hücre hücre doğrulanır.
///
/// 🔒 ÇEVRİMDIŞI: tamamı yerel SQLite üzerindedir; HTTP yoktur.
/// </summary>
public class StockMovementsTypeFilterTests : IDisposable
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
    private readonly string _depoA, _depoB, _mat, _mat2, _vehicle, _def;

    private const string Rapor = "stock-movements";
    private const long Gunes = 1_699_000_000_000, Batis = 1_701_000_000_000;

    public StockMovementsTypeFilterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_10b1_" + Guid.NewGuid().ToString("N") + ".db");
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
        _opening = new OpeningStockService(_factory, _clock);
        _maintenance = new MaintenanceService(_factory, _clock);
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _tumSubeler = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_tumSubeler, new NewBranch("Depo A"));
        _depoB = branches.Create(_tumSubeler, new NewBranch("Depo B"));
        _depoAOturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        _mat = materials.Create(_tumSubeler, new NewMaterial("TYP-1", "Yağ filtresi"));
        _mat2 = materials.Create(_tumSubeler, new NewMaterial("TYP-2", "Hava filtresi"));
        _vehicle = new VehicleService(_factory, _clock)
            .Create(_tumSubeler, new NewVehicle("TYP-IS", "34TYP01", 2020, 1000m, "km", _depoA));
        _def = new MaintenanceDefinitionService(_factory, _clock)
            .Create(_tumSubeler, new NewMaintenanceDefinition("Periyodik", 10000m, "km"));

        Senaryo();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>8 hareket türünün 8'ini gerçek servislerle üretir.</summary>
    private void Senaryo()
    {
        _opening.RecordOpening(_tumSubeler, _mat, 100m, Op(), branchId: _depoA);                                       // opening
        _clock.Advance(60_000);
        var girisBelge = _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);    // in
        _clock.Advance(60_000);
        _stock.IssueOut(_tumSubeler, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);                       // out
        _clock.Advance(60_000);
        _stock.Transfer(_tumSubeler, _mat, 10m, _depoA, _depoB, Op());                                                 // transfer ×2
        _clock.Advance(60_000);
        _stock.Count(_tumSubeler, new[] { new CountLine(_mat, 99m) }, "sayım", Op(), branchId: _depoA);                // adjustment
        _clock.Advance(60_000);
        _stock.ReverseDocument(_tumSubeler, girisBelge.DocumentId, "yanlış giriş");                                    // reverse
        _clock.Advance(60_000);
        var bakim = _maintenance.Save(_tumSubeler, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 5000m,
            PerformedDate: _clock.UtcNow.ToUnixTimeMilliseconds(),
            Materials: new[] { new MaintenanceMaterialLine(_mat, 2m) },
            StockLocationId: _depoB), Op());                                                                           // usage → Depo B
        _clock.Advance(60_000);
        _maintenance.Cancel(_tumSubeler, bakim, "iptal");                                                              // usage_reverse → Depo B
        _clock.Advance(60_000);
        _opening.RecordOpening(_tumSubeler, _mat2, 7m, Op());                                                          // opening → ATANMAMIŞ
    }

    private ReportRequest Istek(string[]? turler = null, string[]? lokasyonlar = null, long? from = Gunes, long? to = Batis)
        => new(Executed: true, FromDate: from, ToDate: to,
               LocationIds: lokasyonlar, MovementTypes: turler);

    private TableModel Calistir(ReportRequest req, SessionContext? s = null)
        => _reports.Run(s ?? _tumSubeler, Rapor, req);

    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new InvalidOperationException($"'{baslik}' kolonu yok.");
    }

    /// <summary>Sonuçtaki DISTINCT tür etiketleri.</summary>
    private static List<string> Turler(TableModel t)
    {
        var k = K(t, "Tür");
        return t.Rows.Select(r => (string?)r[k] ?? "").Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    // ══════════════ 1. 8 TÜRÜN TAMAMI FİLTRELENEBİLİR ══════════════

    /// <summary>1-8 — Her hareket türü TEK BAŞINA filtrelenebiliyor ve sonuçta YALNIZ o tür var.
    /// (`usage`, `usage_reverse`, `reverse`, `transfer`, `adjustment` dahil — talimatın 3-7. maddeleri.)</summary>
    [Theory]
    [InlineData("opening")]
    [InlineData("in")]
    [InlineData("out")]
    [InlineData("transfer")]
    [InlineData("adjustment")]
    [InlineData("usage")]
    [InlineData("usage_reverse")]
    [InlineData("reverse")]
    public void Her_Hareket_Turu_Tek_Basina_Filtrelenebiliyor(string tur)
    {
        var t = Calistir(Istek(turler: new[] { tur }));

        Assert.NotEmpty(t.Rows);                                   // senaryo bu türü üretiyor
        var beklenen = MovementTypeOptions.Label(tur);
        Assert.Equal(new[] { beklenen }, Turler(t));                // YALNIZ o tür
        Assert.NotEqual(tur, beklenen);                             // ham İngilizce sızmıyor
    }

    /// <summary>9 — Seçilmeyen türler sonuçta YOK: filtresiz sonuç 8 tür, filtreli 1 tür.</summary>
    [Fact]
    public void Secilmeyen_Turler_Sonuca_Girmiyor()
    {
        var hepsi = Calistir(Istek());
        Assert.Equal(8, Turler(hepsi).Count);

        var yalnizTransfer = Calistir(Istek(turler: new[] { "transfer" }));
        Assert.Single(Turler(yalnizTransfer));
        Assert.Equal(2, yalnizTransfer.Rows.Count);                 // transferin İKİ bacağı (semantik korundu)
        Assert.True(yalnizTransfer.Rows.Count < hepsi.Rows.Count);
    }

    /// <summary>10 — ÇOKLU seçim birleşim verir (mevcut çoklu-filtre sözleşmesi).</summary>
    [Fact]
    public void Coklu_Tur_Secimi_Birlesim_Veriyor()
    {
        var giris = Calistir(Istek(turler: new[] { "in" })).Rows.Count;
        var cikis = Calistir(Istek(turler: new[] { "out" })).Rows.Count;
        var ikisi = Calistir(Istek(turler: new[] { "in", "out" }));

        Assert.Equal(giris + cikis, ikisi.Rows.Count);
        Assert.Equal(2, Turler(ikisi).Count);
    }

    /// <summary>11 — 🔒 FAIL-CLOSED: bilinmeyen/uydurma tür anahtarı veri SIZDIRMAZ (boş sonuç),
    /// "filtre yok" gibi davranıp her şeyi göstermez.</summary>
    [Fact]
    public void Bilinmeyen_Tur_Anahtari_Veri_Sizdirmaz()
    {
        var t = Calistir(Istek(turler: new[] { "boyle_bir_tur_yok" }));
        Assert.Empty(t.Rows);

        // Geçerli + geçersiz karışımı: yalnız geçerli olan gelir.
        var karisik = Calistir(Istek(turler: new[] { "in", "uydurma" }));
        Assert.Single(Turler(karisik));
        Assert.Equal(MovementTypeOptions.Label("in"), Turler(karisik)[0]);
    }

    /// <summary>12 — Filtre KANONİK ANAHTARLA çalışır; kullanıcıya gösterilen ETİKET gönderilirse
    /// eşleşmez (etiketle sorgulama yapılmadığının kanıtı).</summary>
    [Fact]
    public void Filtre_Etiketle_Degil_Kanonik_Anahtarla_Calisiyor()
    {
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "usage" })).Rows);
        Assert.Empty(Calistir(Istek(turler: new[] { "Bakım Tüketimi" })).Rows);
    }

    /// <summary>13 — Boş liste = filtre YOK (tüm türler) — "hiçbiri" DEĞİL.</summary>
    [Fact]
    public void Bos_Liste_Filtre_Yok_Anlamina_Geliyor()
    {
        Assert.Equal(Calistir(Istek()).Rows.Count,
                     Calistir(Istek(turler: Array.Empty<string>())).Rows.Count);
    }

    // ══════════════ 2. ETİKET TEK KAYNAK (STK-B1 korunuyor) ══════════════

    /// <summary>14 — Sonuçtaki TÜM tür etiketleri katalogdan geliyor; hiçbiri ham İngilizce değil.</summary>
    [Fact]
    public void Etiketler_MovementTypeOptions_Katalogundan_Geliyor()
    {
        var t = Calistir(Istek());
        var katalogEtiketleri = MovementTypeOptions.All.Select(x => x.Label).ToHashSet(StringComparer.Ordinal);
        var hamAnahtarlar = MovementTypeOptions.All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var etiket in Turler(t))
        {
            Assert.Contains(etiket, katalogEtiketleri);
            Assert.DoesNotContain(etiket, hamAnahtarlar);
        }
    }

    /// <summary>15 — Web ve masaüstü AYNI seçenek listesini kullanıyor: ikisi de MovementTypeOptions'tan
    /// besleniyor (Web paylaşılan dosyayı derliyor). Kaynak taramasıyla kilitli — ikinci harita YOK.</summary>
    [Fact]
    public void Web_ve_Masaustu_Ayni_Secenek_Kaynagini_Kullaniyor()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DepoWise.sln"))) root = root.Parent;
        Assert.NotNull(root);

        var web = File.ReadAllText(Path.Combine(root!.FullName, "src", "DepoWise.Web", "Components", "Pages", "Reports.razor"));
        var vm = File.ReadAllText(Path.Combine(root.FullName, "src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs"));

        Assert.Contains("MovementTypeOptions.All", web, StringComparison.Ordinal);
        Assert.Contains("MovementTypeOptions.All", vm, StringComparison.Ordinal);
        // Hiçbiri kendi tür listesini/haritasını taşımıyor (STK-B1 tek kaynak korunuyor).
        foreach (var (kaynak, ad) in new[] { (web, "Reports.razor"), (vm, "ReportsViewModel.cs") })
            foreach (var yasak in new[] { "\"usage\" =>", "\"transfer\" =>", "\"opening\" =>" })
                Assert.False(kaynak.Contains(yasak, StringComparison.Ordinal),
                    $"{ad} kendi hareket türü haritasını taşıyor ('{yasak}') — STK-B1 tek kaynağı bozuldu.");
    }

    // ══════════════ 3. DİĞER FİLTRELERLE BİRLİKTE ══════════════

    /// <summary>16 — MovementType + Location birlikte: `usage` Depo B'de üretildi → Depo A'da YOK.</summary>
    [Fact]
    public void MovementType_ve_Location_Birlikte_Calisiyor()
    {
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "usage" }, lokasyonlar: new[] { _depoB })).Rows);
        Assert.Empty(Calistir(Istek(turler: new[] { "usage" }, lokasyonlar: new[] { _depoA })).Rows);

        // `in` Depo A'da → tersi geçerli.
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "in" }, lokasyonlar: new[] { _depoA })).Rows);
        Assert.Empty(Calistir(Istek(turler: new[] { "in" }, lokasyonlar: new[] { _depoB })).Rows);
    }

    /// <summary>17 — MovementType + Date birlikte: aralık dışındaki tür kaydı gelmez.</summary>
    [Fact]
    public void MovementType_ve_Date_Birlikte_Calisiyor()
    {
        var acilisAni = 1_700_000_000_000L;   // ilk hareket (opening) tam bu anda
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "opening" }, from: Gunes, to: acilisAni)).Rows);
        Assert.Empty(Calistir(Istek(turler: new[] { "in" }, from: Gunes, to: acilisAni)).Rows);   // `in` daha sonra
    }

    /// <summary>18 — 🔒 MovementType + BranchScope: tür filtresi kapsamı GENİŞLETMEZ.
    /// `usage` Depo B'de üretildi; Depo A oturumu bunu türle isteyince bile GÖREMEZ.</summary>
    [Fact]
    public void MovementType_BranchScope_Sinirini_Asmiyor()
    {
        // Yetkili kullanıcı görüyor.
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "usage" })).Rows);

        // Depo A oturumu göremiyor (hareketin branch_id'si Depo B).
        Assert.Empty(Calistir(Istek(turler: new[] { "usage" }), _depoAOturum).Rows);

        // Depo A'daki türü ise görüyor → kapsam çalışıyor, filtre onu genişletmiyor.
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "in" }), _depoAOturum).Rows);
    }

    /// <summary>19 — 🔒 MovementType + Location + BranchScope üçlüsü: kapsam yine DIŞ SINIR.</summary>
    [Fact]
    public void MovementType_Location_ve_BranchScope_Ucluisu_Kapsami_Koruyor()
    {
        Assert.Empty(Calistir(Istek(turler: new[] { "usage" }, lokasyonlar: new[] { _depoB }), _depoAOturum).Rows);
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "usage" }, lokasyonlar: new[] { _depoB })).Rows);
    }

    // ══════════════ 4. SQL TARAFINDA FİLTRELEME ══════════════

    /// <summary>20 — 🔴 Tür filtresi SQL'de uygulanıyor: satır tavanı, FİLTRELENMİŞ küme üzerine iner.
    /// Bellekte süzülseydi tavan önce tüm hareketleri kesip sonuç boş/eksik kalırdı.</summary>
    [Fact]
    public void Tur_Filtresi_LIMIT_ten_ONCE_SQL_de_Uygulaniyor()
    {
        // 12 ek `in` hareketi → `in` toplam 13 satır, defterde ise çok daha fazla hareket var.
        for (int i = 0; i < 12; i++)
        {
            _clock.Advance(60_000);
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_mat, 1m) }, Op(), branchId: _depoA);
        }

        var hepsi = Calistir(Istek());
        var yalnizGiris = Calistir(Istek(turler: new[] { "in" }));
        Assert.Equal(13, yalnizGiris.Rows.Count);
        Assert.True(hepsi.Rows.Count > yalnizGiris.Rows.Count);

        // Tavan 3 → FİLTRELENMİŞ kümeden 3 satır (tümü `in`). Bellekte süzülseydi en yeni 3 hareket
        // alınır, aralarında `in` olmayanlar da bulunur ya da hiç `in` kalmazdı.
        var kesik = _reports.Run(_tumSubeler, Rapor, Istek(turler: new[] { "in" }), maxRows: 3);
        Assert.Equal(3, kesik.Rows.Count);
        Assert.Equal(new[] { MovementTypeOptions.Label("in") }, Turler(kesik));
    }

    // ══════════════ 5. EXPORT + GERÇEK XLSX ══════════════

    private static (List<string> Headers, List<List<string>> Rows) XlsxOku(byte[] bytes, int satirSayisi, IReadOnlyList<bool> numeric)
    {
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        var headers = new List<string>();
        int c = 1;
        while (!string.IsNullOrEmpty(ws.Cell(1, c).GetString())) { headers.Add(ws.Cell(1, c).GetString()); c++; }

        var rows = new List<List<string>>();
        for (int r = 0; r < satirSayisi; r++)
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

    private static List<List<string>> TabloyuMetne(TableModel t)
        => t.Rows.Select(r => r.Select(h => h switch
        {
            null => "",
            NumCell n => n.Value.ToString("0.####"),
            _ => h.ToString() ?? "",
        }).ToList()).ToList();

    private void EkranVeXlsxAyni(ReportRequest istek, string senaryo)
    {
        var ekran = Calistir(istek);
        var (headers, xlsxRows) = XlsxOku(_excel.Export(ekran), ekran.Rows.Count, ekran.Numeric ?? Array.Empty<bool>());
        Assert.Equal(ekran.Headers, headers);
        var ekranRows = TabloyuMetne(ekran);
        Assert.Equal(ekranRows.Count, xlsxRows.Count);
        for (int i = 0; i < ekranRows.Count; i++)
            Assert.Equal(ekranRows[i], xlsxRows[i]);   // hücre hücre — senaryo: {senaryo}
    }

    /// <summary>21 — 🔴 Tür filtresi EXPORT'a da uygulanıyor ve XLSX ekranla BİREBİR aynı
    /// (gerçek dosya açılıp hücre hücre karşılaştırılıyor).
    ///
    /// ⚠️ KAPSAM DIŞI: "MovementType + Search" kombinasyonu bu artımda ÜRETİLMEDİ — `Search`
    /// filtresi STK-10b-2'nin kapsamıdır (talimatın açık isteği).</summary>
    [Fact]
    public void Tur_Filtresi_Export_a_Uygulaniyor_ve_XLSX_Ekranla_Ayni()
    {
        EkranVeXlsxAyni(Istek(), "filtresiz");
        EkranVeXlsxAyni(Istek(turler: new[] { "transfer" }), "tek tür: transfer");
        EkranVeXlsxAyni(Istek(turler: new[] { "usage", "usage_reverse" }), "çoklu tür: bakım");
        EkranVeXlsxAyni(Istek(turler: new[] { "usage" }, lokasyonlar: new[] { _depoB }), "tür + lokasyon");
        EkranVeXlsxAyni(Istek(turler: new[] { "opening" }, from: Gunes, to: 1_700_000_000_000L), "tür + tarih");
        EkranVeXlsxAyni(Istek(turler: new[] { "boyle_bir_tur_yok" }), "boş sonuç");
    }

    /// <summary>22 — Export'ta tür filtresi GERÇEKTEN uygulanıyor: filtresiz XLSX daha çok satır içeriyor.</summary>
    [Fact]
    public void Export_Filtresiz_ve_Filtreli_Farkli_Satir_Sayisi_Uretiyor()
    {
        var filtresiz = Calistir(Istek());
        var filtreli = Calistir(Istek(turler: new[] { "transfer" }));

        var (_, xFiltresiz) = XlsxOku(_excel.Export(filtresiz), filtresiz.Rows.Count, filtresiz.Numeric!);
        var (_, xFiltreli) = XlsxOku(_excel.Export(filtreli), filtreli.Rows.Count, filtreli.Numeric!);

        Assert.True(xFiltresiz.Count > xFiltreli.Count);
        Assert.Equal(2, xFiltreli.Count);   // transferin iki bacağı
    }

    /// <summary>23 — 🔒 ÇEVRİMDIŞI: tür filtreli rapor + export yerel SQLite'ta, HTTP olmadan çalışıyor
    /// (masaüstünün gerçek yolu). Seçenek listesi de sabittir → ağ gerekmez.</summary>
    [Fact]
    public void Cevrimdisi_Tur_Filtresi_ve_Export_Calisiyor()
    {
        // Masaüstü ReportsViewModel deseni: bayrak kapalıysa alan gönderilmez.
        var d = ReportCatalog.ByKey(Rapor)!;
        Assert.True(d.UsesMovementType);

        var istek = new ReportRequest(
            Executed: true,
            FromDate: d.UsesDate ? Gunes : null,
            ToDate: d.UsesDate ? Batis : null,
            LocationIds: null,
            MovementTypes: d.UsesMovementType ? new[] { "adjustment" } : null);

        var tablo = _reports.Run(_tumSubeler, Rapor, istek);
        Assert.NotEmpty(tablo.Rows);
        Assert.Equal(new[] { MovementTypeOptions.Label("adjustment") }, Turler(tablo));

        var (headers, rows) = XlsxOku(_excel.Export(tablo), tablo.Rows.Count, tablo.Numeric!);
        Assert.Equal(tablo.Headers, headers);
        Assert.Equal(TabloyuMetne(tablo), rows);

        // Seçenekler ağdan değil sabitten: 8 tür her koşulda hazır.
        Assert.Equal(8, MovementTypeOptions.All.Count);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
