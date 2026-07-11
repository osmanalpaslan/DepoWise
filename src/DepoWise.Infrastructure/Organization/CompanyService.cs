using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Organization;

public sealed record CompanyRow(
    string Id, string Name, string? TaxNo, string? TaxOffice, string? Address,
    string? Phone, string? Email, string? AuthorizedPerson, int UserCount, int MaxUsers = 0)
{
    public string TaxDisplay => string.IsNullOrEmpty(TaxNo) ? "—" : TaxNo!;
    /// <summary>Maks kullanıcı (0 = sınırsız). Admin sınırı = MaxUsers'ın 2/10'u (ileride uygulanır).</summary>
    public string MaxUsersText => MaxUsers <= 0 ? "Sınırsız" : MaxUsers.ToString();
}

public sealed record NewCompany(
    string Name, string? TaxNo = null, string? TaxOffice = null, string? Address = null,
    string? Phone = null, string? Email = null, string? AuthorizedPerson = null, int MaxUsers = 0);

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
        cmd.CommandText = "SELECT name FROM companies WHERE id=$c;";
        cmd.Parameters.AddWithValue("$c", companyId);
        return cmd.ExecuteScalar() as string ?? "";
    }

    public IReadOnlyList<CompanyRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT c.id, c.name, c.tax_no, c.tax_office, c.address, c.phone, c.email, c.authorized_person,
       (SELECT COUNT(*) FROM users u WHERE u.company_id = c.id AND u.is_deleted = 0),
       COALESCE(c.max_users,0)
FROM companies c
WHERE c.is_deleted = 0
ORDER BY c.name;";
        var list = new List<CompanyRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CompanyRow(r.GetString(0), r.GetString(1),
                S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), r.GetInt32(8), r.GetInt32(9)));
        return list;
    }

    public string Create(SessionContext s, NewCompany dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Firma adı zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO companies(id, name, tax_no, tax_office, address, phone, email, authorized_person, max_users, created_at, updated_at, version, is_deleted)
VALUES($id,$n,$tn,$to,$ad,$ph,$em,$ap,$mu,$now,$now,1,0);";
            Bind(cmd, dto);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$now", now);
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
UPDATE companies SET name=$n, tax_no=$tn, tax_office=$to, address=$ad, phone=$ph, email=$em,
    authorized_person=$ap, max_users=$mu, version=version+1, updated_at=$now WHERE id=$id AND is_deleted=0;";
            Bind(cmd, dto);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$now", now);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Firma bulunamadı.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Firma sil (soft-delete). Yalnız süper admin. Bağlı aktif kullanıcı varsa engellenir.</summary>
    public void Delete(SessionContext s, string id)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Firma silme yalnız süper admin.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Bağlı kullanıcılar SİLİNMEZ, yalnız PASİFE alınır (is_active=0, is_deleted=0). Yanlışlıkla firma silinirse
        // kullanıcılar korunur; firma geri yüklenince tekrar aktifleştirilebilir. Kullanıcı verisi kaybolmaz.
        using (var deact = conn.CreateCommand())
        {
            deact.Transaction = tx;
            deact.CommandText = "UPDATE users SET is_active=0, updated_at=$now WHERE company_id=$id AND is_deleted=0 AND is_active=1;";
            deact.Parameters.AddWithValue("$id", id);
            deact.Parameters.AddWithValue("$now", now);
            deact.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE companies SET is_deleted=1, updated_at=$now WHERE id=$id AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$now", now);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Firma bulunamadı.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    private static void Bind(SqliteCommand cmd, NewCompany dto)
    {
        cmd.Parameters.AddWithValue("$n", dto.Name.Trim());
        cmd.Parameters.AddWithValue("$tn", (object?)Norm(dto.TaxNo) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)Norm(dto.TaxOffice) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ad", (object?)Norm(dto.Address) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ph", (object?)Norm(dto.Phone) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$em", (object?)Norm(dto.Email) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ap", (object?)Norm(dto.AuthorizedPerson) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mu", dto.MaxUsers < 0 ? 0 : dto.MaxUsers);
    }

    private static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
