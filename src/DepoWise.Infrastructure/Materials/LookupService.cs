using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Materials;

public sealed record LookupItem(string Id, string Name);

/// <summary>
/// Tanımlar CRUD (kategori/marka/birim/tedarikçi) — tenant + "definitions" permission.
/// Benzersizlik DB UNIQUE index'leri ile; hatalar fail-closed.
/// </summary>
public sealed class LookupService
{
    private const string Module = "definitions";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public LookupService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string AddCategory(SessionContext s, string name, string? parentId = null)
        => Insert(s, "material_categories", name, ("parent_id", (object?)parentId ?? DBNull.Value));

    public string AddBrand(SessionContext s, string name, string brandType = "material")
        => Insert(s, "brands", name, ("brand_type", brandType));

    public string AddUnit(SessionContext s, string name) => Insert(s, "units", name);

    public string AddSupplier(SessionContext s, string name) => Insert(s, "suppliers", name);

    public IReadOnlyList<LookupItem> List(SessionContext s, string table)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        EnsureKnownTable(table);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, name FROM {table} WHERE company_id = $c AND is_deleted = 0 ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    private string Insert(SessionContext s, string table, string name, params (string Col, object Val)[] extra)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        EnsureKnownTable(table);
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        var cols = "id, company_id, name, created_at, updated_at, version, is_deleted";
        var vals = "$id, $c, $n, $now, $now, 1, 0";
        foreach (var (col, _) in extra) { cols += $", {col}"; vals += $", ${col}"; }

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO {table}({cols}) VALUES({vals});";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$now", now);
            foreach (var (col, val) in extra) cmd.Parameters.AddWithValue($"${col}", val);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    private static void EnsureKnownTable(string table)
    {
        if (table is not ("material_categories" or "brands" or "units" or "suppliers"))
            throw new ArgumentException($"Bilinmeyen tanım tablosu: {table}");
    }
}
