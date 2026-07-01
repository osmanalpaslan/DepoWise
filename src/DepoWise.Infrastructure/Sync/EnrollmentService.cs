using System;
using System.Collections.Generic;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Sync;

public sealed record EnrollResult(string DeviceId, string Status);
public sealed record DeviceToken(string DeviceId, string Token);

/// <summary>
/// Cihaz kaydı: tek-kullanımlık 10 dk enrollment anahtarı + master onayı + revoke. Cihaz token'ı
/// hash'lenerek saklanır (düz metin yalnız onayda döner). Revoked cihaz push/pull'da 403 alır.
/// </summary>
public sealed class EnrollmentService
{
    public static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(10);
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public EnrollmentService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Master admin tek-kullanımlık enrollment anahtarı üretir (10 dk). Düz metin döner.</summary>
    public string CreateEnrollmentKey(SessionContext s)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Enrollment anahtarı yalnız admin üretir.");
        var key = SyncCrypto.NewKey();
        var now = _clock.UtcNow;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO enrollment_keys(id, company_id, key_hash, expires_at, used_at, created_at) " +
            "VALUES($id,$c,$h,$exp,NULL,$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(key));
        cmd.Parameters.AddWithValue("$exp", now.Add(KeyTtl).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
        return key;
    }

    /// <summary>Personel cihazı anahtarla enroll olur: anahtar tek-kullanımlık + süre kontrolü. Cihaz 'pending'.</summary>
    public EnrollResult Enroll(string companyId, string plaintextKey, string deviceName)
    {
        TenantGuard.Require(companyId);
        var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
        var hash = SyncCrypto.Sha256Hex(plaintextKey);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false);

        string keyId;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText =
                "SELECT id FROM enrollment_keys WHERE company_id=$c AND key_hash=$h AND used_at IS NULL AND expires_at >= $now;";
            find.Parameters.AddWithValue("$c", companyId);
            find.Parameters.AddWithValue("$h", hash);
            find.Parameters.AddWithValue("$now", nowMs);
            keyId = find.ExecuteScalar() as string
                ?? throw new ForbiddenException("Geçersiz, süresi dolmuş veya kullanılmış enrollment anahtarı.");
        }
        // Tek kullanımlık: işaretle
        using (var use = conn.CreateCommand())
        {
            use.Transaction = tx;
            use.CommandText = "UPDATE enrollment_keys SET used_at=$now WHERE id=$id AND used_at IS NULL;";
            use.Parameters.AddWithValue("$now", nowMs);
            use.Parameters.AddWithValue("$id", keyId);
            if (use.ExecuteNonQuery() == 0) throw new ForbiddenException("Anahtar zaten kullanılmış.");
        }

        var deviceId = Guid.NewGuid().ToString("N");
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO sync_devices(id, company_id, device_name, status, created_at, updated_at, version) " +
                "VALUES($id,$c,$n,'pending',$now,$now,1);";
            ins.Parameters.AddWithValue("$id", deviceId);
            ins.Parameters.AddWithValue("$c", companyId);
            ins.Parameters.AddWithValue("$n", deviceName);
            ins.Parameters.AddWithValue("$now", nowMs);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return new EnrollResult(deviceId, "pending");
    }

    /// <summary>Master onayı: cihaz 'active' + token üretir (düz metin döner, hash saklanır).</summary>
    public DeviceToken ApproveDevice(SessionContext s, string deviceId)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Cihaz onayı yalnız admin.");
        var token = SyncCrypto.NewKey();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE sync_devices SET status='active', token_hash=$h, updated_at=$now " +
            "WHERE id=$id AND company_id=$c AND status='pending';";
        cmd.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(token));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Onaylanacak bekleyen cihaz bulunamadı.");
        return new DeviceToken(deviceId, token);
    }

    /// <summary>Cihaz token rotasyonu: yeni token üretir, ESKİ token geçersiz olur (hash değişir).</summary>
    public DeviceToken RotateDeviceToken(SessionContext s, string deviceId)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Token rotasyonu yalnız admin.");
        var token = SyncCrypto.NewKey();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE sync_devices SET token_hash=$h, updated_at=$now WHERE id=$id AND company_id=$c AND status='active';";
        cmd.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(token));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Aktif cihaz bulunamadı.");
        return new DeviceToken(deviceId, token);
    }

    public void RevokeDevice(SessionContext s, string deviceId)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Cihaz iptali yalnız admin.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE sync_devices SET status='revoked', revoked_at=$now, updated_at=$now WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Pasif/iptal edilmiş cihazı yeniden aktifleştir (admin). 'pending' için ApproveDevice kullanılır.</summary>
    public void Reactivate(SessionContext s, string deviceId)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Cihaz aktifleştirme yalnız admin.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE sync_devices SET status='active', revoked_at=NULL, updated_at=$now WHERE id=$id AND company_id=$c AND status<>'active';";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Firmanın kayıtlı makineleri (yönetim ekranı).</summary>
    public IReadOnlyList<DeviceRow> ListDevices(SessionContext s)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Cihaz listesi yalnız admin.");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, device_name, status, last_seen_at, created_at FROM sync_devices WHERE company_id=$c ORDER BY created_at DESC;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var list = new List<DeviceRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DeviceRow(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt64(4)));
        return list;
    }

    /// <summary>Aktif makine sayısı (kota kontrolü).</summary>
    public int ActiveDeviceCount(SessionContext s)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_devices WHERE company_id=$c AND status='active';";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

public sealed record DeviceRow(string Id, string Name, string Status, long? LastSeenAt, long CreatedAt)
{
    public string StatusText => Status switch { "active" => "Aktif", "pending" => "Onay Bekliyor", "revoked" => "Pasif", _ => Status };
    public bool IsActive => Status == "active";
    public bool CanActivate => Status != "active";
    public string LastSeenText => LastSeenAt is long t ? DateTimeOffset.FromUnixTimeMilliseconds(t).LocalDateTime.ToString("dd.MM.yyyy HH:mm") : "—";
    public string CreatedText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy");
}
