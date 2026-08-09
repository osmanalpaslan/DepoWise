using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// TOPLU BAKİYE OKUMA (Faz S / İş #11-A, 2026-08-09).
///
/// <c>/api/materials</c> her satır için ayrı <c>GetBalance</c> çağırıyordu → sayfa başına 200'e kadar
/// sorgu (N+1). Sunucu PostgreSQL'e (ağ üzerinden) geçtiği için her sorgu bir gidiş-dönüş; üstelik bu uç
/// Stok/Talep/Bakım ekranlarının hızlı-arama seçicisidir (sık çağrılır).
///
/// Bu testlerin asıl işi <b>sonucun tek tek okumayla BİREBİR aynı kalmasıdır</b> — hızlandırma uğruna
/// yanlış bakiye göstermek en kötü sonuç olurdu.
/// </summary>
public class BulkBalanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly UserService _users;
    private readonly SessionContext _a, _b;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public BulkBalanceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_bulkbal_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _users = new UserService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);

        Company("A"); Company("B");
        _a = Admin("A", "kul_a");
        _b = Admin("B", "kul_b");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    private void Company(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private SessionContext Admin(string company, string user)
    {
        var uid = _users.EnsureInitialAdmin(company, user, "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Fact]
    public void Toplu_okuma_TEK_TEK_okumayla_AYNI_sonucu_verir()
    {
        var ids = new List<string>();
        for (int i = 1; i <= 30; i++)
        {
            var id = _materials.Create(_a, new NewMaterial($"M-{i:00}", $"Malzeme {i}"));
            ids.Add(id);
            if (i % 3 != 0) _opening.RecordOpening(_a, id, i * 1.5m, "op-" + i);   // bazılarında bilinçli olarak HİÇ hareket yok
        }

        var toplu = _stock.GetBalances(_a, ids);

        foreach (var id in ids)
        {
            var tekTek = _stock.GetBalance(_a, id);
            var topludan = toplu.TryGetValue(id, out var q) ? q : 0m;
            Assert.Equal(tekTek, topludan);   // ← asıl iddia
        }
    }

    [Fact]
    public void Hareketi_OLMAYAN_malzeme_sozlukte_YOKTUR_ve_cagiran_0_sayar()
    {
        var id = _materials.Create(_a, new NewMaterial("M-BOS", "Hiç hareket yok"));
        var toplu = _stock.GetBalances(_a, new[] { id });

        Assert.False(toplu.ContainsKey(id));
        Assert.Equal(0m, _stock.GetBalance(_a, id));   // tek tek okuma da 0 verir → davranış aynı
    }

    [Fact]
    public void BASKA_firmanin_malzemesi_bakiye_DONDURMEZ()
    {
        var idB = _materials.Create(_b, new NewMaterial("M-B", "B malzemesi"));
        _opening.RecordOpening(_b, idB, 100m, "op-b");

        var toplu = _stock.GetBalances(_a, new[] { idB });
        Assert.Empty(toplu);   // A, B'nin bakiyesini GÖREMEZ
    }

    [Fact]
    public void Karisik_istekte_yalniz_KENDI_firmasinin_bakiyesi_doner()
    {
        var idA = _materials.Create(_a, new NewMaterial("M-A", "A malzemesi"));
        _opening.RecordOpening(_a, idA, 7m, "op-a");
        var idB = _materials.Create(_b, new NewMaterial("M-B2", "B malzemesi"));
        _opening.RecordOpening(_b, idB, 100m, "op-b2");

        var toplu = _stock.GetBalances(_a, new[] { idA, idB });

        Assert.Equal(7m, toplu[idA]);
        Assert.False(toplu.ContainsKey(idB));
    }

    [Fact]
    public void Bos_liste_sorgu_ATMAZ_ve_bos_sonuc_verir()
        => Assert.Empty(_stock.GetBalances(_a, System.Array.Empty<string>()));

    [Fact]
    public void Cikis_sonrasi_toplu_bakiye_guncel()
    {
        var id = _materials.Create(_a, new NewMaterial("M-HRK", "Hareketli"));
        _opening.RecordOpening(_a, id, 100m, "op-1");
        _stock.IssueOut(_a, new[] { new StockLine(id, 40m) }, "op-2");

        Assert.Equal(60m, _stock.GetBalances(_a, new[] { id })[id]);
    }
}
