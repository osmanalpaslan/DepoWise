using DepoWise.Application.Files;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Files;

/// <summary>
/// Yerel disk sağlayıcısı: %LOCALAPPDATA%\DepoWise\Files\&lt;company&gt;\&lt;entity&gt;\&lt;id&gt;_&lt;ad&gt;.
/// storage_key = kök'e göre RELATIF yol (taşınabilir). Path traversal'a karşı kök içine sınırlanır.
/// </summary>
public sealed class LocalFileStorageProvider : IFileStorageProvider
{
    public string ProviderName => "local";
    private readonly string _root;

    public LocalFileStorageProvider(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppPaths.AppFolderName, "Files");
        Directory.CreateDirectory(_root);
    }

    public string Save(string companyId, string entityType, string entityId, string safeFileName, byte[] content)
    {
        var rel = Path.Combine(Clean(companyId), Clean(entityType), $"{Clean(entityId)}_{safeFileName}");
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new InvalidOperationException("Geçersiz depolama yolu.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return rel.Replace('\\', '/');
    }

    public byte[] Read(string storageKey)
    {
        var full = Resolve(storageKey);
        return File.ReadAllBytes(full);
    }

    public void Delete(string storageKey)
    {
        var full = Resolve(storageKey);
        if (File.Exists(full)) File.Delete(full);
    }

    private string Resolve(string storageKey)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new InvalidOperationException("Geçersiz storage_key.");
        return full;
    }

    private static string Clean(string s)
        => new(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
}
