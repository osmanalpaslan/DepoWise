using System;
using System.Collections.Generic;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Sync;

public sealed record EnrollResult(string DeviceId, string Status);
/// <summary>Makine kayıt/heartbeat yanıtı: durum + makinenin firması + ADMIN tarafından atanmış şubesi (varsa).
/// Masaüstü bunları önbelleğe alır (çevrimdışı otomatik giriş + ana ekran + farklı-şube uyarısı + süper admin
/// "makine firması/şubesi ile giriş" seçenekleri için).</summary>
public sealed record RegisterResult(string DeviceId, string Status, string? BranchId, string? BranchName,
    string? CompanyId = null, string? CompanyName = null);
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

    /// <summary>Sıfır-sürtünmeli bulut kaydı: masaüstü açılışta kendini kaydeder (isim bazlı idempotent).
    /// KOTA MANTIĞI: firmanın aktif makine sayısı kotanın altındaysa makine otomatik 'active' olur ve giriş
    /// yapabilir; kota doluysa 'pending' kalır (süper admin onaylayana / başka makineyi pasife alana kadar
    /// giriş yapamaz). Pasife alınmış (revoked) makine otomatik aktifleşmez — yalnız süper admin açar.</summary>
    /// <summary>Makine kendini kaydeder/heartbeat atar. NOT: makinenin şubesi (branch_id) ARTIK login'de seçilen
    /// şubeyle YAZILMAZ — şube yalnız admin tarafından web'den atanır (AssignBranch, otoriter). <paramref name="branchId"/>
    /// parametresi geriye dönük uyumluluk için korunur ama yazımda kullanılmaz.</summary>
    public RegisterResult RegisterSelf(string companyId, string deviceName, string? ip = null, string? branchId = null)
    {
        TenantGuard.Require(companyId);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        // Bağlanılan IP'nin ailesine göre v4/v6 slotunu ayır (diğer slot COALESCE ile korunur).
        string? ip4 = null, ip6 = null;
        if (!string.IsNullOrWhiteSpace(ip) && System.Net.IPAddress.TryParse(ip, out var addr))
        {
            if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ip4 = ip;
            else if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) ip6 = ip;
        }
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false);

        int quota = QuotaFor(conn, tx, companyId);

        string? existingId = null, existingStatus = null;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id, status FROM sync_devices WHERE company_id=$c AND device_name=$n ORDER BY created_at LIMIT 1;";
            find.Parameters.AddWithValue("$c", companyId);
            find.Parameters.AddWithValue("$n", deviceName);
            using var r = find.ExecuteReader();
            if (r.Read()) { existingId = r.GetString(0); existingStatus = r.GetString(1); }
        }

        int ActiveCount(string? excludeId)
        {
            using var cnt = conn.CreateCommand();
            cnt.Transaction = tx;
            cnt.CommandText = "SELECT COUNT(*) FROM sync_devices WHERE company_id=$c AND status='active'" +
                              (excludeId is null ? ";" : " AND id<>$x;");
            cnt.Parameters.AddWithValue("$c", companyId);
            if (excludeId is not null) cnt.Parameters.AddWithValue("$x", excludeId);
            return Convert.ToInt32(cnt.ExecuteScalar());
        }

        if (existingId is not null)
        {
            // Revoked makine ASLA otomatik aktifleşmez. Active kalır. Pending → kota varsa aktifleşir.
            var newStatus = existingStatus ?? "pending";
            if (existingStatus == "pending" && ActiveCount(existingId) < quota) newStatus = "active";

            // Şube (branch_id) BURADA GÜNCELLENMEZ — admin ataması otoriterdir (AssignBranch).
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE sync_devices SET last_seen_at=$now, updated_at=$now, status=$s, " +
                "ip_address=COALESCE($ip, ip_address), ip_v4=COALESCE($ip4, ip_v4), ip_v6=COALESCE($ip6, ip_v6) " +
                "WHERE id=$id;";
            upd.Parameters.AddWithValue("$now", now);
            upd.Parameters.AddWithValue("$s", newStatus);
            upd.Parameters.AddWithValue("$ip", (object?)ip ?? System.DBNull.Value);
            upd.Parameters.AddWithValue("$ip4", (object?)ip4 ?? System.DBNull.Value);
            upd.Parameters.AddWithValue("$ip6", (object?)ip6 ?? System.DBNull.Value);
            upd.Parameters.AddWithValue("$id", existingId);
            upd.ExecuteNonQuery();
            var (exBid, exBname, exCid, exCname) = ReadDeviceInfo(conn, tx, existingId);
            tx.Commit();
            return new RegisterResult(existingId, newStatus, exBid, exBname, exCid, exCname);
        }

        var status = ActiveCount(null) < quota ? "active" : "pending";
        var deviceId = Guid.NewGuid().ToString("N");
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO sync_devices(id, company_id, device_name, status, ip_address, ip_v4, ip_v6, branch_id, last_seen_at, created_at, updated_at, version) " +
                "VALUES($id,$c,$n,$s,$ip,$ip4,$ip6,$bid,$now,$now,$now,1);";
            ins.Parameters.AddWithValue("$id", deviceId);
            ins.Parameters.AddWithValue("$c", companyId);
            ins.Parameters.AddWithValue("$n", deviceName);
            ins.Parameters.AddWithValue("$s", status);
            ins.Parameters.AddWithValue("$ip", (object?)ip ?? System.DBNull.Value);
            ins.Parameters.AddWithValue("$ip4", (object?)ip4 ?? System.DBNull.Value);
            ins.Parameters.AddWithValue("$ip6", (object?)ip6 ?? System.DBNull.Value);
            ins.Parameters.AddWithValue("$bid", System.DBNull.Value); // yeni makine: şube YOK, admin atar
            ins.Parameters.AddWithValue("$now", now);
            ins.ExecuteNonQuery();
        }
        var (_, _, nCid, nCname) = ReadDeviceInfo(conn, tx, deviceId);
        tx.Commit();
        return new RegisterResult(deviceId, status, null, null, nCid, nCname); // yeni makine → şubesiz (firma bilinir)
    }

    /// <summary>Bir makinenin firması + atanmış şubesi (id + ad) bilgisini okur.</summary>
    private static (string? BranchId, string? BranchName, string? CompanyId, string? CompanyName) ReadDeviceInfo(SqliteConnection conn, SqliteTransaction tx, string deviceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT d.branch_id, br.name, d.company_id, co.name " +
                          "FROM sync_devices d LEFT JOIN branches br ON br.id=d.branch_id " +
                          "LEFT JOIN companies co ON co.id=d.company_id WHERE d.id=$id;";
        cmd.Parameters.AddWithValue("$id", deviceId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null, null, null);
        return (
            r.IsDBNull(0) ? null : r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

    /// <summary>Admin bir makineye ŞUBE atar (otoriter). Şube makinenin firmasına ait olmalı. Süper admin tüm
    /// firmalarda; diğer admin yalnız kendi firmasında. branchId boş → atama kaldırılır (şubesiz).</summary>
    public void AssignBranch(SessionContext s, string deviceId, string? branchId)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Makineye şube atama yalnız admin.");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false);

        // Makineyi bul + firmasını al (tenant kontrolü)
        string? machineCompany = null;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT company_id FROM sync_devices WHERE id=$id;";
            find.Parameters.AddWithValue("$id", deviceId);
            machineCompany = find.ExecuteScalar() as string;
        }
        if (machineCompany is null) throw new ForbiddenException("Makine bulunamadı.");
        if (!s.IsSuperAdmin && !string.Equals(machineCompany, s.CompanyId, StringComparison.Ordinal))
            throw new ForbiddenException("Bu makine başka firmaya ait.");

        // Şube (verildiyse) aynı firmaya ait ve geçerli olmalı
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            using var bc = conn.CreateCommand();
            bc.Transaction = tx;
            bc.CommandText = "SELECT COUNT(*) FROM branches WHERE id=$b AND company_id=$c AND is_deleted=0;";
            bc.Parameters.AddWithValue("$b", branchId);
            bc.Parameters.AddWithValue("$c", machineCompany);
            if (Convert.ToInt64(bc.ExecuteScalar()) == 0) throw new ForbiddenException("Şube bulunamadı veya başka firmaya ait.");
        }

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE sync_devices SET branch_id=$b, updated_at=$now WHERE id=$id;";
        upd.Parameters.AddWithValue("$b", (object?)(string.IsNullOrWhiteSpace(branchId) ? null : branchId) ?? System.DBNull.Value);
        upd.Parameters.AddWithValue("$now", _clock.UtcNow.ToUnixTimeMilliseconds());
        upd.Parameters.AddWithValue("$id", deviceId);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    private static int QuotaFor(SqliteConnection conn, SqliteTransaction tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT machine_quota FROM companies WHERE id=$c;";
        cmd.Parameters.AddWithValue("$c", companyId);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? 3 : Convert.ToInt32(v);
    }

    /// <summary>Firma makine kotasını ayarlar (yalnız süper admin).</summary>
    public void SetQuota(SessionContext s, string companyId, int quota)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Makine kotası yalnız süper admin.");
        if (quota < 1) quota = 1;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE companies SET machine_quota=$q WHERE id=$c;";
        cmd.Parameters.AddWithValue("$q", quota);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.ExecuteNonQuery();
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
            "WHERE id=$id AND status='pending'" + (s.IsSuperAdmin ? ";" : " AND company_id=$c;");
        cmd.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(token));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        if (!s.IsSuperAdmin) cmd.Parameters.AddWithValue("$c", s.CompanyId);
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
            "UPDATE sync_devices SET status='revoked', revoked_at=$now, updated_at=$now WHERE id=$id" +
            (s.IsSuperAdmin ? ";" : " AND company_id=$c;");
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        if (!s.IsSuperAdmin) cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Cihaz kaydını KALICI sil (admin). Süper admin tüm firmalarda; diğer admin kendi firmasında.</summary>
    public void DeleteDevice(SessionContext s, string deviceId)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Cihaz silme yalnız admin.");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sync_devices WHERE id=$id" + (s.IsSuperAdmin ? ";" : " AND company_id=$c;");
        cmd.Parameters.AddWithValue("$id", deviceId);
        if (!s.IsSuperAdmin) cmd.Parameters.AddWithValue("$c", s.CompanyId);
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
            "UPDATE sync_devices SET status='active', revoked_at=NULL, updated_at=$now WHERE id=$id AND status<>'active'" +
            (s.IsSuperAdmin ? ";" : " AND company_id=$c;");
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        if (!s.IsSuperAdmin) cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Kayıtlı makineler — filtreli. Süper admin TÜM firmaları görür; diğer admin yalnız kendi firmasını.
    /// <paramref name="companyFilter"/> + <paramref name="branchFilter"/> ile daraltılır (firma→şube akışı).
    /// <paramref name="unassignedOnly"/>=true → yalnız ŞUBESİ ATANMAMIŞ ("kayıtsız") makineler; süper admin için
    /// firma bağımsızdır. Her çağrı yalnız istenen kümeyi çeker (menü açılışında tüm makineleri çekmeden).</summary>
    public IReadOnlyList<DeviceRow> ListDevices(SessionContext s, string? companyFilter = null, string? branchFilter = null, bool unassignedOnly = false)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Cihaz listesi yalnız admin.");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sb = "SELECT d.id, d.device_name, d.status, d.last_seen_at, d.created_at, d.company_id, " +
                 "COALESCE(c.name, d.company_id), COALESCE(c.machine_quota,3), COALESCE(d.ip_address,''), " +
                 "COALESCE(d.ip_v4,''), COALESCE(d.ip_v6,''), COALESCE(br.name,''), COALESCE(d.branch_id,'') " +
                 "FROM sync_devices d LEFT JOIN companies c ON c.id=d.company_id " +
                 "LEFT JOIN branches br ON br.id=d.branch_id ";

        var conds = new List<string>();
        // Tenant sınırı: süper admin değilse daima kendi firması. Kayıtsız modda süper admin firma-bağımsız.
        if (!s.IsSuperAdmin) { conds.Add("d.company_id=$c"); cmd.Parameters.AddWithValue("$c", s.CompanyId); }
        else if (!unassignedOnly && !string.IsNullOrWhiteSpace(companyFilter)) { conds.Add("d.company_id=$c"); cmd.Parameters.AddWithValue("$c", companyFilter!); }

        if (unassignedOnly) conds.Add("d.branch_id IS NULL"); // "kayıtsız" = şube atanmamış
        else if (!string.IsNullOrWhiteSpace(branchFilter)) { conds.Add("d.branch_id=$b"); cmd.Parameters.AddWithValue("$b", branchFilter!); }

        if (conds.Count > 0) sb += "WHERE " + string.Join(" AND ", conds) + " ";
        sb += "ORDER BY d.created_at DESC;";
        cmd.CommandText = sb;
        var list = new List<DeviceRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DeviceRow(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt64(4), r.GetString(5), r.GetString(6), r.GetInt32(7),
                r.GetString(8), r.GetString(9), r.GetString(10), r.GetString(11), r.GetString(12)));
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

public sealed record DeviceRow(string Id, string Name, string Status, long? LastSeenAt, long CreatedAt,
    string CompanyId = "", string CompanyName = "", int Quota = 3, string Ip = "", string Ip4 = "", string Ip6 = "",
    string BranchName = "", string BranchId = "")
{
    public string IpText => string.IsNullOrEmpty(Ip) ? "—" : Ip;
    public string Ip4Text => string.IsNullOrEmpty(Ip4) ? "—" : Ip4;
    public string Ip6Text => string.IsNullOrEmpty(Ip6) ? "—" : Ip6;
    public string BranchText => string.IsNullOrEmpty(BranchName) ? "—" : BranchName;
    public string StatusText => Status switch { "active" => "Aktif", "pending" => "Onay Bekliyor", "revoked" => "Pasif", _ => Status };
    public bool IsActive => Status == "active";
    public bool CanActivate => Status != "active";
    public string LastSeenText => LastSeenAt is long t ? DateTimeOffset.FromUnixTimeMilliseconds(t).LocalDateTime.ToString("dd.MM.yyyy HH:mm") : "—";
    public string CreatedText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy");
}
