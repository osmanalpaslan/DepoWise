using DepoWise.Application.Security;

namespace DepoWise.Application.Reports;

/// <summary>Genel tablo modeli (rapor + Excel export ortak).</summary>
public sealed record TableModel(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>
/// Rapor isteği. Ağır rapor kullanıcı Sorgula/Filtrele demeden çalışmaz → <see cref="Executed"/>
/// yalnız kullanıcı tetiklemesiyle true olur.
/// </summary>
public sealed record ReportRequest(
    bool Executed,
    long? FromDate = null,
    long? ToDate = null,
    IReadOnlyList<string>? BranchIds = null,
    IReadOnlyList<string>? VehicleIds = null,
    string? CompanyId = null);

public static class ReportGate
{
    /// <summary>Filtre/Sorgula tıklanmadan (Executed=false) rapor çalıştırılamaz.</summary>
    public static void EnsureRunnable(ReportRequest req)
    {
        if (!req.Executed)
            throw new InvalidOperationException("Rapor, Sorgula/Filtrele tıklanmadan çalışmaz.");
    }

    /// <summary>Firma alanı yalnız Süper Admin'e gösterilir; diğer adminler kendi firmasına kilitli.</summary>
    public static bool ShowCompanyFilter(SessionContext s) => s.IsSuperAdmin;

    /// <summary>Hedef firma: Süper Admin seçebilir; diğerleri yalnız oturum firması (fail-closed).</summary>
    public static string ResolveCompany(SessionContext s, string? requested)
        => TenantAccessGuard.ResolveCompanyId(s, requested);
}

public enum AlertKind { Maintenance, Inspection, LowStock, Fuel }

public sealed record DashboardAlert(AlertKind Kind, string Title, string Detail, string NavigateKey, bool IsCritical, string? EntityId = null)
{
    /// <summary>Uyarı tipine göre ikon (emoji) — ana ekran uyarı listesinde gösterilir.</summary>
    public string Icon => Kind switch
    {
        AlertKind.Maintenance => "🔧",
        AlertKind.Inspection => "🛡️",
        AlertKind.LowStock => "📦",
        AlertKind.Fuel => "⛽",
        _ => "⚠️",
    };
}

public sealed record DashboardSummary(
    int VehicleCount, int MaterialCount, int LowStockCount, int PendingRequestCount, int PersonnelCount,
    IReadOnlyList<DashboardAlert> Alerts);
