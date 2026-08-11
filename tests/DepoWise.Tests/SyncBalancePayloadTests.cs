using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// SNK-11 (2026-08-11) — TÜRETİLMİŞ BAKİYE SENKRON PAKETİNDEN ÇIKARILDI.
///
/// Değişikliğin TAMAMI tek satırdır: <c>BusinessSyncService.Tables</c> listesinden
/// <c>stock_balances</c> çıkarıldı. Bu dosya hem faydayı (paket küçüldü) hem de hiçbir şeyin
/// bozulmadığını (defter otoriter, çevrimdışı çalışma sürüyor) kilitler.
///
/// 🔒 TABLO KALDIRILMADI: yerel SQLite'ta <c>stock_balances</c> aynen duruyor; masaüstü çevrimdışı
/// stok işlemleri ve bakiye görüntüleme bundan etkilenmiyor (testler bunu ayrıca kanıtlıyor).
/// </summary>
public class SyncBalancePayloadTests : IDisposable
{
    private readonly string _localPath, _serverPath;
    private readonly SqliteConnectionFactory _local, _server;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _oturum;
    private readonly string _depoA, _depoB, _mat;

    public SyncBalancePayloadTests()
    {
        _localPath = Path.Combine(Path.GetTempPath(), "dw_snk11_" + Guid.NewGuid().ToString("N") + ".db");
        _serverPath = Path.Combine(Path.GetTempPath(), "dw_snk11_srv_" + Guid.NewGuid().ToString("N") + ".db");
        _local = new SqliteConnectionFactory(_localPath);
        _server = new SqliteConnectionFactory(_serverPath);
        new MigrationRunner(_local).Run();
        new MigrationRunner(_server).Run();
        Seed(_local); Seed(_server);

        _stock = new StockService(_local, _clock);
        _opening = new OpeningStockService(_local, _clock);
        var users = new UserService(_local, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var yonetici = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_local, _clock);
        _depoA = branches.Create(yonetici, new NewBranch("Depo A"));
        _depoB = branches.Create(yonetici, new NewBranch("Depo B"));
        _mat = new MaterialService(_local, _clock).Create(yonetici, new NewMaterial("SNK11-1", "Malzeme"));
        _oturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static void Seed(SqliteConnectionFactory f)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);";
        cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private string Snapshot() => new BusinessSyncService(_local, _clock).BuildSnapshot("A");

    private StockService SyncToServer()
    {
        using (var doc = JsonDocument.Parse(Snapshot()))
            new BusinessSyncService(_server, _clock).Apply("A", doc.RootElement);
        var srv = new StockService(_server, _clock);
        srv.RecomputeBalances("A");
        return srv;
    }

    // ── 1-2. Sözleşme ve fayda ───────────────────────────────────────────────────────────

    /// <summary>1 — SÖZLEŞME: paket <c>stock_movements</c> taşır, <c>stock_balances</c> TAŞIMAZ.</summary>
    [Fact]
    public void Paket_Defteri_Tasir_Bakiyeyi_TASIMAZ()
    {
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);

        using var doc = JsonDocument.Parse(Snapshot());
        var tables = doc.RootElement.GetProperty("tables");

        Assert.True(tables.TryGetProperty("stock_movements", out _));
        Assert.False(tables.TryGetProperty("stock_balances", out _));
        Assert.DoesNotContain("stock_balances", BusinessSyncService.Tables);
        Assert.Contains("stock_movements", BusinessSyncService.Tables);
    }

    /// <summary>2 — FAYDA: bakiye satırı sayısı arttıkça paket BÜYÜMEZ. 50 malzemenin bakiyesi
    /// yerelde varken pakette bakiye bölümü hiç yoktur.</summary>
    [Fact]
    public void Bakiye_Satirlari_Paketi_Buyutmez()
    {
        var materials = new MaterialService(_local, _clock);
        for (int i = 0; i < 50; i++)
        {
            var m = materials.Create(_oturum, new NewMaterial($"SNK11-P{i}", $"Malzeme {i}"));
            _stock.ReceiveIn(_oturum, new[] { new StockLine(m, 1m) }, Op(), branchId: _depoA);
        }

        var paket = Snapshot();

        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_balances WHERE company_id='A';";
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 50, "Yerelde bakiye satırları oluşmalı.");
        Assert.DoesNotContain("\"stock_balances\"", paket);
    }

    // ── 3-5. Doğruluk: defter otoriter ───────────────────────────────────────────────────

    /// <summary>3 (Test A) — NORMAL SENKRON: giriş + çıkış + transfer + sayım sonrası sunucu bakiyeyi
    /// DEFTERDEN kurar; lokasyon kırılımı birebir korunur.</summary>
    [Fact]
    public void Normal_Senkron_Sunucuda_Dogru_Kirilimi_Uretir()
    {
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);
        _stock.IssueOut(_oturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoA);
        _stock.Transfer(_oturum, _mat, 6m, _depoA, _depoB, Op());
        _stock.Count(_oturum, new[] { new CountLine(_mat, 12m) }, "sayım", Op(), branchId: _depoA);

        var srv = SyncToServer();

        Assert.Equal(_stock.GetBalanceAt(_oturum, _mat, _depoA), srv.GetBalanceAt(_oturum, _mat, _depoA));
        Assert.Equal(_stock.GetBalanceAt(_oturum, _mat, _depoB), srv.GetBalanceAt(_oturum, _mat, _depoB));
        Assert.Equal(_stock.GetBalance(_oturum, _mat), srv.GetBalance(_oturum, _mat));
        Assert.Equal(12m, srv.GetBalanceAt(_oturum, _mat, _depoA));
    }

    /// <summary>4 (Test C) — KASTEN BOZUK BAKİYE sunucuya BULAŞMAZ: yerel bakiye 999 yapılsa bile
    /// sunucuda defterin değeri oluşur. Bakiye artık paketle gitmediği için bu daha da kesindir.</summary>
    [Fact]
    public void Kasten_Bozuk_Bakiye_Sunucuya_Bulasmaz()
    {
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);

        using (var conn = _local.Create())
        using (var bad = conn.CreateCommand())
        {
            bad.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m AND location_id=@l;";
            bad.AddWithValue("@m", _mat); bad.AddWithValue("@l", _depoA);
            Assert.Equal(1, bad.ExecuteNonQuery());
        }

        var srv = SyncToServer();
        Assert.Equal(10m, srv.GetBalanceAt(_oturum, _mat, _depoA));
    }

    /// <summary>5 (Test D) — YALNIZ BAKİYE DEĞİŞTİYSE senkronda taşınacak bir şey yoktur; sunucu
    /// etkilenmez. Ama YEREL çalışma bozulmaz — yerel bakiye okunmaya devam eder (tablo duruyor).</summary>
    [Fact]
    public void Yalniz_Bakiye_Degisirse_Sunucu_Etkilenmez_Yerel_Calismaya_Devam_Eder()
    {
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        Assert.Equal(10m, SyncToServer().GetBalanceAt(_oturum, _mat, _depoA));

        using (var conn = _local.Create())
        using (var bad = conn.CreateCommand())
        {
            bad.CommandText = "UPDATE stock_balances SET quantity='777' WHERE material_id=@m AND location_id=@l;";
            bad.AddWithValue("@m", _mat); bad.AddWithValue("@l", _depoA);
            bad.ExecuteNonQuery();
        }

        Assert.DoesNotContain("777", Snapshot());                          // paket taşımıyor
        Assert.Equal(777m, _stock.GetBalanceAt(_oturum, _mat, _depoA));    // yerel okuma çalışıyor
        Assert.Equal(10m, SyncToServer().GetBalanceAt(_oturum, _mat, _depoA));   // sunucu defterin doğrusunda
    }

    // ── 6-7. Çevrimdışı akışlar ve yakınsama ─────────────────────────────────────────────

    /// <summary>6 — ÇEVRİMDIŞI AKIŞLARIN TAMAMI çalışmaya devam ediyor: giriş · çıkış · ters kayıt ·
    /// transfer · sayım · STK-08 dağıtımı · bakiye kırılımı görüntüleme. Hiçbiri ağa çıkmıyor.</summary>
    [Fact]
    public void Cevrimdisi_Tum_Stok_Akislari_Calismaya_Devam_Ediyor()
    {
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);
        var cikis = _stock.IssueOut(_oturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);
        Assert.Equal(15m, _stock.GetBalanceAt(_oturum, _mat, _depoA));

        _stock.ReverseDocument(_oturum, cikis.DocumentId, "hatalı çıkış");
        Assert.Equal(20m, _stock.GetBalanceAt(_oturum, _mat, _depoA));

        _stock.Transfer(_oturum, _mat, 8m, _depoA, _depoB, Op());
        Assert.Equal(12m, _stock.GetBalanceAt(_oturum, _mat, _depoA));
        Assert.Equal(8m, _stock.GetBalanceAt(_oturum, _mat, _depoB));

        _stock.Count(_oturum, new[] { new CountLine(_mat, 10m) }, "sayım", Op(), branchId: _depoA);
        Assert.Equal(10m, _stock.GetBalanceAt(_oturum, _mat, _depoA));

        var m2 = new MaterialService(_local, _clock).Create(_oturum, new NewMaterial("SNK11-2", "Dağıtımlık"));
        _opening.RecordOpening(_oturum, m2, 30m, Op());   // ATANMAMIŞ
        Assert.Single(_stock.ListUnassigned(_oturum));
        _stock.DistributeUnassigned(_oturum, new[] { new StockLine(m2, 12m) }, _depoB, Op());
        Assert.Equal(12m, _stock.GetBalanceAt(_oturum, m2, _depoB));
        Assert.Equal(18m, _stock.GetBalanceAt(_oturum, m2, StockBalanceWriter.Unassigned));

        var kirilim = _stock.GetLocationBalances(_oturum, _mat);
        Assert.Equal(_stock.GetBalance(_oturum, _mat), kirilim.Sum(x => x.Quantity));
    }

    /// <summary>7 (Test B + E) — offline→online→offline→online: KOPYA hareket yok, lokasyon kaybı yok,
    /// bakiye yakınsıyor. Aynı paket tekrar gönderildiğinde sonuç değişmiyor.</summary>
    [Fact]
    public void Offline_Online_Dongusu_Yakinsiyor_Kopya_Uretmiyor()
    {
        _stock.ReceiveIn(_oturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        SyncToServer();

        _stock.Transfer(_oturum, _mat, 4m, _depoA, _depoB, Op());
        SyncToServer();
        var srv = SyncToServer();   // AYNI paket tekrar

        Assert.Equal(6m, srv.GetBalanceAt(_oturum, _mat, _depoA));
        Assert.Equal(4m, srv.GetBalanceAt(_oturum, _mat, _depoB));
        Assert.Equal(Count(_local), Count(_server));

        static long Count(IDbConnectionFactory f)
        {
            using var c = f.Create();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id='A';";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_localPath); } catch { }
        try { File.Delete(_serverPath); } catch { }
    }
}
