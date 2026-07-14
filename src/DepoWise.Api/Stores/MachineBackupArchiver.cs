using System.IO.Compression;

namespace DepoWise.Api;

/// <summary>Bir makinenin yedek özeti (ekran için).</summary>
public sealed record MachineBackupSummary(
    string CompanyId, string Machine,
    int DailyCount, long DailyBytes, DateTimeOffset? LastBackup,
    int ArchiveCount, long ArchiveBytes)
{
    public long TotalBytes => DailyBytes + ArchiveBytes;
}

/// <summary>Aylık arşiv (zip) satırı.</summary>
public sealed record MachineArchiveItem(string Name, string Month, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Makine yedeklerinin sunucuda ARŞİVLENMESİ ve SAKLANMASI.
///
/// Politika (kullanıcı kararı):
/// - Masaüstü her gün yedek yükler → /data/backups/{firma}/{makine}/{stamp}__ad.db (ham günlük).
/// - Bir AY TAMAMLANINCA (o ayın günlük yedekleri artık artmaz) o ayın tüm günlükleri tek bir
///   {yyyy-MM}.zip içine alınır ve HAM DOSYALAR SİLİNİR (sunucuda yer kaplamasın).
/// - Aylık arşivler 3 YIL (36 ay) saklanır; daha eskiler budanır.
///
/// DİSK KORUMASI (ADR-070: disk dolunca TÜM API 500 vermişti):
/// - Bakım her çalıştığında disk doluluğu kontrol edilir. Kritik eşiğin altına inilirse EN ESKİ arşivler
///   3 yıl dolmasa bile budanır. Bu, "dolmaz" varsayımına güvenmek yerine sigortadır.
/// </summary>
public sealed class MachineBackupArchiver
{
    public const int RetentionMonths = 36;          // 3 yıl
    private const string ArchiveDir = "_arsiv";
    private const long MinFreeBytes = 150L * 1024 * 1024;   // en az 150 MB boş kalmalı
    private const double MinFreeRatio = 0.12;               // veya %12 boş

    private readonly string _root;
    private static DateTime _lastRun = DateTime.MinValue;
    private static readonly object _gate = new();

    public MachineBackupArchiver(string backupsRoot) { _root = backupsRoot; Directory.CreateDirectory(_root); }

    private static string Safe(string s) => string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    private string MachineDir(string company, string machine) => Path.Combine(_root, Safe(company), Safe(machine));
    private string ArchivePath(string company, string machine) => Path.Combine(MachineDir(company, machine), ArchiveDir);

    /// <summary>Ham günlük yedekler (arşiv klasörü hariç).</summary>
    private static FileInfo[] RawFiles(string machineDir) =>
        !Directory.Exists(machineDir) ? Array.Empty<FileInfo>()
        : Directory.GetFiles(machineDir).Select(f => new FileInfo(f)).ToArray();

    /// <summary>Yedek yükleme sonrası ÇAĞRILIR (kısıtlı: 6 saatte bir). Arşivleme + budama + disk koruması.</summary>
    public void RunMaintenanceThrottled()
    {
        lock (_gate)
        {
            if ((DateTime.UtcNow - _lastRun).TotalHours < 6) return;
            _lastRun = DateTime.UtcNow;
        }
        try { RunMaintenance(); } catch { /* bakım hatası yükleme akışını bozmaz */ }
    }

    /// <summary>Arşivle (tamamlanan aylar) → ham dosyaları sil → 3 yılı aşan arşivleri buda → disk koruması.</summary>
    public void RunMaintenance()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var companyDir in Directory.GetDirectories(_root))
            foreach (var machineDir in Directory.GetDirectories(companyDir))
            {
                ArchiveCompletedMonths(machineDir);
                PruneOldArchives(machineDir);
            }
        EnforceDiskGuard();
    }

    /// <summary>Tamamlanan ayların günlük yedeklerini {yyyy-MM}.zip'e alır, sonra ham dosyaları SİLER.</summary>
    private void ArchiveCompletedMonths(string machineDir)
    {
        var raw = RawFiles(machineDir);
        if (raw.Length == 0) return;
        var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");
        var archiveDir = Path.Combine(machineDir, ArchiveDir);
        Directory.CreateDirectory(archiveDir);

        foreach (var group in raw.GroupBy(f => f.LastWriteTimeUtc.ToString("yyyy-MM")))
        {
            // İçinde bulunulan AY henüz tamamlanmadı → dokunma (günlükler artmaya devam eder).
            if (group.Key == currentMonth) continue;

            var zipPath = Path.Combine(archiveDir, group.Key + ".zip");
            try
            {
                // Mevcut zip'e ekle (kesinti sonrası tekrar çalışırsa kayıp olmasın), yoksa oluştur.
                using (var zip = ZipFile.Open(zipPath, File.Exists(zipPath) ? ZipArchiveMode.Update : ZipArchiveMode.Create))
                {
                    foreach (var f in group)
                    {
                        if (zip.GetEntry(f.Name) is not null) continue; // zaten arşivde
                        zip.CreateEntryFromFile(f.FullName, f.Name, CompressionLevel.Optimal);
                    }
                }
                // Zip yazıldı → ham dosyaları sil (kullanıcı: "ziplenen yedekler yer kaplamasın diye silinsin").
                foreach (var f in group) { try { f.Delete(); } catch { } }
            }
            catch { /* bu ay arşivlenemedi → ham dosyalar KORUNUR, sonraki turda tekrar denenir */ }
        }
    }

    /// <summary>3 yılı (36 ay) aşan aylık arşivleri siler.</summary>
    private void PruneOldArchives(string machineDir)
    {
        var archiveDir = Path.Combine(machineDir, ArchiveDir);
        if (!Directory.Exists(archiveDir)) return;
        var cutoff = DateTime.UtcNow.AddMonths(-RetentionMonths);
        foreach (var z in Directory.GetFiles(archiveDir, "*.zip"))
        {
            var name = Path.GetFileNameWithoutExtension(z);   // yyyy-MM
            if (DateTime.TryParse(name + "-01", out var month) && month < cutoff)
                try { File.Delete(z); } catch { }
        }
    }

    /// <summary>DİSK KORUMASI: kritik doluluğa yaklaşılırsa 3 yıl dolmasa bile EN ESKİ arşivleri buda.
    /// Amaç: disk dolup TÜM API'nin 500 vermesini (ADR-070) önlemek.</summary>
    public void EnforceDiskGuard()
    {
        var archives = AllArchives().OrderBy(a => a.Month).ToList();   // en eski önce
        int guard = 0;
        while (IsDiskCritical() && archives.Count > 0 && guard++ < 500)
        {
            var oldest = archives[0];
            archives.RemoveAt(0);
            try { File.Delete(oldest.Path); } catch { break; }
        }
    }

    private bool IsDiskCritical()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_root))!);
            if (d.TotalSize <= 0) return false;
            double freeRatio = (double)d.AvailableFreeSpace / d.TotalSize;
            return d.AvailableFreeSpace < MinFreeBytes || freeRatio < MinFreeRatio;
        }
        catch { return false; }
    }

    private sealed record ArchiveFile(string Path, string Month);

    private List<ArchiveFile> AllArchives()
    {
        var list = new List<ArchiveFile>();
        if (!Directory.Exists(_root)) return list;
        foreach (var companyDir in Directory.GetDirectories(_root))
            foreach (var machineDir in Directory.GetDirectories(companyDir))
            {
                var ad = Path.Combine(machineDir, ArchiveDir);
                if (!Directory.Exists(ad)) continue;
                foreach (var z in Directory.GetFiles(ad, "*.zip"))
                    list.Add(new ArchiveFile(z, Path.GetFileNameWithoutExtension(z)));
            }
        return list;
    }

    // ---- Ekran için okuma ----

    /// <summary>Tüm makinelerin yedek özeti (firma+makine bazında).</summary>
    public IReadOnlyList<MachineBackupSummary> Summaries()
    {
        var list = new List<MachineBackupSummary>();
        if (!Directory.Exists(_root)) return list;
        foreach (var companyDir in Directory.GetDirectories(_root))
        {
            var company = Path.GetFileName(companyDir);
            foreach (var machineDir in Directory.GetDirectories(companyDir))
            {
                var machine = Path.GetFileName(machineDir);
                var raw = RawFiles(machineDir);
                var arch = ListArchives(company, machine);
                DateTimeOffset? last = raw.Length > 0
                    ? new DateTimeOffset(raw.Max(f => f.LastWriteTimeUtc), TimeSpan.Zero)
                    : (arch.Count > 0 ? arch.Max(a => a.CreatedAt) : null);
                list.Add(new MachineBackupSummary(company, machine,
                    raw.Length, raw.Sum(f => f.Length), last,
                    arch.Count, arch.Sum(a => a.SizeBytes)));
            }
        }
        return list.OrderByDescending(x => x.LastBackup ?? DateTimeOffset.MinValue).ToList();
    }

    /// <summary>Bir makinenin aylık arşivleri (yeni → eski).</summary>
    public IReadOnlyList<MachineArchiveItem> ListArchives(string company, string machine)
    {
        var ad = ArchivePath(company, machine);
        if (!Directory.Exists(ad)) return Array.Empty<MachineArchiveItem>();
        return Directory.GetFiles(ad, "*.zip")
            .Select(p => new FileInfo(p))
            .Select(fi => new MachineArchiveItem(fi.Name, Path.GetFileNameWithoutExtension(fi.Name), fi.Length,
                new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)))
            .OrderByDescending(a => a.Month)
            .ToList();
    }

    /// <summary>Bir makinenin HAM (henüz arşivlenmemiş, içinde bulunulan ay) günlük yedekleri.</summary>
    public IReadOnlyList<ServerBackupItem> ListDaily(string company, string machine)
    {
        var md = MachineDir(company, machine);
        return RawFiles(md)
            .Select(fi => new ServerBackupItem(machine, fi.Name,
                new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero), fi.Length))
            .OrderByDescending(x => x.Date)
            .ToList();
    }

    /// <summary>İndirme için arşiv dosya yolu (yol kaçışına karşı korumalı). Yoksa null.</summary>
    public string? ResolveArchive(string company, string machine, string name)
    {
        if (name.Contains("..") || name.Contains('/') || name.Contains('\\')) return null;
        var p = Path.Combine(ArchivePath(company, machine), Safe(name));
        return File.Exists(p) ? p : null;
    }
}
