using DepoWise.Application.Common;

namespace DepoWise.Application.Security;

/// <summary>
/// Deny-by-default erişim değerlendirici. UI (menü/buton/alan) ve API/servis aynı sonucu üretir.
/// Süper Admin ve Firma Admini tam yetkilidir; diğerleri yalnız verilmiş izinler kadar.
/// </summary>
public static class AccessControl
{
    public static bool IsAdmin(SessionContext s) => s.IsSuperAdmin || s.IsCompanyAdmin || DeveloperMode.IsActive;

    public static bool Can(SessionContext s, string moduleKey, PermissionAction action)
    {
        // Herkese açık modüller yalnız okuma için açıktır.
        if (AppModules.IsPublic(moduleKey)) return action == PermissionAction.View;

        // Rol Yetki Kontrol: süper adminin bu ROLE kapattığı ekran — admin bypass'ı dahil hiçbir yolla açılmaz.
        // (Süper admin ve geliştirici modu muaf; aksi halde platform sahibi kendini kilitler.)
        if (!s.IsSuperAdmin && !DeveloperMode.IsActive && s.BlockedModules.Contains(moduleKey)) return false;
        // Yalnız Süper Admin'e açık modüller (Kota, Canlı Sunucu, Yedekler, Makine, Güncelleme, Firma Tanım):
        // Süper Admin tam yetkili; firma admini bypass GEÇERSİZ. Süper admin bunları YALNIZ "Kısıtlı Süper Admin"e
        // devredebilir → o rol de yalnız AÇIKÇA verilen işlem kadar erişir.
        if (AppModules.IsSuperAdminOnly(moduleKey))
        {
            if (s.IsSuperAdmin || DeveloperMode.IsActive) return true;
            if (s.IsRestrictedSuperAdmin) return Explicit(s, moduleKey, action);
            return false;
        }
        if (IsAdmin(s)) return true;

        return Explicit(s, moduleKey, action);
    }

    /// <summary>Açıkça verilmiş modül izni (deny-by-default; rol bypass'ı yok).</summary>
    private static bool Explicit(SessionContext s, string moduleKey, PermissionAction action)
    {
        var p = s.Permissions.For(moduleKey);
        if (p is null) return false; // deny-by-default
        return action switch
        {
            PermissionAction.View => p.CanView,
            PermissionAction.Create => p.CanCreate,
            PermissionAction.Edit => p.CanEdit,
            PermissionAction.Delete => p.CanDelete,
            _ => false,
        };
    }

    /// <summary>Menüde görünürlük = okuma yetkisi. Kullanıcı REHBERİ istisnası: kullanıcı listesi tüm oturum
    /// sahiplerine görünür (yönetim yine admin — menü create/edit/delete bayrakları admin dışına false gelir).</summary>
    public static bool CanSeeMenu(SessionContext s, string moduleKey)
        => AppModules.IsUserDirectory(moduleKey) || Can(s, moduleKey, PermissionAction.View);

    /// <summary>Aktörün AÇIKÇA verilmiş herhangi bir izni (modül bayrağı veya buton) var mı?
    /// Yoksa firma admini "ilk admin" sayılır (geriye dönük uyum → sınırsız).</summary>
    private static bool HasAnyExplicit(SessionContext s)
        => s.Permissions.Modules.Any(m => m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
           || s.Permissions.Buttons.Any();

    /// <summary>
    /// Yetki AĞACINDA aktörün görebileceği/verebileceği modül mü (delegasyon tavanı):
    /// - Süper Admin: tümü (süper-admin-only dahil — devretmek için).
    /// - Kısıtlı Süper Admin / sınırlı admin/personel: YALNIZ kendi sahip olduğu modüller.
    /// - Açık izni hiç olmayan firma admini (ilk admin): tüm NORMAL modüller (süper-admin-only hariç).
    /// Aktörün veremeyeceği yetkiler ağaçta hiç görünmez.
    /// </summary>
    public static bool CanGrantModule(SessionContext s, string moduleKey)
    {
        if (s.IsSuperAdmin || DeveloperMode.IsActive) return true;
        var own = s.Permissions.For(moduleKey);
        if (own is not null && (own.CanView || own.CanCreate || own.CanEdit || own.CanDelete)) return true;
        if (!AppModules.IsSuperAdminOnly(moduleKey) && s.IsCompanyAdmin && !HasAnyExplicit(s)) return true;
        return false;
    }

    /// <summary>Yetki ağacında aktörün verebileceği özel buton mu (delegasyon tavanı; modülle aynı mantık).</summary>
    public static bool CanGrantButton(SessionContext s, string buttonKey)
    {
        if (s.IsSuperAdmin || DeveloperMode.IsActive) return true;
        if (s.Permissions.HasButton(buttonKey)) return true;
        if (s.IsCompanyAdmin && !HasAnyExplicit(s)) return true;
        return false;
    }

    /// <summary>Özel buton/alan: admin bypass + açık izin; aksi halde gizli (deny-by-default).</summary>
    public static bool CanUseButton(SessionContext s, string buttonKey)
        => IsAdmin(s) || s.Permissions.HasButton(buttonKey);

    /// <summary>API/servis sınırında fail-closed: yetki yoksa exception.</summary>
    public static void Require(SessionContext s, string moduleKey, PermissionAction action)
    {
        if (!Can(s, moduleKey, action))
            throw new ForbiddenException($"Yetki yok: {moduleKey}/{action}.");
    }

    public static void RequireButton(SessionContext s, string buttonKey)
    {
        if (!CanUseButton(s, buttonKey))
            throw new ForbiddenException($"Yetki yok: buton {buttonKey}.");
    }
}

/// <summary>
/// Tenant erişim koruması. İstek payload'ı bir company_id taşıyorsa, bu değer oturumdaki
/// company_id ile eşleşmek zorundadır; Süper Admin dışında firma değiştirilemez (analiz §4/§9).
/// </summary>
public static class TenantAccessGuard
{
    /// <summary>Hedef company_id'yi GÜVENLE çözer: payload yok sayılır, oturum esas alınır.</summary>
    public static string ResolveCompanyId(SessionContext s, string? payloadCompanyId)
    {
        if (!string.IsNullOrWhiteSpace(payloadCompanyId) &&
            !string.Equals(payloadCompanyId, s.CompanyId, StringComparison.Ordinal) &&
            !s.IsSuperAdmin)
        {
            throw new ForbiddenException("Tenant ihlali: farklı firma erişimi reddedildi.");
        }
        return s.IsSuperAdmin && !string.IsNullOrWhiteSpace(payloadCompanyId)
            ? payloadCompanyId!
            : s.CompanyId;
    }

    /// <summary>Bir kaydın company_id'si oturuma ait mi? Süper Admin hariç fail-closed.</summary>
    public static void EnsureOwnership(SessionContext s, string recordCompanyId)
    {
        if (s.IsSuperAdmin) return;
        if (!string.Equals(recordCompanyId, s.CompanyId, StringComparison.Ordinal))
            throw new ForbiddenException("Tenant ihlali: kayıt başka firmaya ait.");
    }
}

/// <summary>API katmanında 403'e çevrilen yetki/tenant hatası.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

/// <summary>
/// DÜZENLEME KİLİDİ (2026-07-22): kayıt, kullanıcı formu açtıktan SONRA başkası (başka kullanıcı ya da
/// eşitlemeyle gelen başka makine) tarafından değiştirilmiş. Kaydetme sessizce ÜZERİNE YAZMAZ; kullanıcıya
/// sorulur. API katmanında 409 Conflict'e çevrilir.
///
/// Neden gerçek "kilit" değil: DepoWise çevrimdışı çalışabilmeli. Sunucu tabanlı kilit çevrimdışı makinede
/// işlemez ve program çökerse kayıt kilitli kalır. Sürüm karşılaştırması ise çevrimdışı dahil her zaman
/// çalışır ve asıl zararı (sessiz üzerine yazma) önler.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    /// <summary>Kullanıcının formu açtığı andaki sürüm.</summary>
    public long ExpectedVersion { get; }
    /// <summary>Kayıttaki güncel sürüm.</summary>
    public long ActualVersion { get; }

    public ConcurrencyException(long expectedVersion, long actualVersion)
        : base("Bu kayıt siz düzenlemeye başladıktan sonra bir başkası tarafından değiştirildi. " +
               "Değişikliklerinizi kaybetmemek için kaydı yeniden açıp tekrar deneyin.")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
