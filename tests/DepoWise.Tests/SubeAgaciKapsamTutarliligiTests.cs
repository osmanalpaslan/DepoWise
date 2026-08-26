using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SB-01 · ŞUBE AĞACI İKİ KAPSAM OTORİTESİNDE FARKLI UYGULANIYORDU ═══
/// (denetim 2026-08-26, dördüncü tur)
///
/// <b>Ürün kuralı (ŞB-04, 2026-08-18):</b> "Üst şubeye yetkili kullanıcı alt şubeleri de görsün."
/// <see cref="BranchAccess"/> bunu uygular: <c>Expand</c> ile izinli küme alt şubeleri de kapsar
/// (araçlar, raporlar, stok hareketleri… hepsi bu yoldan geçer).
///
/// <b>Bulunan durum:</b> projede İKİNCİ bir kapsam otoritesi var — <see cref="ScopeResolver"/> —
/// ve o <c>user_scopes</c> satırlarını <b>olduğu gibi</b> döndürüyor, ağacı HİÇ genişletmiyordu.
/// Canlı kullanıcısı <see cref="PersonnelService"/>'tir (hem liste hem yazma kapısı). Sonuç:
///
/// <list type="bullet">
///   <item>Üst şubeye yetkili kullanıcı alt şantiyenin <b>araçlarını/raporlarını görüyor</b>,</item>
///   <item>ama aynı şantiyenin <b>personelini görmüyor</b>,</item>
///   <item>ve o şantiyeye <b>personel ekleyemiyor</b> ("şube kapsam dışı" hatası).</item>
/// </list>
///
/// ⚠️ Güvenlik açığı DEĞİLDİR (fazla değil, EKSİK gösterme + meşru işlemin engellenmesi) ama gerçek
/// bir tutarsızlıktır ve <b>artık canlıda görünürdür</b>: üretim veritabanında bu turda 9 şube
/// bulundu ve bunların 5'i "ANKARA GENEL MERKEZ" altında alt şantiyedir (önceki turlarda 0 şube vardı,
/// bu yüzden fark edilemiyordu).
///
/// <b>Düzeltme:</b> <see cref="ScopeResolver"/> de <see cref="BranchTree"/> ile genişletir → iki otorite
/// AYNI cevabı verir. Yeni bir kural getirilmez; ŞB-04'ün zaten verdiği karar ikinci yerde de uygulanır.
/// </summary>
public class SubeAgaciKapsamTutarliligiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dw_sb01_{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly ScopeResolver _scope;
    private readonly PersonnelService _personel;

    private const string Co = "SB01-CO";
    private const string Ust = "SUBE-UST";        // ANKARA GENEL MERKEZ benzeri
    private const string Alt = "SANTIYE-ALT";     // onun altındaki şantiye
    private const string Kardes = "SUBE-KARDES";  // ilgisiz başka şube (kendi altı olan)
    private const string KardesAlt = "SANTIYE-KARDES-ALT";  // kapsam DIŞI bir alt şube
    private const string Kullanici = "u-ust";     // YALNIZ üst şubeye kapsamlı

    public SubeAgaciKapsamTutarliligiTests()
    {
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _scope = new ScopeResolver(_factory);
        _personel = new PersonnelService(_factory, _scope);

        Sql("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,'Firma',1,1,1,0);",
            ("@c", Co));
        Sube(Ust, null, "ANKARA GENEL MERKEZ");
        Sube(Alt, Ust, "DUZCE SANTIYE");
        Sube(Kardes, null, "DENIZLI");
        // ⚠️ KARDEŞİN DE BİR ALTI OLMALI. İlk kurguda yoktu ve bu yüzden test "aşırı genişletme"yi
        // (ağaçtaki TÜM alt şubeleri herkese ekleme) YAKALAYAMIYORDU — kasten bozma denemesinde ortaya
        // çıktı. Artık kapsam dışı bir ALT şube de var; sızarsa test kırılır.
        Sube(KardesAlt, Kardes, "DENIZLI SANTIYE");

        Sql("INSERT INTO users(id,company_id,username,password_hash,full_name,is_active," +
            "created_at,updated_at,version,is_deleted) VALUES(@u,@c,'ustkullanici','x','Ust Kullanici',1,1,1,1,0);",
            ("@u", Kullanici), ("@c", Co));
        Sql("INSERT INTO user_scopes(user_id,company_id,branch_id) VALUES(@u,@c,@b);",
            ("@u", Kullanici), ("@c", Co), ("@b", Ust));   // YALNIZ üst şube

        Personel("p-ust", Ust, "UST PERSONELI");
        Personel("p-alt", Alt, "ALT SANTIYE PERSONELI");
        Personel("p-kardes", Kardes, "KARDES SUBE PERSONELI");
        Personel("p-kardes-alt", KardesAlt, "KARDES ALT SANTIYE PERSONELI");
    }

    private void Sql(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private void Sube(string id, string? ust, string ad)
        => Sql("INSERT INTO branches(id,company_id,parent_id,name,kind,created_at,updated_at,version,is_deleted) " +
               "VALUES(@id,@c,@p,@n,'branch',1,1,1,0);",
            ("@id", id), ("@c", Co), ("@p", (object?)ust ?? DBNull.Value), ("@n", ad));

    private void Personel(string id, string sube, string ad)
        => Sql("INSERT INTO personnel(id,company_id,branch_id,full_name,is_active,is_field_staff," +
               "created_at,updated_at,version,is_deleted) VALUES(@id,@c,@b,@n,1,0,1,1,1,0);",
            ("@id", id), ("@c", Co), ("@b", sube), ("@n", ad));

    private static SessionContext Oturum() => new(Kullanici, Co, Array.Empty<string>(),
        new PermissionSet(new[] { new ModulePermission("personnel", CanView: true, CanCreate: true, CanEdit: true, CanDelete: false) }));

    // ── 1) İKİ OTORİTE AYNI CEVABI VERMELİ ────────────────────────────────────────────────────

    /// <summary>Kontrol: <see cref="BranchAccess"/> üst şubeye yetkiliye alt şubeyi ZATEN veriyor.</summary>
    [Fact]
    public void SB01_BranchAccess_Alt_Subeyi_Kapsiyor()
    {
        using var conn = _factory.Create();
        var s = new SessionContext(Kullanici, Co, Array.Empty<string>(), PermissionSet.Empty)
        {
            ScopeBranchIds = new[] { Ust },
            BranchDescendants = BranchTree.LoadDescendants(conn, Co),
        };

        var izinli = BranchAccess.Allowed(s);

        Assert.NotNull(izinli);
        Assert.Contains(Ust, izinli!);
        Assert.Contains(Alt, izinli!);          // ŞB-04 kuralı
        Assert.DoesNotContain(Kardes, izinli!);
    }

    /// <summary>⭐ SB-01 — <see cref="ScopeResolver"/> de AYNI cevabı vermeli (alt şube dahil).</summary>
    [Fact]
    public void SB01a_ScopeResolver_Alt_Subeyi_De_Kapsamali()
    {
        var izinli = _scope.AllowedBranchIds(Oturum());

        Assert.Contains(Ust, izinli);
        Assert.Contains(Alt, izinli);
        Assert.DoesNotContain(Kardes, izinli);
        Assert.DoesNotContain(KardesAlt, izinli);   // aşırı genişletme kilidi
    }

    // ── 2) KULLANICIYA YANSIYAN SONUÇ ─────────────────────────────────────────────────────────

    /// <summary>⭐ SB-01 — üst şubeye yetkili kullanıcı ALT ŞANTİYENİN personelini de görmeli.</summary>
    [Fact]
    public void SB01b_Alt_Santiye_Personeli_Listede_Gorunur()
    {
        var liste = _personel.List(Oturum(), new PageRequest { Limit = 200 });
        var adlar = liste.Items.Select(p => p.FullName).ToList();

        Assert.Contains("UST PERSONELI", adlar);
        Assert.Contains("ALT SANTIYE PERSONELI", adlar);
    }

    /// <summary>⭐ SB-01 — üst şubeye yetkili kullanıcı alt şantiyeye personel EKLEYEBİLMELİ.</summary>
    [Fact]
    public void SB01c_Alt_Santiyeye_Personel_Eklenebilir()
    {
        var ex = Record.Exception(() => _scope.EnsureBranchAllowed(Oturum(), Alt));
        Assert.Null(ex);
    }

    // ── 3) REGRESYON KİLİTLERİ: KAPSAM GENİŞLEMESİ SINIRLI OLMALI ────────────────────────────

    /// <summary>⭐ Kilit: KARDEŞ şube kapsama GİRMEZ (genişletme yalnız ALTA doğrudur).</summary>
    [Fact]
    public void SB01d_Kardes_Sube_Kapsama_Girmez()
    {
        var adlar = _personel.List(Oturum(), new PageRequest { Limit = 200 }).Items.Select(p => p.FullName).ToList();

        Assert.DoesNotContain("KARDES SUBE PERSONELI", adlar);
        Assert.DoesNotContain("KARDES ALT SANTIYE PERSONELI", adlar);   // aşırı genişletme kilidi
        Assert.Throws<ForbiddenException>(() => _scope.EnsureBranchAllowed(Oturum(), KardesAlt));
        Assert.Throws<ForbiddenException>(() => _scope.EnsureBranchAllowed(Oturum(), Kardes));
    }

    /// <summary>Kilit: ALT şubeye yetkili kullanıcı ÜST şubeyi GÖRMEZ (yukarı doğru genişleme YOK).</summary>
    [Fact]
    public void SB01e_Alt_Subeye_Yetkili_Ust_Subeyi_Gormez()
    {
        Sql("INSERT INTO users(id,company_id,username,password_hash,full_name,is_active," +
            "created_at,updated_at,version,is_deleted) VALUES('u-alt',@c,'altkullanici','x','Alt',1,1,1,1,0);",
            ("@c", Co));
        Sql("INSERT INTO user_scopes(user_id,company_id,branch_id) VALUES('u-alt',@c,@b);",
            ("@c", Co), ("@b", Alt));

        var s = new SessionContext("u-alt", Co, Array.Empty<string>(),
            new PermissionSet(new[] { new ModulePermission("personnel", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false) }));

        var izinli = _scope.AllowedBranchIds(s);

        Assert.Contains(Alt, izinli);
        Assert.DoesNotContain(Ust, izinli);
    }

    /// <summary>Kilit: ADMİN davranışı değişmez (kapsamsız → firmanın tüm şubeleri).</summary>
    [Fact]
    public void SB01f_Admin_Tum_Subeleri_Gorur()
    {
        var admin = new SessionContext("adm", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var izinli = _scope.AllowedBranchIds(admin);

        Assert.Contains(Ust, izinli);
        Assert.Contains(Alt, izinli);
        Assert.Contains(Kardes, izinli);
    }

    /// <summary>Kilit: kapsamı OLMAYAN admin-olmayan kullanıcı hiçbir şubeyi görmez (deny-by-default).</summary>
    [Fact]
    public void SB01g_Kapsamsiz_Kullanici_Bos_Kalir()
    {
        var s = new SessionContext("u-yok", Co, Array.Empty<string>(), PermissionSet.Empty);
        Assert.Empty(_scope.AllowedBranchIds(s));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
