using System.Text.Json;
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
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ STK-11 — İŞLEM TARİHİ ile KAYIT ZAMANI AYRIMI ═══ (kullanıcı isteği 2026-08-26)
///
/// <b>İSTENEN.</b> Kullanıcı bugün (26.08) otururken 25.08 tarihli bir malzeme girişi ya da 30.08
/// tarihli planlanmış bir hareket kaydedebilmeli — ama <b>kaydı bugün attığı gerçeği kaybolmamalı</b>.
///
/// <b>İKİ ZAMAN, KESİN AYRIM:</b>
/// <list type="bullet">
///   <item><b>İşlem tarihi</b> — hareketin ait olduğu iş günü. Kullanıcı seçer, geçmiş/gelecek serbest.
///         Sütun: <c>stock_documents.doc_date</c>. Ekran + rapor + Excel bunu gösterir/süzer.</item>
///   <item><b>Kayıt zamanı</b> — kaydın sisteme gerçekten girildiği an. Kullanıcı DEĞİŞTİREMEZ.
///         Sütun: <c>stock_movements.created_at</c> ve audit kaydı. Sunucu saatinden yazılır.</item>
/// </list>
///
/// <b>MIGRATION AÇILMADI.</b> Şema zaten bu ayrımı taşıyordu: <c>stock_documents</c> tablosunda
/// <c>doc_date</c> ve <c>created_at</c> ayrı sütunlar (Migration006) ve <c>StockService</c>'in tüm
/// giriş noktaları (<c>ReceiveIn/IssueOut/Transfer/Count</c>) baştan beri opsiyonel bir
/// <c>docDate</c> parametresi alıyordu. Eksik olan yalnız arayüz ve API alanıydı. Şema <b>72'de kaldı</b>.
///
/// <b>GEÇMİŞ VERİ GÜVENDE.</b> Bu tur öncesi hiçbir çağıran <c>docDate</c> göndermiyordu
/// (<c>RunDocumentInTx</c>: <c>date = docDate ?? now</c>, <c>created_at = now</c> — aynı değişken),
/// yani mevcut TÜM satırlarda <c>doc_date == created_at</c>. Aşağıdaki
/// <see cref="IST11_Tarih_Verilmezse_Eski_Davranis_Aynen_Surer"/> bunu kilitler.
/// </summary>
public class IslemTarihiTests : IDisposable
{
    private readonly string _localPath, _serverPath;
    private readonly SqliteConnectionFactory _local, _server;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel = new();
    private readonly SessionContext _oturum;
    /// <summary>Şube kapsamı OLMAYAN oturum — transferin İKİ bacağını da görebilmek için.</summary>
    private readonly SessionContext _firmaGeneli;
    private readonly string _depoA, _depoB, _mat;

    /// <summary>Kayıt zamanı ("bugün"): 26.08.2026 14:00 UTC.</summary>
    private const long Bugun = 1_787_407_200_000;
    private const long Gun = 86_400_000L;

    /// <summary>İşlem tarihi olarak seçilen GEÇMİŞ gün (kabaca 25.08.2026 00:00).</summary>
    private static readonly long GecmisGun = GunBasi(Bugun - Gun);
    /// <summary>İşlem tarihi olarak seçilen GELECEK gün (kabaca 30.08.2026 00:00).</summary>
    private static readonly long GelecekGun = GunBasi(Bugun + 4 * Gun);

    private static long GunBasi(long ms)
    {
        var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.Date;
        return new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    public IslemTarihiTests()
    {
        _localPath = Path.Combine(Path.GetTempPath(), "dw_stk11_" + Guid.NewGuid().ToString("N") + ".db");
        _serverPath = Path.Combine(Path.GetTempPath(), "dw_stk11_srv_" + Guid.NewGuid().ToString("N") + ".db");
        _local = new SqliteConnectionFactory(_localPath);
        _server = new SqliteConnectionFactory(_serverPath);
        new MigrationRunner(_local).Run();
        new MigrationRunner(_server).Run();
        Seed(_local); Seed(_server);

        _stock = new StockService(_local, _clock);
        _reports = new ReportService(_local);
        var users = new UserService(_local, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var yonetici = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_local, _clock);
        _depoA = branches.Create(yonetici, new NewBranch("Depo A"));
        _depoB = branches.Create(yonetici, new NewBranch("Depo B"));
        _mat = new MaterialService(_local, _clock).Create(yonetici, new NewMaterial("STK11-1", "Rulman"));
        _oturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };
        _firmaGeneli = yonetici;
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Bugun);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static void Seed(SqliteConnectionFactory f)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);";
        cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Giriş kaydeder; <paramref name="islemTarihi"/> null ise kullanıcı tarih seçmemiş demektir.</summary>
    private string Giris(decimal miktar, long? islemTarihi, string? op = null)
        => _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, miktar) }, op ?? Op(),
            branchId: _depoA, docDate: islemTarihi).DocumentId;

    /// <summary>Belgenin (işlem tarihi, kayıt zamanı) ikilisini veritabanından OKUR.</summary>
    private (long DocDate, long CreatedAt) BelgeZamanlari(string docId, SqliteConnectionFactory? f = null)
    {
        using var conn = (f ?? _local).Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT doc_date, created_at FROM stock_documents WHERE id=@i;";
        cmd.AddWithValue("@i", docId);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read(), "belge bulunamadı");
        return (r.GetInt64(0), r.GetInt64(1));
    }

    private long HareketKayitZamani(string docId)
    {
        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT created_at FROM stock_movements WHERE document_id=@i LIMIT 1;";
        cmd.AddWithValue("@i", docId);
        return (long)cmd.ExecuteScalar()!;
    }

    private IReadOnlyList<StockMovementRow> Ekran(long? from = null, long? to = null)
        => _stock.SearchMovements(_oturum, from, to, null, null, null, null, 500);

    private TableModel Rapor(long? from = null, long? to = null)
        => _reports.Run(_oturum, "stock-movements",
            new ReportRequest(Executed: true, FromDate: from, ToDate: to));

    // ══════════════ A) TEMEL AYRIM ══════════════

    /// <summary>⭐ ASIL KURAL — GEÇMİŞ tarih: işlem tarihi seçilen gün, kayıt zamanı BUGÜN.
    /// Kullanıcı geri tarih seçerek kaydı bugün attığını GİZLEYEMEZ.</summary>
    [Fact]
    public void IST1_Gecmis_Tarih_Kayit_Zamanini_DEGISTIRMEZ()
    {
        var doc = Giris(10m, GecmisGun);

        var (islem, kayit) = BelgeZamanlari(doc);
        Assert.Equal(GecmisGun, islem);                       // hareket 25.08'e ait
        Assert.Equal(Bugun, kayit);                           // ama BUGÜN girilmiş
        Assert.Equal(Bugun, HareketKayitZamani(doc));         // hareket satırı da gerçek zamanı tutar
        Assert.True(islem < kayit, "geri tarihli kayıtta işlem tarihi kayıt zamanından ÖNCE olmalı");
    }

    /// <summary>⭐ GELECEK tarih de serbest (üst sınır YOK) — planlanmış hareket girilebilir.</summary>
    [Fact]
    public void IST2_Gelecek_Tarih_Kabul_Edilir()
    {
        var doc = Giris(7m, GelecekGun);

        var (islem, kayit) = BelgeZamanlari(doc);
        Assert.Equal(GelecekGun, islem);
        Assert.Equal(Bugun, kayit);
        Assert.True(islem > kayit, "ileri tarihli kayıtta işlem tarihi kayıt zamanından SONRA olmalı");
    }

    /// <summary>BUGÜNÜN tarihi seçilirse de kabul edilir (varsayılan yol).</summary>
    [Fact]
    public void IST3_Bugun_Kabul_Edilir()
    {
        var bugunBasi = GunBasi(Bugun);
        var doc = Giris(3m, bugunBasi);

        var (islem, kayit) = BelgeZamanlari(doc);
        Assert.Equal(bugunBasi, islem);
        Assert.Equal(Bugun, kayit);
    }

    /// <summary>⭐ AUDIT — denetim kaydı GERÇEK zamanı tutar, seçilen işlem tarihini DEĞİL.
    /// "Bugün attığım belli olsun" şartının asıl kanıtı budur.</summary>
    [Fact]
    public void IST4_Audit_Gercek_Zamani_Tutar()
    {
        var doc = Giris(10m, GecmisGun);

        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT created_at FROM audit_logs WHERE entity_id=@i AND entity_type='stock_document';";
        cmd.AddWithValue("@i", doc);
        var auditZaman = cmd.ExecuteScalar();

        Assert.NotNull(auditZaman);
        Assert.Equal(Bugun, Convert.ToInt64(auditZaman));
        Assert.NotEqual(GecmisGun, Convert.ToInt64(auditZaman));
    }

    // ══════════════ B) EKRAN / RAPOR / EXCEL ══════════════

    /// <summary>⭐ Hareket EKRANI işlem tarihini gösterir (kayıt zamanını değil).</summary>
    [Fact]
    public void IST5_Ekran_Islem_Tarihini_Gosterir()
    {
        Giris(10m, GecmisGun);

        var satir = Assert.Single(Ekran());
        Assert.Equal(GecmisGun, satir.CreatedAt);   // alan adı eski, ANLAMI işlem tarihi (bkz. StockMovementRow)
        Assert.Contains(DateTimeOffset.FromUnixTimeMilliseconds(GecmisGun).LocalDateTime.ToString("dd.MM.yyyy"),
            satir.DateText);
    }

    /// <summary>⭐ RAPOR TARİH FİLTRESİ işlem tarihine göre çalışır: kullanıcı 25.08–25.08 dediğinde,
    /// 26.08'de GİRİLMİŞ ama 25.08 tarihli hareketi GÖRÜR.</summary>
    [Fact]
    public void IST6_Rapor_Filtresi_Islem_Tarihini_Kullanir()
    {
        Giris(10m, GecmisGun);

        var gunSonu = GecmisGun + Gun - 1;
        var tablo = Rapor(GecmisGun, gunSonu);

        Assert.Single(tablo.Rows);
    }

    /// <summary>⭐ Aynı hareket, KAYIT gününün aralığında ARANDIĞINDA çıkmaz — iki tarih gerçekten
    /// ayrışmış demektir (aksi hâlde bu test de geçerdi ve ayrım sahte olurdu).</summary>
    [Fact]
    public void IST7_Kayit_Gunu_Araliginda_Cikmaz()
    {
        Giris(10m, GecmisGun);

        var bugunBasi = GunBasi(Bugun);
        var tablo = Rapor(bugunBasi, bugunBasi + Gun - 1);

        Assert.Empty(tablo.Rows);
    }

    /// <summary>⭐ EXCEL çıktısı da işlem tarihini yazar (rapor ile aynı satırlar).</summary>
    [Fact]
    public void IST8_Excel_Islem_Tarihini_Yazar()
    {
        Giris(10m, GecmisGun);

        var tablo = Rapor(GecmisGun, GecmisGun + Gun - 1);
        var bayt = _excel.Export(tablo);
        using var wb = new XLWorkbook(new MemoryStream(bayt));
        var ws = wb.Worksheets.First();

        var beklenen = DateTimeOffset.FromUnixTimeMilliseconds(GecmisGun).LocalDateTime.ToString("dd.MM.yyyy");
        var metin = string.Join("|", ws.RowsUsed().Select(r => string.Join(",", r.Cells().Select(c => c.GetString()))));
        Assert.Contains(beklenen, metin);
    }

    /// <summary>⭐ EKRAN ↔ RAPOR PARİTESİ korunur: ikisi de AYNI tarihi gösterir (tek kaynak
    /// <c>StockMovementFilterSql.IslemTarihiSql</c>). Biri değişip diğeri kalırsa bu test kırılır.</summary>
    [Fact]
    public void IST9_Ekran_Ve_Rapor_Ayni_Tarihi_Gosterir()
    {
        Giris(10m, GecmisGun);
        _clock.Advance(1000);
        Giris(4m, GelecekGun);

        var ekran = Ekran().Select(m => m.CreatedAt).OrderBy(x => x).ToList();
        var tarihKolon = Rapor().Headers.ToList().FindIndex(h => h.Contains("Tarih", StringComparison.OrdinalIgnoreCase));
        Assert.True(tarihKolon >= 0, "raporda Tarih kolonu bulunmalı");
        var rapor = Rapor().Rows
            .Select(r => DateTime.Parse(r[tarihKolon]!.ToString()!, System.Globalization.CultureInfo.GetCultureInfo("tr-TR")))
            .OrderBy(x => x).ToList();

        Assert.Equal(ekran.Count, rapor.Count);
        for (int i = 0; i < ekran.Count; i++)
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(ekran[i]).LocalDateTime.Date, rapor[i].Date);
    }

    /// <summary>⭐ SIRALAMA BİLİNÇLİ OLARAK DEĞİŞMEDİ: geri tarihli kayıt, en son girildiği için
    /// listenin EN ÜSTÜNDE görünür. (İşlem tarihine göre sıralansaydı kullanıcı az önce kaydettiği
    /// satırı listenin ortasında arar, "kaydedilmedi mi?" derdi.)</summary>
    [Fact]
    public void IST10_Az_Once_Girilen_En_Ustte_Kalir()
    {
        Giris(1m, GunBasi(Bugun));      // önce bugün tarihli
        _clock.Advance(60_000);
        Giris(2m, GecmisGun);           // sonra GERİ tarihli — ama EN SON girildi

        var satirlar = Ekran();
        Assert.Equal(2, satirlar.Count);
        Assert.Equal(2m, satirlar[0].Quantity);   // en son girilen en üstte
    }

    // ══════════════ C) GERİYE UYUMLULUK / DİĞER TÜRLER ══════════════

    /// <summary>⭐ GEÇMİŞ VERİ GÜVENDE: tarih verilmezse eski davranış birebir sürer —
    /// işlem tarihi = kayıt zamanı. Mevcut tüm kayıtlar bu durumdadır.</summary>
    [Fact]
    public void IST11_Tarih_Verilmezse_Eski_Davranis_Aynen_Surer()
    {
        var doc = Giris(5m, null);

        var (islem, kayit) = BelgeZamanlari(doc);
        Assert.Equal(kayit, islem);
        Assert.Equal(Bugun, islem);
        Assert.Equal(Bugun, Assert.Single(Ekran()).CreatedAt);
    }

    /// <summary>Diğer hareket türleri de aynı sözleşmeyi kullanır — çıkış ve transfer.
    /// Transferde İKİ bacak da AYNI belgeye bağlıdır → tek işlem tarihi taşır.</summary>
    [Fact]
    public void IST12_Cikis_Ve_Transfer_De_Islem_Tarihi_Tasir()
    {
        Giris(100m, null);   // stok oluştur

        var cikis = _stock.IssueOut(_oturum, new[] { new StockLine(_mat, 5m) }, Op(),
            branchId: _depoA, docDate: GecmisGun).DocumentId;
        Assert.Equal(GecmisGun, BelgeZamanlari(cikis).DocDate);
        Assert.Equal(Bugun, BelgeZamanlari(cikis).CreatedAt);

        _stock.Transfer(_oturum, new[] { new StockLine(_mat, 3m) }, _depoA, _depoB, Op(),
            docDate: GelecekGun);
        // Şube kapsamı DOĞRU çalıştığı için _oturum (Depo A) yalnız kendi bacağını görür;
        // iki bacağı da görmek için firma geneli oturum kullanılır (kapsam davranışı DEĞİŞMEDİ).
        var transferSatirlari = _stock.SearchMovements(_firmaGeneli, null, null, null, null, null, null, 500)
            .Where(m => m.MovementType == "transfer").ToList();
        Assert.Equal(2, transferSatirlari.Count);                       // iki bacak
        Assert.All(transferSatirlari, m => Assert.Equal(GelecekGun, m.CreatedAt));
    }

    /// <summary>⭐ STOK MUHASEBESİ DEĞİŞMEDİ (bilinçli): ileri tarihli hareket bakiyeyi
    /// BEKLETMEDEN etkiler — mevcut iş kuralı budur ve bu turda değiştirilmedi.
    /// Bu test, ileride biri farkında olmadan "tarihi gelince işlesin" davranışına geçerse uyarır.</summary>
    [Fact]
    public void IST13_Gelecek_Tarihli_Hareket_Bakiyeyi_HEMEN_Etkiler()
    {
        Giris(40m, GelecekGun);

        var bakiye = _stock.GetBalance(_oturum, _mat);
        Assert.Equal(40m, bakiye);
    }

    // ══════════════ D) SENKRON / OFFLINE ══════════════

    /// <summary>⭐ SENKRON: çevrimdışı girilen geri tarihli hareket sunucuya taşındığında
    /// İŞLEM TARİHİ KORUNUR; senkron zamanı onun yerine geçmez. Kayıt zamanı da korunur.</summary>
    [Fact]
    public void IST14_Senkron_Islem_Tarihini_ve_Kayit_Zamanini_Korur()
    {
        var doc = Giris(12m, GecmisGun);

        _clock.Advance(3 * Gun);   // senkron GÜNLER sonra gerçekleşiyor
        var paket = new BusinessSyncService(_local, _clock).BuildSnapshot("A");
        using (var d = JsonDocument.Parse(paket))
            new BusinessSyncService(_server, _clock).Apply("A", d.RootElement);

        var (islem, kayit) = BelgeZamanlari(doc, _server);
        Assert.Equal(GecmisGun, islem);   // senkron tarihi işlem tarihini EZMEDİ
        Assert.Equal(Bugun, kayit);       // kayıt zamanı da korundu
    }

    /// <summary>⭐ AYNI paket TEKRAR gönderilirse tarihler DEĞİŞMEZ (idempotency).</summary>
    [Fact]
    public void IST15_Tekrar_Senkron_Tarihi_Degistirmez()
    {
        var doc = Giris(12m, GecmisGun);

        var paket = new BusinessSyncService(_local, _clock).BuildSnapshot("A");
        using (var d = JsonDocument.Parse(paket))
            new BusinessSyncService(_server, _clock).Apply("A", d.RootElement);
        var ilk = BelgeZamanlari(doc, _server);

        _clock.Advance(2 * Gun);
        using (var d = JsonDocument.Parse(paket))
            new BusinessSyncService(_server, _clock).Apply("A", d.RootElement);
        var ikinci = BelgeZamanlari(doc, _server);

        Assert.Equal(ilk, ikinci);
    }

    /// <summary>Aynı <c>operationId</c> ile ikinci kayıt denemesi YENİ belge açmaz ve ilk
    /// işlem tarihini korur — geri tarihli kayıtta da idempotency bozulmamalı.</summary>
    [Fact]
    public void IST16_Ayni_Operation_Tarihi_Degistirmez()
    {
        var op = Op();
        var ilk = Giris(9m, GecmisGun, op);
        var ikinci = Giris(9m, GelecekGun, op);   // aynı jeton, FARKLI tarih denenirse

        Assert.Equal(ilk, ikinci);                                  // aynı belge döner
        Assert.Equal(GecmisGun, BelgeZamanlari(ilk).DocDate);       // ilk tarih korunur
        Assert.Single(Ekran());                                     // çift hareket YOK
    }

    // ══════════════ E) WEB / MASAÜSTÜ PARİTESİ (kaynak sözleşmesi) ══════════════

    /// <summary>⭐ İki platform da AYNI API alanını (<c>docDate</c>) ve AYNI varsayılanı (BUGÜN)
    /// kullanır; ikinci bir tarih mantığı yoktur.</summary>
    [Fact]
    public void IST17_Web_Ve_Masaustu_Ayni_Sozlesmeyi_Kullanir()
    {
        var kok = RepoKok();
        var web = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Web", "Components", "Pages", "Stock.razor"));
        var masaVm = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Desktop", "ViewModels", "StockEntryViewModel.cs"));
        var masaView = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Desktop", "Views", "StockEntryView.axaml"));
        var api = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Api", "Program.cs"));

        // Web: ALAN TANIMI varsayılanı BUGÜN olmalı (yalnız "metin geçiyor mu" değil — mutasyon
        // turunda M8 tam bu zayıflıktan kaçmıştı: ClearForm içindeki aynı metin testi geçiriyordu).
        Assert.Matches(@"private\s+DateTime\?\s+_docDate\s*=\s*DateTime\.Today\s*;", web);
        Assert.Contains("İşlem Tarihi", web);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(web, @"docDate = DocDateMs\(\)").Count);

        // Masaüstü: ALAN TANIMI varsayılanı BUGÜN + üç kaydetme yolunun üçünde de gönderiliyor
        Assert.Matches(@"private\s+DateTimeOffset\?\s+_docDate\s*=\s*new DateTimeOffset\(DateTime\.Today\)\s*;", masaVm);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(masaVm, @"docDate: DocDate\?\.ToUnixTimeMilliseconds\(\)").Count);
        Assert.Contains("Label=\"İşlem Tarihi\"", masaView);

        // API: üç ucun üçü de DTO'daki tarihi servise geçiriyor
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(api, @"docDate: d\.DocDate").Count);
    }

    /// <summary>⭐ Ekran ve rapor tarihi TEK kaynaktan alır — iki sorguya ayrı ayrı yazılırsa
    /// sessizce ayrışabilirlerdi.</summary>
    [Fact]
    public void IST18_Tarih_Ifadesi_Tek_Kaynaktan_Gelir()
    {
        var kok = RepoKok();
        var servis = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Infrastructure", "Materials", "StockService.cs"));
        var rapor = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Infrastructure", "Reporting", "ReportService.cs"));

        Assert.Equal("COALESCE(d.doc_date, sm.created_at)", StockMovementFilterSql.IslemTarihiSql);
        Assert.Contains("StockMovementFilterSql.IslemTarihiSql", servis);
        Assert.Contains("StockMovementFilterSql.IslemTarihiSql", rapor);
        // Ham sütun adı, tarih filtresine/gösterimine ELLE yazılmış olmamalı.
        Assert.DoesNotContain("DateFilter(req, \"sm.created_at\")", rapor);
    }

    /// <summary>⭐ ŞEMA 72'DE KALDI — bu özellik için yeni migration açılmadı.</summary>
    [Fact]
    public void IST19_Yeni_Migration_Acilmadi()
    {
        var enSon = MigrationCatalog.All().Max(m => m.Version);
        Assert.Equal(72, enSon);
    }

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    public void Dispose()
    {
        try { File.Delete(_localPath); } catch { }
        try { File.Delete(_serverPath); } catch { }
    }
}
