using ClosedXML.Excel;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-10b-2 (2026-08-11) — STOK HAREKETLERİ RAPORU: SERBEST METİN ARAMA (ADR-104 / KARAR-10).
///
/// <b>Kapsam:</b> YALNIZ <c>Search</c>. <c>Material</c> (10b-3) ve ekran bağlantıları + B-1 (10b-4)
/// bu artımda YOKTUR.
///
/// <b>Semantik mevcut ekrandan AYNEN taşındı</b> (yeniden tasarlanmadı):
/// <c>(m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q)</c>
/// · kalıp <c>"%" + Trim() + "%"</c> · boş/yalnız-boşluk → FİLTRE YOK.
///
/// 🔒 Arama SUNUCU/SQL tarafında uygulanır ve <c>BranchScope</c>'u GENİŞLETEMEZ
/// (<c>WHERE kapsam AND lokasyon AND tür AND arama</c>).
///
/// Tamamı yerel SQLite üzerindedir; HTTP yoktur.
/// </summary>
public class StockMovementsSearchFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel = new();
    private readonly SessionContext _tumSubeler, _depoAOturum;
    private readonly string _depoA, _depoB, _filtre, _yag;

    private const string Rapor = "stock-movements";
    private const long Gunes = 1_699_000_000_000, Batis = 1_701_000_000_000;

    public StockMovementsSearchFilterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_10b2_" + Guid.NewGuid().ToString("N") + ".db");
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
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _tumSubeler = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _depoA = branches.Create(_tumSubeler, new NewBranch("Depo A"));
        _depoB = branches.Create(_tumSubeler, new NewBranch("Depo B"));
        _depoAOturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        _filtre = materials.Create(_tumSubeler, new NewMaterial("ARA-FLT", "Yag filtresi"));
        _yag = materials.Create(_tumSubeler, new NewMaterial("ARA-YAG", "Motor yagi"));

        Senaryo();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Aranabilir 5 alanın her birini temsil eden hareketler üretir.
    ///
    /// ⚠️ <b>KODDAN DOĞRULANAN MEVCUT DAVRANIŞ (§ bulgu):</b> giriş/çıkış/transfer/sayımda kullanıcının
    /// yazdığı not <c>stock_documents.note</c>'a gider; <c>ApplyLine</c> hareket satırının
    /// <c>note</c>'unu <b>NULL</b> yazar. Mevcut arama ise <c>sm.note</c>'a bakar (<c>d.note</c>'a DEĞİL).
    /// Yani "not" araması bugün YALNIZ hareket satırında not bulunan yolları bulur:
    /// <b>ters kayıt gerekçesi</b> ve bakım tüketimi. Bu davranış STK-10b-2'de <b>değiştirilmedi</b>
    /// (semantik birebir taşındı); bulgu rapora yazıldı, kararı kullanıcıya bırakıldı.</summary>
    private void Senaryo()
    {
        // 1) KOD + AD üzerinden aranacak açılışlar (Depo A)
        _opening.RecordOpening(_tumSubeler, _filtre, 100m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _opening.RecordOpening(_tumSubeler, _yag, 50m, Op(), branchId: _depoA);
        _clock.Advance(60_000);

        // 2) BELGE NOTU olan giriş — belge notu `d.note`'a yazılır (aramada YOK, mevcut davranış).
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_filtre, 20m) }, Op(),
            branchId: _depoA, note: "belge notu satirdaki nota gitmez");
        _clock.Advance(60_000);

        // 3) FATURA NO üzerinden aranacak giriş (+ ters kaydı için belge kimliği)
        var faturaliBelge = _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_yag, 10m) }, Op(),
            branchId: _depoA, invoiceNo: "FTR-2026-777");
        _clock.Advance(60_000);

        // 4) HAREKET NOTU: ters kayıt gerekçesi `sm.note`'a yazılır → "not" araması bunu bulur.
        _stock.ReverseDocument(_tumSubeler, faturaliBelge.DocumentId, "acil iade gerekcesi");
        _clock.Advance(60_000);

        // 5) Depo B'ye transfer (BranchScope + lokasyon testleri için)
        _stock.Transfer(_tumSubeler, _filtre, 5m, _depoA, _depoB, Op());
    }

    private ReportRequest Istek(string? arama = null, string[]? lokasyonlar = null,
        string[]? turler = null, long? from = Gunes, long? to = Batis)
        => new(Executed: true, FromDate: from, ToDate: to,
               LocationIds: lokasyonlar, MovementTypes: turler, SearchText: arama);

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

    /// <summary>Bir hareketin BELGE NO'sunu defterden okur (arama testinde kullanmak için).</summary>
    private string IlkBelgeNo()
    {
        var t = Calistir(Istek());
        var k = K(t, "Belge No");
        return t.Rows.Select(r => (string?)r[k] ?? "").First(x => x.Length > 0);
    }

    // ══════════════ 1. BEŞ ALANIN HER BİRİ ══════════════

    /// <summary>1 — MALZEME KODU ile arama.</summary>
    [Fact]
    public void Kod_ile_Arama_Calisiyor()
    {
        var t = Calistir(Istek(arama: "ARA-YAG"));
        Assert.NotEmpty(t.Rows);
        Assert.All(Kodlar(t), k => Assert.Equal("ARA-YAG", k));
    }

    /// <summary>2 — MALZEME ADI ile arama.</summary>
    [Fact]
    public void Malzeme_Adi_ile_Arama_Calisiyor()
    {
        var t = Calistir(Istek(arama: "Motor yagi"));
        Assert.NotEmpty(t.Rows);
        Assert.All(Kodlar(t), k => Assert.Equal("ARA-YAG", k));
    }

    /// <summary>3 — NOT ile arama — HAREKET satırının notu (`sm.note`). Bugün bunu ters kayıt
    /// gerekçesi doldurur; giriş/çıkış belgesinin notu `d.note`'dadır ve aramada YOKTUR (mevcut davranış).</summary>
    [Fact]
    public void Not_ile_Arama_Calisiyor()
    {
        var t = Calistir(Istek(arama: "acil iade gerekcesi"));
        var satir = Assert.Single(t.Rows);
        Assert.Equal("ARA-YAG", (string?)satir[K(t, "Kod")]);
        Assert.Contains("acil iade", (string?)satir[K(t, "Açıklama")] ?? "", StringComparison.Ordinal);
    }

    /// <summary>3b — 🔴 MEVCUT DAVRANIŞ KİLİTLENDİ: BELGE notu (`stock_documents.note`) aramada YOK.
    /// STK-10b-2 semantiği birebir taşıdı, genişletmedi. Bulgu rapora yazıldı — kararı kullanıcının.</summary>
    [Fact]
    public void Belge_Notu_Aramada_YOK_Mevcut_Davranis()
    {
        Assert.Empty(Calistir(Istek(arama: "belge notu satirdaki")).Rows);
        // Mevcut ekran da aynı sonucu veriyor → semantik kaymadı.
        Assert.Empty(_stock.SearchMovements(_tumSubeler, Gunes, Batis, "belge notu satirdaki", 5000));
    }

    /// <summary>4 — FATURA NO ile arama.</summary>
    [Fact]
    public void Fatura_No_ile_Arama_Calisiyor()
    {
        // Ters kayıt AYNI belgeye bağlıdır → fatura no ikisinde de eşleşir (mevcut ve doğru davranış).
        var t = Calistir(Istek(arama: "FTR-2026-777"));
        Assert.Equal(2, t.Rows.Count);
        Assert.All(t.Rows, r => Assert.Equal("FTR-2026-777", (string?)r[K(t, "Fatura No")]));
        Assert.All(t.Rows, r => Assert.Equal("ARA-YAG", (string?)r[K(t, "Kod")]));
    }

    /// <summary>5 — BELGE NO ile arama (defterden okunan gerçek belge numarası).</summary>
    [Fact]
    public void Belge_No_ile_Arama_Calisiyor()
    {
        var belgeNo = IlkBelgeNo();
        var t = Calistir(Istek(arama: belgeNo));
        Assert.NotEmpty(t.Rows);
        Assert.All(t.Rows, r => Assert.Equal(belgeNo, (string?)r[K(t, "Belge No")]));
    }

    /// <summary>6 — Beş alanın TAMAMI aynı OR grubunda: farklı alanlardan eşleşen aramalar çalışıyor
    /// ve birbirini engellemiyor.</summary>
    [Fact]
    public void Bes_Alanin_Tamami_Ayni_OR_Grubunda()
    {
        Assert.NotEmpty(Calistir(Istek(arama: "ARA-")).Rows);          // kod
        Assert.NotEmpty(Calistir(Istek(arama: "filtresi")).Rows);      // ad
        Assert.NotEmpty(Calistir(Istek(arama: "acil iade")).Rows);     // not (sm.note)
        Assert.NotEmpty(Calistir(Istek(arama: "FTR-")).Rows);          // fatura no
        Assert.NotEmpty(Calistir(Istek(arama: IlkBelgeNo())).Rows);    // belge no
    }

    // ══════════════ 2. SINIR DAVRANIŞLARI (mevcut semantik) ══════════════

    /// <summary>7 — Eşleşme yoksa BOŞ sonuç (hata değil).</summary>
    [Fact]
    public void Eslesme_Yoksa_Bos_Sonuc()
    {
        var t = Calistir(Istek(arama: "boyle-bir-sey-yok-12345"));
        Assert.Empty(t.Rows);
        Assert.NotEmpty(t.Headers);
        Assert.Null(t.TotalRow);
    }

    /// <summary>8 — null / boş / YALNIZ BOŞLUK → filtre YOK (mevcut `SearchMovements` semantiği).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Bos_veya_Bosluk_Arama_Filtre_Uygulamiyor(string? arama)
    {
        var filtresiz = Calistir(Istek()).Rows.Count;
        Assert.Equal(filtresiz, Calistir(Istek(arama: arama)).Rows.Count);
    }

    /// <summary>9 — Arama metni TRIM ediliyor (mevcut davranış: <c>"%" + Trim() + "%"</c>).</summary>
    [Fact]
    public void Arama_Metni_Trim_Ediliyor()
    {
        var duz = Calistir(Istek(arama: "ARA-YAG")).Rows.Count;
        Assert.True(duz > 0);
        Assert.Equal(duz, Calistir(Istek(arama: "   ARA-YAG   ")).Rows.Count);
    }

    /// <summary>10 — Kısmi eşleşme (içerir) çalışıyor — kalıp <c>%…%</c>.</summary>
    [Fact]
    public void Kismi_Eslesme_Icerir_Calisiyor()
    {
        Assert.NotEmpty(Calistir(Istek(arama: "RA-YA")).Rows);   // ARA-YAG'ın ortası
    }

    /// <summary>11 — BÜYÜK/KÜÇÜK HARF davranışı mevcut ekranla AYNI olmalı: rapor ile
    /// <c>StockService.SearchMovements</c> aynı girdide aynı sonucu vermeli.
    ///
    /// ⚠️ Mutlak bir büyük/küçük harf iddiası yapılmıyor: <c>LIKE</c>'ın davranışı LEHÇEYE bağlıdır
    /// (SQLite ASCII'de duyarsız, PostgreSQL duyarlı). Test, davranışın ne olduğunu değil
    /// <b>ikisinin AYNI olduğunu</b> kilitler — semantik taşındı, değiştirilmedi.</summary>
    [Theory]
    [InlineData("ara-yag")]
    [InlineData("ARA-YAG")]
    [InlineData("Ara-Yag")]
    [InlineData("motor")]
    [InlineData("MOTOR")]
    public void Buyuk_Kucuk_Harf_Davranisi_Mevcut_Ekranla_AYNI(string arama)
    {
        var ekran = _stock.SearchMovements(_tumSubeler, Gunes, Batis, arama, 5000).Count;
        var rapor = Calistir(Istek(arama: arama)).Rows.Count;
        Assert.Equal(ekran, rapor);
    }

    /// <summary>12 — Rapor ile mevcut ekran aynı girdide AYNI kayıt kümesini döndürüyor
    /// (semantik birebir taşındı — beş alanın hepsinde).</summary>
    [Theory]
    [InlineData("ARA-")]
    [InlineData("filtresi")]
    [InlineData("acil iade")]
    [InlineData("FTR-2026")]
    [InlineData("yok-boyle")]
    public void Rapor_ve_Mevcut_Ekran_Ayni_Kumeyi_Doner(string arama)
    {
        var ekran = _stock.SearchMovements(_tumSubeler, Gunes, Batis, arama, 5000);
        var rapor = Calistir(Istek(arama: arama));
        Assert.Equal(ekran.Count, rapor.Rows.Count);
    }

    // ══════════════ 3. DİĞER FİLTRELERLE KOMBİNASYON ══════════════

    /// <summary>13 — Search + Date.</summary>
    [Fact]
    public void Search_ve_Date_Birlikte()
    {
        var acilisAni = 1_700_000_000_000L;
        Assert.NotEmpty(Calistir(Istek(arama: "ARA-FLT", from: Gunes, to: acilisAni)).Rows);
        Assert.Empty(Calistir(Istek(arama: "acil iade", from: Gunes, to: acilisAni)).Rows);   // ters kayıt sonra
    }

    /// <summary>14 — Search + Location.</summary>
    [Fact]
    public void Search_ve_Location_Birlikte()
    {
        // Transfer ARA-FLT malzemesinde → kod aramasıyla Depo B bacağı bulunur.
        Assert.NotEmpty(Calistir(Istek(arama: "ARA-FLT", lokasyonlar: new[] { _depoB })).Rows);
        // Fatura kaydı Depo A'da → Depo B filtresinde yok.
        Assert.Empty(Calistir(Istek(arama: "FTR-2026-777", lokasyonlar: new[] { _depoB })).Rows);
        Assert.NotEmpty(Calistir(Istek(arama: "FTR-2026-777", lokasyonlar: new[] { _depoA })).Rows);
    }

    /// <summary>15 — Search + MovementType.</summary>
    [Fact]
    public void Search_ve_MovementType_Birlikte()
    {
        Assert.NotEmpty(Calistir(Istek(arama: "ARA-FLT", turler: new[] { "opening" })).Rows);
        Assert.Empty(Calistir(Istek(arama: "acil iade", turler: new[] { "opening" })).Rows);   // not ters kayıtta
        Assert.NotEmpty(Calistir(Istek(arama: "acil iade", turler: new[] { "reverse" })).Rows);
    }

    /// <summary>16 — Search + Location + MovementType üçlüsü.</summary>
    [Fact]
    public void Search_Location_ve_MovementType_Uclusu()
    {
        var t = Calistir(Istek(arama: "ARA-FLT", lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" }));
        var satir = Assert.Single(t.Rows);
        Assert.Equal("ARA-FLT", (string?)satir[K(t, "Kod")]);
        Assert.Equal("Depo B", (string?)satir[K(t, "Hedef")]);

        // Aynı arama + YANLIŞ tür → boş (filtreler AND'leniyor, OR değil).
        Assert.Empty(Calistir(Istek(arama: "ARA-FLT", lokasyonlar: new[] { _depoB }, turler: new[] { "opening" })).Rows);
    }

    // ══════════════ 4. 🔒 BranchScope × Search ══════════════

    /// <summary>17 — 🔴 Arama, şube kapsamını GENİŞLETMİYOR: Depo A oturumu, Depo B'ye ait transfer
    /// giriş bacağını arayarak da göremez.</summary>
    [Fact]
    public void Search_BranchScope_Sinirini_Asmiyor()
    {
        // Yetkili kullanıcı transferin İKİ bacağını da görüyor (arama + tür ile yalıtıldı).
        var yetkili = Calistir(Istek(arama: "ARA-FLT", turler: new[] { "transfer" }));
        Assert.Equal(2, yetkili.Rows.Count);

        // Depo A oturumu yalnız kapsamındaki bacağı görüyor — arama bunu değiştirmiyor.
        var kapsamli = Calistir(Istek(arama: "ARA-FLT", turler: new[] { "transfer" }), _depoAOturum);
        Assert.Single(kapsamli.Rows);
        Assert.Equal("Depo A", (string?)kapsamli.Rows[0][K(kapsamli, "Kaynak")]);
    }

    /// <summary>18 — 🔒 Arama + yetkisiz depo filtresi → BOŞ (yetki aşılamaz).</summary>
    [Fact]
    public void Search_Yetkisiz_Depo_ile_Bos_Doner()
    {
        Assert.NotEmpty(Calistir(Istek(arama: "ARA-FLT", lokasyonlar: new[] { _depoB })).Rows);
        Assert.Empty(Calistir(Istek(arama: "ARA-FLT", lokasyonlar: new[] { _depoB }), _depoAOturum).Rows);
    }

    /// <summary>19 — 🔒 FİRMA İZOLASYONU: başka firmanın kaydı arama ile de görünmez.</summary>
    [Fact]
    public void Search_Baska_Firmanin_Kaydini_Gostermiyor()
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
        var matB = new MaterialService(_factory, _clock).Create(sB, new NewMaterial("ARA-YABANCI", "Yabanci malzeme"));
        var depoBsirket = new BranchService(_factory, _clock).Create(sB, new NewBranch("Yabanci Depo"));
        _stock.ReceiveIn(sB, new[] { new StockLine(matB, 5m) }, Op(), branchId: depoBsirket, note: "aktarimi");

        // "ARA-" A firmasının malzemelerine de uyuyor ama yabancı kod GELMEMELİ.
        var t = Calistir(Istek(arama: "ARA-"));
        Assert.DoesNotContain("ARA-YABANCI", Kodlar(t));
        Assert.Empty(Calistir(Istek(arama: "ARA-YABANCI")).Rows);
    }

    // ══════════════ 5. SQL TARAFI + LIMIT SIRASI ══════════════

    /// <summary>20 — 🔴 Arama SQL'de uygulanıyor: tavan FİLTRELENMİŞ küme üzerine iniyor.
    /// Bellekte süzülseydi, tavan önce tüm hareketleri kesip aramaya uymayan satırlar kalırdı.</summary>
    [Fact]
    public void Arama_LIMIT_ten_ONCE_SQL_de_Uygulaniyor()
    {
        // "ARA-YAG" dışında 12 yeni hareket üret (aramaya UYMAYAN, daha YENİ kayıtlar).
        for (int i = 0; i < 12; i++)
        {
            _clock.Advance(60_000);
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_filtre, 1m) }, Op(), branchId: _depoA);
        }

        var aramali = Calistir(Istek(arama: "ARA-YAG"));
        Assert.NotEmpty(aramali.Rows);
        Assert.All(Kodlar(aramali), k => Assert.Equal("ARA-YAG", k));

        // Tavan 1 → aramaya UYAN en yeni satır gelmeli (uymayan yeni kayıtlar değil).
        var kesik = _reports.Run(_tumSubeler, Rapor, Istek(arama: "ARA-YAG"), maxRows: 1);
        Assert.Single(kesik.Rows);
        Assert.Equal("ARA-YAG", Kodlar(kesik)[0]);
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

    /// <summary>21 — 🔴 Arama EXPORT'a da uygulanıyor; XLSX ekranla hücre hücre AYNI.</summary>
    [Fact]
    public void Arama_Export_a_Uygulaniyor_ve_XLSX_Ekranla_Ayni()
    {
        EkranVeXlsxAyni(Istek());                                                        // filtresiz
        EkranVeXlsxAyni(Istek(arama: "ARA-YAG"));                                        // yalnız arama (kod)
        EkranVeXlsxAyni(Istek(arama: "acil iade"));                                      // arama (hareket notu)
        EkranVeXlsxAyni(Istek(arama: "FTR-2026-777"));                                   // arama (fatura no)
        EkranVeXlsxAyni(Istek(arama: "ARA-FLT", lokasyonlar: new[] { _depoB }));         // arama + lokasyon
        EkranVeXlsxAyni(Istek(arama: "ARA-FLT", turler: new[] { "opening" }));            // arama + tür
        EkranVeXlsxAyni(Istek(arama: "yok-boyle-bir-sey"));                               // arama → boş sonuç
    }

    /// <summary>22 — Export'ta arama GERÇEKTEN uygulanıyor: filtresiz XLSX daha çok satır içeriyor.</summary>
    [Fact]
    public void Export_Filtresiz_ve_Aramali_Farkli_Satir_Sayisi()
    {
        var filtresiz = Calistir(Istek());
        var aramali = Calistir(Istek(arama: "ARA-YAG"));

        var (_, xF) = XlsxOku(_excel.Export(filtresiz), filtresiz.Rows.Count, filtresiz.Numeric!);
        var (_, xA) = XlsxOku(_excel.Export(aramali), aramali.Rows.Count, aramali.Numeric!);

        Assert.True(xF.Count > xA.Count);
        Assert.NotEmpty(xA);
    }

    /// <summary>23 — 🔒 ÇEVRİMDIŞI: aramalı rapor + export yerel SQLite'ta, HTTP olmadan
    /// (masaüstünün gerçek yolu). Masaüstü `BuildTable` deseniyle istek kurulur.</summary>
    [Fact]
    public void Cevrimdisi_Arama_ve_Export_Calisiyor()
    {
        var d = ReportCatalog.ByKey(Rapor)!;
        Assert.True(d.UsesSearch);

        var istek = new ReportRequest(
            Executed: true,
            FromDate: d.UsesDate ? Gunes : null,
            ToDate: d.UsesDate ? Batis : null,
            LocationIds: null,
            MovementTypes: null,
            SearchText: d.UsesSearch ? "ARA-YAG" : null);

        var tablo = _reports.Run(_tumSubeler, Rapor, istek);
        Assert.NotEmpty(tablo.Rows);
        Assert.All(Kodlar(tablo), k => Assert.Equal("ARA-YAG", k));

        var (headers, rows) = XlsxOku(_excel.Export(tablo), tablo.Rows.Count, tablo.Numeric!);
        Assert.Equal(tablo.Headers, headers);
        Assert.Equal(TabloyuMetne(tablo), rows);
    }

    /// <summary>24 — Diğer filtrelerin davranışı BOZULMADI (STK-10a/10b-1 regresyon nöbetçisi).</summary>
    [Fact]
    public void Onceki_Filtreler_Bozulmadi()
    {
        // Location
        Assert.NotEmpty(Calistir(Istek(lokasyonlar: new[] { _depoB })).Rows);
        // MovementType
        Assert.NotEmpty(Calistir(Istek(turler: new[] { "transfer" })).Rows);
        // Date
        Assert.Empty(Calistir(Istek(from: Batis - 1, to: Batis)).Rows);
        // Kaynak/Hedef semantiği
        var t = Calistir(Istek(turler: new[] { "transfer" }));
        Assert.Equal(2, t.Rows.Count);
        Assert.Contains(t.Rows, r => (string?)r[K(t, "Hedef")] == "Depo B");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
