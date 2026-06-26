namespace DepoWise.Application.Common;

/// <summary>Araç sayacı geriye gidemez. Web `meter.ts` ile aynı kurallar.</summary>
public static class MeterRule
{
    /// <summary>İleri-yön güncelleme: yeni &gt; mevcut ise ilerler (true), aksi halde dokunmaz (false).
    /// Geçmiş tarihli kayıt girişini ENGELLEMEZ (no-op). Bakım/yakıt modülleri bunu kullanır.</summary>
    public static bool ShouldAdvance(decimal current, decimal incoming) => incoming > current;

    /// <summary>Doğrudan sayaç düzenleme: yeni &lt; mevcut ise geçersiz (geriye gidemez).</summary>
    public static bool IsValidDirectSet(decimal current, decimal incoming) => incoming >= current;
}

/// <summary>Sayaç doğrudan geriye alınmaya çalışıldığında fırlatılır.</summary>
public sealed class MeterBackwardException : Exception
{
    public MeterBackwardException(string message) : base(message) { }
}
