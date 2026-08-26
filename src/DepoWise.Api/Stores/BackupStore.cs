namespace DepoWise.Api;

public sealed record ServerBackupItem(string Machine, string FileName, DateTimeOffset Date, long SizeBytes);

/// <summary>
/// Sunucu yedek deposu (SERVER_BACKUP_CONTRACT). Firma+makine bazında saklar; ÜZERİNE YAZMAZ / OTOMATİK SİLMEZ.
/// Yalnız Süper Admin'in DeleteRange çağrısı kasıtlı temizlik yapar.
/// </summary>
public sealed class BackupStore
{
    private readonly string _root;
    public BackupStore(string root) { _root = root; Directory.CreateDirectory(root); }

    private static string Safe(string s) => string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    public async Task<string> SaveAsync(string company, string machine, string filename, Stream content, CancellationToken ct)
    {
        // ⭐ YOL-01 (denetim 2026-08-26): Safe() yalnız "dosya adında geçersiz karakterleri" atar —
        // LINUX'ta bu küme yalnız '\0' ve '/' olduğu için ".." olduğu gibi geçer ve klasör kökün
        // DIŞINA çıkardı. Firma artık kimlikten geldiği için erişilemez bir yol; yine de kapatıldı.
        var dir = DepoWise.Application.Common.SafePath.UnderRoot(_root, Safe(company), Safe(machine))
                  ?? throw new InvalidOperationException("Geçersiz yedek yolu.");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(dir, $"{stamp}__{Safe(filename)}"); // benzersiz ad → üzerine yazma yok
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return path;
    }

    public IReadOnlyList<ServerBackupItem> List(string company, DateOnly from, DateOnly to)
    {
        // YOL-01: bkz. SaveAsync — firma adı kök dışına çıkamaz.
        var dir = DepoWise.Application.Common.SafePath.UnderRoot(_root, Safe(company));
        if (dir is null || !Directory.Exists(dir)) return Array.Empty<ServerBackupItem>();
        var list = new List<ServerBackupItem>();
        foreach (var machineDir in Directory.GetDirectories(dir))
        {
            var machine = Path.GetFileName(machineDir);
            foreach (var f in Directory.GetFiles(machineDir))
            {
                var info = new FileInfo(f);
                var d = DateOnly.FromDateTime(info.LastWriteTime);
                if (d < from || d > to) continue;
                list.Add(new ServerBackupItem(machine, Path.GetFileName(f),
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length));
            }
        }
        return list.OrderByDescending(x => x.Date).ToList();
    }

    public int DeleteRange(string company, DateOnly from, DateOnly to)
    {
        // ⭐ YOL-01 (denetim 2026-08-26) — BU EN TEHLİKELİ NOKTAYDI: firma adı istekten geliyor
        // (DELETE /api/backups?company=…) ve ".." verilseydi taranan klasör YEDEKLERİN ÜST KLASÖRÜ
        // (sunucu veri kökü) olurdu → tarih aralığına giren TÜM dosyalar (fotoğraflar, yayın paketleri,
        // SQLite'a düşülmüşse veritabanı) silinirdi. Süper admin gerektirir ama sonuç geri alınamaz.
        var dir = DepoWise.Application.Common.SafePath.UnderRoot(_root, Safe(company));
        if (dir is null || !Directory.Exists(dir)) return 0;
        int n = 0;
        foreach (var machineDir in Directory.GetDirectories(dir))
            foreach (var f in Directory.GetFiles(machineDir))
            {
                var d = DateOnly.FromDateTime(new FileInfo(f).LastWriteTime);
                if (d < from || d > to) continue;
                try { File.Delete(f); n++; } catch { }
            }
        return n;
    }
}
