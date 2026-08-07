namespace DepoWise.Application.Reports;

/// <summary>
/// Maksimum kayıt koruması (kullanıcı isteği 2026-08-07, madde 7): yanlışlıkla milyonlarca kaydın tek sorguda
/// çekilmesini engellemek için ortak üst sınır. Bu faz: varsayılan sabit + Sistem Ayarları'ndan okunabilir
/// KANCA (mimari uygun; tam bitmek zorunda değil). İleride her raporun sorgusuna SQL LIMIT olarak da bağlanır.
/// </summary>
public static class ReportLimits
{
    /// <summary>Ayarlar'da tanımlı değilse kullanılacak varsayılan üst sınır.</summary>
    public const int DefaultMaxRows = 50_000;

    /// <summary>Sistem Ayarları anahtarı (ileride Ayarlar ekranından değiştirilebilir).</summary>
    public const string MaxRowsKey = "reports.max_rows";

    /// <summary>Ayar değerini (varsa) çözer; yoksa/geçersizse <see cref="DefaultMaxRows"/>. 1000 altına düşmez
    /// (kaza koruması). settingsGet: SettingsService.Get(companyId, key) delegesi (Application katmanı
    /// Infrastructure'a bağlı değil → delege ile enjekte edilir).</summary>
    public static int Resolve(System.Func<string, string?>? settingsGet)
    {
        var raw = settingsGet?.Invoke(MaxRowsKey);
        if (int.TryParse(raw, out var n) && n >= 1000) return n;
        return DefaultMaxRows;
    }
}
