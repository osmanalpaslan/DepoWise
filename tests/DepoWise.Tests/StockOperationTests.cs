using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
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
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));

    [Fact]
    public void Giris_BakiyeyiArtirir_BelgeNoUretir()
    {
        var m = Mat("M-1");
        var res = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-in-1");
        Assert.StartsWith("GIR-", res.DocNo);
        Assert.Equal(10m, _stock.GetBalance(m));
    }

    [Fact]
    public void Cikis_BakiyeyiAzaltir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        _stock.IssueOut(_admin, new[] { new StockLine(m, 4m) }, "out");
        Assert.Equal(6m, _stock.GetBalance(m));
    }

    [Fact]
    public void Cikis_NegatifStok_Engellenir_BakiyeDegismez()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 3m) }, "in");
        Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 5m) }, "out"));
        Assert.Equal(3m, _stock.GetBalance(m)); // rollback → değişmedi
    }

    [Fact]
    public void Idempotency_AyniOperation_IkinciHareketUretmez()
    {
        var m = Mat("M-1");
        var r1 = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-dup");
        var r2 = _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "op-dup");
        Assert.Equal(r1.DocumentId, r2.DocumentId);
        Assert.Equal(10m, _stock.GetBalance(m)); // 20 değil

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE material_id=$m;";
        cmd.Parameters.AddWithValue("$m", m);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Transfer_KaynakCikisHedefGiris_AtomikVeGrupli()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        var res = _stock.Transfer(_admin, m, 4m, "branch-1", "branch-2", "op-trf");
        Assert.StartsWith("TRF-", res.DocNo);
        Assert.Equal(10m, _stock.GetBalance(m)); // transfer toplam stoğu değiştirmez

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE document_id=$d AND movement_type='transfer';";
        cmd.Parameters.AddWithValue("$d", res.DocumentId);
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar())); // çıkış + giriş
    }

    [Fact]
    public void Transfer_YetersizStok_Engellenir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 2m) }, "in");
        Assert.Throws<NegativeStockException>(() => _stock.Transfer(_admin, m, 5m, "b1", "b2", "op-trf"));
        Assert.Equal(2m, _stock.GetBalance(m));
    }

    [Fact]
    public void Sayim_FarkHareketi_GerekceliUretir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 10m) }, "in");
        // Fiziksel sayım 7 → fark -3
        _stock.Count(_admin, new[] { new CountLine(m, 7m) }, "Fire", "op-count");
        Assert.Equal(7m, _stock.GetBalance(m));

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT diff_qty, reason FROM stock_count_lines WHERE material_id=$m;";
        cmd.Parameters.AddWithValue("$m", m);
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
        Assert.Equal(10m, _stock.GetBalance(m));

        var admin = AdminWithReverse();
        _stock.ReverseDocument(admin, doc.DocumentId, "Hatalı giriş");
        Assert.Equal(0m, _stock.GetBalance(m)); // ters hareketle geri alındı

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM stock_documents WHERE id=$d;";
        cmd.Parameters.AddWithValue("$d", doc.DocumentId);
        Assert.Equal("cancelled", cmd.ExecuteScalar());

        // Orijinal hareket fiziksel silinmedi (is_reversed=1) + ters hareket eklendi
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE document_id=$d;";
        cmd2.Parameters.AddWithValue("$d", doc.DocumentId);
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
        Assert.Equal(0m, _stock.GetBalance(m));
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
        Assert.Equal(0m, _stock.GetBalance(m)); // asla negatif
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
