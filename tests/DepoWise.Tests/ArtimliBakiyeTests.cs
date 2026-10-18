using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-21 (AR-GE 2026-10-18, 05 F-108) — ARTIMLI BAKİYE YENİDEN HESABI ═══
/// Eskiden her gönderimden (ve çekmeden) sonra firmanın TÜM stok defteri okunup bakiyeler baştan yazılıyordu (büyük firmada
/// 12,1 sn + yazma kilidi). Artık yalnız paketteki stok hareketlerinin malzemeleri hesaplanır.
///
///  ART1 — Kapsam: yalnız verilen malzeme düzelir; kapsam dışındaki (kasten bozulmuş) bakiyeye DOKUNULMAZ
///  ART2 — Boş küme hiçbir şey yapmaz; parametresiz çağrı eskisi gibi TAM hesap (hepsi düzelir)
///  ART3 — Artımlı sonuç tam hesapla BİREBİR aynı (çok lokasyon, ters kayıt, hayalet bakiye temizliği dahil)
///  ART4 — Paketten malzeme çıkarma: yalnız stock_movements'taki malzemeler; hareket yoksa boş
///  ART5 — Çekme (ApplyPull): stok hareketi olmayan paket bakiyelere dokunmaz; hareketli paket o malzemeyi düzeltir
/// </summary>
public class ArtimliBakiyeTests : IDisposable
{
    private const string Co = "ART";
    private readonly string _yol;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly StockService _stok;
    private readonly string _depo1 = "D1", _depo2 = "D2";

    public ArtimliBakiyeTests()
    {
        _yol = Path.Combine(Path.GetTempPath(), "dw_art_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);" +
             $"INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) VALUES('{_depo1}','{Co}','Depo 1','branch',1,1,1,0),('{_depo2}','{Co}','Depo 2','branch',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin_art", "admin123", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _stok = new StockService(_f);
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(_yol); } catch { } }

    private void Exec(string sql)
    {
        using var c = _f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private Dictionary<string, string> Bakiyeler()
    {
        using var c = _f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT material_id || '@' || location_id, quantity FROM stock_balances WHERE company_id='{Co}' ORDER BY 1;";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, string>();
        while (r.Read()) d[r.GetString(0)] = Money.Serialize(Money.Parse(r.GetString(1)));
        return d;
    }

    private (string A, string B) Hazirla()
    {
        var mat = new MaterialService(_f);
        var a = mat.Create(_s, new NewMaterial("A-1", "A"));
        var b = mat.Create(_s, new NewMaterial("B-1", "B"));
        _stok.ReceiveIn(_s, new[] { new StockLine(a, 10m), new StockLine(b, 7m) }, "op-1", branchId: _depo1);
        _stok.ReceiveIn(_s, new[] { new StockLine(a, 3m) }, "op-2", branchId: _depo2);
        _stok.IssueOut(_s, new[] { new StockLine(a, 4m) }, "op-3", branchId: _depo1);
        return (a, b);
    }

    [Fact]
    public void ART1_Yalniz_Kapsamdaki_Malzeme_Duzelir()
    {
        var (a, b) = Hazirla();
        var dogru = Bakiyeler();
        Exec($"UPDATE stock_balances SET quantity='999' WHERE company_id='{Co}';");   // hepsini boz

        _stok.RecomputeBalances(Co, new[] { a });
        var sonra = Bakiyeler();
        Assert.Equal(dogru[$"{a}@{_depo1}"], sonra[$"{a}@{_depo1}"]);
        Assert.Equal(dogru[$"{a}@{_depo2}"], sonra[$"{a}@{_depo2}"]);
        Assert.Equal("999", sonra[$"{b}@{_depo1}"]);                                  // kapsam dışı → dokunulmadı
    }

    [Fact]
    public void ART2_Bos_Kume_Hicbir_Sey_Parametresiz_Tam()
    {
        Hazirla();
        var dogru = Bakiyeler();
        Exec($"UPDATE stock_balances SET quantity='999' WHERE company_id='{Co}';");
        _stok.RecomputeBalances(Co, Array.Empty<string>());
        Assert.All(Bakiyeler().Values, v => Assert.Equal("999", v));
        _stok.RecomputeBalances(Co);
        Assert.Equal(dogru, Bakiyeler());
    }

    [Fact]
    public void ART3_Artimli_Sonuc_Tam_Hesapla_Ayni()
    {
        var (a, b) = Hazirla();
        // Hayalet bakiye: defterde karşılığı olmayan lokasyon satırı → artımlı hesap da temizlemeli.
        Exec($"INSERT INTO stock_balances(company_id, material_id, location_id, quantity, updated_at) VALUES('{Co}','{a}','HAYALET','5',1);");
        _stok.RecomputeBalances(Co, new[] { a, b });
        var artimli = Bakiyeler();
        _stok.RecomputeBalances(Co);
        Assert.Equal(Bakiyeler(), artimli);
        Assert.DoesNotContain($"{a}@HAYALET", artimli.Keys);
    }

    [Fact]
    public void ART4_Paketten_Malzeme_Cikarma()
    {
        var bos = JsonDocument.Parse("""{ "tables": { "materials": [ { "id": "x" } ] } }""").RootElement;
        Assert.Empty(StockService.PakettekiMalzemeler(bos));
        var dolu = JsonDocument.Parse("""{ "tables": { "stock_movements": [ { "material_id": "m1" }, { "material_id": "m2" }, { "material_id": "m1" }, { "id": "x" } ] } }""").RootElement;
        Assert.Equal(new[] { "m1", "m2" }, StockService.PakettekiMalzemeler(dolu).OrderBy(x => x));
        Assert.Empty(StockService.PakettekiMalzemeler(JsonDocument.Parse("[]").RootElement));
    }

    [Fact]
    public void ART5_Cekme_Yalniz_Hareketli_Malzemeyi_Hesaplar()
    {
        var (a, b) = Hazirla();
        Exec($"UPDATE stock_balances SET quantity='999' WHERE company_id='{Co}' AND material_id='{b}';");
        var sync = new BusinessSyncService(_f);
        // Hareketsiz paket (yalnız malzeme kartı) → bakiyelere dokunulmaz.
        sync.ApplyPull(Co, JsonDocument.Parse($$"""{ "tables": { "materials": [ { "id": "{{a}}", "company_id": "{{Co}}", "code": "A-1", "name": "A yeni ad", "created_at": 1, "updated_at": 9999999999999, "version": 2, "is_deleted": 0 } ] } }""").RootElement);
        Assert.Equal("999", Bakiyeler()[$"{b}@{_depo1}"]);
        // B'ye hareket inince B düzelir.
        sync.ApplyPull(Co, JsonDocument.Parse($$"""{ "tables": { "stock_movements": [ { "id": "mv-x", "company_id": "{{Co}}", "material_id": "{{b}}", "branch_id": "{{_depo1}}", "movement_type": "in", "direction": 1, "quantity": "1", "operation_id": "op-x", "created_at": 1, "updated_at": 1 } ] } }""").RootElement);
        Assert.Equal("8", Bakiyeler()[$"{b}@{_depo1}"]);
    }
}
