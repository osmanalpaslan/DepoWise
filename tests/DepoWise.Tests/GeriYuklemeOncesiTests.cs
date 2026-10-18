using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-19 (AR-GE 2026-10-18, 05 F-043) — YEDEKTEN DÖNÜŞTE GÖNDERİLMEMİŞ İŞLER KAYBOLMASIN ═══
///  GYO1 — Geri yükleme önce mevcut verinin "geri_yukleme_oncesi" kopyasını alır: yedekten SONRA yapılan iş kopyada durur
///  GYO2 — Boş (şemasız) veritabanı dosyası geri yüklenmez; mevcut veri değişmez
///  GYO3 — Masaüstü: eşitleme kapısı tutulur, önce bekleyenler gönderilir, gönderilemeyen varsa ayrıca onay (kaynak kilidi)
/// </summary>
public class GeriYuklemeOncesiTests : IDisposable
{
    private readonly string _dbPath, _backupFolder;
    private readonly SqliteConnectionFactory _factory;
    private readonly MaterialService _materials;
    private readonly BackupService _backup;
    private readonly SessionContext _admin;

    public GeriYuklemeOncesiTests()
    {
        var stamp = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_gyo_" + stamp + ".db");
        _backupFolder = Path.Combine(Path.GetTempPath(), "depowise_gyo_bak_" + stamp);
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory);
        _backup = new BackupService(_factory, backupFolder: _backupFolder);
        var uid = new UserService(_factory).EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" }) try { File.Delete(p); } catch { }
        try { Directory.Delete(_backupFolder, true); } catch { }
    }

    private static List<string> Kodlar(string dbYolu)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbYolu, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT code FROM materials WHERE is_deleted=0 ORDER BY code;";
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    [Fact]
    public void GYO1_Geri_Yukleme_Oncesi_Kopya_Sonradan_Yapilan_Isi_Tutar()
    {
        _materials.Create(_admin, new NewMaterial("GYO-1", "Yedekte var"));
        var yedek = _backup.Backup();
        _materials.Create(_admin, new NewMaterial("GYO-2", "Yedekten sonra (gönderilmemiş)"));

        var oncesi = _backup.Restore(_admin, yedek, reauthenticated: true);

        Assert.Equal(new[] { "GYO-1" }, Kodlar(_dbPath));                         // geri yüklendi
        Assert.True(File.Exists(oncesi));
        Assert.EndsWith("_geri_yukleme_oncesi.db", oncesi);
        Assert.StartsWith(_backupFolder, oncesi);
        Assert.True(_backup.YedekGecerliMi(oncesi, out _));
        Assert.Equal(new[] { "GYO-1", "GYO-2" }, Kodlar(oncesi));               // sonradan yapılan iş kaybolmadı
        Assert.Contains(_backup.ListBackups(), b => b.Path == oncesi);           // yedek listesinde görünür (geri dönülebilir)
    }

    [Fact]
    public void GYO2_Bos_Veritabani_Geri_Yuklenmez()
    {
        _materials.Create(_admin, new NewMaterial("GYO-3", "Mevcut"));
        Directory.CreateDirectory(_backupFolder);
        var bos = Path.Combine(_backupFolder, "depowise_yedek_bos.db");
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = bos, Pooling = false }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE x(a INTEGER);";                        // geçerli SQLite, bütünlük "ok" — ama şema yok
            cmd.ExecuteNonQuery();
        }
        Assert.True(_backup.IntegrityCheck(bos));                                 // eski kontrol bunu kabul ederdi

        var ex = Assert.Throws<InvalidOperationException>(() => _backup.Restore(_admin, bos, reauthenticated: true));
        Assert.Contains("geri yüklenemez", ex.Message);
        Assert.Equal(new[] { "GYO-3" }, Kodlar(_dbPath));                         // mevcut veri yerinde
    }

    [Fact]
    public void GYO3_Masaustu_Once_Gonderir_Sonra_Geri_Yukler()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var vm = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "ViewModels", "BackupViewModel.cs"));
        var kapi = vm.IndexOf("await SyncGate.EnterAsync()", StringComparison.Ordinal);
        var gonder = vm.IndexOf("await BusinessSyncPushService.PushAsync()", StringComparison.Ordinal);
        var uyari = vm.IndexOf("GÖNDERİLEMEYEN değişiklikler var", StringComparison.Ordinal);
        var geri = vm.IndexOf("DesktopServices.Backup.Restore(", StringComparison.Ordinal);
        Assert.True(kapi > 0 && gonder > kapi && uyari > gonder && geri > uyari, "sıra: kapı → gönder → uyarı → geri yükle");
        Assert.Contains("finally { if (!basarili) SyncGate.Exit(); }", vm);     // başarılıysa kapı bırakılmaz
    }
}
