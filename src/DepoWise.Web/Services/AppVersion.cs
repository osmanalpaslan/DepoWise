namespace DepoWise.Web;

/// <summary>
/// Çalışan web sürümünün kimliği. Her yeni deploy'da (yeni ikili/dosya) değişir; salt process
/// yeniden başlatmada aynı kalır. Tarayıcı bu değeri saklar; değişince oturum kapatılıp taze kodla yeniden yüklenir.
/// </summary>
public static class AppVersion
{
    public static readonly string Value = Compute();

    private static string Compute()
    {
        try
        {
            var loc = typeof(AppVersion).Assembly.Location;
            if (!string.IsNullOrEmpty(loc) && System.IO.File.Exists(loc))
                return System.IO.File.GetLastWriteTimeUtc(loc).ToString("yyyyMMddHHmmss");
        }
        catch { }
        return "0";
    }
}
