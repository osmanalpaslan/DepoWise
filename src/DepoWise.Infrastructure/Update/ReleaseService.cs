using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Update;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Update;

public sealed record NewRelease(string Version, string ChecksumSha256, long SizeBytes,
    string MinSupportedVersion = "0.0.0", string? ReleaseNotes = null, bool Signed = false, string? DownloadUrl = null);

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

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // ⚠️ 2026-09-02 DÜZELTMESİ — AYNI SÜRÜMÜ YENİDEN YAYINLAMA.
        // `app_releases(version)` ÜZERİNDE UNIQUE INDEX VARDIR (Migration012). Eskiden burada koşulsuz
        // INSERT yapılıyordu; aynı sürüm ikinci kez yayınlandığında bu INSERT unique ihlaliyle PATLIYORDU.
        // Ama uç (`POST /api/releases`) paket dosyasını BU ÇAĞRIDAN ÖNCE diske yazar ve dosyayı sürüm
        // adıyla EZER → sonuç: disktekiler yeni paket, veritabanındaki checksum/boyut/not ESKİ kayıt.
        // Yani "sürüm kaydı ile paket birbirini tutmuyor" durumu oluşuyordu (güncelleme checksum
        // doğrulamasında bozuk paket sayılır ve KURULMAZ).
        // Doğru semantik: "X sürümünü yayınla" YENİDEN yayınlanabilir olmalı ve kaydı GÜNCELLEMELİDİR.
        // Kimlik (id) korunur; sürüm satırı ikizlenmez → Latest() belirsizliğe düşmez.
        var mevcutId = FindIdByVersion(conn, tx, dto.Version);
        var id = mevcutId ?? Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = mevcutId is null
                ? @"
INSERT INTO app_releases(id, version, checksum_sha256, size_bytes, min_supported_version, release_notes, signed, download_url, published_at, created_at, is_deleted)
VALUES(@id,@v,@cs,@sz,@min,@notes,@signed,@url,@now,@now,0);"
                : @"
UPDATE app_releases
   SET checksum_sha256=@cs, size_bytes=@sz, min_supported_version=@min, release_notes=@notes,
       signed=@signed, download_url=@url, published_at=@now, is_deleted=0
 WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@v", dto.Version);
            cmd.AddWithValue("@cs", dto.ChecksumSha256.ToUpperInvariant());
            cmd.AddWithValue("@sz", dto.SizeBytes);
            cmd.AddWithValue("@min", dto.MinSupportedVersion);
            cmd.AddWithValue("@notes", (object?)dto.ReleaseNotes ?? DBNull.Value);
            cmd.AddWithValue("@signed", dto.Signed ? 1 : 0);
            cmd.AddWithValue("@url", (object?)dto.DownloadUrl ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry("__global__", "app_release", id,
            mevcutId is null ? AuditActions.Create : AuditActions.Update, s.UserId,
            AfterJson: $"{{\"version\":\"{dto.Version}\"}}"), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Bu sürüm zaten yayınlanmış mı? (Yeniden yayında kaydın kimliği korunur.)</summary>
    private static string? FindIdByVersion(DbConnection conn, DbTransaction tx, string version)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM app_releases WHERE version=@v;";
        cmd.AddWithValue("@v", version);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>En güncel (en yüksek SemVer) yayın. Yoksa null.</summary>
    public UpdatePackage? Latest()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT version, checksum_sha256, size_bytes, min_supported_version, release_notes, signed, download_url " +
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
                    r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5) == 1,
                    r.IsDBNull(6) ? null : r.GetString(6));
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
