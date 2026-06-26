namespace DepoWise.Application.Maintenance;

public enum AlertLevel
{
    Normal,       // < %85 — sessiz
    Approaching,  // %85–95 — sarı (yaklaşıyor)
    Critical,     // %95–100 — turuncu (kritik)
    Overdue,      // >= %100 — kırmızı (gecikti)
}

public enum IntervalUnit { Km, Hour, Day }

/// <summary>
/// Periyodik bakım uyarı eşikleri (analiz §6.8). Web `alertRules.ts` ile aynı.
/// progress = tüketilen / interval (0..).
/// </summary>
public static class AlertRules
{
    public const double Approaching = 0.85;
    public const double Critical = 0.95;
    public const double Overdue = 1.0;

    public static AlertLevel Level(double progress)
    {
        if (progress >= Overdue) return AlertLevel.Overdue;
        if (progress >= Critical) return AlertLevel.Critical;
        if (progress >= Approaching) return AlertLevel.Approaching;
        return AlertLevel.Normal;
    }

    /// <summary>İlerleme = tüketilen / interval. interval &lt;= 0 ise 0 (uyarı yok).</summary>
    public static double Progress(decimal consumed, decimal interval)
        => interval <= 0 ? 0 : (double)(consumed / interval);

    public static IntervalUnit ParseUnit(string unit) => unit switch
    {
        "hour" => IntervalUnit.Hour,
        "day" => IntervalUnit.Day,
        _ => IntervalUnit.Km,
    };
}
