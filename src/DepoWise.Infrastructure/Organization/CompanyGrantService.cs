using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Firma Yetki Kontrol satırı (#6). grantable = firma admini bu modülü Personel'e doğrudan verebilir mi.
/// <paramref name="GlobalRestricted"/> ETKİN global kilit (sabit VEYA dinamik). <paramref name="GlobalHardLocked"/>
/// derleme-zamanı SABİT kilit (değiştirilemez); false ise süper admin dinamik global kilidi açıp kapatabilir.</summary>
public sealed record CompanyGrantRow(string ModuleKey, string Label, bool GlobalRestricted, bool CompanyRestricted, bool GlobalHardLocked = false)
{
    /// <summary>Etkin: global VEYA firmaya özel kısıt yoksa verilebilir.</summary>
    public bool Grantable => !GlobalRestricted && !CompanyRestricted;
    public string StatusText => GlobalRestricted ? "Yalnız Admin (global)" : CompanyRestricted ? "Yalnız Admin (firma)" : "Verilebilir";
}

/// <summary>
/// #6 — Firma Yetki Kontrol (YALNIZ Süper Admin, yalnız web). Bir firmanın adminlerinin Personel'e
/// verebildiği/veremediği modülleri gösterir; süper admin firmaya özel EK kısıt ekleyebilir.
/// Global IsAdminRestricted her zaman geçerlidir; bu servis firmaya özel sıkılaştırma yönetir.
/// </summary>
public sealed class CompanyGrantService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CompanyGrantService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Süper adminin belirlediği DİNAMİK global kilit ayarının saklandığı global app_settings anahtarı.</summary>
    public const string GlobalLockKey = "global_grant_limits";

    public IReadOnlyList<CompanyGrantRow> GetControl(SessionContext s, string companyId)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Firma Yetki Kontrol yalnız süper admine açıktır.");
        var company = LoadCompanyLimits(companyId);
        HashSet<string> globalLocks;
        using (var conn = _factory.Create()) globalLocks = LoadGlobalLocks(conn);
        var rows = new List<CompanyGrantRow>();
        foreach (var (key, label) in AppModules.All)
        {
            if (AppModules.IsPublic(key) || AppModules.IsSuperAdminOnly(key)) continue; // atanamaz/gizli
            bool hard = AppModules.IsAdminRestricted(key);                    // derleme-zamanı sabit kilit
            bool globalEff = hard || globalLocks.Contains(key);              // etkin global kilit (sabit VEYA dinamik)
            rows.Add(new CompanyGrantRow(key, label, globalEff, company.Contains(key), hard));
        }
        return rows;
    }

    /// <summary>DİNAMİK global kilidi (tüm firmalar) TAM DEĞİŞTİRİR. YALNIZ süper admin. Derleme-zamanı sabit
    /// kilitli (IsAdminRestricted) / süper-admin-özel / public modüller yok sayılır (zaten değiştirilemez).</summary>
    public void SetGlobalLocks(SessionContext s, IReadOnlyList<string> moduleKeys)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Global kilit yalnız süper admine açıktır.");
        var keys = moduleKeys
            .Where(k => AppModules.All.Any(m => m.Key == k) && !AppModules.IsAdminRestricted(k)
                        && !AppModules.IsSuperAdminOnly(k) && !AppModules.IsPublic(k))
            .Distinct(StringComparer.Ordinal).ToList();
        var value = string.Join(",", keys);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO app_settings(id, company_id, setting_key, setting_value, updated_at)
VALUES($id, NULL, $k, $v, $now)
ON CONFLICT(IFNULL(company_id,''), setting_key)
DO UPDATE SET setting_value = excluded.setting_value, updated_at = excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$k", GlobalLockKey);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>DİNAMİK global kilit kümesi (global app_settings satırından; virgülle ayrık modül anahtarları).</summary>
    private static HashSet<string> LoadGlobalLocks(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT setting_value FROM app_settings WHERE company_id IS NULL AND setting_key=$k;";
        cmd.Parameters.AddWithValue("$k", GlobalLockKey);
        var raw = cmd.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(raw)
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
    }

    /// <summary>PermissionService için: modül DİNAMİK global kilitli mi (tüm firmalarda)?</summary>
    public static bool IsGlobalRestricted(SqliteConnection conn, SqliteTransaction tx, string moduleKey)
        => LoadGlobalLocks(conn, tx).Contains(moduleKey);

    /// <summary>Firmaya özel kısıtlı modülleri TAM DEĞİŞTİRİR (global-restricted olanlar yok sayılır).</summary>
    public void SetLimits(SessionContext s, string companyId, IReadOnlyList<string> restrictedKeys)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Firma Yetki Kontrol yalnız süper admine açıktır.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var keys = restrictedKeys
            .Where(k => AppModules.All.Any(m => m.Key == k) && !AppModules.IsAdminRestricted(k)
                        && !AppModules.IsSuperAdminOnly(k) && !AppModules.IsPublic(k))
            .Distinct().ToList();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM company_grant_limits WHERE company_id=$c;";
            del.Parameters.AddWithValue("$c", companyId);
            del.ExecuteNonQuery();
        }
        foreach (var k in keys)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO company_grant_limits(id, company_id, module_key, created_at) VALUES($id,$c,$k,$now);";
            ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            ins.Parameters.AddWithValue("$c", companyId);
            ins.Parameters.AddWithValue("$k", k);
            ins.Parameters.AddWithValue("$now", now);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private HashSet<string> LoadCompanyLimits(string companyId)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT module_key FROM company_grant_limits WHERE company_id=$c;";
        cmd.Parameters.AddWithValue("$c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    /// <summary>PermissionService için: modül, bu firmada firmaya-özel kısıtlı mı?</summary>
    public static bool IsCompanyRestricted(SqliteConnection conn, SqliteTransaction tx, string companyId, string moduleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM company_grant_limits WHERE company_id=$c AND module_key=$k;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$k", moduleKey);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
}
