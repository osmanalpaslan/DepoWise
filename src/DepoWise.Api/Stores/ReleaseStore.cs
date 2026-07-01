namespace DepoWise.Api;

/// <summary>Güncelleme paketi dosya deposu (UPDATE_CONTRACT). Sürüme göre saklar; indirme URL'i buradan servis edilir.</summary>
public sealed class ReleaseStore
{
    private readonly string _root;
    public ReleaseStore(string root) { _root = root; Directory.CreateDirectory(root); }

    private static string Safe(string s) => string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    public async Task<string> SaveAsync(string version, Stream content, CancellationToken ct)
    {
        var path = Path.Combine(_root, $"DepoWise-{Safe(version)}.pkg");
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return path;
    }

    public string? PathFor(string version)
    {
        var path = Path.Combine(_root, $"DepoWise-{Safe(version)}.pkg");
        return File.Exists(path) ? path : null;
    }
}
