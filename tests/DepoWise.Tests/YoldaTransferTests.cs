using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-27 (AR-GE 2026-10-18, 05 F-070) — İKİ ADIMLI TRANSFER: GÖNDER → TESLİM AL ("YOLDA") ═══
///  YT1 — Gönderim: kaynak düşer, hedef DEĞİŞMEZ, mal "yolda" lokasyonunda; firma toplamı aynı; listede görünür
///  YT2 — Tam teslim: yoldan hedefe geçer, durum "teslim", listeden çıkar; tekrar gönderim ikinci hareket yazmaz
///  YT3 — Eksik teslim: fark yolda kalır ("eksik"); fazla teslim reddedilir; kayıp kapatma stoktan düşer, gerekçe işlem geçmişinde
///  YT4 — Firma ayrımı: başka firma teslim alamaz, kapatamaz, listede görmez
///  YT5 — Tek adımlı transfer eskisi gibi (yolda yok, listede yok); yolda transfer iptal edilemez
///  YT6 — Migration109 tekrar çalışabilir; eski şemada iki adımlı reddedilir, tek adımlı çalışır, liste boş
///  YT7 — Lokasyon kırılımında "Yolda (teslim bekliyor)" adı
///  YT8 — İki platform + API (kaynak kilidi)
/// </summary>
public class YoldaTransferTests : IDisposable
{
    private const string Co = "YTR";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_ytr_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly string _a, _b, _m;
    private readonly StockService _stock;

    public YoldaTransferTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "ytr", "Yolda!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new BranchService(_f);
        _a = sb.Create(_s, new NewBranch("Merkez"));
        _b = sb.Create(_s, new NewBranch("Şantiye"));
        _m = new MaterialService(_f).Create(_s, new NewMaterial("YT-1", "Kablo"));
        _stock = new StockService(_f);
        _stock.ReceiveIn(_s, new[] { new StockLine(_m, 20m) }, Op(), _a);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private decimal At(string loc) => _stock.GetBalanceAt(_s, _m, loc);
    private string Durum(string doc) => (string)Tek($"SELECT transit_status FROM stock_documents WHERE id='{doc}';")!;

    // ══════════════════ YT1 — GÖNDERİM ══════════════════

    [Fact]
    public void YT1_Gonderim_Yolda_Kalir()
    {
        var r = _stock.YoldaGonder(_s, new[] { new StockLine(_m, 10m) }, _a, _b, Op(), "nakliye");
        Assert.Equal(10m, At(_a));
        Assert.Equal(0m, At(_b));
        Assert.Equal(10m, At(StockBalanceWriter.Transit));
        Assert.Equal(20m, _stock.GetBalance(_s, _m));   // firma toplamı değişmez
        Assert.Equal("yolda", Durum(r.DocumentId));
        var y = Assert.Single(_stock.Yoldakiler(_s));
        Assert.Equal(r.DocumentId, y.DocumentId);
        Assert.Equal(10m, Assert.Single(y.Kalemler).Yolda);
        Assert.Contains("Yolda", y.Ozet);
        Assert.Throws<NegativeStockException>(() => _stock.YoldaGonder(_s, new[] { new StockLine(_m, 11m) }, _a, _b, Op()));   // kaynakta 10 kaldı
    }

    // ══════════════════ YT2 — TAM TESLİM ══════════════════

    [Fact]
    public void YT2_Tam_Teslim()
    {
        var r = _stock.YoldaGonder(_s, new[] { new StockLine(_m, 10m) }, _a, _b, Op());
        var op = Op();
        _stock.TeslimAl(_s, r.DocumentId, new[] { new StockLine(_m, 10m) }, op);
        Assert.Equal(10m, At(_b));
        Assert.Equal(0m, At(StockBalanceWriter.Transit));
        Assert.Equal("teslim", Durum(r.DocumentId));
        Assert.Empty(_stock.Yoldakiler(_s));
        Assert.NotNull(Tek($"SELECT received_at FROM stock_documents WHERE id='{r.DocumentId}';"));
        _stock.TeslimAl(_s, r.DocumentId, new[] { new StockLine(_m, 10m) }, op);   // tekrar gönderim zararsız
        Assert.Equal(10m, At(_b));
        Assert.Throws<InvalidOperationException>(() => _stock.TeslimAl(_s, r.DocumentId, new[] { new StockLine(_m, 1m) }, Op()));
    }

    // ══════════════════ YT3 — EKSİK TESLİM + KAYIP ══════════════════

    [Fact]
    public void YT3_Eksik_Teslim_Ve_Kayip()
    {
        var r = _stock.YoldaGonder(_s, new[] { new StockLine(_m, 10m) }, _a, _b, Op());
        Assert.Contains("fazla", Assert.Throws<ArgumentException>(() =>
            _stock.TeslimAl(_s, r.DocumentId, new[] { new StockLine(_m, 11m) }, Op())).Message);
        _stock.TeslimAl(_s, r.DocumentId, new[] { new StockLine(_m, 7m) }, Op());
        Assert.Equal(7m, At(_b));
        Assert.Equal(3m, At(StockBalanceWriter.Transit));
        Assert.Equal("eksik", Durum(r.DocumentId));
        var k = Assert.Single(Assert.Single(_stock.Yoldakiler(_s)).Kalemler);
        Assert.Equal(7m, k.TeslimAlinan);
        Assert.Equal(3m, k.Yolda);

        Assert.Throws<ArgumentException>(() => _stock.YoldaKayipKapat(_s, r.DocumentId, " "));
        var depocu = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("stock", true, true, true, false) }));   // ters kayıt düğmesi YOK
        Assert.Throws<ForbiddenException>(() => _stock.YoldaKayipKapat(depocu, r.DocumentId, "kırıldı"));
        Assert.Equal(3m, At(StockBalanceWriter.Transit));
        _stock.YoldaKayipKapat(_s, r.DocumentId, "nakliyede kırıldı");
        Assert.Equal(0m, At(StockBalanceWriter.Transit));
        Assert.Equal(17m, _stock.GetBalance(_s, _m));   // 3 kayıp firma toplamından düştü
        Assert.Equal("kapandi", Durum(r.DocumentId));
        Assert.Empty(_stock.Yoldakiler(_s));
        Assert.Contains("nakliyede kırıldı", (string)Tek($"SELECT after_json FROM audit_logs WHERE entity_id='{r.DocumentId}' AND after_json LIKE '%yoldaKayip%';")!);
        _stock.YoldaKayipKapat(_s, r.DocumentId, "tekrar");   // zararsız
        Assert.Equal(17m, _stock.GetBalance(_s, _m));
    }

    // ══════════════════ YT4 — FİRMA AYRIMI ══════════════════

    [Fact]
    public void YT4_Baska_Firma()
    {
        var r = _stock.YoldaGonder(_s, new[] { new StockLine(_m, 5m) }, _a, _b, Op());
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('YTB','YTB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("YTB", "ytb", "Yolda!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "YTB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _stock.TeslimAl(b, r.DocumentId, new[] { new StockLine(_m, 5m) }, Op()));
        Assert.Throws<ForbiddenException>(() => _stock.YoldaKayipKapat(b, r.DocumentId, "x"));
        Assert.Empty(_stock.Yoldakiler(b));
        Assert.Equal(5m, At(StockBalanceWriter.Transit));
    }

    // ══════════════════ YT5 — TEK ADIM / İPTAL ══════════════════

    [Fact]
    public void YT5_Tek_Adimli_Transfer_Aynen()
    {
        var tek = _stock.Transfer(_s, new[] { new StockLine(_m, 4m) }, _a, _b, Op());
        Assert.Equal(4m, At(_b));
        Assert.True(Tek($"SELECT transit_status FROM stock_documents WHERE id='{tek.DocumentId}';") is DBNull);
        Assert.Empty(_stock.Yoldakiler(_s));
        Assert.Throws<InvalidOperationException>(() => _stock.YoldaKayipKapat(_s, tek.DocumentId, "x"));

        var yolda = _stock.YoldaGonder(_s, new[] { new StockLine(_m, 2m) }, _a, _b, Op());
        Assert.Throws<ForbiddenException>(() => _stock.ReverseDocument(_s, yolda.DocumentId, "yanlış"));   // transfer geri alınmaz
        Assert.Equal(2m, At(StockBalanceWriter.Transit));
    }

    // ══════════════════ YT6 — MIGRATION / ESKİ ŞEMA ══════════════════

    [Fact]
    public void YT6_Migration_Ve_Eski_Sema()
    {
        using (var c = _f.Create()) new Migration109_TransferInTransit().Up(c, null!);   // tekrar çalışabilir
        Exec("DROP INDEX ix_stock_documents_transit;");
        Exec("ALTER TABLE stock_documents DROP COLUMN transit_status;");
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _stock.YoldaGonder(_s, new[] { new StockLine(_m, 1m) }, _a, _b, Op())).Message);
        Assert.Equal(20m, At(_a));
        Assert.Empty(_stock.Yoldakiler(_s));
        _stock.Transfer(_s, new[] { new StockLine(_m, 1m) }, _a, _b, Op());
        Assert.Equal(1m, At(_b));
    }

    // ══════════════════ YT7 — LOKASYON ADI ══════════════════

    [Fact]
    public void YT7_Lokasyon_Kirliminda_Yolda_Adi()
    {
        _stock.YoldaGonder(_s, new[] { new StockLine(_m, 3m) }, _a, _b, Op());
        var y = Assert.Single(_stock.GetLocationBalances(_s, _m), l => l.LocationId == StockBalanceWriter.Transit);
        Assert.Equal("Yolda (teslim bekliyor)", y.LocationName);
    }

    // ══════════════════ YT8 — KAYNAK KİLİDİ ══════════════════

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void YT8_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        // 2026-10-25 (kullanıcı isteği, BİLİNÇLİ): şube dışı çıkış HER ZAMAN iki adımlı → seçenek dalı kalktı (TeslimKusurluTests TA6).
        Assert.DoesNotContain("if (d.Yolda == true)", api);
        Assert.Contains("var y = svc.Stock.YoldaGonder(s, lines, d.FromBranchId, d.ToBranchId,", api);
        Assert.Contains("app.MapGet(\"/api/stock/in-transit\"", api);
        Assert.Contains("app.MapPost(\"/api/stock/in-transit/{documentId}/receive\"", api);
        Assert.Contains("app.MapPost(\"/api/stock/in-transit/{documentId}/close-loss\"", api);

        var mvm = Oku("src/DepoWise.Desktop/ViewModels/StockEntryViewModel.cs");
        Assert.Contains("DesktopServices.Stock.YoldaGonder(", mvm);
        Assert.Contains("DesktopServices.Stock.TeslimAl(", mvm);
        Assert.Contains("DesktopServices.Stock.YoldaKayipKapat(", mvm);
        var axaml = Oku("src/DepoWise.Desktop/Views/StockEntryView.axaml");
        Assert.Contains("TeslimAlCommand", axaml);
        Assert.DoesNotContain("{Binding YoldaTransfer}", axaml);   // seçim kutusu kalktı (her zaman iki adımlı)

        var web = Oku("src/DepoWise.Web/Components/Pages/Stock.razor");
        Assert.DoesNotContain("yolda = _yolda", web);              // seçim kutusu kalktı (her zaman iki adımlı)
        Assert.Contains("/api/stock/in-transit", web);
        Assert.Contains("/close-loss", web);
    }
}
