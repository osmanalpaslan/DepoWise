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
}
