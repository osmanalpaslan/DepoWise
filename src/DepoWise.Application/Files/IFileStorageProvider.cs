namespace DepoWise.Application.Files;

/// <summary>
/// Dosya saklama sağlayıcısı arayüzü. Geliştirmede yerel disk; üretimde nesne depolama ile değiştirilebilir.
/// Fotoğraflar operasyonel tablolara base64 YAZILMAZ; yalnız storage_key + metadata file_records'ta tutulur.
/// </summary>
public interface IFileStorageProvider
{
    string ProviderName { get; }
    /// <summary>İçeriği saklar; geri okunabilir storage_key döndürür.</summary>
    string Save(string companyId, string entityType, string entityId, string safeFileName, byte[] content);
    byte[] Read(string storageKey);
    void Delete(string storageKey);
}
