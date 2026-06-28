using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Update;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Update;

public sealed record NewRelease(string Version, string ChecksumSha256, long SizeBytes,
    string MinSupportedVersion = "0.0.0", string? ReleaseNotes = null, bool Signed = false);

/// <summary>
/// Sürüm yayın yönetimi — yalnız Süper Admin. Checksum/sürüm doğrulaması; en güncel sürümü döndürür.
/// </summary>
public sealed class ReleaseService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public ReleaseService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Publish(SessionContext s, NewRelease dto)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Sürüm yayını yalnız Süper Admin yetkisindedir.");
        if (!SemVer.TryParse(dto.Version, out _)) throw new ArgumentException("Geçersiz sürüm (X.Y.Z).");
        if (!SemVer.TryParse(dto.MinSupportedVersion, out _)) throw new ArgumentException("Geçersiz min sürüm.");
        if (dto.ChecksumSha256?.Length != 64 || !IsHex(dto.ChecksumSha256))
            throw new ArgumentException("Geçersiz SHA-256 checksum (64 hex).");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO app_releases(id, version, checksum_sha256, size_bytes, min_supported_version, release_notes, signed, published_at, created_at, is_deleted)
VALUES($id,$v,$cs,$sz,$min,$notes,$signed,$now,$now,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$v", dto.Version);
            cmd.Parameters.AddWithValue("$cs", dto.ChecksumSha256.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$sz", dto.SizeBytes);
            cmd.Parameters.AddWithValue("$min", dto.MinSupportedVersion);
            cmd.Parameters.AddWithValue("$notes", (object?)dto.ReleaseNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$signed", dto.Signed ? 1 : 0);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry("__global__", "app_release", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"version\":\"{dto.Version}\"}}"), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>En güncel (en yüksek SemVer) yayın. Yoksa null.</summary>
    public UpdatePackage? Latest()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT version, checksum_sha256, size_bytes, min_supported_version, release_notes, signed " +
            "FROM app_releases WHERE is_deleted=0;";
        UpdatePackage? best = null;
        SemVer bestV = default;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!SemVer.TryParse(r.GetString(0), out var v)) continue;
            if (best is null || v.CompareTo(bestV) > 0)
            {
                bestV = v;
                best = new UpdatePackage(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5) == 1);
            }
        }
        return best;
    }

    /// <summary>Yayınlanan tüm sürümler (en yeni üstte). Yalnız Süper Admin.</summary>
    public IReadOnlyList<ReleaseRow> List(SessionContext s)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Sürüm listesi yalnız Süper Admin yetkisindedir.");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT version, checksum_sha256, size_bytes, min_supported_version, release_notes, signed, published_at " +
            "FROM app_releases WHERE is_deleted=0 ORDER BY published_at DESC;";
        var list = new List<ReleaseRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ReleaseRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5) == 1, r.GetInt64(6)));
        return list;
    }

    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);
}

public sealed record ReleaseRow(string Version, string ChecksumSha256, long SizeBytes,
    string MinSupportedVersion, string? ReleaseNotes, bool Signed, long PublishedAt)
{
    public string SizeDisplay => $"{SizeBytes / 1024.0 / 1024.0:0.##} MB";
    public string SignedDisplay => Signed ? "İmzalı" : "İmzasız";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(PublishedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string NotesDisplay => string.IsNullOrWhiteSpace(ReleaseNotes) ? "—" : ReleaseNotes!;
}
