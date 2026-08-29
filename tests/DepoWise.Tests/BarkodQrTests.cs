using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Search;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ BAR-01 (ADR-177) — BARKOD / QR TESTLERİ ═══
///
/// Kilitler (PK-O1..O4 = A): QR üretimi geçerli PNG + içerik = MEVCUT kayıt kodu (yeni kimlik alanı
/// YOK) · üretim SALT-OKUNUR (kaynak satırlar bit-bit değişmez; tekrar üretim de) · tara→bul→git
/// kuralı (TekTamEslesme): TAM ve TEK eşleşmede hit, birden çok tam / yalnız kısmi / sıfır / HasMore
/// durumlarında NULL (mevcut panel davranışı korunur) · tarama ARA-01 kapılarından geçer: yetkisiz
/// kaynak sorgulanmaz, tenant, BranchAccess, silinmiş kayıt bulunmaz · web QR ucunun kod çözümü
/// kaynak servis kapılarıyla (Require/tenant) çalışır · MIGRATION YOK — şema 81'de kalır.
/// </summary>
public class BarkodQrTests : IDisposable
{
    private const string Co = "BAR";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly string _uid, _sube1, _sube2;
    private readonly SessionContext _admin;
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly EquipmentService _equipment;
    private readonly SearchService _search;

    public BarkodQrTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_bar_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _sube1 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye B", "site"));
        _materials = new MaterialService(_f);
        _vehicles = new VehicleService(_f);
        _equipment = new EquipmentService(_f);
        _search = new SearchService(_f);   // masaüstü bağlaması: documents null (çevrimdışı yerel arama)
    }

    private void Firma(string id)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, false, false, false))))
        { ScopeBranchIds = kapsam };

    /// <summary>Kaynak tabloların satır fotoğrafı — QR/tarama salt-okunurluğunun bit-bit kanıtı.</summary>
    private string Foto(params string[] tablolar)
    {
        var sb = new System.Text.StringBuilder();
        using var conn = _f.Create();
        foreach (var t in tablolar)
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

    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47 };

    private static void PngMi(byte[] bytes)
    {
        Assert.True(bytes.Length > 100, "PNG beklenenden küçük");
        Assert.Equal(PngMagic, bytes.Take(4).ToArray());
    }

    private IReadOnlyList<SearchGroup> Ara(SessionContext s, string q) => _search.Search(s, q);

    // ══════════════ QR ÜRETİMİ (O1..O5) ══════════════

    /// <summary>Malzeme/araç/ekipman: kod KAYNAK SERVİSLE çözülür (API ucundaki yolun aynısı) ve
    /// geçerli PNG üretilir. Kod = MEVCUT kimlik alanı (yeni alan yok).</summary>
    [Fact]
    public void BAR1_Uc_Kaynaktan_Qr_Uretimi()
    {
        var mat = _materials.Create(_admin, new NewMaterial("MLZ-01", "Çimento"));
        var arac = _vehicles.Create(_admin, new NewVehicle("ARC-01", "34ABC123"));
        var ekp = _equipment.Create(_admin, new NewEquipment("EKP-01", "Jeneratör", SerialNo: "SN-99"));

        // /api/qr/{entity}/{id} ucundaki çözümün birebir aynısı:
        var mKod = _materials.GetDetail(_admin, mat).Code;
        var aKod = _vehicles.Get(_admin, arac).InternalCode;
        var eKod = _equipment.List(_admin).First(e => e.Id == ekp).Code;
        Assert.Equal("MLZ-01", mKod);
        Assert.Equal("ARC-01", aKod);
        Assert.Equal("EKP-01", eKod);
        PngMi(QrLabelService.Png(mKod));
        PngMi(QrLabelService.Png(aKod));
        PngMi(QrLabelService.Png(eKod));
    }

    /// <summary>Üretim deterministik (aynı kod → aynı bayt) ve içerik koda bağlı (farklı kod → farklı
    /// bayt) — kütüphanede çözücü olmadığından içerik kanıtı budur; ayrıca dosya adı güvenli üretilir.</summary>
    [Fact]
    public void BAR2_Icerik_Koda_Bagli_Ve_Dosya_Adi_Guvenli()
    {
        var a1 = QrLabelService.Png("MLZ-01");
        var a2 = QrLabelService.Png("MLZ-01");
        var b = QrLabelService.Png("MLZ-02");
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.Equal("QR_MLZ-01.png", QrLabelService.FileName("MLZ-01"));
        Assert.Equal("QR_A_B_C.png", QrLabelService.FileName("A/B\\C"));   // dosya sistemi karakterleri _
    }

    /// <summary>Türkçe karakterli kod üretilir; boş içerik reddedilir.</summary>
    [Fact]
    public void BAR3_Turkce_Ve_Bos_Icerik()
    {
        PngMi(QrLabelService.Png("ĞÜŞİÖÇ-ığüşiöç-01"));
        Assert.Throws<ArgumentException>(() => QrLabelService.Png("   "));
    }

    // ══════════════ QR ÇÖZÜMÜNDE KAPILAR (O11/O13/O16) ══════════════

    /// <summary>Yetkisiz kullanıcı QR ucundan kod ÇÖZEMEZ: kaynak servis Require fırlatır (403).</summary>
    [Fact]
    public void BAR4_Qr_Ucu_Kaynak_Yetkisi_Ister()
    {
        var mat = _materials.Create(_admin, new NewMaterial("GIZLI-1", "Gizli"));
        var yetkisiz = Personel();   // hiçbir modül yetkisi yok
        Assert.Throws<ForbiddenException>(() => _materials.GetDetail(yetkisiz, mat));
        Assert.Throws<ForbiddenException>(() => _vehicles.Get(yetkisiz, "x"));
        Assert.Throws<ForbiddenException>(() => _equipment.List(yetkisiz));
    }

    /// <summary>Başka firmanın kayıt id'siyle QR ucundan kod SIZMAZ (tenant serviste).</summary>
    [Fact]
    public void BAR5_Qr_Ucu_Tenant_Sizdirmaz()
    {
        Firma("BAR-B");
        var uidB = new UserService(_f).EnsureInitialAdmin("BAR-B", "adminb", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(uidB, "BAR-B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var matB = _materials.Create(adminB, new NewMaterial("B-GIZ", "B Malzemesi"));
        // A firması admin'i B'nin id'siyle kod çözemez (kayıt A kapsamında yok → istisna; veri dönmez).
        Assert.ThrowsAny<Exception>(() => _materials.GetDetail(_admin, matB));
    }

    // ══════════════ TARA → BUL → GİT (PK-O4 · O7..O14) ══════════════

    /// <summary>⭐ TAM ve TEK eşleşme → hit döner (kısmi eşleşen komşular otomatik açılışı ENGELLEMEZ:
    /// "MLZ-100" taraması, "MLZ-1000" listede diye 2 adıma düşmez).</summary>
    [Fact]
    public void BAR6_Tam_Tek_Eslesme_Acilir()
    {
        var hedef = _materials.Create(_admin, new NewMaterial("MLZ-100", "Vida"));
        _materials.Create(_admin, new NewMaterial("MLZ-1000", "Somun"));
        var hit = SearchService.TekTamEslesme(Ara(_admin, "MLZ-100"), "MLZ-100");
        Assert.NotNull(hit);
        Assert.Equal(hedef, hit!.Id);
        Assert.Equal("materials", hit.NavigateKey);
    }

    /// <summary>⭐ Aynı kod İKİ kaynakta (malzeme + ekipman) → otomatik açılış YOK (panel).</summary>
    [Fact]
    public void BAR7_Coklu_Tam_Eslesme_Acilmaz()
    {
        _materials.Create(_admin, new NewMaterial("ORTAK-1", "Malzeme"));
        _equipment.Create(_admin, new NewEquipment("ORTAK-1", "Ekipman"));
        Assert.Null(SearchService.TekTamEslesme(Ara(_admin, "ORTAK-1"), "ORTAK-1"));
    }

    /// <summary>Yalnız KISMİ eşleşme → otomatik açılış YOK; sıfır sonuç → YOK (mevcut davranış).</summary>
    [Fact]
    public void BAR8_Kismi_Veya_Sifir_Sonuc_Acilmaz()
    {
        _materials.Create(_admin, new NewMaterial("MLZ-200", "Vida"));
        _materials.Create(_admin, new NewMaterial("MLZ-201", "Somun"));
        Assert.Null(SearchService.TekTamEslesme(Ara(_admin, "MLZ-20"), "MLZ-20"));      // kısmi (2 sonuç, tam yok)
        Assert.Null(SearchService.TekTamEslesme(Ara(_admin, "YOK-999"), "YOK-999"));    // sıfır sonuç
    }

    /// <summary>HasMore'lu grup varsa açılmaz: kırpılan satırlarda ikinci tam eşleşme gizlenmiş olabilir.</summary>
    [Fact]
    public void BAR9_HasMore_Grubu_Acilmaz()
    {
        var hit = new SearchHit("materials", "Malzemeler", "id1", "KOD-1", null, "materials");
        var gruplar = new[] { new SearchGroup("Malzemeler", "materials", new[] { hit }, HasMore: true) };
        Assert.Null(SearchService.TekTamEslesme(gruplar, "KOD-1"));
    }

    /// <summary>⭐ Silinmiş kayıt taramayla BULUNMAZ (Çöp Kutusu'nda kalır) → otomatik açılış da olmaz.</summary>
    [Fact]
    public void BAR10_Silinmis_Kayit_Taranamaz()
    {
        var id = _materials.Create(_admin, new NewMaterial("SIL-9", "Silinecek"));
        Assert.NotNull(SearchService.TekTamEslesme(Ara(_admin, "SIL-9"), "SIL-9"));
        _materials.Delete(_admin, id);
        var gruplar = Ara(_admin, "SIL-9");
        Assert.DoesNotContain(gruplar.SelectMany(g => g.Hits), h => h.Id == id);
        Assert.Null(SearchService.TekTamEslesme(gruplar, "SIL-9"));
    }

    /// <summary>⭐ Yetkisiz kaynak taramada HİÇ sorgulanmaz → kodu bilmek erişim vermez.</summary>
    [Fact]
    public void BAR11_Yetkisiz_Kaynak_Taramayla_Bulunamaz()
    {
        _materials.Create(_admin, new NewMaterial("YTK-1", "Yetki Testi"));
        var yetkisiz = Personel();   // materials View yok
        Assert.Empty(Ara(yetkisiz, "YTK-1"));
        Assert.Null(SearchService.TekTamEslesme(Ara(yetkisiz, "YTK-1"), "YTK-1"));
        // Yetki verilince aynı tarama bulur (kapı gerçekten yetkiye bağlı).
        var yetkili = Personel(izinler: ("materials", true));
        Assert.NotNull(SearchService.TekTamEslesme(Ara(yetkili, "YTK-1"), "YTK-1"));
    }

    /// <summary>⭐ BranchAccess: kapsam dışı şubenin kaydı taramayla bulunamaz (talep — şubeli kaynak).</summary>
    [Fact]
    public void BAR12_Kapsam_Disi_Sube_Taranamaz()
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO material_requests(id,company_id,doc_no,request_date,branch_id,status,created_at,updated_at,version,is_deleted)
VALUES(@id,@c,'TAL-777',1,@b,'pending',1,1,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", Co);
            cmd.AddWithValue("@b", _sube2);
            cmd.ExecuteNonQuery();
        }
        var kapsamli = Personel(kapsam: new[] { _sube1 }, izinler: ("requests", true));
        Assert.Null(SearchService.TekTamEslesme(Ara(kapsamli, "TAL-777"), "TAL-777"));
        Assert.NotNull(SearchService.TekTamEslesme(Ara(_admin, "TAL-777"), "TAL-777"));
    }

    /// <summary>⭐ Tenant: başka firmanın kodu taramayla bulunamaz.</summary>
    [Fact]
    public void BAR13_Tenant_Kodu_Taranamaz()
    {
        Firma("BAR-C");
        var uidC = new UserService(_f).EnsureInitialAdmin("BAR-C", "adminc", "admin123", RoleKeys.CompanyAdmin);
        var adminC = new SessionContext(uidC, "BAR-C", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _materials.Create(adminC, new NewMaterial("C-KOD", "C Malzemesi"));
        Assert.Null(SearchService.TekTamEslesme(Ara(_admin, "C-KOD"), "C-KOD"));
    }

    // ══════════════ SALT-OKUNURLUK + ŞEMA (O6/O18/O19/O20) ══════════════

    /// <summary>⭐ Tarama + QR üretimi (tekrar tekrar) kaynak satırları BİT-BİT değiştirmez; senkrona
    /// yeni veri de girmez (hiçbir tabloya yazılmadığının kanıtı aynı fotoğraftır).</summary>
    [Fact]
    public void BAR14_Tarama_Ve_Qr_BitBit_Degistirmez()
    {
        var mat = _materials.Create(_admin, new NewMaterial("BIT-1", "Çimento"));
        _vehicles.Create(_admin, new NewVehicle("BIT-ARC", "06XYZ42"));
        _equipment.Create(_admin, new NewEquipment("BIT-EKP", "Kompresör"));
        var tablolar = new[] { "materials", "vehicles", "equipment", "branches", "users" };
        var once = Foto(tablolar);
        for (int i = 0; i < 3; i++)   // tekrar üretim de değiştirmez (O20)
        {
            _ = QrLabelService.Png(_materials.GetDetail(_admin, mat).Code);
            _ = SearchService.TekTamEslesme(Ara(_admin, "BIT-1"), "BIT-1");
            _ = Ara(_admin, "BIT");
        }
        Assert.Equal(once, Foto(tablolar));
    }

    /// <summary>⭐ Şema, migration KATALOĞUYLA tutarlıdır (runner her kayıtlı migration'ı uygular).
    /// Tarihçe: BAR-01 turunda sabit "81" idi ("BAR migration getirmez" kilidi — hâlâ doğru: BAR
    /// migration eklemedi). ADR-179'da Migration082 BİLİNÇLİ eklendiği için sabit ESKİDİ; kilit artık
    /// eskimeyecek biçimde kataloğun kendisine bağlandı (amaç aynı: kayıt dışı şema değişikliği olamaz).
    /// ADR-180 (2026-08-29): Migration082 master'dan geri çekildi (PK-R4=B) — katalog max yine 81.
    /// ADR-185 (2026-08-29): FIN-B1 onaylandı; Migration082 yeniden eklendi (7 hedef: 6 operasyon tablosu
    /// + sync_inbox) → katalog max 82. Kilit kataloğa bağlı olduğu için sabit güncellemesi GEREKMEDİ.</summary>
    [Fact]
    public void BAR15_Sema_Katalogla_Tutarli()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal((long)MigrationCatalog.All().Max(m => m.Version), Convert.ToInt64(cmd.ExecuteScalar()));
    }
}
