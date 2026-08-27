using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ PRJ-01 (ADR-164, 2026-08-27) — PROJE / ŞANTİYE YÖNETİMİ TESTLERİ ═══
///
/// Ürün kararları: PK-C1 (model çoklu şantiyeye hazır, UI tek) · PK-C2 (Saha = branches.kind üçüncü değer)
/// · PK-C3 (ad dışında her alan opsiyonel) · PK-C4 (yetki = branches modülü + BranchAccess, ayrı kapı yok).
///
/// EN KRİTİK: PRJ7 — Migration073'ün MEVCUT verilere dokunmadığının satır-satır kanıtı
/// (canlı veri koruma protokolü: yeni özellik eski kayıtların hiçbir değerini değiştiremez).
/// </summary>
public class ProjeTests : IDisposable
{
    private const string Co = "PRJ";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly ProjectService _svc;
    private readonly BranchService _branches;
    private readonly string _uid;
    private readonly string _sube, _santiye1, _santiye2;
    private readonly SessionContext _admin;

    public ProjeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_prj_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _branches = new BranchService(_f);
        _sube = _branches.Create(_admin, new NewBranch("Merkez"));
        _santiye1 = _branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _santiye2 = _branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _svc = new ProjectService(_f);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    /// <summary>Personel oturumu: branches modül izinleri + istenen şube kapsamı (admin bypass YOK).</summary>
    private SessionContext Personel(bool view = true, bool create = true, bool edit = true, bool delete = false,
        string[]? kapsam = null)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("branches", view, create, edit, delete) }))
        { ScopeBranchIds = kapsam };

    // ══════════════ TEMEL CRUD ══════════════

    [Fact]
    public void PRJ1_Olustur_Ve_Listele()
    {
        var id = _svc.Create(_admin, new NewProject("Yol Projesi", "active", 1_700_000_000_000, 1_710_000_000_000,
            null, "Ankara", "açıklama", new[] { _santiye1 }));
        var p = Assert.Single(_svc.List(_admin), x => x.Id == id);
        Assert.Equal("Yol Projesi", p.Name);
        Assert.Equal("Aktif", p.StatusDisplay);
        Assert.Equal(new[] { _santiye1 }, p.BranchIds);
        Assert.Equal("Şantiye A", p.BranchDisplay);
        Assert.Equal("Ankara", p.Location);
    }

    /// <summary>PK-C3: ad dışında HİÇBİR alan zorunlu değil — yalnız adla proje açılabilir.</summary>
    [Fact]
    public void PRJ2_Yalniz_Adla_Acilir_Tum_Alanlar_Opsiyonel()
    {
        var id = _svc.Create(_admin, new NewProject("Sade Proje"));
        var p = Assert.Single(_svc.List(_admin), x => x.Id == id);
        Assert.Equal("active", p.Status);          // varsayılan durum
        Assert.Empty(p.BranchIds);                 // şantiyesiz proje geçerli
        Assert.Null(p.StartDate);
        Assert.Equal("—", p.ManagerDisplay);
    }

    [Fact]
    public void PRJ3_Duzenleme_Ve_Duzenleme_Kilidi()
    {
        var id = _svc.Create(_admin, new NewProject("P", BranchIds: new[] { _santiye1 }));
        var v1 = _svc.List(_admin).Single(x => x.Id == id).Version;
        _svc.Update(_admin, id, new NewProject("P2", "on_hold", BranchIds: new[] { _santiye2 }), v1);

        var p = _svc.List(_admin).Single(x => x.Id == id);
        Assert.Equal("P2", p.Name);
        Assert.Equal("Beklemede", p.StatusDisplay);
        Assert.Equal(new[] { _santiye2 }, p.BranchIds);   // bağ kümesi hedefe eşitlendi

        // ESKİ sürümle ikinci yazma reddedilir (düzenleme kilidi) — hiçbir alan ezilmez.
        Assert.Throws<ConcurrencyException>(() => _svc.Update(_admin, id, new NewProject("P3"), v1));
        Assert.Equal("P2", _svc.List(_admin).Single(x => x.Id == id).Name);
    }

    [Fact]
    public void PRJ4_Bitis_Baslangictan_Once_Olamaz()
        => Assert.Throws<ArgumentException>(() =>
            _svc.Create(_admin, new NewProject("T", StartDate: 2_000, EndDate: 1_000)));

    /// <summary>PK-C1 GELECEK GARANTİSİ: veri modeli bugünden ÇOKLU şantiyeyi taşır — servis iki şantiyeyi
    /// kabul eder ve ikisini de döndürür. İleride UI genişlediğinde migration GEREKMEYECEK; bu test o sözü kilitler.</summary>
    [Fact]
    public void PRJ5_Coklu_Santiye_Modeli_Hazir()
    {
        var id = _svc.Create(_admin, new NewProject("Büyük Proje", BranchIds: new[] { _santiye1, _santiye2 }));
        var p = _svc.List(_admin).Single(x => x.Id == id);
        Assert.Equal(2, p.BranchIds.Count);
        Assert.Contains("Şantiye A", p.BranchDisplay);
        Assert.Contains("Şantiye B", p.BranchDisplay);
    }

    // ══════════════ YETKİ + KAPSAM (PK-C4) ══════════════

    /// <summary>branches modülünde View yetkisi olmayan HİÇBİR şey göremez (deny-by-default).</summary>
    [Fact]
    public void PRJ6_Yetkisiz_Kullanici_Listeyi_Goremez_Yazamaz()
    {
        var yetkisiz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewProject("X")));
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kullanıcı yalnız Şantiye A'ya yetkiliyse Şantiye B'nin projesini GÖREMEZ;
    /// şantiyesiz proje ("şubesiz kayıt gizlenmez" ilkesi) görünür.</summary>
    [Fact]
    public void PRJ7a_Kapsam_Disi_Santiyenin_Projesi_Gorunmez()
    {
        var pa = _svc.Create(_admin, new NewProject("A Projesi", BranchIds: new[] { _santiye1 }));
        var pb = _svc.Create(_admin, new NewProject("B Projesi", BranchIds: new[] { _santiye2 }));
        var serbest = _svc.Create(_admin, new NewProject("Genel Proje"));

        var dar = Personel(kapsam: new[] { _santiye1 });
        var gorulen = _svc.List(dar).Select(x => x.Id).ToHashSet();
        Assert.Contains(pa, gorulen);
        Assert.DoesNotContain(pb, gorulen);
        Assert.Contains(serbest, gorulen);
    }

    /// <summary>⭐ Kapsam dışına YAZMA da kapalı: Şantiye B'ye proje bağlayamaz, B'nin projesini
    /// düzenleyemez/silemez (listede görünmüyor diye güvenlik bitmez — id tahmini de işe yaramaz).</summary>
    [Fact]
    public void PRJ7b_Kapsam_Disina_Yazma_Kapali()
    {
        var pb = _svc.Create(_admin, new NewProject("B Projesi", BranchIds: new[] { _santiye2 }));
        var dar = Personel(delete: true, kapsam: new[] { _santiye1 });

        Assert.Throws<ForbiddenException>(() =>
            _svc.Create(dar, new NewProject("Sızma", BranchIds: new[] { _santiye2 })));
        Assert.Throws<ForbiddenException>(() => _svc.Update(dar, pb, new NewProject("Ele Geçirildi")));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(dar, pb));
        Assert.Equal("B Projesi", _svc.List(_admin).Single(x => x.Id == pb).Name);   // dokunulmadı
    }

    /// <summary>⭐ TENANT: başka firmanın projesi görünmez, düzenlenemez; başka firmanın şubesine bağ kurulamaz.</summary>
    [Fact]
    public void PRJ8_Firma_Izolasyonu()
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('BASKA','BASKA',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var id = _svc.Create(_admin, new NewProject("Gizli", BranchIds: new[] { _santiye1 }));

        Assert.DoesNotContain(_svc.List(yabanci), x => x.Id == id);
        Assert.Throws<ArgumentException>(() => _svc.Update(yabanci, id, new NewProject("Çalındı")));
        Assert.Throws<ArgumentException>(() => _svc.Delete(yabanci, id));
        // Başka firmanın ŞUBESİNE bağ da kurulamaz:
        Assert.Throws<ArgumentException>(() => _svc.Create(yabanci, new NewProject("X", BranchIds: new[] { _santiye1 })));
        Assert.Equal("Gizli", _svc.List(_admin).Single(x => x.Id == id).Name);
    }

    // ══════════════ SİLME (mevcut desen) ══════════════

    /// <summary>Silme SOFT'tur (fiziksel DELETE yok) + Çöp Kutusu geri getirir; şantiye bağı geri döner.</summary>
    [Fact]
    public void PRJ9_Soft_Delete_Ve_Cop_Kutusundan_Geri_Yukleme()
    {
        var id = _svc.Create(_admin, new NewProject("Silinecek", BranchIds: new[] { _santiye1 }));
        _svc.Delete(_admin, id);
        Assert.DoesNotContain(_svc.List(_admin), x => x.Id == id);

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT is_deleted FROM projects WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // satır DURUYOR (fiziksel silinmedi)
        }

        var trash = new TrashService(_f);
        Assert.Contains(trash.List(_admin, reauthenticated: true), t => t.Table == "projects" && t.Id == id);
        trash.Restore(_admin, "projects", id, reauthenticated: true);

        var geri = _svc.List(_admin).Single(x => x.Id == id);
        Assert.Equal("Silinecek", geri.Name);
        Assert.Equal(new[] { _santiye1 }, geri.BranchIds);   // bağ korunmuştu, aynen döndü
    }

    // ══════════════ AUDIT ══════════════

    /// <summary>Proje işlemleri denetim izine yazılır ve LOG-01 ekran logu (branches modülü) bunları kapsar.</summary>
    [Fact]
    public void PRJ10_Audit_Yazilir_Ve_Ekran_Logu_Kapsar()
    {
        var id = _svc.Create(_admin, new NewProject("İzli"));
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_type='project' AND entity_id=@id;";
            cmd.AddWithValue("@c", Co);
            cmd.AddWithValue("@id", id);
            Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 1);
        }
        Assert.Contains("project", ScreenAuditMap.EntityTypes("branches"));
    }

    // ══════════════ SAHA TÜRÜ (PK-C2) ══════════════

    /// <summary>Yeni 'Saha' türü kaydedilir ve "Saha" görünür; MEVCUT türlerin görünümü değişmez.</summary>
    [Fact]
    public void PRJ11_Saha_Turu_Kaydedilir_Mevcut_Turler_Degismez()
    {
        var saha = _branches.Create(_admin, new NewBranch("Kuzey Sahası", "field", ParentId: _santiye1));
        var rows = _branches.List(_admin);
        Assert.Equal("Saha", rows.Single(b => b.Id == saha).KindDisplay);
        Assert.Equal("Şantiye A", rows.Single(b => b.Id == saha).ParentName);   // hiyerarşi aynen çalışır
        Assert.Equal("Şube", rows.Single(b => b.Id == _sube).KindDisplay);
        Assert.Equal("Şantiye", rows.Single(b => b.Id == _santiye1).KindDisplay);
    }

    /// <summary>Bilinmeyen tür değeri fail-safe 'branch' yazılır (uydurma değer DB'ye giremez).</summary>
    [Fact]
    public void PRJ12_Bilinmeyen_Tur_Fail_Safe()
    {
        var id = _branches.Create(_admin, new NewBranch("Garip", "warehouse"));
        Assert.Equal("Şube", _branches.List(_admin).Single(b => b.Id == id).KindDisplay);
    }

    // ══════════════ ⭐⭐ CANLI VERİ GÜVENLİĞİ — MIGRATION KANITI ══════════════

    /// <summary>
    /// EN KRİTİK TEST. Canlı senaryonun birebir provası:
    /// (1) veritabanı Migration 72'ye kadar kurulur (= bugünkü canlı üretim şeması),
    /// (2) canlıdaki gibi şube + kullanıcı + stok hareketi verisi girilir,
    /// (3) TÜM satırların TÜM kolon değerlerinin fotoğrafı alınır,
    /// (4) yalnız Migration073 uygulanır,
    /// (5) fotoğraf yeniden alınır → BİT DEĞERİ BİLE DEĞİŞMEMİŞ olmalı; yeni tablolar BOŞ doğmalı.
    /// </summary>
    [Fact]
    public void PRJ13_Migration073_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_prj_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            // (1) canlı üretim şeması: 1..72
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 72)).Run();

            // (2) canlı benzeri veri
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO branches(id,company_id,parent_id,name,kind,created_at,updated_at,version,is_deleted)
    VALUES('B1','C1',NULL,'Merkez','branch',11,11,1,0),
          ('B2','C1','B1','Şantiye X','site',12,12,3,0);
INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,currency_code,created_at,updated_at,version,is_deleted)
    VALUES('M1','C1','K-1','Çimento','0','12.5','TRY',13,13,1,0);
INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,unit_price,operation_id,created_at)
    VALUES('SM1','C1','M1','B2','in',1,'10','12.5','op-1',14);";
                cmd.ExecuteNonQuery();
            }

            // (3) fotoğraf: korunması zorunlu tabloların TÜM içerikleri
            string[] tablolar = { "branches", "companies", "materials", "stock_movements", "users" };
            var once = Fotograf(f, tablolar);

            // (4) YALNIZ Migration073
            var uygulanan = new MigrationRunner(f, new IMigration[] { new Migration073_Projects() }).Run();
            Assert.Equal(new[] { 73 }, uygulanan);

            // (5) tek değer bile değişmedi + yeni tablolar boş
            Assert.Equal(once, Fotograf(f, tablolar));
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM projects) + (SELECT COUNT(*) FROM project_branches);";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    /// <summary>Ek statik kanıt: Migration073 kaynak kodunda mevcut veriyi değiştirebilecek
    /// HİÇBİR komut yoktur (ALTER/UPDATE/DELETE/DROP/INSERT) — yalnız CREATE.</summary>
    [Fact]
    public void PRJ14_Migration073_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration073_Projects.cs"));
        // cmd.CommandText içindeki SQL bloğunu al (yorumlar/doc değil):
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }

    /// <summary>Tablo içeriğinin deterministik fotoğrafı: her satırın her kolonu değişmez metin olarak.</summary>
    private static string Fotograf(SqliteConnectionFactory f, string[] tablolar)
    {
        var sb = new System.Text.StringBuilder();
        using var conn = f.Create();
        foreach (var t in tablolar)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {t} ORDER BY 1;";
            using var r = cmd.ExecuteReader();
            sb.Append("== ").Append(t).Append(" ==\n");
            while (r.Read())
            {
                for (int i = 0; i < r.FieldCount; i++)
                    sb.Append(r.GetName(i)).Append('=').Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }
}
