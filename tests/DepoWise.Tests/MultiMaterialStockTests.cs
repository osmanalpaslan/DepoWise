using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ÇOK MALZEMELİ STOK İŞLEMİ (İş #8, 2026-08-09).
///
/// Başlangıç: <c>ReceiveIn</c> ve <c>IssueOut</c> zaten çok satırlıydı ama <c>Transfer</c> TEK malzemeydi;
/// API'nin üç ucu da tek malzeme alıyordu → 10 malzeme veren depocu 10 ayrı belge açmak zorundaydı.
///
/// Buradaki asıl iddia: <b>tek belge, tek transaction</b>. Bir satır bile başarısız olursa (ör. negatif stok)
/// belgenin TAMAMI geri alınır — yarım transfer/çıkış oluşmaz.
/// </summary>
public class MultiMaterialStockTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly BranchService _branches;
    private readonly UserService _users;
    private readonly SessionContext _a;
    private readonly string _m1, _m2, _m3, _branchA, _branchB;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public MultiMaterialStockTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_multimat_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _users = new UserService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);

        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }
        var uid = _users.EnsureInitialAdmin("A", "admin_a", "Test!2026", RoleKeys.CompanyAdmin);
        _a = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _branchA = _branches.Create(_a, new NewBranch("Merkez"));
        _branchB = _branches.Create(_a, new NewBranch("Şantiye"));

        _m1 = _materials.Create(_a, new NewMaterial("M-1", "Filtre"));
        _m2 = _materials.Create(_a, new NewMaterial("M-2", "Yağ"));
        _m3 = _materials.Create(_a, new NewMaterial("M-3", "Conta"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    private void Open(string materialId, decimal qty, string? branchId = null)
        => _opening.RecordOpening(_a, materialId, qty, "op-" + Guid.NewGuid().ToString("N"), branchId: branchId);

    private decimal Balance(string materialId) => _stock.GetBalance(_a, materialId);

    private int MovementCount(string documentId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE document_id=@d;";
        cmd.AddWithValue("@d", documentId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int DocumentCount()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_documents WHERE company_id='A';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── ÇIKIŞ (IssueOut) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Cok_malzemeli_cikis_TEK_belgede_islenir()
    {
        Open(_m1, 100m); Open(_m2, 100m); Open(_m3, 100m);
        var docsBefore = DocumentCount();

        var res = _stock.IssueOut(_a, new[]
        {
            new StockLine(_m1, 5m), new StockLine(_m2, 3m), new StockLine(_m3, 1m),
        }, "op-cikis");

        Assert.Equal(docsBefore + 1, DocumentCount());   // 3 malzeme → 3 belge DEĞİL, 1 belge
        Assert.Equal(3, MovementCount(res.DocumentId));
        Assert.Equal(95m, Balance(_m1));
        Assert.Equal(97m, Balance(_m2));
        Assert.Equal(99m, Balance(_m3));
    }

    [Fact]
    public void Cikista_BIR_satir_bile_yetersizse_TAMAMI_geri_alinir()
    {
        Open(_m1, 100m); Open(_m2, 2m);   // m2 yetersiz
        var docsBefore = DocumentCount();

        Assert.ThrowsAny<Exception>(() => _stock.IssueOut(_a, new[]
        {
            new StockLine(_m1, 5m), new StockLine(_m2, 50m),
        }, "op-yarim"));

        // ASIL İDDİA: m1'den de düşülmemiş olmalı — yarım belge yok.
        Assert.Equal(100m, Balance(_m1));
        Assert.Equal(2m, Balance(_m2));
        Assert.Equal(docsBefore, DocumentCount());
    }

    [Fact]
    public void Cok_malzemeli_cikis_TEK_islemde_iptal_edilir()
    {
        Open(_m1, 100m); Open(_m2, 100m);
        var res = _stock.IssueOut(_a, new[] { new StockLine(_m1, 5m), new StockLine(_m2, 3m) }, "op-iptal");

        _stock.ReverseDocument(_a, res.DocumentId, "Test iptali");

        Assert.Equal(100m, Balance(_m1));   // iki satır da geri geldi
        Assert.Equal(100m, Balance(_m2));
    }

    // ── TRANSFER ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cok_malzemeli_transfer_TEK_belgede_islenir()
    {
        Open(_m1, 100m, _branchA); Open(_m2, 100m, _branchA);
        var docsBefore = DocumentCount();

        var res = _stock.Transfer(_a, new[] { new StockLine(_m1, 10m), new StockLine(_m2, 4m) },
            _branchA, _branchB, "op-transfer");

        Assert.Equal(docsBefore + 1, DocumentCount());
        Assert.Equal(4, MovementCount(res.DocumentId));   // her malzeme için çıkış + giriş = 2×2

        // Transfer firma toplamını DEĞİŞTİRMEZ (şubeler arası taşıma).
        Assert.Equal(100m, Balance(_m1));
        Assert.Equal(100m, Balance(_m2));
    }

    [Fact]
    public void Transferde_BIR_satir_bile_yetersizse_TAMAMI_geri_alinir()
    {
        Open(_m1, 100m, _branchA); Open(_m2, 2m, _branchA);
        var docsBefore = DocumentCount();

        Assert.ThrowsAny<Exception>(() => _stock.Transfer(_a, new[]
        {
            new StockLine(_m1, 10m), new StockLine(_m2, 50m),
        }, _branchA, _branchB, "op-yarim-transfer"));

        Assert.Equal(docsBefore, DocumentCount());
        Assert.Equal(0, BranchBalance(_m1, _branchB));   // hedef şubeye HİÇBİR ŞEY geçmedi
    }

    [Fact]
    public void Tek_malzemeli_transfer_ESKI_imzayla_calismaya_devam_eder()
    {
        // Geriye uyumluluk: eski çağrı yerleri (masaüstü paketleri, API) bozulmamalı.
        Open(_m1, 100m, _branchA);
        _stock.Transfer(_a, _m1, 10m, _branchA, _branchB, "op-eski");

        Assert.Equal(10m, BranchBalance(_m1, _branchB));
        Assert.Equal(90m, BranchBalance(_m1, _branchA));
    }

    [Fact]
    public void Bos_liste_reddedilir()
    {
        Assert.Throws<ArgumentException>(() =>
            _stock.Transfer(_a, System.Array.Empty<StockLine>(), _branchA, _branchB, "op-bos"));
    }

    [Fact]
    public void Sifir_veya_negatif_miktarli_satir_reddedilir()
    {
        Open(_m1, 100m, _branchA);
        Assert.Throws<ArgumentException>(() =>
            _stock.Transfer(_a, new[] { new StockLine(_m1, 10m), new StockLine(_m2, 0m) },
                _branchA, _branchB, "op-sifir"));
    }

    private decimal BranchBalance(string materialId, string branchId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT direction, quantity FROM stock_movements WHERE company_id='A' AND material_id=@m AND branch_id=@b;";
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@b", branchId);
        decimal total = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read()) total += r.GetInt64(0) * Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
        return total;
    }
}
