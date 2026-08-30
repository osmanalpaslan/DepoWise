using DepoWise.Application.Security;
using DepoWise.Application.Teams;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Teams;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — EKİP TANIMI SÖZLEŞME KİLİTLERİ ═══
///
/// Kararlar: PK-EK-07=B (yeni yetki modülü YOK → <c>users</c>) · İK-1 (çoklu ekip üyeliği) ·
/// İK-6 (ekip yöneticisi üye yönetir) · İK-7 (ekipler arası görünürlük) · İK-8 (firma bazlı).
///
/// Bu dosya tenant izolasyonunu, yetki kapılarını, çoklu üyeliği, çift üyelik yasağını, "lider gerçekten
/// üye olmalı" değişmezini, yumuşak silmeyi ve audit'i kilitler. Hiçbir kapı gevşetilmez; negatif
/// senaryolar açıkça test edilir.
///
/// ⚠️ ONAY İLE BAĞ YOKTUR: ekip lideri otomatik onaycı değildir (ADR-187 §3/§5). Onay yapıları
/// ALT FAZ 2 kapsamıdır ve burada test edilmez — çünkü henüz YOKTUR.
/// </summary>
public class EkipTanimiTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TeamService _svc;
    private readonly SessionContext _adminA, _adminB;
    private readonly string _u1A, _u2A, _u1B;

    public EkipTanimiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ekip_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        _svc = new TeamService(_f);

        (_adminA, _u1A) = Kur("EK-A", "admina");
        _u2A = KullaniciEkle("EK-A", "uye2a");
        (_adminB, _u1B) = Kur("EK-B", "adminb");
    }

    private (SessionContext, string) Kur(string co, string user)
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return (new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty), uid);
    }

    private string KullaniciEkle(string co, string username)
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

    /// <summary>Yalnız verilen modüllere verilen eylemleri taşıyan personel oturumu.</summary>
    private static SessionContext Personel(string company, string userId, bool view, bool edit)
        => new(userId, company, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("users", view, edit, edit, edit) }));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // ══════════════════════ MIGRATION 084 ══════════════════════

    /// <summary>EK01 — Migration084 iki tabloyu kurar ve katalog azamisi 84 olur.</summary>
    [Fact]
    public void EK01_Migration084_Tablolari_Kurar()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('teams','team_members');";
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));

        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version=84;";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));

        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal((long)MigrationCatalog.All().Max(m => m.Version), Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>EK02 — <b>İK-8:</b> ekipler FİRMA bazlıdır: şube kolonu OLUŞTURULMAMIŞTIR.
    /// Ayrıca <b>PK-EK-02:</b> <c>users</c> tablosuna hiyerarşi kolonu EKLENMEMİŞTİR.</summary>
    [Fact]
    public void EK02_Sube_Kolonu_Yok_Users_Degismedi()
    {
        using var conn = _f.Create();
        Assert.False(DbIntrospect.ColumnExists(conn, null, "teams", "branch_id"));
        Assert.False(DbIntrospect.ColumnExists(conn, null, "team_members", "branch_id"));
        foreach (var yasak in new[] { "manager_id", "parent_user_id", "is_manager", "manager_user_id" })
            Assert.False(DbIntrospect.ColumnExists(conn, null, "users", yasak));
    }

    /// <summary>
    /// EK03 — KAPSAM SINIRI KİLİDİ: onay/hiyerarşi tabloları <b>Migration084'ün işi DEĞİLDİR</b>.
    ///
    /// ⚠️ Bu test önce "bu tablolar hiç yok" diyordu; ALT FAZ 2 (Migration085) onları BİLİNÇLİ olarak
    /// ekleyince kırıldı. Kilit <b>gevşetilmedi</b> — asıl niyetine (faz sınırı) yeniden hedeflendi:
    /// katalog 084'te durdurulduğunda bu tablolar OLUŞMAMALI. Böylece "ekip fazı onay yapısı kurmaz"
    /// kuralı korunur ve biri Migration084'e onay tablosu eklerse test yine kırılır.
    /// </summary>
    [Fact]
    public void EK03_Onay_Ve_Hiyerarsi_Tablolari_Migration084un_Isi_Degil()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ek84_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f84 = new SqliteConnectionFactory(yol);
            new MigrationRunner(f84, MigrationCatalog.All().Where(m => m.Version <= 84)).Run();

            using var conn = f84.Create();
            Assert.True(DbIntrospect.TableExists(conn, null, "teams"));            // 084'ün işi
            Assert.True(DbIntrospect.TableExists(conn, null, "team_members"));
            foreach (var t in new[] { "user_hierarchy", "approval_instance", "approval_step" })
                Assert.False(DbIntrospect.TableExists(conn, null, t));             // 085'in işi
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    // ══════════════════════ CRUD ══════════════════════

    /// <summary>EK04 — Ekip oluşturma/güncelleme/listeleme temel akışı.</summary>
    [Fact]
    public void EK04_Ekip_Olustur_Guncelle_Listele()
    {
        var id = _svc.Create(_adminA, "Saha Ekibi");
        Assert.NotEqual("", id);

        var t = _svc.ById(_adminA, id);
        Assert.NotNull(t);
        Assert.Equal("Saha Ekibi", t!.Name);
        Assert.Null(t.LeadUserId);      // lider ancak ÜYE olduktan sonra atanabilir
        Assert.True(t.IsActive);

        _svc.Update(_adminA, id, "Saha Ekibi 2", null, isActive: false);
        Assert.Empty(_svc.List(_adminA));                                  // pasif → varsayılan listede yok
        Assert.Single(_svc.List(_adminA, includeInactive: true));
        Assert.Equal("Saha Ekibi 2", _svc.ById(_adminA, id)!.Name);
    }

    /// <summary>EK05 — Ekip adı zorunlu ve azami uzunlukla sınırlı (doğrulama iş katmanında).</summary>
    [Fact]
    public void EK05_Gecersiz_Ad_Reddedilir()
    {
        Assert.Throws<ArgumentException>(() => _svc.Create(_adminA, "   "));
        Assert.Throws<ArgumentException>(() => _svc.Create(_adminA, new string('x', TeamRules.MaxNameLength + 1)));
    }

    // ══════════════════════ ÜYELİK ══════════════════════

    /// <summary>EK06 — <b>İK-1:</b> bir kullanıcı BİRDEN FAZLA ekibe üye olabilir.</summary>
    [Fact]
    public void EK06_Coklu_Ekip_Uyeligi_Serbest()
    {
        var e1 = _svc.Create(_adminA, "Ekip 1");
        var e2 = _svc.Create(_adminA, "Ekip 2");
        _svc.AddMember(_adminA, e1, _u2A);
        _svc.AddMember(_adminA, e2, _u2A);

        Assert.Single(_svc.Members(_adminA, e1));
        Assert.Single(_svc.Members(_adminA, e2));
        Assert.Equal(2, _svc.TeamsOfUser(_adminA, _u2A).Count);
    }

    /// <summary>EK07 — Aynı kullanıcı AYNI ekibe iki kez eklenemez; çıkarılıp yeniden eklenebilir
    /// (kısmi benzersiz indeks <c>is_deleted=0</c> koşulludur).</summary>
    [Fact]
    public void EK07_Ayni_Ekibe_Cift_Uyelik_Engellenir()
    {
        var e = _svc.Create(_adminA, "Ekip");
        _svc.AddMember(_adminA, e, _u2A);
        Assert.Throws<ArgumentException>(() => _svc.AddMember(_adminA, e, _u2A));

        _svc.RemoveMember(_adminA, e, _u2A);
        Assert.Empty(_svc.Members(_adminA, e));
        _svc.AddMember(_adminA, e, _u2A);          // yumuşak silinmiş üyelik yeniden eklenebilir
        Assert.Single(_svc.Members(_adminA, e));
    }

    /// <summary>EK08 — Veritabanı seviyesindeki kısmi benzersiz indeks GERÇEKTEN vardır:
    /// servis atlansa bile aynı aktif üyelik iki kez yazılamaz (yarış durumu koruması).</summary>
    [Fact]
    public void EK08_Aktif_Uyelik_Benzersizligi_Veritabaninda_Zorlanir()
    {
        var e = _svc.Create(_adminA, "Ekip");
        _svc.AddMember(_adminA, e, _u2A);

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO team_members(id,company_id,team_id,user_id,is_lead,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,'EK-A',@t,@u,0,1,1,1,0);";
        cmd.AddWithValue("@i", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@t", e);
        cmd.AddWithValue("@u", _u2A);
        Assert.ThrowsAny<Exception>(() => cmd.ExecuteNonQuery());
    }

    /// <summary>EK09 — Başka FİRMANIN kullanıcısı ekibe eklenemez (Migration084 kullanıcıya FK
    /// vermez; bütünlük kapısı serviste olmalıdır).</summary>
    [Fact]
    public void EK09_Baska_Firmanin_Kullanicisi_Uye_Yapilamaz()
    {
        var e = _svc.Create(_adminA, "Ekip");
        Assert.Throws<ForbiddenException>(() => _svc.AddMember(_adminA, e, _u1B));
        Assert.Throws<ArgumentException>(() => _svc.AddMember(_adminA, e, ""));
    }

    // ══════════════════════ LİDER ══════════════════════

    /// <summary>EK10 — Lider YALNIZ ekibin aktif üyesi olabilir; üye olmayan atanamaz.</summary>
    [Fact]
    public void EK10_Lider_Gercekten_Uye_Olmali()
    {
        var e = _svc.Create(_adminA, "Ekip");
        Assert.Throws<ArgumentException>(() => _svc.Update(_adminA, e, "Ekip", _u2A, true));

        _svc.AddMember(_adminA, e, _u2A);
        _svc.Update(_adminA, e, "Ekip", _u2A, true);
        Assert.Equal(_u2A, _svc.ById(_adminA, e)!.LeadUserId);
        Assert.True(_svc.Members(_adminA, e).Single(m => m.UserId == _u2A).IsLead);
    }

    /// <summary>EK11 — Lider ekipten çıkarılırsa liderlik de temizlenir; "lider üye olmalı"
    /// değişmezi sarkan bir referansla bozulmaz.</summary>
    [Fact]
    public void EK11_Lider_Cikarilinca_Liderlik_Temizlenir()
    {
        var e = _svc.Create(_adminA, "Ekip");
        _svc.AddMember(_adminA, e, _u2A);
        _svc.Update(_adminA, e, "Ekip", _u2A, true);

        _svc.RemoveMember(_adminA, e, _u2A);
        Assert.Null(_svc.ById(_adminA, e)!.LeadUserId);
    }

    // ══════════════════════ YETKİ (PK-EK-07=B, İK-6) ══════════════════════

    /// <summary>EK12 — Yetkisiz kullanıcı ekip göremez/oluşturamaz. Yetki modülü <c>users</c>'tır;
    /// AYRI bir <c>teams</c> modülü YOKTUR (PK-EK-07=B).</summary>
    [Fact]
    public void EK12_Yetkisiz_Kullanici_Engellenir()
    {
        var yetkisiz = new SessionContext(_u2A, "EK-A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, "Ekip"));

        // Yalnız görüntüleme yetkisi: liste açılır, oluşturma kapalı kalır.
        var okur = Personel("EK-A", _u2A, view: true, edit: false);
        _ = _svc.List(okur);
        Assert.Throws<ForbiddenException>(() => _svc.Create(okur, "Ekip"));
    }

    /// <summary>EK13 — <b>İK-6:</b> ekip yöneticisi, <c>users</c> düzenleme yetkisi olmasa da
    /// KENDİ ekibinin üyelerini yönetebilir. Ayrıcalık BAŞKA ekibe geçmez.</summary>
    [Fact]
    public void EK13_Ekip_Yoneticisi_Kendi_Ekibinin_Uyelerini_Yonetir()
    {
        var e = _svc.Create(_adminA, "Ekip");
        var digerEkip = _svc.Create(_adminA, "Diğer Ekip");
        _svc.AddMember(_adminA, e, _u2A);
        _svc.Update(_adminA, e, "Ekip", _u2A, true);          // _u2A artık lider

        var lider = Personel("EK-A", _u2A, view: true, edit: false);
        var yeni = KullaniciEkle("EK-A", "uye3a");
        _svc.AddMember(lider, e, yeni);                        // kendi ekibi → serbest
        Assert.Equal(2, _svc.Members(_adminA, e).Count);

        // Başka ekipte lider DEĞİL → engellenir.
        Assert.Throws<ForbiddenException>(() => _svc.AddMember(lider, digerEkip, yeni));
        // Lider olmak ekip OLUŞTURMA/SİLME yetkisi vermez.
        Assert.Throws<ForbiddenException>(() => _svc.Create(lider, "Yeni Ekip"));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(lider, e));
    }

    // ══════════════════════ TENANT / IDOR ══════════════════════

    /// <summary>EK14 — <b>Tenant izolasyonu:</b> B firması A'nın ekibini göremez, okuyamaz,
    /// güncelleyemez, silemez ve üyelerine erişemez (IDOR kapalı).</summary>
    [Fact]
    public void EK14_Tenant_Izolasyonu_Ve_IDOR()
    {
        var e = _svc.Create(_adminA, "A Ekibi");
        _svc.AddMember(_adminA, e, _u2A);

        Assert.Empty(_svc.List(_adminB));
        Assert.Null(_svc.ById(_adminB, e));
        Assert.Throws<ForbiddenException>(() => _svc.Members(_adminB, e));
        Assert.Throws<ForbiddenException>(() => _svc.Update(_adminB, e, "Çalındı", null, true));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(_adminB, e));
        Assert.Throws<ForbiddenException>(() => _svc.AddMember(_adminB, e, _u1B));
        Assert.Throws<ForbiddenException>(() => _svc.RemoveMember(_adminB, e, _u2A));

        Assert.Equal("A Ekibi", _svc.ById(_adminA, e)!.Name);   // hiçbiri A'nın verisini bozmadı
        Assert.Single(_svc.Members(_adminA, e));
    }

    /// <summary>EK15 — <b>İK-7:</b> ekipler arası görünürlük AÇIK — aynı firmadaki bir kullanıcı,
    /// üyesi OLMADIĞI ekibi de görebilir (gereksiz izolasyon eklenmedi).</summary>
    [Fact]
    public void EK15_Ekipler_Arasi_Gorunurluk_Acik()
    {
        var e = _svc.Create(_adminA, "Başka Ekip");
        var okur = Personel("EK-A", _u2A, view: true, edit: false);
        Assert.Contains(_svc.List(okur), t => t.Id == e);
        Assert.Empty(_svc.Members(okur, e));   // üye değil ama listeyi görebiliyor
    }

    // ══════════════════════ SİLME / AUDIT ══════════════════════

    /// <summary>EK16 — Silme YUMUŞAKTIR (satır durur, <c>is_deleted=1</c>) ve üyelikleri de kapatır;
    /// fiziksel silme yoktur.</summary>
    [Fact]
    public void EK16_Yumusak_Silme_Uyelikleri_De_Kapatir()
    {
        var e = _svc.Create(_adminA, "Ekip");
        _svc.AddMember(_adminA, e, _u2A);
        _svc.Delete(_adminA, e);

        Assert.Null(_svc.ById(_adminA, e));
        Assert.Empty(_svc.List(_adminA, includeInactive: true));

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_deleted FROM teams WHERE id=@i;";
        cmd.AddWithValue("@i", e);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // satır FİZİKSEL olarak duruyor

        cmd.CommandText = "SELECT COUNT(*) FROM team_members WHERE team_id=@i AND is_deleted=0;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>EK17 — Ekip ve üyelik işlemleri audit kaydı üretir.</summary>
    [Fact]
    public void EK17_Audit_Yazilir()
    {
        var e = _svc.Create(_adminA, "Ekip");
        _svc.AddMember(_adminA, e, _u2A);
        _svc.Update(_adminA, e, "Ekip 2", _u2A, true);
        _svc.RemoveMember(_adminA, e, _u2A);
        _svc.Delete(_adminA, e);

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM audit_logs WHERE company_id='EK-A' AND entity_type IN ('team','team_member');";
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 5);
    }
}
