using System.Collections.Concurrent;
using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Yönetim ekranının bir satırı: ekran + varsayılan + etkin + firma kaydı.</summary>
public sealed record ScreenVisibilityRow(
    string ScreenKey, string Group, string Label, string ModuleKey,
    bool DefaultDesktop, bool DefaultWeb,
    bool EffectiveDesktop, bool EffectiveWeb,
    bool? OverrideDesktop, bool? OverrideWeb,
    long? UpdatedAt)
{
    /// <summary>Kullanıcıya gösterilecek kısa durum (teknik terim yok).</summary>
    public string StatusText => (EffectiveDesktop, EffectiveWeb) switch
    {
        (true, true) => "Masaüstü + Web",
        (true, false) => "Yalnız Masaüstü",
        (false, true) => "Yalnız Web",
        _ => "Kapalı",
    };

    /// <summary>Bu ekran o platformda katalogda hiç YOK mu (kutu devre dışı gösterilir)?</summary>
    public bool DesktopUnavailable => !DefaultDesktop;
    public bool WebUnavailable => !DefaultWeb;
}

/// <summary>
/// ═══ G5 — EKRAN PLATFORM GÖRÜNÜRLÜĞÜ SERVİSİ (2026-08-12) ═══
///
/// Firma bazında "bu ekran bu platformda açık mı" kaydını okur/yazar. Kayıt YOKSA
/// <c>AppScreens.Platforms</c> varsayılanı geçerlidir → migration sonrası hiçbir ekran kapanmaz.
///
/// <b>Desen:</b> <see cref="CompanyGrantService"/> / <see cref="RoleGrantService"/> ile aynı
/// (yeni mimari icat edilmedi). <b>Yalnız daraltır</b> — bkz. <see cref="ScreenVisibility"/>.
///
/// <b>ÖNBELLEK:</b> menü/route/gezinme her istekte okunur; her seferinde veritabanına gitmek
/// masaüstünde gezinmeyi yavaşlatırdı. Firma başına kısa ömürlü (TTL) önbellek tutulur ve
/// <b>yazma anında ANINDA düşürülür</b> (<see cref="Invalidate"/>) → yönetici bir platformu
/// kapattığında bayat veri kalmaz. Desen <c>PermissionSnapshotCache</c> ile aynıdır.
/// </summary>
public sealed class ScreenVisibilityService
{
    /// <summary>Yönetim ekranının yetki modülü — dar tutuldu (yalnız süper admin düzeyi).</summary>
    public const string Module = "screen_visibility";

    private const string PlatformDesktop = Database.Migrations.Migration065_ScreenPlatformVisibility.PlatformDesktop;
    private const string PlatformWeb = Database.Migrations.Migration065_ScreenPlatformVisibility.PlatformWeb;

    /// <summary>Önbellek ömrü — yetki fotoğrafıyla aynı mantık (kısa; yazmada zaten düşürülür).</summary>
    public const int CacheTtlSeconds = 60;

    private sealed record Entry(IReadOnlyDictionary<string, ScreenVisibilityOverride> Map, DateTimeOffset Expires);

    private static readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public ScreenVisibilityService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Firmanın kayıtlarını düşürür (yazma sonrası ve testlerde çağrılır).</summary>
    public static void Invalidate(string companyId) => _cache.TryRemove(companyId, out _);

    /// <summary>Tüm önbelleği düşürür.</summary>
    public static void InvalidateAll() => _cache.Clear();

    /// <summary>
    /// Firmanın platform kısıtları (ekran anahtarı → kısıt). Yetki GEREKTİRMEZ: menü/route/gezinme
    /// her kullanıcı için çağırır ve bu bilgi yetki taşımaz — hangi ekranın hangi platformda açık
    /// olduğu, kullanıcının o ekrana erişip erişemeyeceğinden BAĞIMSIZDIR (erişim ayrıca kontrol edilir).
    /// </summary>
    public IReadOnlyDictionary<string, ScreenVisibilityOverride> OverridesFor(string companyId)
    {
        if (_cache.TryGetValue(companyId, out var hit) && hit.Expires > _clock.UtcNow) return hit.Map;

        var map = new Dictionary<string, ScreenVisibilityOverride>(StringComparer.Ordinal);
        try
        {
            using var conn = _factory.Create();
            if (!DbIntrospect.TableExists(conn, null, "screen_platform_visibility")) return Store(companyId, map);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT screen_key, platform, enabled FROM screen_platform_visibility WHERE company_id=@c;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                var platform = r.GetString(1);
                var enabled = Convert.ToInt64(r.GetValue(2)) == 1;
                map.TryGetValue(key, out var cur);
                cur ??= new ScreenVisibilityOverride(key, null, null);
                map[key] = platform == PlatformDesktop
                    ? cur with { Desktop = enabled }
                    : cur with { Web = enabled };
            }
        }
        catch { /* okuma hatası menüyü çökertmez → varsayılanlar geçerli kalır */ }
        return Store(companyId, map);
    }

    private IReadOnlyDictionary<string, ScreenVisibilityOverride> Store(string companyId,
        Dictionary<string, ScreenVisibilityOverride> map)
    {
        _cache[companyId] = new Entry(map, _clock.UtcNow.AddSeconds(CacheTtlSeconds));
        return map;
    }

    /// <summary>Yönetim ekranının listesi: her ekran için varsayılan + etkin + firma kaydı.</summary>
    public IReadOnlyList<ScreenVisibilityRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var overrides = OverridesFor(s.CompanyId);
        var updated = UpdatedAtMap(s.CompanyId);

        var rows = new List<ScreenVisibilityRow>();
        foreach (var sc in AppScreens.All)
        {
            var eff = ScreenVisibility.Effective(sc, overrides);
            overrides.TryGetValue(sc.Key, out var o);
            updated.TryGetValue(sc.Key, out var at);
            rows.Add(new ScreenVisibilityRow(
                sc.Key, sc.Group, sc.Label, sc.ModuleKey,
                sc.OnDesktop, sc.OnWeb,
                eff.HasFlag(ScreenPlatform.Desktop), eff.HasFlag(ScreenPlatform.Web),
                o?.Desktop, o?.Web,
                at == 0 ? null : at));
        }
        return rows;
    }

    private Dictionary<string, long> UpdatedAtMap(string companyId)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            using var conn = _factory.Create();
            if (!DbIntrospect.TableExists(conn, null, "screen_platform_visibility")) return map;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT screen_key, MAX(updated_at) FROM screen_platform_visibility WHERE company_id=@c GROUP BY screen_key;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) map[r.GetString(0)] = Convert.ToInt64(r.GetValue(1));
        }
        catch { }
        return map;
    }


    /// <summary>
    /// Bir ekranın platform ayarını yazar. <paramref name="desktop"/> / <paramref name="web"/>
    /// <c>null</c> ise o platformun kaydı SİLİNİR → katalog varsayılanına döner.
    ///
    /// ⚠️ Katalogda o platformda VAR OLMAYAN bir ekran için "açık" yazılamaz (yalnız daraltma kuralı);
    /// böyle bir istek sessizce yok sayılmaz, <see cref="InvalidOperationException"/> ile reddedilir.
    /// Tek transaction + audit.
    /// </summary>
    public void Set(SessionContext s, string screenKey, bool? desktop, bool? web)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var screen = AppScreens.ByKey(screenKey)
            ?? throw new ArgumentException("Bilinmeyen ekran: " + screenKey);

        if (desktop == true && !screen.OnDesktop)
            throw new InvalidOperationException($"'{screen.Label}' masaüstünde bulunmuyor; buradan açılamaz.");
        if (web == true && !screen.OnWeb)
            throw new InvalidOperationException($"'{screen.Label}' web'de bulunmuyor; buradan açılamaz.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        Apply(conn, tx, s.CompanyId, screenKey, PlatformDesktop, desktop, now);
        Apply(conn, tx, s.CompanyId, screenKey, PlatformWeb, web, now);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "screen_platform_visibility", screenKey,
            AuditActions.Update, s.UserId,
            AfterJson: $"{{\"desktop\":{Json(desktop)},\"web\":{Json(web)}}}"), _clock);

        tx.Commit();
        Invalidate(s.CompanyId);   // yönetici kapattığı anda etkili olsun (bayat veri kalmaz)
    }

    private static string Json(bool? v) => v is null ? "null" : (v.Value ? "true" : "false");

    private static void Apply(DbConnection conn, DbTransaction tx, string companyId, string screenKey,
        string platform, bool? enabled, long now)
    {
        if (enabled is null)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM screen_platform_visibility WHERE company_id=@c AND screen_key=@s AND platform=@p;";
            del.AddWithValue("@c", companyId); del.AddWithValue("@s", screenKey); del.AddWithValue("@p", platform);
            del.ExecuteNonQuery();
            return;
        }

        // Önce güncelle; satır yoksa ekle (UNIQUE kısıtı mükerrer kaydı zaten engeller).
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE screen_platform_visibility SET enabled=@e, updated_at=@now WHERE company_id=@c AND screen_key=@s AND platform=@p;";
            upd.AddWithValue("@e", enabled.Value ? 1 : 0); upd.AddWithValue("@now", now);
            upd.AddWithValue("@c", companyId); upd.AddWithValue("@s", screenKey); upd.AddWithValue("@p", platform);
            if (upd.ExecuteNonQuery() > 0) return;
        }
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO screen_platform_visibility(id, company_id, screen_key, platform, enabled, created_at, updated_at) " +
                          "VALUES(@id,@c,@s,@p,@e,@now,@now);";
        ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        ins.AddWithValue("@c", companyId); ins.AddWithValue("@s", screenKey); ins.AddWithValue("@p", platform);
        ins.AddWithValue("@e", enabled.Value ? 1 : 0); ins.AddWithValue("@now", now);
        ins.ExecuteNonQuery();
    }
}
