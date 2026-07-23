using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class FileTrashBackupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _filesRoot;
    private readonly string _backupFolder;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly FileService _files;
    private readonly TrashService _trash;
    private readonly BackupService _backup;
    private readonly SessionContext _admin;

    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 1, 2, 3 };
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 };
    private static readonly byte[] FakeBytes = { 0x00, 0x01, 0x02, 0x03, 0x04 };

    public FileTrashBackupTests()
    {
        var stamp = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_file_" + stamp + ".db");
        _filesRoot = Path.Combine(Path.GetTempPath(), "depowise_files_" + stamp);
        _backupFolder = Path.Combine(Path.GetTempPath(), "depowise_bak_" + stamp);
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _files = new FileService(_factory, new LocalFileStorageProvider(_filesRoot), _clock);
        _trash = new TrashService(_factory, _clock);
        _backup = new BackupService(_factory, _clock, _backupFolder);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    // ---- Dosya doğrulama ----
    [Fact]
    public void Foto_GecerliJpeg_Kaydedilir_Base64Yok()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var rec = _files.SavePhoto(_admin, "material", m, "resim.jpg", "image/jpeg", JpegBytes);
        Assert.Equal("image/jpeg", rec.Mime);
        Assert.Single(_files.GetPhotos(_admin, "material", m));

        // file_records'ta yalnız storage_key var; operasyonel tabloda base64 yok (içerik diskte)
        Assert.True(File.Exists(Path.Combine(_filesRoot, rec.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void Foto_SahteDosya_MagicByte_Reddedilir()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        // Uzantı .jpg ama içerik sahte → magic-byte eşleşmez
        Assert.Throws<InvalidOperationException>(() => _files.SavePhoto(_admin, "material", m, "sahte.jpg", "image/jpeg", FakeBytes));
    }

    [Fact]
    public void Foto_MimeIcerikUyusmazligi_Reddedilir()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        // PNG içerik ama JPEG bildirilmiş
        Assert.Throws<InvalidOperationException>(() => _files.SavePhoto(_admin, "material", m, "x.jpg", "image/jpeg", PngBytes));
    }

    [Fact]
    public void Foto_BuyukDosya_Reddedilir()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var big = new byte[FileValidation.MaxBytes + 1];
        big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        Assert.Throws<InvalidOperationException>(() => _files.SavePhoto(_admin, "material", m, "big.jpg", "image/jpeg", big));
    }

    [Fact]
    public void Foto_DenyByDefault()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _files.SavePhoto(noPerm, "material", m, "r.jpg", "image/jpeg", JpegBytes));
    }

    [Fact]
    public void GuvenliDosyaAdi_PathTraversal_Temizlenir()
    {
        var name = FileValidation.SafeFileName("../../etc/passwd", "jpg");
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("..", name);
        Assert.EndsWith(".jpg", name);
    }

    // ---- Çöp Kutusu ----
    [Fact]
    public void CopKutusu_SoftDelete_Listele_GeriYukle()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Silinecek"));
        // soft delete (doğrudan)
        SoftDeleteMaterial(m);

        var trash = _trash.List(_admin, reauthenticated: true);
        Assert.Contains(trash, t => t.Id == m && t.Table == "materials");

        _trash.Restore(_admin, "materials", m, reauthenticated: true);
        Assert.DoesNotContain(_trash.List(_admin, reauthenticated: true), t => t.Id == m);
        // Geri yüklendi → listede görünür
        Assert.Contains(_materials.List(_admin, new PageRequest { Limit = 50 }).Items, x => x.Id == m);
    }

    [Fact]
    public void CopKutusu_YenidenDogrulamaYok_Reddedilir()
    {
        Assert.Throws<ForbiddenException>(() => _trash.List(_admin, reauthenticated: false));
    }

    [Fact]
    public void CopKutusu_YetkisizKullanici_Reddedilir()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "x"));
        SoftDeleteMaterial(m);
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _trash.Restore(noPerm, "materials", m, reauthenticated: true));
    }

    // ---- Yedek ----
    [Fact]
    public void Yedek_Al_IntegrityCheck_Gecer()
    {
        _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var path = _backup.Backup();
        Assert.True(File.Exists(path));
        Assert.True(_backup.IntegrityCheck(path)); // integrity_check = ok
    }

    [Fact]
    public void Yedek_GeriYukle_VeriKorunur_AdminReauth()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var backup = _backup.Backup();

        // Yedek sonrası yeni kayıt ekle
        _materials.Create(_admin, new NewMaterial("M-2", "Sonradan"));
        Assert.Equal(2, _materials.List(_admin, new PageRequest { Limit = 50 }).Items.Count);

        // Geri yükle → yedek anına döner (M-2 yok)
        _backup.Restore(_admin, backup, reauthenticated: true);
        Assert.Single(_materials.List(_admin, new PageRequest { Limit = 50 }).Items);
        Assert.Contains(_materials.List(_admin, new PageRequest { Limit = 50 }).Items, x => x.Id == m);
    }

    [Fact]
    public void Yedek_GeriYukle_YetkiVeReauth_Zorunlu()
    {
        var backup = _backup.Backup();
        var noAdmin = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _backup.Restore(noAdmin, backup, reauthenticated: true));
        Assert.Throws<ForbiddenException>(() => _backup.Restore(_admin, backup, reauthenticated: false));
    }

    private void SoftDeleteMaterial(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE materials SET is_deleted=1 WHERE id=@id;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
        try { if (Directory.Exists(_filesRoot)) Directory.Delete(_filesRoot, true); } catch { }
        try { if (Directory.Exists(_backupFolder)) Directory.Delete(_backupFolder, true); } catch { }
    }
}
