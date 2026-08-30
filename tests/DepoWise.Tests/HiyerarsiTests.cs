using DepoWise.Application.Approvals;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Approvals;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187) — KULLANICI HİYERARŞİSİ KİLİTLERİ ═══
///
/// <b>PK-EK-02:</b> hiyerarşi ayrı tabloda, <c>users</c>'a sütun EKLENMEZ.
/// <b>İK-2:</b> azami 4 DÜĞÜM — ADR örneği bağlayıcı: <c>A→B→C→D</c> geçerli, <c>A→B→C→D→E</c> geçersiz.
/// <b>İK-8:</b> firma bazlı — <c>branch_id</c> yok.
///
/// Kilitlenenler: derinlik · döngü · self-reference · çapraz firma · tekil aktif ilişki ·
/// yumuşak silme sonrası yeniden kurulabilme · zincir çözümlemenin döngüye karşı dayanıklılığı.
/// </summary>
public class HiyerarsiTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly UserHierarchyService _svc;
    private readonly SessionContext _adminA, _adminB;
    private readonly Dictionary<string, string> _u = new(StringComparer.Ordinal);

    public HiyerarsiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_hier_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        _svc = new UserHierarchyService(_f);

        _adminA = Firma("HI-A", "admina");
        _adminB = Firma("HI-B", "adminb");
        foreach (var ad in new[] { "A", "B", "C", "D", "E" }) _u[ad] = Kullanici("HI-A", "u" + ad);
        _u["B_A"] = Kullanici("HI-B", "ubb");
    }

    private SessionContext Firma(string co, string user)
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private string Kullanici(string co, string username)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@u,'x',1,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
        return id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // ══════════════════════ ŞEMA ══════════════════════

    /// <summary>HI01 — Migration085 üç tabloyu kurar; <c>users</c> DEĞİŞMEZ; şube kolonu YOK.</summary>
    [Fact]
    public void HI01_Migration085_Semayi_Kurar_Users_Degismez()
    {
        using var conn = _f.Create();
        foreach (var t in new[] { "user_hierarchy", "approval_instance", "approval_step" })
            Assert.True(DbIntrospect.TableExists(conn, null, t));

        foreach (var yasak in new[] { "manager_id", "parent_user_id", "is_manager", "manager_user_id" })
            Assert.False(DbIntrospect.ColumnExists(conn, null, "users", yasak));

        Assert.False(DbIntrospect.ColumnExists(conn, null, "user_hierarchy", "branch_id"));

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal((long)MigrationCatalog.All().Max(m => m.Version), Convert.ToInt64(cmd.ExecuteScalar()));
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version=85;";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>HI02 — <c>purchase_orders.status</c> sözleşmesi DEĞİŞMEDİ ve PO'ya onay kolonu
    /// EKLENMEDİ (ADR-188 §2). Onay durumu yalnız <c>approval_instance</c>'ta yaşar.</summary>
    [Fact]
    public void HI02_PurchaseOrders_Semasi_Degismedi()
    {
        using var conn = _f.Create();
        foreach (var yasak in new[] { "approval_status", "approver_id", "approved_by", "approved_at", "approval_instance_id" })
            Assert.False(DbIntrospect.ColumnExists(conn, null, "purchase_orders", yasak));
    }

    // ══════════════════════ TEMEL ══════════════════════

    /// <summary>HI03 — Tek seviye ilişki kurulur ve zincir çözülür.</summary>
    [Fact]
    public void HI03_Tek_Seviye()
    {
        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        Assert.Equal(_u["B"], _svc.ManagerOf(_adminA, _u["A"]));
        Assert.Equal(new[] { _u["B"] }, _svc.ResolveChain(_adminA, _u["A"]));
    }

    /// <summary>HI04 — <b>İK-2 sınırı:</b> A→B→C→D (4 düğüm) GEÇERLİ; zincir 3 onaycı döner.</summary>
    [Fact]
    public void HI04_Dort_Seviye_Gecerli()
    {
        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        _svc.SetManager(_adminA, _u["B"], _u["C"]);
        _svc.SetManager(_adminA, _u["C"], _u["D"]);

        Assert.Equal(new[] { _u["B"], _u["C"], _u["D"] }, _svc.ResolveChain(_adminA, _u["A"]));
        Assert.Equal(HierarchyRules.MaxApprovers, _svc.ResolveChain(_adminA, _u["A"]).Count);
    }

    /// <summary>HI05 — <b>5. seviye REDDEDİLİR:</b> A→B→C→D→E kurulamaz. Kritik nokta: yeni kenar
    /// zincirin ÜSTÜNE eklenir; yalnız yukarı bakan bir kontrol bunu KAÇIRIRDI.</summary>
    [Fact]
    public void HI05_Besinci_Seviye_Reddedilir()
    {
        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        _svc.SetManager(_adminA, _u["B"], _u["C"]);
        _svc.SetManager(_adminA, _u["C"], _u["D"]);

        // D'nin üstüne E eklenirse A'dan başlayan zincir 5 düğüm olurdu → reddedilmeli.
        var ex = Assert.Throws<ArgumentException>(() => _svc.SetManager(_adminA, _u["D"], _u["E"]));
        Assert.Contains("4", ex.Message);
        Assert.Null(_svc.ManagerOf(_adminA, _u["D"]));
    }

    /// <summary>HI06 — Self-reference reddedilir.</summary>
    [Fact]
    public void HI06_Self_Reference_Reddedilir()
        => Assert.Throws<ArgumentException>(() => _svc.SetManager(_adminA, _u["A"], _u["A"]));

    /// <summary>HI07 — <b>Döngü reddedilir:</b> A→B→C kurulduktan sonra C→A kurulamaz.</summary>
    [Fact]
    public void HI07_Dongu_Reddedilir()
    {
        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        _svc.SetManager(_adminA, _u["B"], _u["C"]);

        Assert.Throws<ArgumentException>(() => _svc.SetManager(_adminA, _u["C"], _u["A"]));
        Assert.Null(_svc.ManagerOf(_adminA, _u["C"]));
    }

    /// <summary>HI08 — Çapraz firma ilişkisi reddedilir (tenant kapısı serviste).</summary>
    [Fact]
    public void HI08_Capraz_Firma_Reddedilir()
    {
        Assert.Throws<ForbiddenException>(() => _svc.SetManager(_adminA, _u["A"], _u["B_A"]));
        Assert.Throws<ForbiddenException>(() => _svc.SetManager(_adminA, _u["B_A"], _u["A"]));
        Assert.Empty(_svc.List(_adminB));
    }

    /// <summary>HI09 — Bir kullanıcının AKTİF tek üstü olur; ikinci atama öncekini değiştirir
    /// (veritabanındaki kısmi benzersiz indeks de bunu zorlar).</summary>
    [Fact]
    public void HI09_Tek_Aktif_Ust()
    {
        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        _svc.SetManager(_adminA, _u["A"], _u["C"]);
        Assert.Equal(_u["C"], _svc.ManagerOf(_adminA, _u["A"]));
        Assert.Single(_svc.List(_adminA));

        // Servis atlansa bile ikinci AKTİF satır veritabanına yazılamaz.
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO user_hierarchy(id,company_id,user_id,manager_user_id,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,'HI-A',@u,@m,1,1,1,0);";
        cmd.AddWithValue("@i", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@u", _u["A"]);
        cmd.AddWithValue("@m", _u["D"]);
        Assert.ThrowsAny<Exception>(() => cmd.ExecuteNonQuery());
    }

    /// <summary>HI10 — Yumuşak silme sonrası ilişki YENİDEN kurulabilir (kısmi indeks doğru).</summary>
    [Fact]
    public void HI10_Silinen_Iliski_Yeniden_Kurulabilir()
    {
        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        _svc.RemoveManager(_adminA, _u["A"]);
        Assert.Null(_svc.ManagerOf(_adminA, _u["A"]));
        Assert.Throws<ArgumentException>(() => _svc.RemoveManager(_adminA, _u["A"]));   // ikinci kez yok

        _svc.SetManager(_adminA, _u["A"], _u["B"]);
        Assert.Equal(_u["B"], _svc.ManagerOf(_adminA, _u["A"]));
    }

    /// <summary>HI11 — Yetkisiz kullanıcı hiyerarşiyi okuyamaz/yazamaz (modül: <c>users</c>).</summary>
    [Fact]
    public void HI11_Yetki_Kapisi()
    {
        var yetkisiz = new SessionContext(_u["A"], "HI-A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.SetManager(yetkisiz, _u["A"], _u["B"]));

        var okur = new SessionContext(_u["A"], "HI-A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("users", true, false, false, false) }));
        _ = _svc.List(okur);                                                        // görüntüleme serbest
        Assert.Throws<ForbiddenException>(() => _svc.SetManager(okur, _u["A"], _u["B"]));
    }

    /// <summary>HI12 — <b>Çözümleme tarafında da döngü koruması:</b> veriye elle döngü sokulsa bile
    /// zincir çözümleme sonsuza gitmez ve sınırlı sayıda onaycı döner.</summary>
    [Fact]
    public void HI12_Cozumlemede_Dongu_Korumasi()
    {
        // Servis kapısını ATLAYARAK doğrudan döngülü veri yazıyoruz (kötü niyet/bozuk veri senaryosu).
        Ekle(_u["A"], _u["B"]);
        Ekle(_u["B"], _u["C"]);
        Ekle(_u["C"], _u["A"]);

        var zincir = _svc.ResolveChain(_adminA, _u["A"]);
        Assert.True(zincir.Count <= HierarchyRules.MaxApprovers);
        Assert.Equal(new[] { _u["B"], _u["C"] }, zincir);   // A'ya dönülünce durur
    }

    private void Ekle(string userId, string managerId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO user_hierarchy(id,company_id,user_id,manager_user_id,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,'HI-A',@u,@m,1,1,1,0);";
        cmd.AddWithValue("@i", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@m", managerId);
        cmd.ExecuteNonQuery();
    }
}
