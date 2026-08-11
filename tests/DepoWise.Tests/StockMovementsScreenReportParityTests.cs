using System.Text;
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
/// STK-10b-4 (2026-08-12) — STOK HAREKETLERİ EKRANI ↔ RAPOR ↔ XLSX PARİTESİ + <b>B-1 DÜZELTMESİ</b>.
///
/// <b>🔴 B-1 (kapatılan hata).</b> Web'deki Stok Hareketleri ekranı, lokasyon süzmesini sunucudan
/// gelen <b>LİMİTLİ</b> liste üzerinde İSTEMCİDE yapıyordu. Seçilen depoya ait hareket ilk N kaydın
/// dışındaysa kullanıcı onu <b>hiç göremiyordu</b> ve eksikliği fark edemiyordu — sessiz yanlış sonuç.
/// Filtre artık SQL'de ve <b>LIMIT'ten ÖNCE</b> uygulanıyor. Aşağıdaki
/// <see cref="B1_Limit_Disindaki_Hareket_Lokasyon_Filtresiyle_GELIR"/> testi hem doğru davranışı
/// kilitler hem de ESKİ yöntemin aynı veride kaybettiğini gösterir.
///
/// <b>Tek filtre kaynağı.</b> Ekran (<c>StockService.SearchMovements</c>) ve rapor
/// (<c>ReportService.StockMovements</c>) artık AYNI WHERE üretecini kullanır
/// (<c>StockMovementFilterSql</c>) → ikinci bir hareket sorgulama mimarisi YOK.
///
/// Tamamı yerel SQLite üzerindedir; HTTP yoktur (masaüstünün çevrimdışı yolu).
/// </summary>
public class StockMovementsScreenReportParityTests : IDisposable
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
    private const long Gunes = 1_699_000_000_000, Batis = 1_710_000_000_000;

    public StockMovementsScreenReportParityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_10b4_" + Guid.NewGuid().ToString("N") + ".db");
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

        _filtre = materials.Create(_tumSubeler, new NewMaterial("EKR-FLT", "Yag filtresi"));
        _yag = materials.Create(_tumSubeler, new NewMaterial("EKR-YAG", "Motor yagi"));

        Senaryo();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private void Senaryo()
    {
        _opening.RecordOpening(_tumSubeler, _filtre, 500m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _opening.RecordOpening(_tumSubeler, _yag, 200m, Op(), branchId: _depoA);
        _clock.Advance(60_000);
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_yag, 10m) }, Op(),
            branchId: _depoA, invoiceNo: "FTR-EKR-777");
        _clock.Advance(60_000);
        // Transfer: defterde İKİ satır (Depo A çıkış bacağı + Depo B giriş bacağı).
        _stock.Transfer(_tumSubeler, _filtre, 5m, _depoA, _depoB, Op());
        _clock.Advance(60_000);
        // Lokasyonu OLMAYAN hareket (📦 Atanmamış kovası).
        _opening.RecordOpening(_tumSubeler, _yag, 3m, Op());
    }

    // ── Ekran (servis) ve rapor çağrıları — AYNI filtreler ────────────────────────────────────

    private IReadOnlyList<StockMovementRow> Ekran(
        string[]? lokasyonlar = null, string[]? turler = null, string? arama = null,
        string[]? malzemeler = null, long? from = Gunes, long? to = Batis,
        int limit = 1000, SessionContext? s = null)
        => _stock.SearchMovements(s ?? _tumSubeler, from, to, arama, lokasyonlar, turler, malzemeler, limit);

    private TableModel RaporTablo(
        string[]? lokasyonlar = null, string[]? turler = null, string? arama = null,
        string[]? malzemeler = null, long? from = Gunes, long? to = Batis, SessionContext? s = null)
        => _reports.Run(s ?? _tumSubeler, Rapor, new ReportRequest(
            Executed: true, FromDate: from, ToDate: to,
            LocationIds: lokasyonlar, MovementTypes: turler, SearchText: arama, MaterialIds: malzemeler));

    /// <summary>Ekran satırını, raporun ürettiği metinlerle karşılaştırılabilir bir anahtara çevirir.</summary>
    private static string EkranAnahtari(StockMovementRow m)
    {
        var signed = m.Direction > 0 ? m.Quantity : -m.Quantity;
        return string.Join("|",
            DateTimeOffset.FromUnixTimeMilliseconds(m.CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm"),
            MovementTypeOptions.Label(m.MovementType), m.Code, m.Name,
            (signed >= 0 ? "+" : "") + signed.ToString("0.##"));
    }

    /// <summary>Rapor satırını AYNI anahtara çevirir (kolon adlarından okunur, sıra varsayımı yok).</summary>
    private static string RaporAnahtari(TableModel t, IReadOnlyList<object?> r)
    {
        int K(string b) { for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == b) return i; throw new InvalidOperationException(b); }
        var miktar = r[K("Miktar")] as NumCell;
        return string.Join("|", (string?)r[K("Tarih")], (string?)r[K("Tür")], (string?)r[K("Kod")],
            (string?)r[K("Malzeme")], miktar?.Display ?? "");
    }

    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new InvalidOperationException($"'{baslik}' kolonu yok.");
    }

    // ══════════════ 1. 🔴 B-1 — LİMİT DIŞINDAKİ HAREKET ══════════════

    /// <summary>1 — 🔴 <b>B-1'İN KENDİSİ.</b> 520 yeni hareket Depo B'de; Depo A'nın hareketi EN ESKİ
    /// kayıt, yani 500'lük pencerenin DIŞINDA. Lokasyon filtresi sunucuda uygulandığı için kayıt GELİR.
    /// Aynı test, ESKİ yöntemin (önce 500 çek, sonra istemcide süz) bu kaydı KAYBETTİĞİNİ de gösterir.</summary>
    [Fact]
    public void B1_Limit_Disindaki_Hareket_Lokasyon_Filtresiyle_GELIR()
    {
        // Depo A'da tek, EN ESKİ hareket (kurulumdaki açılış zaten var; kimliğini işaretlemek için yenisi).
        var isaret = new MaterialService(_factory, _clock).Create(_tumSubeler, new NewMaterial("EKR-ESKI", "Eski kayit"));
        _opening.RecordOpening(_tumSubeler, isaret, 1m, Op(), branchId: _depoA);

        // Sonra Depo B'de 520 DAHA YENİ hareket → A'nın kaydı sıralamada en sona düşer.
        for (int i = 0; i < 520; i++)
        {
            _clock.Advance(60_000);
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_yag, 1m) }, Op(), branchId: _depoB);
        }

        // ✅ YENİ DAVRANIŞ: filtre SQL'de, LIMIT'ten önce → kayıt geliyor.
        var sunucuTarafli = Ekran(lokasyonlar: new[] { _depoA }, limit: 500);
        Assert.Contains(sunucuTarafli, m => m.Code == "EKR-ESKI");

        // ❌ ESKİ DAVRANIŞ (B-1): önce 500 satır çek, sonra istemcide süz → kayıt KAYBOLUYOR.
        var eskiYol = _stock.SearchMovements(_tumSubeler, Gunes, Batis, null, 500)
            .Where(m => m.LocationId == _depoA || m.FromLocationId == _depoA)
            .ToList();
        Assert.DoesNotContain(eskiYol, m => m.Code == "EKR-ESKI");

        // Ve iki yol GERÇEKTEN farklı sonuç veriyor (test kendi kendini kanıtlıyor).
        Assert.True(sunucuTarafli.Count > eskiYol.Count);

        // Rapor da aynı kaydı getiriyor (ekran = rapor).
        var rapor = RaporTablo(lokasyonlar: new[] { _depoA });
        Assert.Contains(rapor.Rows, r => (string?)r[K(rapor, "Kod")] == "EKR-ESKI");
    }

    /// <summary>2 — Tavan gerçekten FİLTRELENMİŞ küme üzerine iniyor (bellekte kesilmiyor).</summary>
    [Fact]
    public void Limit_Filtrelenmis_Kume_Uzerine_Iniyor()
    {
        for (int i = 0; i < 30; i++)
        {
            _clock.Advance(60_000);
            _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_yag, 1m) }, Op(), branchId: _depoB);
        }
        var kesik = Ekran(lokasyonlar: new[] { _depoA }, limit: 3);
        Assert.Equal(3, kesik.Count);
        Assert.All(kesik, m => Assert.True(m.LocationId == _depoA || m.FromLocationId == _depoA));
    }

    // ══════════════ 2. EKRAN FİLTRELERİ ══════════════

    /// <summary>3 — Ekran + Lokasyon. Transferin İKİ bacağı da ilgili depoda görünür (STK-06 semantiği).</summary>
    [Fact]
    public void Ekran_Lokasyon_Filtresi()
    {
        var a = Ekran(lokasyonlar: new[] { _depoA });
        Assert.All(a, m => Assert.True(m.LocationId == _depoA || m.FromLocationId == _depoA));
        Assert.Contains(a, m => m.MovementType == "transfer");

        var b = Ekran(lokasyonlar: new[] { _depoB });
        var tekSatir = Assert.Single(b);
        Assert.Equal("transfer", tekSatir.MovementType);
        Assert.Equal(_depoB, tekSatir.LocationId);
        Assert.Equal(_depoA, tekSatir.FromLocationId);   // Kaynak → Hedef korunuyor
    }

    /// <summary>4 — 📦 Atanmamış: yalnız iki tarafı da boş olan hareketler. "Tüm Şubeler" ile AYNI DEĞİL.</summary>
    [Fact]
    public void Ekran_Atanmamis_Filtresi()
    {
        var atanmamis = Ekran(lokasyonlar: new[] { "" });
        var satir = Assert.Single(atanmamis);
        Assert.True(string.IsNullOrEmpty(satir.LocationId));

        Assert.True(Ekran().Count > atanmamis.Count);   // Tüm Şubeler = filtre yok (Atanmamış DAHİL hepsi)
    }

    /// <summary>5 — Ekran + Lokasyon + Hareket Türü.</summary>
    [Fact]
    public void Ekran_Lokasyon_ve_MovementType()
    {
        // Depo A transferin İKİ bacağını da ilgilendiriyor (çıkış: branch_id=A · giriş: branch_from_id=A).
        Assert.Equal(2, Ekran(lokasyonlar: new[] { _depoA }, turler: new[] { "transfer" }).Count);
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, turler: new[] { "opening" }));
        // Bilinmeyen tür → fail-closed.
        Assert.Empty(Ekran(turler: new[] { "uydurma_tur" }));
    }

    /// <summary>6 — Ekran + Lokasyon + Arama (mevcut arama semantiği DEĞİŞMEDİ).</summary>
    [Fact]
    public void Ekran_Lokasyon_ve_Search()
    {
        Assert.NotEmpty(Ekran(lokasyonlar: new[] { _depoA }, arama: "FTR-EKR-777"));
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, arama: "FTR-EKR-777"));
        Assert.NotEmpty(Ekran(arama: "Motor yagi"));
        Assert.Empty(Ekran(arama: "yok-boyle-kayit-999"));
    }

    /// <summary>7 — Ekran + Lokasyon + Malzeme.</summary>
    [Fact]
    public void Ekran_Lokasyon_ve_Material()
    {
        Assert.Single(Ekran(lokasyonlar: new[] { _depoB }, malzemeler: new[] { _filtre }));
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, malzemeler: new[] { _yag }));
        Assert.Empty(Ekran(malzemeler: new[] { "yok-boyle-malzeme" }));   // fail-closed
    }

    /// <summary>8 — Ekran + TÜM filtreler birlikte (AND). Bir filtreyi bozmak sonucu boşaltır.</summary>
    [Fact]
    public void Ekran_Tum_Filtreler_Birlikte()
    {
        var t = Ekran(lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" },
                      arama: "EKR-FLT", malzemeler: new[] { _filtre });
        Assert.Single(t);

        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" },
                           arama: "EKR-FLT", malzemeler: new[] { _yag }));
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, turler: new[] { "opening" },
                           arama: "EKR-FLT", malzemeler: new[] { _filtre }));
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" },
                           arama: "yok-boyle", malzemeler: new[] { _filtre }));
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, turler: new[] { "transfer" },
                           arama: "EKR-FLT", malzemeler: new[] { _filtre }, from: Gunes, to: Gunes + 1));
    }

    /// <summary>9 — Boş/null filtre = mevcut (eski) davranış: hepsi.</summary>
    [Fact]
    public void Bos_Filtreler_Eski_Davranisi_Koruyor()
    {
        var hepsi = Ekran();
        Assert.Equal(hepsi.Count, Ekran(lokasyonlar: Array.Empty<string>(), turler: Array.Empty<string>(),
                                        malzemeler: Array.Empty<string>()).Count);
        // Eski imza (STK-10b-4 öncesi çağrı biçimi) de aynı sonucu veriyor → geriye dönük uyum.
        Assert.Equal(hepsi.Count, _stock.SearchMovements(_tumSubeler, Gunes, Batis, null, 1000).Count);
    }

    // ══════════════ 3. 🔒 BranchScope × Location ══════════════

    /// <summary>10 — 🔴 Kapsam DIŞ SINIR: Depo A oturumu + Depo B filtresi → BOŞ (yetki aşılmaz).
    /// STK-10a'da rapor için doğrulanan kural artık EKRAN için de geçerli.</summary>
    [Fact]
    public void BranchScope_Location_Ekranda_da_Asilamiyor()
    {
        // Tüm Şubeler + Depo A → A ile ilişkili İKİ bacak (açılışlar + transferin çıkış bacağı) görünür.
        Assert.Contains(Ekran(lokasyonlar: new[] { _depoA }), m => m.MovementType == "transfer");

        // Depo A kapsamındaki kullanıcı + Depo A → yalnız kapsam içindeki bacaklar.
        var kapsamli = Ekran(lokasyonlar: new[] { _depoA }, s: _depoAOturum);
        Assert.NotEmpty(kapsamli);
        Assert.All(kapsamli, m => Assert.True(m.LocationId == _depoA || string.IsNullOrEmpty(m.LocationId)));

        // Depo A kapsamındaki kullanıcı + Depo B → BOŞ.
        Assert.Empty(Ekran(lokasyonlar: new[] { _depoB }, s: _depoAOturum));

        // Aynı kayıt "Tüm Şubeler" oturumunda GÖRÜNÜYOR → boşluk kapsamdan geliyor, kayıt yokluğundan değil.
        Assert.Single(Ekran(lokasyonlar: new[] { _depoB }));
    }

    /// <summary>11 — 🔒 Firma izolasyonu: başka firmanın hareketi hiçbir filtreyle görünmüyor.</summary>
    [Fact]
    public void Ekran_Baska_Firmanin_Kaydini_Gostermiyor()
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
        var matB = new MaterialService(_factory, _clock).Create(sB, new NewMaterial("EKR-YAG", "Motor yagi"));
        var depoYabanci = new BranchService(_factory, _clock).Create(sB, new NewBranch("Yabanci Depo"));
        _stock.ReceiveIn(sB, new[] { new StockLine(matB, 5m) }, Op(), branchId: depoYabanci);

        Assert.Empty(Ekran(lokasyonlar: new[] { depoYabanci }));
        Assert.Empty(Ekran(malzemeler: new[] { matB }));
        Assert.DoesNotContain(Ekran(arama: "EKR-YAG"), m => m.Code == "EKR-YAG" && m.LocationId == depoYabanci);
    }

    // ══════════════ 4. 🔴 EKRAN = RAPOR = XLSX ══════════════

    /// <summary>12 — 🔴 EKRAN = RAPOR: aynı filtrelerle iki yol AYNI satır kümesini, AYNI SIRADA üretir.
    /// Tek filtre üreteci kullanıldığı için bu bir tesadüf değil, yapısal zorunluluktur.</summary>
    [Theory]
    [InlineData("filtresiz")]
    [InlineData("depoA")]
    [InlineData("depoB")]
    [InlineData("atanmamis")]
    [InlineData("tur")]
    [InlineData("arama")]
    [InlineData("malzeme")]
    [InlineData("hepsi")]
    [InlineData("bos")]
    public void Ekran_Sonucu_Rapor_Sonucuyla_Ayni(string senaryo)
    {
        (string[]? loc, string[]? tur, string? ara, string[]? mat) f = senaryo switch
        {
            "depoA" => (new[] { _depoA }, null, null, null),
            "depoB" => (new[] { _depoB }, null, null, null),
            "atanmamis" => (new[] { "" }, null, null, null),
            "tur" => (null, new[] { "opening" }, null, null),
            "arama" => (null, null, "FTR-EKR-777", null),
            "malzeme" => (null, null, null, new[] { _filtre }),
            "hepsi" => (new[] { _depoB }, new[] { "transfer" }, "EKR-FLT", new[] { _filtre }),
            "bos" => (null, null, "yok-boyle-kayit-999", null),
            _ => (null, null, null, null),
        };

        var ekran = Ekran(f.Item1, f.Item2, f.Item3, f.Item4);
        var rapor = RaporTablo(f.Item1, f.Item2, f.Item3, f.Item4);

        Assert.Equal(ekran.Count, rapor.Rows.Count);
        var ekranAnahtarlari = ekran.Select(EkranAnahtari).ToList();
        var raporAnahtarlari = rapor.Rows.Select(r => RaporAnahtari(rapor, r)).ToList();
        Assert.Equal(ekranAnahtarlari, raporAnahtarlari);
    }

    /// <summary>13 — 🔴 RAPOR = XLSX: aynı filtrelerle export ekranla hücre hücre aynı
    /// (yeni bir export yolu açılmadı — mevcut ReportService sonucu kullanılıyor).</summary>
    [Theory]
    [InlineData("filtresiz")]
    [InlineData("depoA")]
    [InlineData("depoB")]
    [InlineData("atanmamis")]
    [InlineData("hepsi")]
    [InlineData("bos")]
    public void Rapor_Sonucu_XLSX_ile_Ayni(string senaryo)
    {
        (string[]? loc, string[]? tur, string? ara, string[]? mat) f = senaryo switch
        {
            "depoA" => (new[] { _depoA }, null, null, null),
            "depoB" => (new[] { _depoB }, null, null, null),
            "atanmamis" => (new[] { "" }, null, null, null),
            "hepsi" => (new[] { _depoB }, new[] { "transfer" }, "EKR-FLT", new[] { _filtre }),
            "bos" => (null, null, "yok-boyle-kayit-999", null),
            _ => (null, null, null, null),
        };

        var rapor = RaporTablo(f.Item1, f.Item2, f.Item3, f.Item4);
        var bytes = _excel.Export(rapor);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        for (int c = 0; c < rapor.Headers.Count; c++)
            Assert.Equal(rapor.Headers[c], ws.Cell(1, c + 1).GetString());

        var numeric = rapor.Numeric ?? Array.Empty<bool>();
        for (int r = 0; r < rapor.Rows.Count; r++)
            for (int c = 0; c < rapor.Headers.Count; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                var beklenen = rapor.Rows[r][c] switch
                {
                    null => "",
                    NumCell n => n.Value.ToString("0.####"),
                    var v => v.ToString() ?? "",
                };
                var gercek = c < numeric.Count && numeric[c]
                    ? (cell.IsEmpty() ? "" : cell.GetDouble().ToString("0.####"))
                    : cell.GetString();
                Assert.Equal(beklenen, gercek);
            }
    }

    // ══════════════ 5. KAYNAK TARAMASI — istemci süzmesi geri gelmesin ══════════════

    private static string Kaynak(params string[] parcalar)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DepoWise.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var yol = Path.Combine(new[] { dir!.FullName }.Concat(parcalar).ToArray());
        Assert.True(File.Exists(yol), $"Kaynak bulunamadı: {yol}");
        return File.ReadAllText(yol, Encoding.UTF8);
    }

    /// <summary>14 — 🔴 B-1 NÖBETÇİSİ: Web ekranı lokasyonu ARTIK istemcide süzmüyor; sunucuya
    /// gönderiyor. İstemci süzmesi geri eklenirse bu test kırılır.</summary>
    [Fact]
    public void Web_Ekrani_Lokasyonu_Istemcide_Suzmuyor()
    {
        var razor = Kaynak("src", "DepoWise.Web", "Components", "Pages", "StockMovements.razor");

        // Eski (hatalı) desen: gelen satırları istemcide lokasyona göre süzmek.
        Assert.DoesNotContain("rows.Where(r => Str(r, \"locationId\")", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("Str(r, \"fromLocationId\") == _location", razor, StringComparison.Ordinal);

        // Yeni (doğru) desen: lokasyon sunucuya sorgu parametresi olarak gidiyor.
        Assert.Contains("qs.Add($\"location=", razor, StringComparison.Ordinal);
        Assert.Contains("_location != LocationOptions.AllId", razor, StringComparison.Ordinal);
    }

    /// <summary>15 — 🔴 TEK FİLTRE KAYNAĞI: ekran ve rapor aynı üreteci çağırıyor; ikinci bir
    /// lokasyon/tür/arama/malzeme WHERE'i yazılmamış.</summary>
    [Fact]
    public void Ekran_ve_Rapor_Ayni_Filtre_Uretecini_Kullaniyor()
    {
        var stok = Kaynak("src", "DepoWise.Infrastructure", "Materials", "StockService.cs");
        var rapor = Kaynak("src", "DepoWise.Infrastructure", "Reporting", "ReportService.cs");

        Assert.Contains("StockMovementFilterSql.Build(", stok, StringComparison.Ordinal);
        Assert.Contains("StockMovementFilterSql.Build(", rapor, StringComparison.Ordinal);

        // Arama SQL'i artık YALNIZ ortak üreteçte yazılıdır (iki yerde ayrı ayrı değil).
        const string aramaSql = "m.code LIKE @q OR m.name LIKE @q";
        Assert.DoesNotContain(aramaSql, stok, StringComparison.Ordinal);
        Assert.DoesNotContain(aramaSql, rapor, StringComparison.Ordinal);
        Assert.Contains(aramaSql, Kaynak("src", "DepoWise.Infrastructure", "Materials", "StockMovementFilterSql.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>16 — Masaüstü ekranı lokasyon filtresini SERVİSE geçiriyor (parite) ve ağ kullanmıyor.</summary>
    [Fact]
    public void Masaustu_Ekrani_Lokasyonu_Servise_Geciriyor()
    {
        var vm = Kaynak("src", "DepoWise.Desktop", "ViewModels", "StockMovementsViewModel.cs");
        var xaml = Kaynak("src", "DepoWise.Desktop", "Views", "StockMovementsView.axaml");

        Assert.Contains("SelectedLocation", vm, StringComparison.Ordinal);
        Assert.Contains("DesktopServices.Stock.SearchMovements(", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", vm, StringComparison.Ordinal);   // çevrimdışı
        Assert.Contains("{Binding SelectedLocation}", xaml, StringComparison.Ordinal);
        Assert.Contains("Depo / Şantiye", xaml, StringComparison.Ordinal);

        // İstemci tarafı süzme deseni masaüstünde de YOK.
        Assert.DoesNotContain("Movements.Where(", vm, StringComparison.Ordinal);
    }

    // ══════════════ 6. REGRESYON ══════════════

    /// <summary>17 — STK-B2 kararsız: arama semantiği bu artımda da DEĞİŞMEDİ (belge notu aramada yok).</summary>
    [Fact]
    public void Search_Semantigi_Degismedi()
    {
        _clock.Advance(60_000);
        _stock.ReceiveIn(_tumSubeler, new[] { new StockLine(_yag, 2m) }, Op(),
            branchId: _depoA, note: "belge notu aramada yok");

        Assert.Empty(Ekran(arama: "belge notu aramada yok"));
        Assert.Empty(RaporTablo(arama: "belge notu aramada yok").Rows);
        Assert.NotEmpty(Ekran(arama: "EKR-YAG"));   // kod araması çalışmaya devam ediyor
    }

    /// <summary>18 — Diğer çağıranlar bozulmadı: <c>RecentMovements</c> eski davranışını sürdürüyor.</summary>
    [Fact]
    public void RecentMovements_Bozulmadi()
    {
        var son = _stock.RecentMovements(_tumSubeler, 3);
        Assert.Equal(3, son.Count);
        // Tarihe göre AZALAN sıra korunuyor.
        Assert.True(son[0].CreatedAt >= son[1].CreatedAt && son[1].CreatedAt >= son[2].CreatedAt);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
