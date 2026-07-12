namespace DepoWise.Api;

/// <summary>Güncelleme paketi dosya deposu (UPDATE_CONTRACT). Sürüme göre saklar; indirme URL'i buradan servis edilir.
///
/// SAKLAMA POLİTİKASI (KRİTİK): Her paket ~85 MB, Fly.io kalıcı diski ~1 GB. Eski paketler hiç temizlenmediği için
/// disk 12.07.2026'da DOLDU; SQLite "database or disk is full" verip **login dahil tüm API 500** döndü (tam kesinti).
/// Güncelleyici daima EN SON sürümü indirdiğinden eski paketler ölü ağırlıktır → yeni paket kaydedilince
/// en yeni <see cref="KeepCount"/> paket dışındakiler otomatik silinir.
/// </summary>
public sealed class ReleaseStore
{
    /// <summary>Diskte tutulacak en yeni paket sayısı (geri dönüş ihtimaline karşı 1'den fazla).</summary>
    public const int KeepCount = 3;

    private readonly string _root;
    public ReleaseStore(string root) { _root = root; Directory.CreateDirectory(root); }

    private static string Safe(string s) => string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    public async Task<string> SaveAsync(string version, Stream content, CancellationToken ct)
    {
        var path = Path.Combine(_root, $"DepoWise-{Safe(version)}.pkg");
        await using (var fs = File.Create(path))
            await content.CopyToAsync(fs, ct);

        PruneOld();   // disk dolup sistemi kilitlemesin
        return path;
    }

    /// <summary>En yeni KeepCount paket dışındaki .pkg dosyalarını siler (en son yazılan = en yeni).
    /// Temizlik başarısız olursa yayın bozulmaz (sessiz geçilir; bir sonraki yayında tekrar denenir).</summary>
    private void PruneOld()
    {
        try
        {
            var old = new DirectoryInfo(_root)
                .GetFiles("DepoWise-*.pkg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepCount)
                .ToList();
            foreach (var f in old)
            {
                try { f.Delete(); Console.WriteLine($"[DepoWise] Eski güncelleme paketi silindi: {f.Name}"); }
                catch { /* dosya kilitliyse bir sonraki yayında denenir */ }
            }
        }
        catch { /* dizin okunamazsa yayını bozma */ }
    }

    public string? PathFor(string version)
    {
        var path = Path.Combine(_root, $"DepoWise-{Safe(version)}.pkg");
        return File.Exists(path) ? path : null;
    }

    public sealed record PackageInfo(string Version, string FileName, long SizeBytes, DateTime ModifiedUtc);

    /// <summary>Diskteki güncelleme paketleri (en yeni önce). Canlı sunucu ekranı bunu listeler.</summary>
    public IReadOnlyList<PackageInfo> ListPackages()
    {
        try
        {
            return new DirectoryInfo(_root).GetFiles("DepoWise-*.pkg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new PackageInfo(VersionOf(f.Name), f.Name, f.Length, f.LastWriteTimeUtc))
                .ToList();
        }
        catch { return Array.Empty<PackageInfo>(); }
    }

    /// <summary>"DepoWise-1.0.47.pkg" → "1.0.47".</summary>
    private static string VersionOf(string fileName)
    {
        var n = Path.GetFileNameWithoutExtension(fileName);
        const string pre = "DepoWise-";
        return n.StartsWith(pre, StringComparison.Ordinal) ? n[pre.Length..] : n;
    }

    /// <summary>Belirli sürümün paketini MANUEL siler (süper admin, canlı sunucu ekranı). Yoksa false.</summary>
    public bool Delete(string version)
    {
        try
        {
            var path = Path.Combine(_root, $"DepoWise-{Safe(version)}.pkg");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            Console.WriteLine($"[DepoWise] Güncelleme paketi MANUEL silindi: {version}");
            return true;
        }
        catch { return false; }
    }

    public sealed record DiskInfo(long TotalBytes, long FreeBytes, long UsedBytes, long PackagesBytes, int PackageCount);

    /// <summary>Paketlerin bulunduğu diskin (Fly.io kalıcı disk /data) canlı doluluk bilgisi + paket toplamı.</summary>
    public DiskInfo GetDiskInfo()
    {
        long total = 0, free = 0;
        try
        {
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && _root.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
                .OrderByDescending(d => d.RootDirectory.FullName.Length)
                .FirstOrDefault();
            if (drive is not null) { total = drive.TotalSize; free = drive.AvailableFreeSpace; }
        }
        catch { }
        long pkgBytes = 0; int pkgCount = 0;
        try
        {
            foreach (var f in new DirectoryInfo(_root).GetFiles("DepoWise-*.pkg")) { pkgBytes += f.Length; pkgCount++; }
        }
        catch { }
        return new DiskInfo(total, free, total - free, pkgBytes, pkgCount);
    }
}
