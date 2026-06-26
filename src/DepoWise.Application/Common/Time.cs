namespace DepoWise.Application.Common;

/// <summary>
/// Tüm zaman merkezi olarak UTC üretilir; dış sözleşmede Unix ms kullanılır.
/// Test edilebilirlik için soyutlanmıştır (sabit saat ile deterministik test).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>UTC / Unix ms dönüşüm yardımcıları (dış sözleşme zaman birimi).</summary>
public static class UnixTime
{
    public static long ToUnixMs(DateTimeOffset value) => value.ToUnixTimeMilliseconds();
    public static DateTimeOffset FromUnixMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);
}
