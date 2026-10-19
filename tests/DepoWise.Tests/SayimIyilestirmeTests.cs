using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-28 (AR-GE 2026-10-18, 05 F-075) — SAYIM: TASLAK · FARK TOLERANSI · KÖR SAYIM · TABLO ═══
///  SI1 — Tolerans kuralı (saf): yüzde, sistem 0, fark 0
///  SI2 — Tolerans ayarı: yalnız yönetici, 0–100, işlem geçmişi, kaldırılabilir; masaüstü giriş paketi yerele yazar
///  SI3 — Toleransı aşan sayımı yetkisiz kişi kesinleştiremez (hiçbir şey yazılmaz); tolerans içi ve yetkili serbest
///  SI4 — Taslak: kaydet/güncelle stoğu değiştirmez; kesinleştirme TEK belge, taslak kapanır, ikinci kez kesinleşmez
///  SI5 — Firma ayrımı: başka firmanın taslağı görünmez, güncellenemez, kesinleşmez
///  SI6 — Migration110 tekrar çalışabilir; eski şemada taslak anlaşılır hata, sayım toleranssız çalışır
///  SI7 — İki platform + API (kaynak kilidi)
/// </summary>
public class SayimIyilestirmeTests : IDisposable
{
    private const string Co = "SYI";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_syi_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin, _depocu;
    private readonly string _uid, _sube, _m;
    private readonly StockService _stock;
    private readonly SayimService _sayim;

    public SayimIyilestirmeTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "syi", "Sayim!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        // Depocu: stok görme/oluşturma var, ters kayıt (onay) yetkisi YOK
        _depocu = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("stock", true, true, false, false) }));
        _sube = new BranchService(_f).Create(_admin, new NewBranch("Depo"));
        _m = new MaterialService(_f).Create(_admin, new NewMaterial("SY-1", "Vida"));
        _stock = new StockService(_f);
        _sayim = new SayimService(_f, _stock);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_m, 100m) }, Op(), _sube);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql, SqliteConnectionFactory? f = null)
    {
        using var c = (f ?? _f).Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql, SqliteConnectionFactory? f = null)
    {
        using var c = (f ?? _f).Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private decimal Stok() => _stock.GetBalanceAt(_admin, _m, _sube);
    private long Belge() => Convert.ToInt64(Tek($"SELECT COUNT(*) FROM stock_documents WHERE company_id='{Co}' AND doc_type='count';"));

    [Fact]
    public void SI1_Tolerans_Kurali()
    {
        Assert.False(SayimService.ToleransiAsar(100m, 95m, 5m));    // tam %5 → aşmaz
        Assert.True(SayimService.ToleransiAsar(100m, 94m, 5m));
        Assert.True(SayimService.ToleransiAsar(100m, 106m, 5m));    // fazla da sayılır
        Assert.False(SayimService.ToleransiAsar(0m, 0m, 5m));
        Assert.True(SayimService.ToleransiAsar(0m, 1m, 50m));       // sistemde yok, sayımda var
        Assert.False(SayimService.ToleransiAsar(-4m, -4m, 0m));
    }

    [Fact]
    public void SI2_Tolerans_Ayari_Ve_Giris_Paketi()
    {
        Assert.Throws<ForbiddenException>(() => _sayim.ToleransAyarla(_depocu, 5m));
        Assert.Throws<ArgumentException>(() => _sayim.ToleransAyarla(_admin, 101m));
        Assert.Throws<ArgumentException>(() => _sayim.ToleransAyarla(_admin, -1m));
        _sayim.ToleransAyarla(_admin, 5m);
        Assert.Equal(5m, _sayim.Tolerans(_admin));
        Assert.Contains("sayimToleransi", (string)Tek($"SELECT after_json FROM audit_logs WHERE entity_type='company' ORDER BY created_at DESC, rowid DESC LIMIT 1;")!);

        var paket = new AuthService(_f).ExportForSync(Co, "syi", "Sayim!2026")!;
        Assert.Equal(5m, paket.SayimToleransi);
        var yerelYol = Path.Combine(Path.GetTempPath(), "depowise_syi_yerel_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var yerel = new SqliteConnectionFactory(yerelYol);
            new MigrationRunner(yerel).Run();
            new AuthService(yerel).ImportRemoteUser(paket);
            Assert.Equal(5m, new SayimService(yerel, new StockService(yerel)).Tolerans(_admin));
            _sayim.ToleransAyarla(_admin, null);
            new AuthService(yerel).ImportRemoteUser(new AuthService(_f).ExportForSync(Co, "syi", "Sayim!2026")!);
            Assert.Null(new SayimService(yerel, new StockService(yerel)).Tolerans(_admin));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { yerelYol, yerelYol + "-wal", yerelYol + "-shm" }) try { File.Delete(p); } catch { }
        }
    }

    [Fact]
    public void SI3_Toleransi_Asan_Sayim_Yetkili_Ister()
    {
        _sayim.ToleransAyarla(_admin, 5m);
        var ex = Assert.Throws<ForbiddenException>(() =>
            _stock.Count(_depocu, new[] { new CountLine(_m, 90m) }, "sayım", Op(), _sube));
        Assert.Contains("tolerans", ex.Message);
        Assert.Equal(100m, Stok());
        Assert.Equal(0L, Belge());

        _stock.Count(_depocu, new[] { new CountLine(_m, 96m) }, "sayım", Op(), _sube);   // %4 → serbest
        Assert.Equal(96m, Stok());
        _stock.Count(_admin, new[] { new CountLine(_m, 50m) }, "yetkili sayım", Op(), _sube);   // yetkili aşabilir
        Assert.Equal(50m, Stok());
        _sayim.ToleransAyarla(_admin, null);
        _stock.Count(_depocu, new[] { new CountLine(_m, 10m) }, "toleranssız", Op(), _sube);   // kontrol yok (eski davranış)
        Assert.Equal(10m, Stok());
    }

    [Fact]
    public void SI4_Taslak_Kaydet_Ve_Kesinlestir()
    {
        var id = _sayim.TaslakKaydet(_depocu, null, _sube, true, "ay sonu", new[] { new CountLine(_m, 97m) });
        Assert.Equal(100m, Stok());
        Assert.Equal(0L, Belge());
        var t = Assert.Single(_sayim.Taslaklar(_depocu));
        Assert.True(t.Kor);
        Assert.Equal(97m, Assert.Single(t.Satirlar).CountedQuantity);
        Assert.Contains("kör sayım", t.Ozet);

        Assert.Equal(id, _sayim.TaslakKaydet(_depocu, id, _sube, true, "ay sonu", new[] { new CountLine(_m, 98m) }));   // güncelleme
        Assert.Equal(98m, Assert.Single(Assert.Single(_sayim.Taslaklar(_depocu)).Satirlar).CountedQuantity);

        _sayim.Kesinlestir(_depocu, id, "ay sonu");
        Assert.Equal(98m, Stok());
        Assert.Equal(1L, Belge());
        Assert.Empty(_sayim.Taslaklar(_depocu));
        Assert.Throws<ForbiddenException>(() => _sayim.Kesinlestir(_depocu, id, "tekrar"));
        Assert.Equal(1L, Belge());
        Assert.Throws<ArgumentException>(() => _sayim.TaslakKaydet(_depocu, null, _sube, false, null, Array.Empty<CountLine>()));
        Assert.Throws<ArgumentException>(() => _sayim.TaslakKaydet(_depocu, null, _sube, false, null, new[] { new CountLine(_m, -1m) }));
    }

    [Fact]
    public void SI5_Baska_Firma()
    {
        var id = _sayim.TaslakKaydet(_admin, null, _sube, false, null, new[] { new CountLine(_m, 90m) });
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('SYB','SYB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("SYB", "syb", "Sayim!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "SYB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_sayim.Taslaklar(b));
        Assert.Throws<ForbiddenException>(() => _sayim.TaslakKaydet(b, id, null, false, null, new[] { new CountLine(_m, 1m) }));
        Assert.Throws<ForbiddenException>(() => _sayim.Kesinlestir(b, id, "x"));
        _sayim.TaslakSil(b, id);   // başka firmanın taslağına dokunmaz
        Assert.Single(_sayim.Taslaklar(_admin));
        Assert.Equal(100m, Stok());
    }

    [Fact]
    public void SI6_Migration_Ve_Eski_Sema()
    {
        using (var c = _f.Create()) new Migration110_StockCountDrafts().Up(c, null!);   // tekrar çalışabilir
        Exec("DROP TABLE stock_count_drafts;");
        Exec("ALTER TABLE companies DROP COLUMN count_tolerance_pct;");
        Assert.Empty(_sayim.Taslaklar(_admin));
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _sayim.TaslakKaydet(_admin, null, _sube, false, null, new[] { new CountLine(_m, 1m) })).Message);
        Assert.Null(_sayim.Tolerans(_admin));
        _stock.Count(_depocu, new[] { new CountLine(_m, 10m) }, "eski şema", Op(), _sube);
        Assert.Equal(10m, Stok());
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void SI7_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapPut(\"/api/stock/count-tolerance\"", api);
        Assert.Contains("app.MapPost(\"/api/stock/count-drafts\"", api);
        Assert.Contains("app.MapPost(\"/api/stock/count-drafts/{id}/commit\"", api);

        var mvm = Oku("src/DepoWise.Desktop/ViewModels/StockCountViewModel.cs");
        Assert.Contains("DesktopServices.Sayim.TaslakKaydet(", mvm);
        Assert.Contains("DesktopServices.Sayim.Kesinlestir(", mvm);
        Assert.Contains("DesktopServices.Stock.GetCountSheet(", mvm);
        var axaml = Oku("src/DepoWise.Desktop/Views/StockCountView.axaml");
        Assert.Contains("{Binding KorSayim}", axaml);
        Assert.Contains("TaslakKaydetCommand", axaml);
        Assert.Contains("SayimToleransIstemci.AyarlaAsync(", Oku("src/DepoWise.Desktop/ViewModels/FieldSettingsViewModel.cs"));

        var web = Oku("src/DepoWise.Web/Components/Pages/StockCount.razor");
        Assert.Contains("/api/stock/count-drafts", web);
        Assert.Contains("/commit", web);
        Assert.Contains("Kör sayım", web);
        Assert.Contains("/api/stock/count-tolerance", Oku("src/DepoWise.Web/Components/Pages/FieldSettings.razor"));
    }
}
