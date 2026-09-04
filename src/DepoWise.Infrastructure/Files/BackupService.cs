using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Files;

public sealed record BackupInfo(string Path, long SizeBytes, long CreatedAt);

/// <summary>
/// Masaüstü SQLite yedeği: tutarlı tek dosya (`VACUUM INTO`), 30 gün saklama, bütünlük kontrolü
/// (PRAGMA integrity_check) ve gerçek geri yükleme. Yedek klasörü uygulama dışı (Belgeler\Alpnex_Yedekler).
/// </summary>
public sealed class BackupService
{
    public const int RetentionDays = 30;
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly string _folder;

    public BackupService(IDbConnectionFactory factory, IClock? clock = null, string? backupFolder = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _folder = backupFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Alpnex_Yedekler");
        Directory.CreateDirectory(_folder);
    }

    public string GetBackupFolder() => _folder;

    /// <summary>
    /// ⭐ YED-01 (denetim 2026-08-26) — BU YEDEKLEME YÖNTEMİ YALNIZ SQLite İÇİNDİR.
    ///
    /// <b>Bulunan durum:</b> yöntem <c>VACUUM INTO</c> (tek dosya kopyası) + <c>PRAGMA integrity_check</c>
    /// kullanır; ikisi de SQLite'a özgüdür. Sunucu 2026-07-24'te PostgreSQL'e taşındığından beri
    /// üretimde "Yedek Al" düğmesi ham bir veritabanı hatasıyla düşüyordu (kullanıcıya anlamsız metin).
    ///
    /// <b>Neden burada düzeltilmiyor:</b> PostgreSQL'in dosya yedeği <c>pg_dump</c> ister; o araç sunucu
    /// konteynerinde yoktur ve uygulama içinde bir dökümcü yazmak yeni bir ÖZELLİKTİR (kararı kullanıcıya
    /// aittir). Bu turda yapılan: hatanın ANLAŞILIR olması ve yanlış bir güven vermemesi.
    ///
    /// Masaüstünde (SQLite) davranış HİÇ DEĞİŞMEDİ — asıl kullanım yeri orasıdır.
    /// </summary>
    private static void SqliteGerekir(System.Data.Common.DbConnection conn, string islem)
    {
        if (SqlDialect.IsSqlite(conn)) return;
        throw new InvalidOperationException(
            $"Sunucu veritabanı {islem} işlemi bu sunucuda yapılamaz: veritabanı PostgreSQL'dir ve bu ekranın " +
            "kullandığı tek-dosya yöntemi (SQLite) PostgreSQL'de çalışmaz. PostgreSQL yedeği, veritabanı " +
            "sağlayıcısının sürekli yedeği (PITR) üzerinden ya da pg_dump ile alınır. Masaüstü yedeklemesi " +
            "etkilenmez.");
    }

    /// <summary>Tutarlı yedek alır (VACUUM INTO); eski yedekleri (30 gün) temizler. Yedek yolunu döndürür.</summary>
    public string Backup()
    {
        var date = _clock.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var path = Path.Combine(_folder, $"depowise_yedek_{date}.db");

        using (var on = _factory.Create()) SqliteGerekir(on, "yedeği");   // YED-01: dosyaya DOKUNMADAN önce

        if (File.Exists(path)) File.Delete(path);

        try
        {
            using var conn = _factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO @p;";
            cmd.AddWithValue("@p", path);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Yarım/boş dosya BIRAKILMAZ: aksi halde "yedeğim var" sanılır (aşağıdaki nota bakın).
            TryDelete(path);
            throw new InvalidOperationException(
                "Yedek alınamadı. Veritabanı hasarlı olabilir; Ayarlar → Veritabanı Sağlığı'ndan kontrol edin. " +
                "Ayrıntı: " + ex.Message, ex);
        }

        // ⭐ 🔴 YEDEK DOĞRULAMA (kullanıcı bildirimi 2026-09-04) ────────────────────────────────
        //
        // BULUNAN GERÇEK OLAY: 04.09.2026 07:41'de üretilen yedek dosyası 0 BAYTTI, ama işlem
        // "başarılı" raporlanmıştı. Nedeni zincirdi:
        //   (1) Kaynak veritabanı bozulmuştu → VACUUM INTO çıktı üretemedi,
        //   (2) Backup() sonucu HİÇ doğrulamıyordu,
        //   (3) IntegrityCheck() metodu vardı ama HİÇBİR YERDEN ÇAĞRILMIYORDU (ölü kod),
        //   (4) integrity_check BOŞ bir veritabanı için de "ok" döner → tek başına yetersiz.
        // Sonuç: kullanıcı elinde geçerli bir yedek olduğunu sanıyordu. Yedeğin sessizce boş olması,
        // yedek olmamasından DAHA TEHLİKELİDİR.
        //
        // Artık yedek, döndürülmeden önce GERÇEKTEN doğrulanır; geçemezse dosya silinir ve hata atılır.
        if (!YedekGecerliMi(path, out var hata))
        {
            TryDelete(path);
            throw new InvalidOperationException("Yedek doğrulanamadı, bu yüzden GEÇERSİZ sayıldı ve silindi: " + hata);
        }

        PurgeOld();
        return path;
    }

    /// <summary>
    /// Yedek dosyasının bütünlüğünü doğrular (integrity_check = ok).
    /// <b>Not:</b> tek başına YETMEZ — boş bir veritabanı da "ok" döner. Bkz. <see cref="YedekGecerliMi"/>.
    /// </summary>
    public bool IntegrityCheck(string backupPath)
    {
        if (!File.Exists(backupPath)) return false;
        var cs = new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return string.Equals(cmd.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Yedeğin KULLANILABİLİR olduğunu doğrular. Üç kapı, sırayla en ucuzdan:
    /// (1) dosya var ve boş değil · (2) <c>integrity_check = ok</c> · (3) şema GERÇEKTEN dolu
    /// (<c>schema_migrations</c> tablosu var ve en az bir satırı var).
    /// Üçüncü kapı kritiktir: 0 baytlık/boş bir veritabanı ilk ikisini geçebilir.
    /// </summary>
    public bool YedekGecerliMi(string backupPath, out string hata)
    {
        hata = "";
        if (!File.Exists(backupPath)) { hata = "yedek dosyası oluşmadı."; return false; }
        if (new FileInfo(backupPath).Length == 0) { hata = "yedek dosyası boş (0 bayt)."; return false; }
        if (!IntegrityCheck(backupPath)) { hata = "yedek dosyası bütünlük kontrolünü geçemedi."; return false; }

        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) { hata = "yedekte veri yok (şema tabloları bulunamadı)."; return false; }

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
            if (Convert.ToInt64(cmd2.ExecuteScalar()) == 0) { hata = "yedekte şema sürümü yok (boş veritabanı)."; return false; }
        }
        catch (Exception ex) { hata = "yedek okunamadı: " + ex.Message; return false; }

        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* silinemezse de hata atılıyor */ }
    }

    /// <summary>Yedeği canlı DB üzerine geri yükler (-wal/-shm temizlenir). Admin + reauth zorunlu.</summary>
    public void Restore(SessionContext s, string backupPath, bool reauthenticated)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Geri yükleme yalnız admin yetkisindedir.");
        if (!reauthenticated) throw new ForbiddenException("Geri yükleme için yeniden kimlik doğrulama gerekli.");
        // ⭐ YED-01: PostgreSQL'de bu yol YIKICI olurdu — yedek dosyası, veritabanı OLMAYAN bir hedefin
        // üzerine kopyalanmaya çalışılırdı. Yetkiden hemen sonra, hiçbir dosyaya dokunmadan durdurulur.
        using (var on = _factory.Create()) SqliteGerekir(on, "geri yükleme");
        if (!IntegrityCheck(backupPath)) throw new InvalidOperationException("Yedek bütünlük kontrolünden geçemedi.");

        var target = _factory.DatabasePath;
        // Bağlantı havuzunu boşalt → dosya kilidi kalmaz (aksi halde File.Copy "kullanımda" hatası)
        SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "-wal", "-shm" })
            if (File.Exists(target + ext)) File.Delete(target + ext);
        File.Copy(backupPath, target, overwrite: true);
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        return Directory.GetFiles(_folder, "depowise_yedek_*.db")
            .Select(p => new FileInfo(p))
            .Select(fi => new BackupInfo(fi.FullName, fi.Length, new DateTimeOffset(fi.CreationTimeUtc).ToUnixTimeMilliseconds()))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    private void PurgeOld()
    {
        var cutoff = _clock.UtcNow.AddDays(-RetentionDays);
        foreach (var p in Directory.GetFiles(_folder, "depowise_yedek_*.db"))
        {
            try { if (File.GetCreationTimeUtc(p) < cutoff.UtcDateTime) File.Delete(p); }
            catch { /* yoksay */ }
        }
    }
}
