using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class StockOperationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly SessionContext _admin;

    public StockOperationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_stk_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // STK-03: stok lokasyonu artık DOĞRULANIYOR (firmaya ait olmayan şube reddedilir) → testler
        // uydurma kimlik ("b1"/"b2") yerine GERÇEK şube kullanır. Üretim kuralı gevşetilmedi.
        var branches = new BranchService(_factory, _clock);
        _b1 = branches.Create(_admin, new NewBranch("Depo 1"));
        _b2 = branches.Create(_admin, new NewBranch("Depo 2"));
    }

    private readonly string _b1;
    private readonly string _b2;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));

    [Fact]
    public void RecomputeBalances_HareketDefterindenDogruHesaplar_BozukSnapshotEzmez()
    {
        var m = Mat("M-RC");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-a"); // +10
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "op-b");  // +5  → 15
        _stock.IssueOut(_admin, new[] { new StockLine(m, 3m) }, "op-c");   // -3  → 12

        // Bakiyeyi KASTEN boz (kötü/eski istemci snapshot'ı gibi) → recompute düzeltmeli
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        { cmd.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m;"; cmd.AddWithValue("@m", m); cmd.ExecuteNonQuery(); }

        _stock.RecomputeBalances("A"); // hareket defterinden yeniden hesapla (sunucu-otoriteli)
        Assert.Equal(12m, _stock.GetBalance(_admin, m)); // 10+5-3 = 12 (999 düzeltildi)
    }

    [Fact]
    public void Giris_BakiyeyiArtirir_BelgeNoUretir()
    {
        var m = Mat("M-1");
        var res = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-in-1");
        Assert.StartsWith("GIR-", res.DocNo);
        Assert.Equal(10m, _stock.GetBalance(_admin, m));
    }

    // ---- Şube-yetki (kullanıcı isteği 2026-08-05): şubeye bağlı kullanıcı yalnız kendi şubesinden çıkış ----
    [Fact]
    public void Cikis_SubeyeBagliKullanici_BaskaSubeden_Reddedilir()
    {
        var branches = new BranchService(_factory, _clock);
        var brA = branches.Create(_admin, new NewBranch("Şube A"), companyId: "A");
        var brB = branches.Create(_admin, new NewBranch("Şube B"), companyId: "A");
        var m = Mat("M-BR");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 100m) }, "in", branchId: brA);

        // Şube A'ya bağlı kullanıcı
        var userA = new SessionContext(_admin.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = brA };
        // Kendi şubesinden (A) çıkış → OK
        _stock.IssueOut(userA, new[] { new StockLine(m, 5m) }, "out-a", branchId: brA);
        // Başka şubeden (B) çıkış → REDDEDİLİR
        Assert.Throws<ForbiddenException>(() =>
            _stock.IssueOut(userA, new[] { new StockLine(m, 5m) }, "out-b", branchId: brB));

        // "Tüm Şubeler" (null → admin) herhangi bir şubeden çıkış yapabilir. (B'ye önce stok gelir — per-branch 8b.)
        var userAll = new SessionContext(_admin.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = null };
        _stock.ReceiveIn(userAll, new[] { new StockLine(m, 20m) }, "in-b", branchId: brB);
        _stock.IssueOut(userAll, new[] { new StockLine(m, 5m) }, "out-all", branchId: brB);
    }

    // ---- Per-branch stok (madde 8b, 2026-08-05): stok olmayan şubeden çıkış REDDEDİLİR ----
    [Fact]
    public void Cikis_StokOlmayanSubeden_Reddedilir()
    {
        var branches = new BranchService(_factory, _clock);
        var brA = branches.Create(_admin, new NewBranch("Şube A"), companyId: "A");
        var brB = branches.Create(_admin, new NewBranch("Şube B"), companyId: "A");
        var m = Mat("M-PB");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 50m) }, "in-a", branchId: brA);   // yalnız A'ya 50

        // _admin = Tüm Şubeler (şube-yetki serbest) → per-branch STOK kontrolünü izole test ederiz.
        // Şube B'de stok YOK (firma-geneli 50 olsa bile) → çıkış REDDEDİLİR
        Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 10m) }, "out-b", branchId: brB));
        // Şube A'da stok VAR → çıkış OK; firma-geneli 50-10=40
        _stock.IssueOut(_admin, new[] { new StockLine(m, 10m) }, "out-a", branchId: brA);
        Assert.Equal(40m, _stock.GetBalance(_admin, m));
        // A'da 40 var; A'dan 45 çıkış (şubede yetersiz) → REDDEDİLİR
        Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 45m) }, "out-a2", branchId: brA));
    }

    [Fact]
    public void Cikis_BakiyeyiAzaltir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        _stock.IssueOut(_admin, new[] { new StockLine(m, 4m) }, "out");
        Assert.Equal(6m, _stock.GetBalance(_admin, m));
    }

    [Fact]
    public void Cikis_NegatifStok_Engellenir_BakiyeDegismez()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 3m) }, "in");
        Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 5m) }, "out"));
        Assert.Equal(3m, _stock.GetBalance(_admin, m)); // rollback → değişmedi
    }

    [Fact]
    public void Idempotency_AyniOperation_IkinciHareketUretmez()
    {
        var m = Mat("M-1");
        var r1 = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-dup");
        var r2 = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-dup");
        Assert.Equal(r1.DocumentId, r2.DocumentId);
        Assert.Equal(10m, _stock.GetBalance(_admin, m)); // 20 değil

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Transfer_KaynakCikisHedefGiris_AtomikVeGrupli()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in", branchId: _b1);   // kaynak şubeye stok (per-branch 8b)
        var res = _stock.Transfer(_admin, m, 4m, _b1, _b2, "op-trf");
        Assert.StartsWith("TRF-", res.DocNo);
        Assert.Equal(10m, _stock.GetBalance(_admin, m)); // transfer toplam stoğu değiştirmez

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE document_id=@d AND movement_type='transfer';";
        cmd.AddWithValue("@d", res.DocumentId);
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar())); // çıkış + giriş
    }

    [Fact]
    public void Transfer_YetersizStok_Engellenir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 2m) }, "in");
        Assert.Throws<NegativeStockException>(() => _stock.Transfer(_admin, m, 5m, _b1, _b2, "op-trf"));
        Assert.Equal(2m, _stock.GetBalance(_admin, m));
    }

    // ---- Transfer per-branch bakiye (kullanıcı isteği 2026-08-06, madde 3): kaynak DÜŞER, hedef ARTAR ----
    [Fact]
    public void Transfer_KaynakSubeDuser_HedefArtar_PerBranch()
    {
        var m = Mat("M-TRB");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in", branchId: _b1);   // b1:10
        _stock.Transfer(_admin, m, 4m, _b1, _b2, "op-trb");                                // b1-4, b2+4
        Assert.Equal(6m, BranchBal(m, _b1));   // kaynak 10-4
        Assert.Equal(4m, BranchBal(m, _b2));   // hedef +4
        Assert.Equal(10m, _stock.GetBalance(_admin, m)); // firma-geneli değişmez
    }

    // ---- Transfer GERİ ALINAMAZ (kullanıcı isteği 2026-08-06, madde 2): ReverseDocument reddeder ----
    [Fact]
    public void Transfer_GeriAlinamaz_Reddedilir()
    {
        var m = Mat("M-TRX");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in", branchId: _b1);
        var res = _stock.Transfer(_admin, m, 3m, _b1, _b2, "op-trx");
        var admin = AdminWithReverse();
        var ex = Assert.Throws<ForbiddenException>(() => _stock.ReverseDocument(admin, res.DocumentId, "x"));
        Assert.Contains("Transfer geri alınamaz", ex.Message);
        Assert.Equal(10m, _stock.GetBalance(_admin, m));            // firma-geneli bakiye değişmedi
        Assert.Equal(7m, BranchBal(m, _b1));               // transfer etkisi korunur: kaynak 10-3
        Assert.Equal(3m, BranchBal(m, _b2));               // hedef +3
    }

    // Test yardımcısı: bir (malzeme, şube) için hareket defteri toplamı (giriş +, çıkış −).
    private decimal BranchBal(string mat, string branch)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT direction, quantity FROM stock_movements WHERE material_id=@m AND branch_id=@b;";
        cmd.AddWithValue("@m", mat); cmd.AddWithValue("@b", branch);
        decimal t = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read()) t += r.GetInt64(0) * Money.Parse(r.GetString(1));
        return t;
    }

    [Fact]
    public void Sayim_FarkHareketi_GerekceliUretir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        // Fiziksel sayım 7 → fark -3
        _stock.Count(_admin, new[] { new CountLine(m, 7m) }, "Fire", "op-count");
        Assert.Equal(7m, _stock.GetBalance(_admin, m));

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT diff_qty, reason FROM stock_count_lines WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(-3m, Money.Parse(r.GetString(0)));
        Assert.Equal("Fire", r.GetString(1));
    }

    [Fact]
    public void Sayim_GerekceZorunlu()
    {
        var m = Mat("M-1");
        Assert.Throws<ArgumentException>(() => _stock.Count(_admin, new[] { new CountLine(m, 7m) }, "", "op"));
    }

    [Fact]
    public void Iptal_TersHareket_BakiyeyiGeriAlir_BelgeIptal()
    {
        var m = Mat("M-1");
        var doc = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        Assert.Equal(10m, _stock.GetBalance(_admin, m));

        var admin = AdminWithReverse();
        _stock.ReverseDocument(admin, doc.DocumentId, "Hatalı giriş");
        Assert.Equal(0m, _stock.GetBalance(_admin, m)); // ters hareketle geri alındı

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM stock_documents WHERE id=@d;";
        cmd.AddWithValue("@d", doc.DocumentId);
        Assert.Equal("cancelled", cmd.ExecuteScalar());

        // Orijinal hareket fiziksel silinmedi (is_reversed=1) + ters hareket eklendi
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE document_id=@d;";
        cmd2.AddWithValue("@d", doc.DocumentId);
        Assert.Equal(2L, Convert.ToInt64(cmd2.ExecuteScalar())); // orijinal + ters
    }

    [Fact]
    public void Iptal_Idempotent_IkinciKezNoOp()
    {
        var m = Mat("M-1");
        var doc = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        var admin = AdminWithReverse();
        _stock.ReverseDocument(admin, doc.DocumentId, "x");
        _stock.ReverseDocument(admin, doc.DocumentId, "x"); // tekrar → no-op
        Assert.Equal(0m, _stock.GetBalance(_admin, m));
    }

    [Fact]
    public void EsZamanli_IkiCikis_NegatifStokOlusturamaz()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "in"); // yalnız bir 5'lik çıkışa yeter

        int ok = 0, failed = 0;
        Parallel.For(0, 2, i =>
        {
            try
            {
                _stock.IssueOut(_admin, new[] { new StockLine(m, 5m) }, "out-" + i);
                Interlocked.Increment(ref ok);
            }
            catch (NegativeStockException) { Interlocked.Increment(ref failed); }
            catch (Microsoft.Data.Sqlite.SqliteException) { Interlocked.Increment(ref failed); }
        });

        Assert.Equal(1, ok);
        Assert.Equal(1, failed);
        Assert.Equal(0m, _stock.GetBalance(_admin, m)); // asla negatif
    }

    [Fact]
    public void DenyByDefault_YetkisizStokIslemi()
    {
        var m = Mat("M-1");
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _stock.ReceiveIn(noPerm, new[] { new StockLine(m, 1m) }, "op"));
    }

    private SessionContext AdminWithReverse()
        => new(_admin.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty); // admin bypass buton dahil

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
