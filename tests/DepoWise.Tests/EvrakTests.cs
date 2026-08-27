using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ EVR-01 (ADR-165, 2026-08-27) — EVRAK / BELGE YÖNETİMİ TESTLERİ ═══
///
/// Kilitlenen kurallar: iki kapılı yetki (files + bağlı kaydın modülü) · tenant · şube/proje kapsamı ·
/// sahte/aşırı dosya reddi · binary bütünlük · fotoğraf akışına sıfır etki · Migration074 canlı-veri kanıtı.
/// </summary>
public class EvrakTests : IDisposable
{
    private const string Co = "EVR";
    private readonly string _dbPath, _storeRoot;
    private readonly SqliteConnectionFactory _f;
    private readonly DocumentService _svc;
    private readonly FileService _photos;
    private readonly string _uid, _mat, _sube1, _sube2;
    private readonly SessionContext _admin;

    /// <summary>Küçük ama GERÇEK bir PDF (magic-byte %PDF ile başlar).</summary>
    private static byte[] Pdf(string icerik = "test")
        => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n" + icerik + "\n%%EOF");
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    public EvrakTests()
    {
        var n = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_evr_" + n + ".db");
        _storeRoot = Path.Combine(Path.GetTempPath(), "dw_evr_store_" + n);
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
        var branches = new BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _mat = new DepoWise.Infrastructure.Materials.MaterialService(_f).Create(_admin, new DepoWise.Infrastructure.Materials.NewMaterial("M-1", "Çimento"));
        var storage = new LocalFileStorageProvider(_storeRoot);
        _svc = new DocumentService(_f, storage);
        _photos = new FileService(_f, storage);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_storeRoot, recursive: true); } catch { }
    }

    /// <summary>Personel oturumu: istenen modül izinleri (admin bypass YOK).</summary>
    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    // ══════════════ TEMEL AKIŞ ══════════════

    [Fact]
    public void EVR1_Yukle_Listele_Indir_Icerik_Birebir()
    {
        var pdf = Pdf("önemli sözleşme içeriği");
        var d = _svc.Save(_admin, "material", _mat, new DocumentMeta("Sözleşme", "Fatura", 1_000, 2_000, "açıklama"),
            "sozlesme.pdf", "application/pdf", pdf);

        var listed = Assert.Single(_svc.List(_admin));
        Assert.Equal("Sözleşme", listed.Title);
        Assert.Equal("Malzeme", listed.EntityTypeDisplay);
        Assert.Equal("Çimento", listed.EntityLabel);
        Assert.Equal("application/pdf", listed.Mime);
        Assert.Equal(pdf.Length, listed.SizeBytes);

        // ⭐ BINARY BÜTÜNLÜK: indirilen içerik yüklenenle BİT-BİT aynı.
        var (bytes, name, mime) = _svc.Download(_admin, d.Id);
        Assert.Equal(pdf, bytes);
        Assert.Equal("application/pdf", mime);
        Assert.EndsWith(".pdf", name);
    }

    /// <summary>Genel firma evrakı: kayda bağlanmadan yüklenir (entity_id = firma).</summary>
    [Fact]
    public void EVR2_Genel_Firma_Evraki()
    {
        _svc.Save(_admin, "company", null, new DocumentMeta("Vergi Levhası"), "levha.png", null, PngBytes);
        var d = Assert.Single(_svc.List(_admin));
        Assert.Equal("company", d.EntityType);
        Assert.Equal(Co, d.EntityId);
    }

    [Fact]
    public void EVR3_Meta_Duzenleme_Dosya_Icerigi_Degismez()
    {
        var pdf = Pdf();
        var d = _svc.Save(_admin, "material", _mat, new DocumentMeta("Eski"), "a.pdf", null, pdf);
        _svc.UpdateMeta(_admin, d.Id, new DocumentMeta("Yeni Başlık", "Ruhsat", 5_000, 9_000, "not"));
        var g = Assert.Single(_svc.List(_admin));
        Assert.Equal("Yeni Başlık", g.Title);
        Assert.Equal("Ruhsat", g.DocType);
        Assert.Equal(pdf, _svc.Download(_admin, d.Id).Bytes);   // içerik aynen durur

        Assert.Throws<ArgumentException>(() => _svc.UpdateMeta(_admin, d.Id, new DocumentMeta("X", ValidFrom: 9, ValidUntil: 5)));
    }

    // ══════════════ DOĞRULAMA (sahte/aşırı dosya) ══════════════

    [Fact]
    public void EVR4_Sahte_Ve_Izinsiz_Dosyalar_Reddedilir()
    {
        // uzantı pdf ama içerik PDF DEĞİL → sahte
        Assert.Throws<InvalidOperationException>(() =>
            _svc.Save(_admin, "material", _mat, new DocumentMeta("X"), "sahte.pdf", null, new byte[] { 1, 2, 3, 4, 5 }));
        // izin verilmeyen uzantı (çalıştırılabilir)
        Assert.Throws<InvalidOperationException>(() =>
            _svc.Save(_admin, "material", _mat, new DocumentMeta("X"), "virus.exe", null, Pdf()));
        // boyut aşımı (7 MB + 1)
        var buyuk = new byte[DocumentValidation.MaxBytes + 1];
        buyuk[0] = 0x25; buyuk[1] = 0x50; buyuk[2] = 0x44; buyuk[3] = 0x46;
        Assert.Throws<InvalidOperationException>(() =>
            _svc.Save(_admin, "material", _mat, new DocumentMeta("X"), "buyuk.pdf", null, buyuk));
        Assert.Empty(_svc.List(_admin));   // hiçbiri kaydolmadı
    }

    // ══════════════ YETKİ — İKİ KAPI ══════════════

    /// <summary>files modülü olmayan HİÇBİR belge işlemi yapamaz (deny-by-default).</summary>
    [Fact]
    public void EVR5_Files_Yetkisi_Olmayan_Reddedilir()
    {
        var d = _svc.Save(_admin, "material", _mat, new DocumentMeta("Gizli"), "a.pdf", null, Pdf());
        var yetkisiz = Personel(izinler: ("materials", true, true, true, true));   // files YOK
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Download(yetkisiz, d.Id));
        Assert.Throws<ForbiddenException>(() => _svc.Save(yetkisiz, "material", _mat, new DocumentMeta("X"), "b.pdf", null, Pdf()));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(yetkisiz, d.Id));
    }

    /// <summary>⭐ İKİNCİ KAPI: files yetkisi VAR ama bağlı kaydın modülünü göremiyorsa —
    /// listede o belge GÖRÜNMEZ, indirmesi REDDEDİLİR (merkezi ekran yan kapı değildir).</summary>
    [Fact]
    public void EVR6_Bagli_Kaydin_Modulunu_Goremeyen_Belgesini_De_Goremez()
    {
        var matDoc = _svc.Save(_admin, "material", _mat, new DocumentMeta("Malzeme Belgesi"), "m.pdf", null, Pdf());
        _svc.Save(_admin, "branch", _sube1, new DocumentMeta("Şube Belgesi"), "s.pdf", null, Pdf());

        // files tam + branches View var, materials YOK:
        var s = Personel(izinler: new[] { ("files", true, true, true, true), ("branches", true, false, false, false) });
        var gorulen = _svc.List(s);
        Assert.DoesNotContain(gorulen, x => x.Id == matDoc.Id);          // malzeme belgesi sızmadı
        Assert.Contains(gorulen, x => x.Title == "Şube Belgesi");        // yetkili olduğu görünür
        Assert.Throws<ForbiddenException>(() => _svc.Download(s, matDoc.Id));   // id tahmini de işe yaramaz
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsamı Şantiye A olan kullanıcı, Şantiye B'nin belgesini görmez/indiremez.</summary>
    [Fact]
    public void EVR7_Sube_Kapsami_Disindaki_Belge_Gorunmez()
    {
        var dA = _svc.Save(_admin, "branch", _sube1, new DocumentMeta("A Belgesi"), "a.pdf", null, Pdf());
        var dB = _svc.Save(_admin, "branch", _sube2, new DocumentMeta("B Belgesi"), "b.pdf", null, Pdf());

        var dar = Personel(kapsam: new[] { _sube1 },
            izinler: new[] { ("files", true, true, true, true), ("branches", true, true, true, true) });
        var gorulen = _svc.List(dar).Select(x => x.Id).ToHashSet();
        Assert.Contains(dA.Id, gorulen);
        Assert.DoesNotContain(dB.Id, gorulen);
        Assert.Throws<ForbiddenException>(() => _svc.Download(dar, dB.Id));
        Assert.Throws<ForbiddenException>(() => _svc.Save(dar, "branch", _sube2, new DocumentMeta("Sızma"), "x.pdf", null, Pdf()));
    }

    /// <summary>⭐ TENANT: başka firmanın belgesi görünmez, indirilemez, silinemez; kaydına belge asılamaz.</summary>
    [Fact]
    public void EVR8_Firma_Izolasyonu()
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('BASKA','BASKA',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var d = _svc.Save(_admin, "material", _mat, new DocumentMeta("Gizli"), "g.pdf", null, Pdf("sır"));
        Assert.Empty(_svc.List(yabanci));
        Assert.Throws<ForbiddenException>(() => _svc.Download(yabanci, d.Id));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(yabanci, d.Id));
        Assert.Throws<ArgumentException>(() =>
            _svc.Save(yabanci, "material", _mat, new DocumentMeta("X"), "x.pdf", null, Pdf()));   // başka firmanın kaydına asılamaz
    }

    // ══════════════ SİLME + AUDIT ══════════════

    [Fact]
    public void EVR9_Soft_Delete_Ve_Audit()
    {
        var d = _svc.Save(_admin, "material", _mat, new DocumentMeta("Silinecek"), "s.pdf", null, Pdf());
        _svc.Delete(_admin, d.Id);
        Assert.Empty(_svc.List(_admin));
        using var conn = _f.Create();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT is_deleted FROM file_records WHERE id=@id;";
            cmd.AddWithValue("@id", d.Id);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // satır DURUYOR (soft)
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_type='file_record' AND entity_id=@id;";
            cmd.AddWithValue("@c", Co);
            cmd.AddWithValue("@id", d.Id);
            Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 2);   // create + delete izleri
        }
        Assert.Contains("file_record", ScreenAuditMap.EntityTypes("files"));   // ekran logu kapsar
    }

    // ══════════════ FOTOĞRAF AKIŞINA SIFIR ETKİ ══════════════

    /// <summary>⭐ Mevcut fotoğraf akışı AYNEN çalışır ve iki dünya birbirine KARIŞMAZ:
    /// fotoğraf belge listesinde görünmez; fotoğraf belge ucundan İNDİRİLEMEZ.</summary>
    [Fact]
    public void EVR10_Fotograf_Akisi_Etkilenmez_Ve_Karismaz()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var foto = _photos.SavePhoto(_admin, "material", _mat, "foto.jpg", null, jpeg);
        Assert.Single(_photos.GetPhotos(_admin, "material", _mat));

        _svc.Save(_admin, "material", _mat, new DocumentMeta("Belge"), "b.pdf", null, Pdf());
        Assert.Single(_svc.List(_admin));                                    // yalnız belge
        Assert.Single(_photos.GetPhotos(_admin, "material", _mat));          // yalnız fotoğraf
        Assert.Throws<ForbiddenException>(() => _svc.Download(_admin, foto.Id));   // foto belge ucundan inmez
    }

    // ══════════════ ⭐⭐ CANLI VERİ GÜVENLİĞİ — MIGRATION074 KANITI ══════════════

    /// <summary>Canlı senaryo provası: v73 şemasında MEVCUT fotoğraf kaydı varken yalnız Migration074
    /// uygulanır → eski kolon değerleri BİT-BİT aynı kalır, yeni kolonlar NULL doğar.</summary>
    [Fact]
    public void EVR11_Migration074_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_evr_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 73)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO file_records(id,company_id,entity_type,entity_id,kind,storage_provider,storage_key,mime,size_bytes,sha256,created_at,updated_at,version,is_deleted)
    VALUES('F1','C1','material','M1','photo','local','C1/material/M1_foto.jpg','image/jpeg',123,'abc',11,11,2,0);";
                cmd.ExecuteNonQuery();
            }
            const string eskiKolonlar = "id,company_id,entity_type,entity_id,kind,storage_provider,storage_key,mime,size_bytes,sha256,created_at,updated_at,version,is_deleted";
            string Foto(SqliteConnectionFactory ff)
            {
                using var conn = ff.Create();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {eskiKolonlar} FROM file_records ORDER BY id;";
                using var r = cmd.ExecuteReader();
                var sb = new System.Text.StringBuilder();
                while (r.Read())
                    for (int i = 0; i < r.FieldCount; i++)
                        sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                return sb.ToString();
            }
            var once = Foto(f);
            var uygulanan = new MigrationRunner(f, new IMigration[] { new Migration074_DocumentFields() }).Run();
            Assert.Equal(new[] { 74 }, uygulanan);
            Assert.Equal(once, Foto(f));   // eski kolonlar bit-bit aynı
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM file_records WHERE title IS NULL AND doc_type IS NULL AND uploaded_by IS NULL;";
                Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // yeni kolonlar NULL doğdu
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    /// <summary>Statik kanıt: Migration074 kaynağında mevcut veriyi değiştirebilecek komut yok
    /// (UPDATE/DELETE/DROP/INSERT) — yalnız ADD COLUMN + CREATE INDEX.</summary>
    [Fact]
    public void EVR12_Migration074_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration074_DocumentFields.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }
}
