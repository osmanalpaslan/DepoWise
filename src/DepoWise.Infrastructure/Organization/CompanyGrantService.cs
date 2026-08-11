using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Firma Yetki Kontrol satırı: her ekranın o firmadaki DÜZEYİ.
/// Level: "serbest" (tüm personele verilebilir) / "admin" (yalnız admin+) / "superadmin" (yalnız süper admin +
/// Kısıtlı Süper Admin). <paramref name="Hard"/> = derleme-zamanı sabit (değiştirilemez):
/// IsAdminRestricted → sabit "admin", IsSuperAdminOnly → sabit "superadmin".</summary>
public sealed record CompanyGrantRow(string ModuleKey, string Label, string Level, bool Hard)
{
    /// <summary>Serbest = firma admini bu ekranı doğrudan Personel'e verebilir.</summary>
    public bool Grantable => Level == CompanyGrantService.LevelFree;
    public string StatusText => Level switch
    {
        CompanyGrantService.LevelSuper => "Süper Admin",
        CompanyGrantService.LevelAdmin => "Admin",
        _ => "Serbest",
    };
}

/// <summary>
/// Firma Yetki Kontrol (YALNIZ Süper Admin, yalnız web). Bir firmadaki her ekranın erişim DÜZEYİNİ yönetir:
/// Serbest / Admin / Süper Admin. Serbest = satır yok; admin/superadmin = company_grant_limits satırı + level.
/// Enforcement PermissionService'te (grant-time): admin düzeyi → hedef admin olmalı; superadmin düzeyi →
/// hedef Kısıtlı Süper Admin (veya süper admin) olmalı.
/// </summary>
public sealed class CompanyGrantService
{
    public const string LevelFree = "serbest";
    public const string LevelAdmin = "admin";
    public const string LevelSuper = "superadmin";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CompanyGrantService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public IReadOnlyList<CompanyGrantRow> GetControl(SessionContext s, string companyId)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Firma Yetki Kontrol yalnız süper admine açıktır.");
        var levels = LoadCompanyLevels(companyId);
        var rows = new List<CompanyGrantRow>();
        foreach (var (key, label) in AppModules.All)
        {
            if (AppModules.IsPublic(key)) continue;
            if (AppModules.IsSuperAdminOnly(key)) { rows.Add(new CompanyGrantRow(key, label, LevelSuper, true)); continue; }
            if (AppModules.IsAdminRestricted(key)) { rows.Add(new CompanyGrantRow(key, label, LevelAdmin, true)); continue; }
            var lvl = levels.TryGetValue(key, out var l) ? l : LevelFree;
            rows.Add(new CompanyGrantRow(key, label, lvl, false));
        }
        return rows;
    }

    /// <summary>Firma bazında ekran düzeylerini TAM DEĞİŞTİRİR. Yalnız süper admin. Serbest = satır silinir;
    /// admin/superadmin = satır yazılır. Sabit (IsAdminRestricted/IsSuperAdminOnly/public) modüller yok sayılır.</summary>
    public void SetLevels(SessionContext s, string companyId, IReadOnlyDictionary<string, string> levels)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Firma Yetki Kontrol yalnız süper admine açıktır.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var clean = levels
            .Where(kv => AppModules.All.Any(m => m.Key == kv.Key)
                        && !AppModules.IsAdminRestricted(kv.Key) && !AppModules.IsSuperAdminOnly(kv.Key) && !AppModules.IsPublic(kv.Key)
                        && (kv.Value == LevelAdmin || kv.Value == LevelSuper))  // serbest = satır yok
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM company_grant_limits WHERE company_id=@c;";
            del.AddWithValue("@c", companyId);
            del.ExecuteNonQuery();
        }
        foreach (var (key, level) in clean)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO company_grant_limits(id, company_id, module_key, level, created_at) VALUES(@id,@c,@k,@lvl,@now);";
            ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            ins.AddWithValue("@c", companyId);
            ins.AddWithValue("@k", key);
            ins.AddWithValue("@lvl", level);
            ins.AddWithValue("@now", now);
            ins.ExecuteNonQuery();
        }

        // G6-06 (2026-08-11): AUDIT. Bir firmadaki ekranların erişim düzeyini değiştirmek en yetkili
        // işlemlerden biridir ama iz bırakmıyordu. Kayıt AKTÖRÜN firmasına yazılır ve HEDEF firma
        // entity_id olarak durur (CompanyPurgeService ile aynı desen). Aynı transaction içinde →
        // işlem geri alınırsa audit de geri alınır (sahte iz oluşmaz).
        // Özet YALNIZ düzey başına ekran SAYISIdır — hassas veri taşımaz.
        int adminCount = clean.Count(kv => kv.Value == LevelAdmin);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "company_permissions", companyId,
            AuditActions.Update, s.UserId,
            AfterJson: $"{{\"admin\":{adminCount},\"superadmin\":{clean.Count - adminCount}}}"), _clock);
        tx.Commit();
    }

    private Dictionary<string, string> LoadCompanyLevels(string companyId)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT module_key, level FROM company_grant_limits WHERE company_id=@c;";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    /// <summary>PermissionService için: modül bu firmada kısıtlı mı (admin VEYA superadmin düzeyi = satır var)?</summary>
    public static bool IsCompanyRestricted(DbConnection conn, DbTransaction tx, string companyId, string moduleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM company_grant_limits WHERE company_id=@c AND module_key=@k;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@k", moduleKey);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>PermissionService için: modül bu firmada "Süper Admin" düzeyinde mi (yalnız kısıtlı süper admine verilir)?</summary>
    public static bool IsCompanySuperRestricted(DbConnection conn, DbTransaction tx, string companyId, string moduleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM company_grant_limits WHERE company_id=@c AND module_key=@k AND level='superadmin';";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@k", moduleKey);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
}
