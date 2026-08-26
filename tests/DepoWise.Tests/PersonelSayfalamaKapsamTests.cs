using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Org;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ PRS-01 · ŞUBE KAPSAMI SAYFALAMADAN <b>SONRA</b> UYGULANIYORDU ═══ (denetim 2026-08-26, ikinci tur)
///
/// <b>Bulunan durum.</b> <see cref="PersonnelService.List"/> veritabanından <c>LIMIT n+1</c> satır çeker,
/// sonra <b>bellekte</b> kapsam dışı şubeleri eler, ve en son <c>items.Count &gt; limit</c> koşuluyla
/// "sonraki sayfa" imlecini üretir. Eleme sayımdan ÖNCE olduğu için:
///
/// <list type="bullet">
///   <item>sayfa kapsam dışı kayıtlarla dolarsa kullanıcı <b>boş liste</b> görür,</item>
///   <item>ve <c>next = null</c> döner → istemci "başka kayıt yok" sanar; <b>sonraki sayfaya hiç geçemez</b>.</item>
/// </list>
///
/// Sonuç: tek şubeye yetkili bir kullanıcı, <b>kendi şubesindeki personeli hiç göremeyebilir</b>.
/// ⚠️ Bu bir güvenlik açığı DEĞİLDİR (fazla gösterme değil, EKSİK gösterme) ama gerçek bir veri
/// görünürlüğü hatasıdır ve şube tanımlandığı anda ortaya çıkar.
///
/// <b>Neden bugüne dek görülmedi:</b> üretimde henüz hiç şube tanımlı değil (0 şube) → tüm kayıtlar
/// <c>branch_id IS NULL</c> ve hiçbir satır elenmiyor.
///
/// <b>Düzeltme.</b> Filtre SQL'e taşındı (araç listesindeki mevcut desenin aynısı): görünen KÜME
/// birebir aynı kalır — şubesiz kayıtlar yine herkese görünür, admin yine sınırsızdır — ama
/// <c>LIMIT</c> artık DOĞRU satırlar üzerinde çalışır, dolayısıyla imleç de doğru üretilir.
/// </summary>
public class PersonelSayfalamaKapsamTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dw_prs01_{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly PersonnelService _personel;
    private const string Co = "PRS-CO";
    private const string SubeA = "SUBE-A";
    private const string SubeB = "SUBE-B";
    private const string Kullanici = "u-a1";

    public PersonelSayfalamaKapsamTests()
    {
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _personel = new PersonnelService(_factory, new ScopeResolver(_factory));

        Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,'Firma',1,1,1,0);",
            ("@c", Co));
        foreach (var (id, ad) in new[] { (SubeA, "A Subesi"), (SubeB, "B Subesi") })
            Calistir("INSERT INTO branches(id,company_id,parent_id,name,kind,created_at,updated_at,version,is_deleted) " +
                     "VALUES(@id,@c,NULL,@n,'branch',1,1,1,0);", ("@id", id), ("@c", Co), ("@n", ad));

        // Kullanıcı YALNIZ A şubesine kapsamlı (admin DEĞİL). user_scopes → users FK'si var.
        Calistir("INSERT INTO users(id,company_id,username,password_hash,full_name,is_active," +
                 "created_at,updated_at,version,is_deleted) VALUES(@u,@c,'a1kullanici','x','A1 Kullanici',1,1,1,1,0);",
            ("@u", Kullanici), ("@c", Co));
        Calistir("INSERT INTO user_scopes(user_id,company_id,branch_id) VALUES(@u,@c,@b);",
            ("@u", Kullanici), ("@c", Co), ("@b", SubeA));

        // ⚠️ Sıralama "created_at DESC, id DESC" (TenantSql.KeysetOrderBy) — yani EN YENİ kayıt ilk sayfada.
        // Hatayı tetiklemek için kapsam DIŞI (B şubesi) kayıtlar EN YENİ olmalı ki ilk sayfayı doldursunlar;
        // kapsam İÇİ tek kayıt (A şubesi) en eskidir ve ancak sonraki sayfalarda görünebilir.
        // (İlk kurguda tam tersini yazmıştım; kasten bozma denemesi testin dişsiz olduğunu gösterdi.)
        for (int i = 0; i < 5; i++) Personel($"b{i}", SubeB, olusturma: 900 + i);
        Personel("a-tek", SubeA, olusturma: 100);
    }

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private void Personel(string id, string? sube, long olusturma)
        => Calistir("INSERT INTO personnel(id,company_id,branch_id,full_name,is_active,is_field_staff," +
                    "created_at,updated_at,version,is_deleted) VALUES(@id,@c,@b,@n,1,0,@t,@t,1,0);",
            ("@id", id), ("@c", Co), ("@b", (object?)sube ?? DBNull.Value),
            ("@n", "Personel " + id), ("@t", olusturma));

    private static SessionContext Oturum() => new(Kullanici, Co, Array.Empty<string>(),
        new PermissionSet(new[] { new ModulePermission("personnel", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false) }));

    /// <summary>⭐ PRS-01 — küçük sayfa boyutunda kullanıcı KENDİ şubesindeki personele ULAŞABİLMELİ.</summary>
    [Fact]
    public void PRS01a_Kapsam_Ici_Personel_Sayfalamada_Kaybolmaz()
    {
        var s = Oturum();
        var bulunanlar = new List<string>();
        string? imlec = null;
        for (int sayfa = 0; sayfa < 20; sayfa++)   // sonsuz döngü koruması
        {
            var r = _personel.List(s, new PageRequest { Limit = 2, Cursor = imlec });
            bulunanlar.AddRange(r.Items.Select(x => x.Id));
            imlec = r.NextCursor;
            if (imlec is null) break;
        }

        Assert.Contains("a-tek", bulunanlar);
    }

    /// <summary>PRS-01 — ilk sayfa BOŞ dönmemeli (kapsam dışı kayıtlar sayfayı yutmamalı).</summary>
    [Fact]
    public void PRS01b_Ilk_Sayfa_Bos_Donmez()
    {
        var r = _personel.List(Oturum(), new PageRequest { Limit = 2 });
        Assert.NotEmpty(r.Items);
    }

    /// <summary>Regresyon kilidi: GÖRÜNEN KÜME değişmemeli — kapsam dışı şube ASLA sızmamalı.</summary>
    [Fact]
    public void PRS01c_Kapsam_Disi_Sube_Hala_Gizli()
    {
        var r = _personel.List(Oturum(), new PageRequest { Limit = 200 });
        Assert.All(r.Items, p => Assert.NotEqual(SubeB, p.BranchId));
        Assert.Contains(r.Items, p => p.Id == "a-tek");
    }

    /// <summary>Regresyon kilidi: ŞUBESİZ (firma geneli) kayıt herkese görünmeye devam eder.</summary>
    [Fact]
    public void PRS01d_Subesiz_Kayit_Gorunur_Kalir()
    {
        Personel("subesiz", null, olusturma: 50);
        var r = _personel.List(Oturum(), new PageRequest { Limit = 200 });
        Assert.Contains(r.Items, p => p.Id == "subesiz");
    }

    /// <summary>Regresyon kilidi: ADMİN tüm şubeleri görmeye devam eder.</summary>
    [Fact]
    public void PRS01e_Admin_Tum_Subeleri_Gorur()
    {
        var admin = new SessionContext("adm", Co, new[] { RoleKeys.CompanyAdmin },
            new PermissionSet(Array.Empty<ModulePermission>()));
        var r = _personel.List(admin, new PageRequest { Limit = 200 });

        Assert.Contains(r.Items, p => p.BranchId == SubeB);
        Assert.Contains(r.Items, p => p.BranchId == SubeA);
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
