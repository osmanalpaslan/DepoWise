using DepoWise.Application.Common;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Settings;

/// <summary>
/// app_settings okuma/yazma + tema/branding üretimi. Firma override yoksa global, o da yoksa
/// kod varsayılanı (ThemeTokens.Default / BrandingSettings.Default). Yazımlar audit'lenir.
/// </summary>
public sealed class SettingsService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public SettingsService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Değer: firma override > global > null.</summary>
    public string? Get(string? companyId, string key)
    {
        using var conn = _factory.Create();
        if (companyId is not null)
        {
            var v = ReadOne(conn, companyId, key);
            if (v is not null) return v;
        }
        return ReadGlobal(conn, key);
    }

    public void Set(string? companyId, string key, string? value, string? userId = null)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        string? before = ReadRaw(conn, tx, companyId, key);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO app_settings(id, company_id, setting_key, setting_value, updated_at)
VALUES($id, $c, $k, $v, $now)
ON CONFLICT(IFNULL(company_id,''), setting_key)
DO UPDATE SET setting_value = excluded.setting_value, updated_at = excluded.updated_at;";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$c", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        // Ayar değişikliği audit'lenir (global ayarlar için company '__global__').
        AuditWriter.Write(conn, tx, new AuditEntry(
            CompanyId: companyId ?? "__global__",
            EntityType: "app_setting",
            EntityId: key,
            Action: AuditActions.Update,
            UserId: userId,
            BeforeJson: before is null ? null : $"{{\"value\":\"{before}\"}}",
            AfterJson: value is null ? null : $"{{\"value\":\"{value}\"}}"), _clock);
        tx.Commit();
    }

    public ThemeTokens GetTheme(string? companyId)
    {
        var d = ThemeTokens.Default;
        return new ThemeTokens(
            Get(companyId, SettingKeys.ThemePrimary) ?? d.Primary,
            Get(companyId, SettingKeys.ThemeOnPrimary) ?? d.OnPrimary,
            Get(companyId, SettingKeys.ThemeSurface) ?? d.Surface,
            Get(companyId, SettingKeys.ThemeOnSurface) ?? d.OnSurface,
            Get(companyId, SettingKeys.ThemeAccent) ?? d.Accent,
            Get(companyId, SettingKeys.ThemeDanger) ?? d.Danger,
            Get(companyId, SettingKeys.ThemeWarning) ?? d.Warning,
            Get(companyId, SettingKeys.ThemeSuccess) ?? d.Success,
            Get(companyId, SettingKeys.ThemeCornerRadius) ?? d.CornerRadius);
    }

    public BrandingSettings GetBranding(string? companyId)
    {
        var d = BrandingSettings.Default;
        return new BrandingSettings(
            Get(companyId, SettingKeys.BrandAppName) ?? d.AppName,
            Get(companyId, SettingKeys.BrandCompanyName) ?? d.CompanyName,
            Get(companyId, SettingKeys.BrandLogoPath) ?? d.LogoPath,
            Get(companyId, SettingKeys.BrandContact) ?? d.Contact,
            Get(companyId, SettingKeys.BrandWebsite) ?? d.Website,
            Get(companyId, SettingKeys.BrandCopyright) ?? d.Copyright);
    }

    private static string? ReadOne(SqliteConnection conn, string companyId, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT setting_value FROM app_settings WHERE company_id = $c AND setting_key = $k;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static string? ReadGlobal(SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT setting_value FROM app_settings WHERE company_id IS NULL AND setting_key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static string? ReadRaw(SqliteConnection conn, SqliteTransaction tx, string? companyId, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = companyId is null
            ? "SELECT setting_value FROM app_settings WHERE company_id IS NULL AND setting_key = $k;"
            : "SELECT setting_value FROM app_settings WHERE company_id = $c AND setting_key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        if (companyId is not null) cmd.Parameters.AddWithValue("$c", companyId);
        return cmd.ExecuteScalar() as string;
    }
}
