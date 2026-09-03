using System.Text;
using ClosedXML.Excel;
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
/// STK-10b-3 (2026-08-12) — STOK HAREKETLERİ RAPORU: MALZEME FİLTRESİ + ARAMA İLE SEÇİM.
///
/// <b>Kapsam:</b> YALNIZ <c>Material</c>. Ekran bağlantıları + Web lokasyon B-1 hatası (10b-4) ve
/// STK-B2 (<c>stock_documents.note</c>'un aramaya girmesi) bu artımda YOKTUR — Search semantiği
/// bilinçli olarak DEĞİŞTİRİLMEDİ.
///
/// <b>Sözleşme:</b> <c>ReportRequest.MaterialIds</c> = <c>materials.id</c> LİSTESİ (diğer kimlik
/// filtreleriyle aynı desen). Arayüz bugün TEK malzeme seçtirir → 0/1 elemanlı gelir. Boş/null =
/// TÜM malzemeler.
///
/// 🔒 Filtre SQL'de (<c>sm.material_id IN (…)</c>) uygulanır ve <c>BranchScope</c>'u GENİŞLETEMEZ:
/// <c>WHERE kapsam AND lokasyon AND tür AND arama AND malzeme</c>.
///
/// ⚡ Seçenekler ÖNCEDEN YÜKLENMEZ: iki platform da MEVCUT malzeme arama desenini kullanır
/// (web <c>/api/materials?search=</c> · masaüstü yerel <c>Materials.List(term)</c>) →
/// <c>/api/reports/scope</c> BÜYÜMEZ. Bu, aşağıda kaynak taramasıyla da kilitlenmiştir.
///
/// Tamamı yerel SQLite üzerindedir; HTTP yoktur.
/// </summary>
public class StockMovementsMaterialFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly MaterialService _materials;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel = new();
    private readonly SessionContext _tumSubeler, _depoAOturum;
    private readonly string _depoA, _depoB, _filtre, _yag, _conta;

    private const string Rapor = "stock-movements";
    private const long Gunes = 1_699_000_000_000, Batis = 1_701_000_000_000;

    public StockMovementsMaterialFilterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_10b3_" + Guid.NewGuid().ToString("N") + ".db");
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
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _tumSubeler = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_tumSubeler, new NewBranch("Depo A"));
        _depoB = branches.Create(_tumSubeler, new NewBranch("Depo B"));
        _depoAOturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        _filtre = _materials.Create(_tumSubeler, new NewMaterial("MAT-FLT", "Yag filtresi"));
        _yag = _materials.Create(_tumSubeler, new NewMaterial("MAT-YAG", "Motor yagi"));
        _conta = _materials.Create(_tumSubeler, new NewMaterial("MAT-CNT", "Conta"));

        Senaryo();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Üç malzemeye yayılmış hareketler: açılış · faturalı giriş · ters kayıt · transfer.
    /// Malzeme filtresinin diğer filtrelerle kesişimini gerçekten sınayabilmek için hareketler
    /// bilinçli olarak İKİ depoya ve BİRDEN ÇOK türe dağıtıldı.</summary>
    private void Senaryo()
    {
        var acilis = _clock.UtcNow.ToUnixTimeMilliseconds();
        _opening.RecordOpening(_tumSubeler, _filtre, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _opening.RecordOpening(_tumSubeler, _yag, 50m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        AcilisAni = acilis;

        // Faturalı giriş (MAT-YAG) — Search testinde fatura no üzerinden aranır.
        var faturaliBelge = _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_yag, 10m) }, Op(),
            branchId: _depoA, invoiceNo: "FTR-MAT-999");
        _clock.Advance(60_000);

        // Ters kayıt — hareket satırının notu (`sm.note`) burada dolar.
        _stock.ReverseDocument(_tumSubeler, faturaliBelge.DocumentId, "iade gerekcesi");
        _clock.Advance(60_000);

        // Transfer (MAT-FLT) Depo A → Depo B: defterde İKİ satır (BranchScope testleri için).
        _stock.Transfer(_tumSubeler, _filtre, 5m, _depoA, _depoB, Op());
        _clock.Advance(60_000);

        // Yalnız Depo B'de duran üçüncü malzeme.
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_conta, 7m) }, Op(), branchId: _depoB);
    }

    /// <summary>İlk açılışın zaman damgası (Material + Date testinde kullanılır).</summary>
    private long AcilisAni { get; set; }

    private ReportRequest Istek(string[]? malzemeler = null, string? arama = null,
        string[]? lokasyonlar = null, string[]? turler = null, long? from = Gunes, long? to = Batis)
        => new(Executed: true, FromDate: from, ToDate: to,
               LocationIds: lokasyonlar, MovementTypes: turler, SearchText: arama, MaterialIds: malzemeler);

    private TableModel Calistir(ReportRequest req, SessionContext? s = null)
        => _reports.Run(s ?? _tumSubeler, Rapor, req);

    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new InvalidOperationException($"'{baslik}' kolonu yok.");
    }

    private static List<string> Kodlar(TableModel t)
    {
        var k = K(t, "Kod");
        return t.Rows.Select(r => (string?)r[k] ?? "").ToList();
    }

    // ══════════════ 1. KATALOG / SÖZLEŞME ══════════════

    /// <summary>1 — Katalog bayrağı YALNIZ bu raporda açık; sözleşme alanı LİSTE ve SONA eklenmiş.</summary>
    [Fact]
    public void Katalog_Material_Bayragi_Yalniz_Hareket_Raporunda()
    {
        var d = ReportCatalog.ByKey(Rapor)!;
        Assert.True(d.UsesMaterial);
        // ADR-182 (PK-G2=A): günlük ÖZET rapor da aynı filtre kümesini kullanır (tek filtre kaynağı).
        Assert.Equal(new[] { Rapor, "stock-movements-daily" }, ReportCatalog.All.Where(x => x.UsesMaterial).Select(x => x.Key));

        // Önceki bayraklar korunuyor (kapsam kayması nöbetçisi).
        Assert.True(d.UsesDate); Assert.True(d.UsesLocation);
        Assert.True(d.UsesMovementType); Assert.True(d.UsesSearch);

        // MaterialIds sözleşmede VAR ve bir LİSTE (skaler SearchText'ten farkı bilinçli).
        var p = typeof(ReportRequest).GetProperty("MaterialIds")!;
        Assert.Equal(typeof(IReadOnlyList<string>), p.PropertyType);
    }

    /// <summary>2 — 🔴 POZİSYONEL ARGÜMAN KAYMASI NÖBETÇİSİ. <c>MaterialIds</c> kaydın SON alanı
    /// olmalı: API uçları <c>ReportRequest</c>'i pozisyonel kuruyor; araya eklenirse LocationIds /
    /// MovementTypes / SearchText sessizce kayar ve filtreler yanlış alana düşer (10b-1'de yaşandı).</summary>
    [Fact]
    public void MaterialIds_Kaydin_SON_Alani()
    {
        var ctor = typeof(ReportRequest).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var p = ctor.GetParameters();
        Assert.Equal("SortKey", p[^1].Name);       // 2026-09-02 sıralama (en son eklenen — SIRA KAYDIRILDI, gevşetilmedi)
        Assert.Equal("ActivityTypes", p[^2].Name); // ADR-182
        Assert.Equal("PartyIds", p[^3].Name);      // G4-4
        Assert.Equal("MaterialIds", p[^4].Name);   // STK-10b-3
        Assert.Equal("SearchText", p[^5].Name);    // STK-10b-2
        Assert.Equal("MovementTypes", p[^6].Name); // STK-10b-1
        Assert.Equal("LocationIds", p[^7].Name);   // STK-06
    }

    // ══════════════ 2. TEMEL DAVRANIŞ ══════════════

    /// <summary>3 — Malzeme kimliğiyle YALNIZ o malzemenin hareketleri gelir.</summary>
    [Fact]
    public void Material_ile_Dogru_Hareketler_Geliyor()
    {
        var t = Calistir(Istek(malzemeler: new[] { _yag }));
        Assert.NotEmpty(t.Rows);
        Assert.All(Kodlar(t), k => Assert.Equal("MAT-YAG", k));

        var f = Calistir(Istek(malzemeler: new[] { _filtre }));
        Assert.NotEmpty(f.Rows);
        Assert.All(Kodlar(f), k => Assert.Equal("MAT-FLT", k));
    }

    /// <summary>4 — Boş / null Material → mevcut FİLTRESİZ davranış (hepsi).</summary>
    [Fact]
    public void Bos_Material_Filtresiz_Davranisi_Koruyor()
    {
        var filtresiz = Calistir(Istek());
        Assert.Equal(filtresiz.Rows.Count, Calistir(Istek(malzemeler: Array.Empty<string>())).Rows.Count);
        // Boş/yalnız-boşluk elemanlar ATILIR → yine filtre yok (hepsi).
        Assert.Equal(filtresiz.Rows.Count, Calistir(Istek(malzemeler: new[] { "", "   " })).Rows.Count);
        Assert.Contains("MAT-FLT", Kodlar(filtresiz));
        Assert.Contains("MAT-YAG", Kodlar(filtresiz));
        Assert.Contains("MAT-CNT", Kodlar(filtresiz));
    }

    /// <summary>5 — Var olmayan malzeme kimliği → BOŞ (sessizce "hepsi" olmaz — fail-closed).</summary>
    [Fact]
    public void Yanlis_Material_Id_Bos_Donuyor()
    {
        Assert.Empty(Calistir(Istek(malzemeler: new[] { "yok-boyle-malzeme" })).Rows);
        Assert.NotEmpty(Calistir(Istek()).Rows);   // filtresiz hâlâ dolu → "boş" filtreden geliyor
    }

    /// <summary>6 — Sözleşme LİSTE: birden çok malzeme verilirse hepsi gelir (arayüz bugün tek seçtirse de
    /// sunucu sözleşmesi diğer kimlik filtreleriyle aynıdır).</summary>
    [Fact]
    public void Coklu_Material_Id_Destekleniyor()
    {
        var t = Calistir(Istek(malzemeler: new[] { _yag, _conta }));
        var kodlar = Kodlar(t).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(new[] { "MAT-CNT", "MAT-YAG" }, kodlar);
        Assert.DoesNotContain("MAT-FLT", Kodlar(t));
    }

    // ══════════════ 3. 🔒 FİRMA / KAPSAM İZOLASYONU ══════════════

    /// <summary>7 — 🔒 Başka firmanın malzeme kimliği ERİŞİLEMEZ (aynı/benzer ad ve kod olsa bile).</summary>
    [Fact]
    public void Baska_Firmanin_Material_Idsi_Erisilemez()
    {
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('B','B',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var users = new UserService(_factory, _clock);
        var uidB = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var sB = new SessionContext(uidB, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        // BİLEREK aynı kod ve aynı ad: ayrımı yapan tek şey company_id olmalı.
        var yabanciMat = _materials.Create(sB, new NewMaterial("MAT-YAG", "Motor yagi"));
        var yabanciDepo = new BranchService(_factory, _clock).Create(sB, new NewBranch("Yabanci Depo"));
        _stock.ReceiveIn(sB, new[] { new StockLine(yabanciMat, 5m) }, Op(), branchId: yabanciDepo);

        // A firması, B'nin malzeme kimliğiyle filtrelerse HİÇBİR ŞEY görmez.
        Assert.Empty(Calistir(Istek(malzemeler: new[] { yabanciMat })).Rows);

        // Ters yön: B firması A'nın kimliğiyle filtrelerse de boş.
        Assert.Empty(_reports.Run(sB, Rapor, Istek(malzemeler: new[] { _yag })).Rows);

        // B kendi kimliğiyle kendi kaydını görüyor → test "her şey boş" diye geçmiyor.
        Assert.NotEmpty(_reports.Run(sB, Rapor, Istek(malzemeler: new[] { yabanciMat })).Rows);
    }

    /// <summary>8 — 🔴 Malzeme filtresi ŞUBE KAPSAMINI genişletmiyor: Depo A oturumu, transferin
    /// Depo B bacağını malzeme seçerek de göremez.</summary>
    [Fact]
    public void Material_BranchScope_Sinirini_Asmiyor()
    {
        // Yetkili kullanıcı transferin İKİ bacağını da görüyor.
        var yetkili = Calistir(Istek(malzemeler: new[] { _filtre }, turler: new[] { "transfer" }));
        Assert.Equal(2, yetkili.Rows.Count);

        // Depo A oturumu yalnız kendi bacağını görüyor — malzeme filtresi bunu değiştirmiyor.
        var kapsamli = Calistir(Istek(malzemeler: new[] { _filtre }, turler: new[] { "transfer" }), _depoAOturum);
        Assert.Single(kapsamli.Rows);
        Assert.Equal("Depo A", (string?)kapsamli.Rows[0][K(kapsamli, "Kaynak")]);
    }

    /// <summary>9 — 🔒 Kapsam dışı deponun malzemesi, malzeme filtresiyle de açığa çıkmıyor.</summary>
    [Fact]
    public void Kapsam_Disi_Deponun_Malzemesi_Gorunmuyor()
    {
        // MAT-CNT yalnız Depo B'de → yetkili görüyor, Depo A oturumu görmüyor.
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _conta })).Rows);
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _conta }), _depoAOturum).Rows);
    }

    // ══════════════ 4. FİLTRE KOMBİNASYONLARI (AND) ══════════════

    /// <summary>10 — Material + Date.</summary>
    [Fact]
    public void Material_ve_Date_Birlikte()
    {
        // Açılış anına kadar: MAT-FLT açılışı VAR, sonraki transfer YOK.
        var erken = Calistir(Istek(malzemeler: new[] { _filtre }, from: Gunes, to: AcilisAni));
        Assert.Single(erken.Rows);
        Assert.Equal("Açılış", (string?)erken.Rows[0][K(erken, "Tür")]);

        // MAT-YAG'ın açılışı bu anın SONRASINDA → boş.
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _yag }, from: Gunes, to: AcilisAni)).Rows);
    }

    /// <summary>11 — Material + Location.</summary>
    [Fact]
    public void Material_ve_Location_Birlikte()
    {
        // MAT-FLT'nin Depo B ile ilgisi YALNIZ transfer bacağı.
        var b = Calistir(Istek(malzemeler: new[] { _filtre }, lokasyonlar: new[] { _depoB }));
        Assert.Single(b.Rows);
        Assert.Equal("Depo B", (string?)b.Rows[0][K(b, "Hedef")]);

        // MAT-YAG Depo B'de hiç yok.
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _yag }, lokasyonlar: new[] { _depoB })).Rows);
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _yag }, lokasyonlar: new[] { _depoA })).Rows);
    }

    /// <summary>12 — Material + MovementType.</summary>
    [Fact]
    public void Material_ve_MovementType_Birlikte()
    {
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _yag }, turler: new[] { "opening" })).Rows);
        // MAT-YAG'ın transferi yok.
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _yag }, turler: new[] { "transfer" })).Rows);
        // MAT-FLT'nin ters kaydı yok (ters kayıt MAT-YAG belgesinde).
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, turler: new[] { "reverse" })).Rows);
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _yag }, turler: new[] { "reverse" })).Rows);
    }

    /// <summary>13 — Material + Search. İki filtre AND'lenir: arama malzeme filtresini GENİŞLETEMEZ.</summary>
    [Fact]
    public void Material_ve_Search_Birlikte()
    {
        // Fatura no MAT-YAG belgesinde → aynı malzemeyle eşleşiyor.
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _yag }, arama: "FTR-MAT-999")).Rows);
        // Aynı arama + BAŞKA malzeme → boş (arama, MAT-FLT'yi getiremez).
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, arama: "FTR-MAT-999")).Rows);
        // Ters kayıt notu da yalnız kendi malzemesinde bulunur.
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _yag }, arama: "iade gerekcesi")).Rows);
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, arama: "iade gerekcesi")).Rows);
    }

    /// <summary>14 — Material + Location + MovementType üçlüsü.</summary>
    [Fact]
    public void Material_Location_ve_MovementType_Uclusu()
    {
        var t = Calistir(Istek(malzemeler: new[] { _filtre }, lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" }));
        var satir = Assert.Single(t.Rows);
        Assert.Equal("MAT-FLT", (string?)satir[K(t, "Kod")]);
        Assert.Equal("Depo B", (string?)satir[K(t, "Hedef")]);

        // Aynı üçlü + YANLIŞ tür → boş (AND, OR değil).
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, lokasyonlar: new[] { _depoB }, turler: new[] { "opening" })).Rows);
    }

    /// <summary>15 — Material + Search + MovementType üçlüsü.</summary>
    [Fact]
    public void Material_Search_ve_MovementType_Uclusu()
    {
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _yag }, arama: "iade gerekcesi", turler: new[] { "reverse" })).Rows);
        // Not ters kayıtta → "opening" ile boş.
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _yag }, arama: "iade gerekcesi", turler: new[] { "opening" })).Rows);
    }

    /// <summary>16 — TÜM filtreler birlikte: Date + Location + MovementType + Search + Material.</summary>
    [Fact]
    public void Tum_Filtreler_Birlikte()
    {
        var t = Calistir(Istek(malzemeler: new[] { _filtre }, arama: "MAT-FLT",
                               lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" },
                               from: Gunes, to: Batis));
        var satir = Assert.Single(t.Rows);
        Assert.Equal("MAT-FLT", (string?)satir[K(t, "Kod")]);
        Assert.Equal("Depo B", (string?)satir[K(t, "Hedef")]);

        // Tek bir filtreyi bozmak sonucu BOŞALTIYOR → beşi de gerçekten uygulanıyor.
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _yag }, arama: "MAT-FLT",
                                    lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" })).Rows);
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, arama: "yok-boyle",
                                    lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" })).Rows);
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, arama: "MAT-FLT",
                                    lokasyonlar: new[] { _depoA }, turler: new[] { "reverse" })).Rows);
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _filtre }, arama: "MAT-FLT",
                                    lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" },
                                    from: Gunes, to: Gunes + 1)).Rows);
    }

    /// <summary>17 — Sonuçsuz filtre: doğru malzeme + hiç eşleşmeyen depo → temiz boş sonuç
    /// (başlıklar duruyor, toplam satırı yok, hata yok).</summary>
    [Fact]
    public void Sonucsuz_Filtre_Temiz_Bos_Donuyor()
    {
        var t = Calistir(Istek(malzemeler: new[] { _conta }, lokasyonlar: new[] { _depoA }));
        Assert.Empty(t.Rows);
        Assert.NotEmpty(t.Headers);
        Assert.Null(t.TotalRow);
    }

    // ══════════════ 5. SQL TARAFI + LIMIT SIRASI ══════════════

    /// <summary>18 — 🔴 Malzeme filtresi SQL'de: tavan FİLTRELENMİŞ küme üzerine iniyor. Bellekte
    /// süzülseydi tavan önce tüm defteri keser, malzemeye uymayan yeni satırlar sonucu boşaltırdı.</summary>
    [Fact]
    public void Material_LIMIT_ten_ONCE_SQL_de_Uygulaniyor()
    {
        // Aranan malzemeye UYMAYAN 12 YENİ hareket üret.
        for (int i = 0; i < 12; i++)
        {
            _clock.Advance(60_000);
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_conta, 1m) }, Op(), branchId: _depoB);
        }

        var malzemeli = Calistir(Istek(malzemeler: new[] { _yag }));
        Assert.NotEmpty(malzemeli.Rows);
        Assert.All(Kodlar(malzemeli), k => Assert.Equal("MAT-YAG", k));

        // Tavan 1 → MALZEMEYE UYAN en yeni satır gelmeli (uymayan yeni kayıtlar değil).
        var kesik = _reports.Run(_tumSubeler, Rapor, Istek(malzemeler: new[] { _yag }), maxRows: 1);
        Assert.Single(kesik.Rows);
        Assert.Equal("MAT-YAG", Kodlar(kesik)[0]);
    }

    // ══════════════ 6. EXPORT + GERÇEK XLSX ══════════════

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

    private void EkranVeXlsxAyni(ReportRequest istek)
    {
        var ekran = Calistir(istek);
        var (headers, xlsx) = XlsxOku(_excel.Export(ekran), ekran.Rows.Count, ekran.Numeric ?? Array.Empty<bool>());
        Assert.Equal(ekran.Headers, headers);
        var beklenen = TabloyuMetne(ekran);
        Assert.Equal(beklenen.Count, xlsx.Count);
        for (int i = 0; i < beklenen.Count; i++) Assert.Equal(beklenen[i], xlsx[i]);
    }

    /// <summary>19 — 🔴 Malzeme filtresi EXPORT'a da uygulanıyor; XLSX ekranla HÜCRE HÜCRE aynı.
    /// Talimattaki 10 kombinasyon.</summary>
    [Fact]
    public void Material_Export_a_Uygulaniyor_ve_XLSX_Ekranla_Ayni()
    {
        EkranVeXlsxAyni(Istek());                                                                     // 1 filtresiz
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _yag }));                                           // 2 yalnız Material
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _filtre }, from: Gunes, to: AcilisAni));            // 3 Material + Date
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _filtre }, lokasyonlar: new[] { _depoB }));         // 4 Material + Location
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _yag }, turler: new[] { "reverse" }));              // 5 Material + MovementType
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _yag }, arama: "FTR-MAT-999"));                     // 6 Material + Search
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _filtre }, lokasyonlar: new[] { _depoB },
                              turler: new[] { "transfer" }));                                         // 7 Material + Location + Tür
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _yag }, arama: "iade gerekcesi",
                              turler: new[] { "reverse" }));                                          // 8 Material + Search + Tür
        EkranVeXlsxAyni(Istek(malzemeler: new[] { _filtre }, lokasyonlar: new[] { _depoA },
                              from: Gunes, to: Batis));                                               // 9 Material + Date + Location
        EkranVeXlsxAyni(Istek(malzemeler: new[] { "yok-boyle-malzeme" }));                            // 10 sonuçsuz Material
    }

    /// <summary>20 — Export'ta malzeme filtresi GERÇEKTEN uygulanıyor: beklenen satırlar birebir.</summary>
    [Fact]
    public void Export_Filtresiz_ve_Malzemeli_Beklenen_Satirlari_Iceriyor()
    {
        var filtresiz = Calistir(Istek());
        var malzemeli = Calistir(Istek(malzemeler: new[] { _yag }));

        var (_, xF) = XlsxOku(_excel.Export(filtresiz), filtresiz.Rows.Count, filtresiz.Numeric!);
        var (_, xM) = XlsxOku(_excel.Export(malzemeli), malzemeli.Rows.Count, malzemeli.Numeric!);

        Assert.True(xF.Count > xM.Count);
        Assert.NotEmpty(xM);

        // Satır sayısı karşılaştırması TEK BAŞINA yeterli değil: filtreli XLSX'in HER satırı
        // yalnız seçilen malzemeye ait olmalı ve filtresiz XLSX'te de birebir bulunmalı.
        var kodKolonu = filtresiz.Headers.ToList().IndexOf("Kod");
        Assert.All(xM, r => Assert.Equal("MAT-YAG", r[kodKolonu]));
        Assert.All(xM, r => Assert.Contains(xF, f => f.SequenceEqual(r)));
        // Filtresizde başka malzemeler de VAR (yani fark gerçekten filtreden geliyor).
        Assert.Contains(xF, f => f[kodKolonu] == "MAT-FLT");
    }

    // ══════════════ 7. ÇEVRİMDIŞI (masaüstü yolu) ══════════════

    /// <summary>21 — 🔒 ÇEVRİMDIŞI: malzeme ARAMASI + malzemeli rapor + export yerel SQLite'ta,
    /// HTTP olmadan. Masaüstünün gerçek yolu: önce <c>Materials.List(term)</c> ile aranır, seçilen
    /// kimlik <c>BuildTable</c> deseniyle isteğe konur.</summary>
    [Fact]
    public void Cevrimdisi_Malzeme_Aramasi_ve_Raporu_Calisiyor()
    {
        var d = ReportCatalog.ByKey(Rapor)!;
        Assert.True(d.UsesMaterial);

        // Masaüstü seçicisinin kullandığı MEVCUT arama deseni — ağ yok.
        var bulunan = _materials.List(_tumSubeler, new PageRequest { Limit = 30 }, "MAT-YAG").Items;
        var secilen = Assert.Single(bulunan);
        Assert.Equal(_yag, secilen.Id);

        var istek = new ReportRequest(
            Executed: true,
            FromDate: d.UsesDate ? Gunes : null,
            ToDate: d.UsesDate ? Batis : null,
            LocationIds: null,
            MovementTypes: null,
            SearchText: null,
            MaterialIds: d.UsesMaterial ? new[] { secilen.Id } : null);

        var tablo = _reports.Run(_tumSubeler, Rapor, istek);
        Assert.NotEmpty(tablo.Rows);
        Assert.All(Kodlar(tablo), k => Assert.Equal("MAT-YAG", k));

        var (headers, rows) = XlsxOku(_excel.Export(tablo), tablo.Rows.Count, tablo.Numeric!);
        Assert.Equal(tablo.Headers, headers);
        Assert.Equal(TabloyuMetne(tablo), rows);
    }

    // ══════════════ 8. UI KAYNAK TARAMASI (parite + performans) ══════════════

    private static string Kaynak(params string[] parcalar)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DepoWise.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var yol = Path.Combine(new[] { dir!.FullName }.Concat(parcalar).ToArray());
        Assert.True(File.Exists(yol), $"Kaynak bulunamadı: {yol}");
        return File.ReadAllText(yol, Encoding.UTF8);
    }

    /// <summary>22 — 🔴 PERFORMANS NÖBETÇİSİ: malzeme listesi rapor KAPSAMINA (scope) eklenmedi.
    /// Eklenirse rapor her açıldığında binlerce malzeme indirilirdi (talimat §2/§12).</summary>
    [Fact]
    public void Rapor_Kapsamina_Malzeme_Listesi_Eklenmedi()
    {
        var api = Kaynak("src", "DepoWise.Api", "Program.cs");
        var i = api.IndexOf("app.MapGet(\"/api/reports/scope\"", StringComparison.Ordinal);
        Assert.True(i >= 0, "/api/reports/scope ucu bulunamadı.");
        var son = api.IndexOf("}).RequireAuthorization();", i, StringComparison.Ordinal);
        var govde = api[i..son];

        Assert.DoesNotContain("FROM materials", govde, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("materials =", govde, StringComparison.Ordinal);

        // Web de scope'tan malzeme OKUMUYOR (Read(doc, "materials", …) yok).
        var razor = Kaynak("src", "DepoWise.Web", "Components", "Pages", "Reports.razor");
        Assert.DoesNotContain("Read(doc, \"materials\"", razor, StringComparison.Ordinal);
    }

    /// <summary>23 — 🔴 PARİTE: iki platform da MEVCUT arama desenini kullanıyor ve rapor isteğine
    /// AYNI alandan (materials.id → MaterialIds) besleniyor. Yeni uç/yeni mimari açılmadı.</summary>
    [Fact]
    public void Web_ve_Masaustu_Ayni_Kimlik_Alanindan_Besleniyor()
    {
        var razor = Kaynak("src", "DepoWise.Web", "Components", "Pages", "Reports.razor");
        var vm = Kaynak("src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs");

        // Web: MEVCUT /api/materials?search= ucu (yeni uç YOK) + autocomplete.
        Assert.Contains("/api/materials", razor, StringComparison.Ordinal);
        Assert.Contains("MudAutocomplete", razor, StringComparison.Ordinal);
        Assert.Contains("_materialSel.Id", razor, StringComparison.Ordinal);

        // Masaüstü: MEVCUT yerel arama (Materials.List) + seçilen kimlik.
        Assert.Contains("DesktopServices.Materials.List(", vm, StringComparison.Ordinal);
        Assert.Contains("PickedMaterial.Id", vm, StringComparison.Ordinal);
        Assert.Contains("MaterialIds:", vm, StringComparison.Ordinal);

        // Masaüstünde malzeme için HTTP/ağ çağrısı YOK (çevrimdışı kuralı).
        Assert.DoesNotContain("HttpClient", vm, StringComparison.Ordinal);
    }

    // ══════════════ 9. REGRESYON ══════════════

    /// <summary>24 — Önceki filtreler (Date/Location/MovementType/Search) BOZULMADI.</summary>
    [Fact]
    public void Onceki_Filtreler_Bozulmadi()
    {
        Assert.NotEmpty(Calistir(Istek(lokasyonlar: new[] { _depoB })).Rows);          // Location
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "transfer" })).Rows);           // MovementType
        Assert.NotEmpty(Calistir(Istek(arama: "FTR-MAT-999")).Rows);                   // Search
        Assert.Empty(Calistir(Istek(from: Batis - 1, to: Batis)).Rows);                // Date

        // Kaynak/Hedef semantiği + transferin İKİ satırı.
        var t = Calistir(Istek(turler: new[] { "transfer" }));
        Assert.Equal(2, t.Rows.Count);
        Assert.Contains(t.Rows, r => (string?)r[K(t, "Hedef")] == "Depo B");
    }

    /// <summary>25 — 🔒 STK-B2 KARARSIZ: Search semantiği bu artımda DEĞİŞMEDİ. Belge notu
    /// (<c>stock_documents.note</c>) hâlâ aramada değildir; malzeme filtresi bunu değiştirmez.</summary>
    [Fact]
    public void Search_Semantigi_Degismedi_Belge_Notu_Hala_Aramada_Yok()
    {
        _clock.Advance(60_000);
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_conta, 3m) }, Op(),
            branchId: _depoB, note: "belge notu aramada yok");

        Assert.Empty(Calistir(Istek(arama: "belge notu aramada yok")).Rows);
        Assert.Empty(Calistir(Istek(malzemeler: new[] { _conta }, arama: "belge notu aramada yok")).Rows);
        // Aynı hareket malzeme filtresiyle GÖRÜNÜYOR → boşluk aramadan geliyor, kayıttan değil.
        Assert.NotEmpty(Calistir(Istek(malzemeler: new[] { _conta })).Rows);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
