using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Organization;

public sealed record CompanyRow(
    string Id, string Name, string? TaxNo, string? TaxOffice, string? Address,
    string? Phone, string? Email, string? AuthorizedPerson, int UserCount, int MaxUsers = 0,
    int MaxAdmins = 0, int MachineQuota = 3)
{
    public string TaxDisplay => string.IsNullOrEmpty(TaxNo) ? "—" : TaxNo!;
    /// <summary>Maks NORMAL (personel) kullanıcı (0 = sınırsız).</summary>
    public string MaxUsersText => MaxUsers <= 0 ? "Sınırsız" : MaxUsers.ToString();
    public string MaxAdminsText => MaxAdmins <= 0 ? "Sınırsız" : MaxAdmins.ToString();
    public string MachineQuotaText => MachineQuota <= 0 ? "Sınırsız" : MachineQuota.ToString();
}

public sealed record NewCompany(
    string Name, string? TaxNo = null, string? TaxOffice = null, string? Address = null,
    string? Phone = null, string? Email = null, string? AuthorizedPerson = null, int MaxUsers = 0,
    int MaxAdmins = 0, int MachineQuota = 3);

/// <summary>
/// Firma Tanım — YALNIZ Süper Admin (AccessControl "companies" süper-admin-only; admin bypass geçersiz).
/// Süper Admin tüm firmaları görür/düzenler. Çok-firmalı dağıtımda platform sahibinin ekranı.
/// </summary>
public sealed class CompanyService
{
    private const string Module = "companies";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CompanyService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Firma adı (login yanıtı için; yetki gerektirmez — kullanıcı kendi firmasının adını görür).</summary>
    public string GetName(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM companies WHERE id=@c;";
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() as string ?? "";
    }

    /// <summary>Firma SEÇİCİLERİ için tenant-kapsamlı liste (Şube, Kullanıcı vb. ekranlarındaki firma dropdown'u).
    /// Süper admin TÜM firmaları; süper-admin-altı roller YALNIZ kendi firmasını görür. "companies" yetkisi
    /// GEREKTİRMEZ (kendi firmasını görmek yetki değil) — ama başka firmayı asla döndürmez (tenant izolasyonu).</summary>
    public IReadOnlyList<(string Id, string Name)> Selectable(SessionContext s)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        if (s.IsSuperAdmin)
            cmd.CommandText = "SELECT id, name FROM companies WHERE is_deleted=0 ORDER BY name;";
        else
        {
            cmd.CommandText = "SELECT id, name FROM companies WHERE id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
        }
        var list = new List<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    public IReadOnlyList<CompanyRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT c.id, c.name, c.tax_no, c.tax_office, c.address, c.phone, c.email, c.authorized_person,
       (SELECT COUNT(*) FROM users u WHERE u.company_id = c.id AND u.is_deleted = 0),
       COALESCE(c.max_users,0), COALESCE(c.max_admins,0), COALESCE(c.machine_quota,3)
FROM companies c
WHERE c.is_deleted = 0
ORDER BY c.name;";
        var list = new List<CompanyRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CompanyRow(r.GetString(0), r.GetString(1),
                S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.GetInt32(11)));
        return list;
    }

    /// <summary>
    /// Firma oluşturur. <paramref name="explicitId"/> verilirse O id ile oluşturulur — masaüstü ÇEVRİMDIŞI
    /// oluşturduğu firmayı, internet gelince aynı id ile sunucuya işleyebilsin diye (yerel ↔ sunucu id'leri eşleşir).
    ///
    /// İDEMPOTENT: aynı id ile tekrar gelirse (kuyruk yeniden denemesi / çift gönderim) HATA VERMEZ, mevcut kaydı
    /// günceller. Kuyruk tekrar denemelerinde "zaten var" hatasına düşmemek için şart.
    /// </summary>
    public string Create(SessionContext s, NewCompany dto, string? explicitId = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Firma adı zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = string.IsNullOrWhiteSpace(explicitId) ? Guid.NewGuid().ToString("N") : explicitId!;
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO companies(id, name, tax_no, tax_office, address, phone, email, authorized_person, max_users, max_admins, machine_quota, created_at, updated_at, version, is_deleted)
VALUES(@id,@n,@tn,@to,@ad,@ph,@em,@ap,@mu,@ma,@mq,@now,@now,1,0)
ON CONFLICT(id) DO UPDATE SET name=@n, tax_no=@tn, tax_office=@to, address=@ad, phone=@ph, email=@em,
    authorized_person=@ap, max_users=@mu, max_admins=@ma, machine_quota=@mq, is_deleted=0, version=companies.version+1, updated_at=@now;";
            Bind(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public void Update(SessionContext s, string id, NewCompany dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Firma adı zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE companies SET name=@n, tax_no=@tn, tax_office=@to, address=@ad, phone=@ph, email=@em,
    authorized_person=@ap, max_users=@mu, max_admins=@ma, machine_quota=@mq, version=version+1, updated_at=@now WHERE id=@id AND is_deleted=0;";
            Bind(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@now", now);
            // İDEMPOTENT: silinmiş firmada 0 satır dönebilir; kayıt hiç yoksa gerçek hata.
            if (cmd.ExecuteNonQuery() == 0 && !CompanyRowExists(conn, tx, id))
                throw new ForbiddenException("Firma bulunamadı.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Firma kaydı (silinmiş olsa bile) var mı? İdempotent kuyruk tekrarlarında "zaten uygulanmış"
    /// ile "gerçekten yok" ayrımı için.</summary>
    private static bool CompanyRowExists(DbConnection conn, DbTransaction tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM companies WHERE id=@id;";
        cmd.AddWithValue("@id", id);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Pasife alınmış (silinmiş / sözleşmesi biten) firmalar. Yalnız süper admin — yeniden aktifleştirme ekranı için.</summary>
    public IReadOnlyList<CompanyRow> ListDeleted(SessionContext s)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Yalnız süper admin.");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT c.id, c.name, c.tax_no, c.tax_office, c.address, c.phone, c.email, c.authorized_person,
       (SELECT COUNT(*) FROM users u WHERE u.company_id = c.id AND u.is_deleted = 0),
       COALESCE(c.max_users,0), COALESCE(c.max_admins,0), COALESCE(c.machine_quota,3)
FROM companies c
WHERE c.is_deleted = 1
ORDER BY c.name;";
        var list = new List<CompanyRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CompanyRow(r.GetString(0), r.GetString(1),
                S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.GetInt32(11)));
        return list;
    }

    /// <summary>
    /// Pasife alınmış firmayı yeniden aktifleştirir (sözleşme yenileme). Firma silinince pasife alınan
    /// kullanıcılar da tekrar aktif edilir ki süreç kaldığı yerden devam etsin. Yalnız süper admin.
    /// </summary>
    public int Reactivate(SessionContext s, string id)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Yalnız süper admin.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE companies SET is_deleted=0, version=version+1, updated_at=@now WHERE id=@id AND is_deleted=1;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@now", now);
            // İDEMPOTENT: 0 satır = firma zaten aktif (kuyruk tekrar denemesi) → hata verme. Hiç yoksa hata.
            if (cmd.ExecuteNonQuery() == 0 && !CompanyRowExists(conn, tx, id))
                throw new ForbiddenException("Firma bulunamadı.");
        }
        int reactivatedUsers;
        using (var u = conn.CreateCommand())
        {
            u.Transaction = tx;
            // Firma silinince pasife alınan (is_active=0, is_deleted=0) kullanıcıları tekrar aktifleştir.
            u.CommandText = "UPDATE users SET is_active=1, updated_at=@now WHERE company_id=@id AND is_deleted=0 AND is_active=0;";
            u.AddWithValue("@id", id);
            u.AddWithValue("@now", now);
            reactivatedUsers = u.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
        return reactivatedUsers;
    }

    /// <summary>Firma sil (soft-delete). Yalnız süper admin. Bağlı kullanıcılar SİLİNMEZ, pasife alınır (geri yüklenebilir).</summary>
    public void Delete(SessionContext s, string id)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Firma silme yalnız süper admin.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Bağlı kullanıcılar SİLİNMEZ, yalnız PASİFE alınır (is_active=0, is_deleted=0). Yanlışlıkla firma silinirse
        // kullanıcılar korunur; firma geri yüklenince tekrar aktifleştirilebilir. Kullanıcı verisi kaybolmaz.
        // KRİTİK: Süper Admin ASLA pasife alınmaz (platform sahibi) — aksi halde kendi home firmasını silen
        // süper admin sistemden tamamen kilitlenir ("kullanıcı adı veya parola hatalı"). Bkz. self-heal (ServerServices).
        using (var deact = conn.CreateCommand())
        {
            deact.Transaction = tx;
            deact.CommandText =
                "UPDATE users SET is_active=0, updated_at=@now WHERE company_id=@id AND is_deleted=0 AND is_active=1 " +
                "AND id NOT IN (SELECT ur.user_id FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE r.role_key=@sa);";
            deact.AddWithValue("@id", id);
            deact.AddWithValue("@now", now);
            deact.AddWithValue("@sa", RoleKeys.SuperAdmin);
            deact.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE companies SET is_deleted=1, updated_at=@now WHERE id=@id AND is_deleted=0;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@now", now);
            // İDEMPOTENT: 0 satır = firma zaten silinmiş (kuyruk tekrar denemesi) → hata verme.
            // Yalnız firma HİÇ YOKSA hata (gerçek hatalı istek).
            if (cmd.ExecuteNonQuery() == 0 && !CompanyRowExists(conn, tx, id))
                throw new ForbiddenException("Firma bulunamadı.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    private static void Bind(DbCommand cmd, NewCompany dto)
    {
        cmd.AddWithValue("@n", dto.Name.Trim());
        cmd.AddWithValue("@tn", (object?)Norm(dto.TaxNo) ?? DBNull.Value);
        cmd.AddWithValue("@to", (object?)Norm(dto.TaxOffice) ?? DBNull.Value);
        cmd.AddWithValue("@ad", (object?)Norm(dto.Address) ?? DBNull.Value);
        cmd.AddWithValue("@ph", (object?)Norm(dto.Phone) ?? DBNull.Value);
        cmd.AddWithValue("@em", (object?)Norm(dto.Email) ?? DBNull.Value);
        cmd.AddWithValue("@ap", (object?)Norm(dto.AuthorizedPerson) ?? DBNull.Value);
        cmd.AddWithValue("@mu", dto.MaxUsers < 0 ? 0 : dto.MaxUsers);
        cmd.AddWithValue("@ma", dto.MaxAdmins < 0 ? 0 : dto.MaxAdmins);
        cmd.AddWithValue("@mq", dto.MachineQuota < 0 ? 0 : dto.MachineQuota);
    }

    private static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? S(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
