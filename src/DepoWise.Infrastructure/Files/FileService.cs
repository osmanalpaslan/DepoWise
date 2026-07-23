using System.Security.Cryptography;
using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Files;

public sealed record FileRecordDto(string Id, string EntityType, string EntityId, string StorageKey, string? Mime, long? SizeBytes);

/// <summary>
/// Dosya/fotoğraf servisi: doğrulama (boyut/MIME/magic-byte) + güvenli ad + provider'a yaz + file_records
/// metadata (provider, key, mime, size, sha256). Operasyonel tabloya base64 YAZMAZ. Tenant + permission fail-closed.
/// </summary>
public sealed class FileService
{
    private static readonly Dictionary<string, string> EntityModule = new(StringComparer.Ordinal)
    {
        ["material"] = "materials",
        ["vehicle"] = "vehicles",
        ["vehicle_template"] = "vehicles",
        ["material_template"] = "materials",
        ["personnel"] = "personnel",
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IFileStorageProvider _storage;
    private readonly IClock _clock;

    public FileService(IDbConnectionFactory factory, IFileStorageProvider storage, IClock? clock = null)
    {
        _factory = factory;
        _storage = storage;
        _clock = clock ?? new SystemClock();
    }

    public FileRecordDto SavePhoto(SessionContext s, string entityType, string entityId, string? fileName, string? declaredMime, byte[] content)
    {
        var module = ResolveModule(entityType);
        AccessControl.Require(s, module, PermissionAction.Edit);

        var v = FileValidation.ValidateImage(fileName, declaredMime, content);
        if (!v.Ok) throw new InvalidOperationException(v.Error);

        // Foto optimizasyonu (SkiaSharp): büyük görseli küçült + JPEG'e sıkıştır. Çözülemezse orijinal kalır.
        var (optMime, optBytes) = ImageOptimizer.Optimize(content, v.DetectedMime!);
        var ext = optMime == "image/jpeg" ? "jpg" : v.DetectedExt!;
        var safeName = FileValidation.SafeFileName(fileName, ext);
        var storageKey = _storage.Save(s.CompanyId, entityType, entityId, safeName, optBytes);
        var sha = Convert.ToHexString(SHA256.HashData(optBytes));
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO file_records(id, company_id, entity_type, entity_id, kind, storage_provider, storage_key,
    mime, size_bytes, sha256, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@et,@eid,'photo',@prov,@key,@mime,@size,@sha,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@et", entityType);
            cmd.AddWithValue("@eid", entityId);
            cmd.AddWithValue("@prov", _storage.ProviderName);
            cmd.AddWithValue("@key", storageKey);
            cmd.AddWithValue("@mime", optMime);
            cmd.AddWithValue("@size", optBytes.Length);
            cmd.AddWithValue("@sha", sha);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "file_record", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"entity\":\"{entityType}\",\"mime\":\"{optMime}\"}}"), _clock);
        tx.Commit();
        return new FileRecordDto(id, entityType, entityId, storageKey, optMime, optBytes.Length);
    }

    public IReadOnlyList<FileRecordDto> GetPhotos(SessionContext s, string entityType, string entityId)
    {
        AccessControl.Require(s, ResolveModule(entityType), PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, entity_type, entity_id, storage_key, mime, size_bytes FROM file_records " +
            "WHERE company_id=@c AND entity_type=@et AND entity_id=@eid AND is_deleted=0 ORDER BY created_at;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@et", entityType);
        cmd.AddWithValue("@eid", entityId);
        var list = new List<FileRecordDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FileRecordDto(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5)));
        return list;
    }

    public void DeletePhoto(SessionContext s, string fileId)
    {
        using var conn = _factory.Create();
        string? entityType, companyId;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT entity_type, company_id FROM file_records WHERE id=@id;";
            read.AddWithValue("@id", fileId);
            using var r = read.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Dosya bulunamadı.");
            entityType = r.GetString(0); companyId = r.GetString(1);
        }
        if (companyId != s.CompanyId) throw new ForbiddenException("Dosya başka firmaya ait.");
        AccessControl.Require(s, ResolveModule(entityType!), PermissionAction.Delete);

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE file_records SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@now", _clock.UtcNow.ToUnixTimeMilliseconds());
            cmd.AddWithValue("@id", fileId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "file_record", fileId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    private static string ResolveModule(string entityType)
        => EntityModule.TryGetValue(entityType, out var m) ? m
            : throw new ArgumentException($"Bilinmeyen entity tipi: {entityType}");
}
