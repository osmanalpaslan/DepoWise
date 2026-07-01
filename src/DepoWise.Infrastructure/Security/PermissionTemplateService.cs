using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Security;

public sealed record PermissionTemplateRow(string Id, string Name);
public sealed record PermissionTemplateData(IReadOnlyList<ModulePermission> Modules, IReadOnlyList<string> Buttons, string? RoleKey);

/// <summary>
/// Kullanıcı yetki şablonları (Süper Admin). İsimli şablon oluştur/listele/sil; yeni kullanıcıda uygulanır.
/// Modül izinleri + butonlar JSON. Tenant: şablonlar süper admin'in firmasına yazılır.
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
        if (!s.IsSuperAdmin) throw new ForbiddenException("Yetki şablonları yalnız Süper Admin yetkisindedir.");
    }

    public string Create(SessionContext s, string name, string? roleKey, IEnumerable<ModulePermission> modules, IEnumerable<string> buttons)
    {
        RequireSuper(s);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Şablon adı zorunlu.");
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var permJson = JsonSerializer.Serialize(modules
            .Where(m => m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
            .Select(m => new[] { m.ModuleKey, m.CanView ? "1" : "0", m.CanCreate ? "1" : "0", m.CanEdit ? "1" : "0", m.CanDelete ? "1" : "0" }));
        var btnJson = JsonSerializer.Serialize(buttons.Distinct().ToArray());

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO permission_templates(id, company_id, name, permissions_json, buttons_json, role_key, created_at, updated_at, version, is_deleted) " +
            "VALUES($id,$c,$n,$p,$b,$role,$now,$now,1,0);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$n", name.Trim());
        cmd.Parameters.AddWithValue("$p", permJson);
        cmd.Parameters.AddWithValue("$b", btnJson);
        cmd.Parameters.AddWithValue("$role", (object?)roleKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
        return id;
    }

    public IReadOnlyList<PermissionTemplateRow> List(SessionContext s)
    {
        RequireSuper(s);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM permission_templates WHERE company_id=$c AND is_deleted=0 ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var list = new List<PermissionTemplateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new PermissionTemplateRow(r.GetString(0), r.GetString(1)));
        return list;
    }

    public PermissionTemplateData GetData(SessionContext s, string templateId)
    {
        RequireSuper(s);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT permissions_json, buttons_json, role_key FROM permission_templates WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", templateId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new PermissionTemplateData(Array.Empty<ModulePermission>(), Array.Empty<string>(), null);

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
        cmd.CommandText = "UPDATE permission_templates SET is_deleted=1, updated_at=$now WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", templateId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.ExecuteNonQuery();
    }
}
