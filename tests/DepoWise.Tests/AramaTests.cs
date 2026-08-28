using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Announcements;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Search;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA-01 (ADR-174, 2026-08-28) — GLOBAL ARAMA TESTLERİ ═══
///
/// Kilitler: PK-K1 kayıt/kart kaynakları · PK-K2 yalnız kimlik alanları · PK-K3 gezinme anahtarları ·
/// PK-K4 silinmiş aranmaz · PK-K5 yeni yetki YOK — kaynak modül kapısı (yetkisiz kategori HİÇ dönmez) ·
/// BranchAccess · tenant · duyuru okuma-herkese + pencere kuralı · LIMIT+HasMore · başlayan-önce
/// sıralama · min uzunluk · salt-okunurluk (bit-bit) · offline (belge servissiz sessiz) · MIGRATION YOK.
/// </summary>
public class AramaTests : IDisposable
{
    private const string Co = "ARA";
    private readonly string _dbPath, _storeRoot;
    private readonly SqliteConnectionFactory _f;
    private readonly SearchService _svc;
    private readonly string _uid, _sube1, _sube2;
    private readonly SessionContext _admin;
    private static readonly long Gun = 1_700_000_000_000;

    public AramaTests()
    {
        var n = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ara_" + n + ".db");
        _storeRoot = Path.Combine(Path.GetTempPath(), "dw_ara_store_" + n);
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _svc = new SearchService(_f, new DocumentService(_f, new LocalFileStorageProvider(_storeRoot)));
    }

    private static void Firma(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_storeRoot, recursive: true); } catch { }
    }

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    private IReadOnlyList<SearchGroup> Ara(string q, SessionContext? s = null) => _svc.Search(s ?? _admin, q);
    private SearchGroup? Grup(IReadOnlyList<SearchGroup> gs, string display) => gs.FirstOrDefault(g => g.ModuleDisplay == display);

    // ══════════════ TEMEL ══════════════

    /// <summary>Kimlik alanlarında eşleşme (ad + kod/SubLabel); BAŞLAYAN içerenden önce gelir.</summary>
    [Fact]
    public void ARA1_Eslesme_Ve_Baslayan_Once()
    {
        var mat = new MaterialService(_f);
        mat.Create(_admin, new NewMaterial("K-100", "Çimento Torba"));
        mat.Create(_admin, new NewMaterial("K-200", "Beyaz Çimento"));
        mat.Create(_admin, new NewMaterial("K-300", "Demir"));

        var g = Grup(Ara("çimento"), "Malzemeler");
        Assert.NotNull(g);
        Assert.Equal(2, g!.Hits.Count);
        Assert.Equal("Çimento Torba", g.Hits[0].Label);   // BAŞLAYAN önce
        Assert.Equal("Beyaz Çimento", g.Hits[1].Label);
        Assert.Equal("materials", g.Hits[0].NavigateKey);

        // Kod (SubLabel) araması da kimlik alanıdır (PK-K2):
        var gk = Grup(Ara("K-300"), "Malzemeler");
        Assert.Equal("Demir", Assert.Single(gk!.Hits).Label);
    }

    /// <summary>Min 2 karakter (PK sade kural): kısa/boş sorgu HİÇ kaynak sorgulamaz.</summary>
    [Fact]
    public void ARA2_Min_Uzunluk()
    {
        new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "A"));
        Assert.Empty(Ara(""));
        Assert.Empty(Ara(" a "));
        Assert.NotEmpty(Ara("K-1"));
    }

    /// <summary>Kategori başına LIMIT + HasMore ("daha fazlası için ekrana git").</summary>
    [Fact]
    public void ARA3_Limit_Ve_HasMore()
    {
        var mat = new MaterialService(_f);
        for (int i = 1; i <= 7; i++) mat.Create(_admin, new NewMaterial($"V-{i}", $"Vida Tip {i}"));
        var g = Grup(Ara("vida"), "Malzemeler");
        Assert.Equal(SearchService.PerSourceLimit, g!.Hits.Count);
        Assert.True(g.HasMore);
    }

    // ══════════════ GÜVENLİK — PK-K5 (yan kapı yok) + kapsam + tenant ══════════════

    /// <summary>⭐ YAN KAPI YOK: kaynak modül yetkisi olmayan kategori HİÇ dönmez; yetkili kategori döner.</summary>
    [Fact]
    public void ARA4_Yetkisiz_Kategori_Hic_Donmez()
    {
        new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "Ortak Kelime"));
        new WorkOrderService(_f).Create(_admin, new NewWorkOrder("IE-1", "Ortak Kelime"));

        var yalnizMalzeme = Personel(null, ("materials", true, false, false, false));
        var gruplar = Ara("ortak", yalnizMalzeme);
        Assert.NotNull(Grup(gruplar, "Malzemeler"));
        Assert.Null(Grup(gruplar, "İş Emirleri"));   // work_orders yetkisi yok → kategori HİÇ sorgulanmadı

        var hicYetkisiz = Personel();
        Assert.Empty(Ara("ortak", hicYetkisiz).Where(g => g.ModuleDisplay is "Malzemeler" or "İş Emirleri"));
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsam dışı şubenin iş emri/takvim kaydı sonuca SIZMAZ; şubesiz görünür.</summary>
    [Fact]
    public void ARA5_Sube_Kapsami()
    {
        var wo = new WorkOrderService(_f);
        wo.Create(_admin, new NewWorkOrder("IE-A", "Kazı A", BranchId: _sube1));
        wo.Create(_admin, new NewWorkOrder("IE-B", "Kazı B", BranchId: _sube2));
        wo.Create(_admin, new NewWorkOrder("IE-0", "Kazı Genel"));
        new DepoWise.Infrastructure.Calendars.CalendarService(_f).Create(_admin,
            new DepoWise.Infrastructure.Calendars.NewCalendarEvent("Kazı Toplantısı", Gun, BranchId: _sube2));

        var dar = Personel(new[] { _sube1 },
            ("work_orders", true, false, false, false), ("calendar", true, false, false, false));
        var gruplar = Ara("kazı", dar);
        var isEmirleri = Grup(gruplar, "İş Emirleri")!.Hits.Select(h => h.Label).ToList();
        Assert.Contains("Kazı A", isEmirleri);
        Assert.Contains("Kazı Genel", isEmirleri);
        Assert.DoesNotContain("Kazı B", isEmirleri);
        Assert.Null(Grup(gruplar, "Takvim"));   // tek takvim kaydı kapsam dışı şubede → kategori boş kalır
    }

    /// <summary>⭐ TENANT: başka firmanın oturumu hiçbir sonucu göremez.</summary>
    [Fact]
    public void ARA6_Firma_Izolasyonu()
    {
        new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "Bizim Malzeme"));
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(Ara("bizim", yabanci));
    }

    /// <summary>PK-K4: SİLİNMİŞ kayıt aranmaz — yalnız Çöp Kutusu'nda kalır.</summary>
    [Fact]
    public void ARA7_Silinmis_Kayit_Aranmaz()
    {
        var id = new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "Silinecek Malzeme"));
        Assert.NotNull(Grup(Ara("silinecek"), "Malzemeler"));
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE materials SET is_deleted=1 WHERE id=@id;";   // test DB'sinde soft-delete temsili
            cmd.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        Assert.Null(Grup(Ara("silinecek"), "Malzemeler"));
    }

    // ══════════════ ÖZEL KURALLI KAYNAKLAR ══════════════

    /// <summary>Duyuru: okuma HERKESE (PK-J1 kuralı aramada da geçerli); yayın penceresi dışındaki duyuru
    /// yönetici-dışına sonuçta görünmez (kural serviste).</summary>
    [Fact]
    public void ARA8_Duyuru_Herkese_Ve_Pencere()
    {
        var ann = new AnnouncementService(_f);
        ann.Create(_admin, new NewAnnouncement("Bayram Duyurusu"));
        ann.Create(_admin, new NewAnnouncement("Gelecek Duyuru", PublishStart: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5 * 86_400_000));

        var yetkisiz = Personel();
        var g = Grup(Ara("duyuru", yetkisiz), "Duyurular");
        Assert.Equal("Bayram Duyurusu", Assert.Single(g!.Hits).Label);   // aktif olmayan sızmadı

        var gAdmin = Grup(Ara("duyuru"), "Duyurular");
        Assert.Equal(2, gAdmin!.Hits.Count);   // yönetici tümünü görür (DYR kuralı serviste)
    }

    /// <summary>Evrak: yalnız METADATA (başlık) aranır; belge servisi YOKKEN (masaüstü çevrimdışı temsili)
    /// kategori sessizce yok — hata yok. onlySources yalnız istenen kaynakları tarar (uzak çağrı sözleşmesi).</summary>
    [Fact]
    public void ARA9_Evrak_Metadata_Ve_Offline()
    {
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\ntest\n%%EOF");
        var matId = new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "Çimento"));
        new DocumentService(_f, new LocalFileStorageProvider(_storeRoot)).Save(_admin, "material", matId,
            new DocumentMeta("Garanti Sözleşmesi", null, null, null, null), "g.pdf", "application/pdf", pdf);

        var g = Grup(Ara("garanti"), "Evrak");
        Assert.Equal("Garanti Sözleşmesi", Assert.Single(g!.Hits).Label);

        var offline = new SearchService(_f, documents: null);
        Assert.Null(Grup(offline.Search(_admin, "garanti"), "Evrak"));   // sessiz, hatasız

        // onlySources: yalnız istenen kaynaklar taranır (masaüstünün uzak Proje+Evrak isteği).
        var yalniz = _svc.Search(_admin, "çimento", new[] { "documents", "projects" });
        Assert.Null(Grup(yalniz, "Malzemeler"));
    }

    /// <summary>Proje: ProjectService üzerinden (kapsam içeride) — ad eşleşmesi.</summary>
    [Fact]
    public void ARA10_Proje_Aramasi()
    {
        new ProjectService(_f).Create(_admin, new NewProject("Köprü Projesi", BranchIds: new[] { _sube1 }));
        var g = Grup(Ara("köprü"), "Projeler");
        Assert.Equal("Köprü Projesi", Assert.Single(g!.Hits).Label);
        Assert.Equal("projects", g.Hits[0].NavigateKey);
    }

    /// <summary>⭐ Arama SALT-OKUNURDUR: kaynak kayıtlar bit-bit değişmez.</summary>
    [Fact]
    public void ARA11_Kaynaklar_BitBit_Degismez()
    {
        new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "Çimento"));
        new WorkOrderService(_f).Create(_admin, new NewWorkOrder("IE-1", "Kazı", BranchId: _sube1));
        new AnnouncementService(_f).Create(_admin, new NewAnnouncement("Duyuru"));

        string Foto()
        {
            var sb = new System.Text.StringBuilder();
            using var conn = _f.Create();
            foreach (var t in new[] { "materials", "work_orders", "announcements", "branches" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM {t} ORDER BY 1;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    for (int i = 0; i < r.FieldCount; i++)
                        sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            }
            return sb.ToString();
        }
        var once = Foto();
        _ = Ara("çimento"); _ = Ara("kazı"); _ = Ara("duyuru");
        Assert.Equal(once, Foto());
    }

    /// <summary>Birden çok kategori aynı sorguda birlikte döner (gruplu model).</summary>
    [Fact]
    public void ARA12_Coklu_Kategori()
    {
        new MaterialService(_f).Create(_admin, new NewMaterial("K-1", "Şantiye Malzemesi"));
        new WorkOrderService(_f).Create(_admin, new NewWorkOrder("IE-1", "Şantiye Kurulumu"));
        var gruplar = Ara("şantiye");
        Assert.NotNull(Grup(gruplar, "Malzemeler"));
        Assert.NotNull(Grup(gruplar, "İş Emirleri"));
        Assert.NotNull(Grup(gruplar, "Şube / Şantiye"));   // şubeler de "Şantiye A/B"
    }
}
