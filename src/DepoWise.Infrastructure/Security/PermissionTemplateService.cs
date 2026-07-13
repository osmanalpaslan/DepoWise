using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Security;

/// <summary>Şablon başlığı. ScopeAll = tüm firmalar; aksi halde CompanyId firmasına özel (CompanyName gösterim).</summary>
public sealed record PermissionTemplateRow(string Id, string Name, string CompanyId, string? CompanyName, bool ScopeAll)
{
    public string ScopeText => ScopeAll ? "Tüm Firmalar" : (CompanyName ?? "—");
}

public sealed record PermissionTemplateData(IReadOnlyList<ModulePermission> Modules, IReadOnlyList<string> Buttons, string? RoleKey);

/// <summary>
/// Kullanıcı yetki şablonları. Oluştur/sil YALNIZ Süper Admin; şablon bir FİRMAYA veya TÜM firmalara (scope_all)
/// tanımlanır. Kullanıcı oluşturma ekranında: kullanıcı-oluşturma yetkili aktör, KENDİ firmasına özel + tüm-firma
/// şablonlarını görür/uygular (tenant izolasyonu).
/// </summary>
public sealed class PermissionTemplateService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public PermissionTemplateService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static void RequireSuper(SessionContext s)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Yetki şablonu oluşturma/silme yalnız Süper Admin yetkisindedir.");
    }

    /// <summary>Süper admin şablon oluşturur. scopeAll=true → tüm firmalar (company_id = süper adminin firması, referans);
    /// aksi halde targetCompanyId (verilmezse süper adminin firması).</summary>
    public string Create(SessionContext s, string name, string? roleKey, IEnumerable<ModulePermission> modules,
        IEnumerable<string> buttons, string? targetCompanyId = null, bool scopeAll = false)
    {
        RequireSuper(s);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Şablon adı zorunlu.");
        var companyId = scopeAll ? s.CompanyId : (string.IsNullOrWhiteSpace(targetCompanyId) ? s.CompanyId : targetCompanyId!);
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var permJson = JsonSerializer.Serialize(modules
            .Where(m => m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
            .Select(m => new[] { m.ModuleKey, m.CanView ? "1" : "0", m.CanCreate ? "1" : "0", m.CanEdit ? "1" : "0", m.CanDelete ? "1" : "0" }));
        var btnJson = JsonSerializer.Serialize(buttons.Distinct().ToArray());

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO permission_templates(id, company_id, name, permissions_json, buttons_json, role_key, scope_all, created_at, updated_at, version, is_deleted) " +
            "VALUES($id,$c,$n,$p,$b,$role,$sa,$now,$now,1,0);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$n", name.Trim());
        cmd.Parameters.AddWithValue("$p", permJson);
        cmd.Parameters.AddWithValue("$b", btnJson);
        cmd.Parameters.AddWithValue("$role", (object?)roleKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sa", scopeAll ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>Şablon YÖNETİM listesi (Süper Admin) — TÜM firmaların şablonları + kapsam (firma adı / Tüm Firmalar).</summary>
    public IReadOnlyList<PermissionTemplateRow> List(SessionContext s)
    {
        RequireSuper(s);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.name, t.company_id, c.name, t.scope_all
FROM permission_templates t LEFT JOIN companies c ON c.id=t.company_id
WHERE t.is_deleted=0 ORDER BY t.scope_all DESC, c.name, t.name;";
        var list = new List<PermissionTemplateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PermissionTemplateRow(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetInt64(4) == 1));
        return list;
    }

    /// <summary>Kullanıcı OLUŞTURMA ekranı için görünür şablonlar: aktörün firmasına özel + tüm-firma şablonları.
    /// YALNIZ kullanıcı-oluşturma yetkisi (users/Create) olan aktör görebilir (tenant izolasyonu).</summary>
    public IReadOnlyList<PermissionTemplateRow> ListForUserCreation(SessionContext s)
    {
        AccessControl.Require(s, "users", PermissionAction.Create);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.name, t.company_id, NULL, t.scope_all
FROM permission_templates t
WHERE t.is_deleted=0 AND (t.scope_all=1 OR t.company_id=$c)
ORDER BY t.scope_all DESC, t.name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var list = new List<PermissionTemplateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PermissionTemplateRow(r.GetString(0), r.GetString(1), r.GetString(2), null, r.GetInt64(4) == 1));
        return list;
    }

    /// <summary>Şablon içeriği. Süper admin herhangi birini; diğerleri YALNIZ kendi firmasına görünür (scope_all
    /// veya company eşleşmesi) + kullanıcı-oluşturma yetkisi ile okuyabilir.</summary>
    public PermissionTemplateData GetData(SessionContext s, string templateId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT permissions_json, buttons_json, role_key, company_id, scope_all FROM permission_templates WHERE id=$id AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", templateId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new PermissionTemplateData(Array.Empty<ModulePermission>(), Array.Empty<string>(), null);

        var companyId = r.GetString(3);
        var scopeAll = r.GetInt64(4) == 1;
        if (!s.IsSuperAdmin)
        {
            AccessControl.Require(s, "users", PermissionAction.Create);
            if (!scopeAll && !string.Equals(companyId, s.CompanyId, StringComparison.Ordinal))
                throw new ForbiddenException("Şablon başka firmaya ait.");
        }

        var mods = new List<ModulePermission>();
        foreach (var row in JsonSerializer.Deserialize<string[][]>(r.GetString(0)) ?? Array.Empty<string[]>())
            if (row.Length == 5)
                mods.Add(new ModulePermission(row[0], row[1] == "1", row[2] == "1", row[3] == "1", row[4] == "1"));
        var btns = JsonSerializer.Deserialize<string[]>(r.GetString(1)) ?? Array.Empty<string>();
        var role = r.IsDBNull(2) ? null : r.GetString(2);
        return new PermissionTemplateData(mods, btns, role);
    }

    public void Delete(SessionContext s, string templateId)
    {
        RequireSuper(s);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE permission_templates SET is_deleted=1, updated_at=$now WHERE id=$id;";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", templateId);
        cmd.ExecuteNonQuery();
    }
}
