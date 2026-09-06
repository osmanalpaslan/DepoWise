namespace DepoWise.Application.Common;

/// <summary>Araç sayacı geriye gidemez. Web `meter.ts` ile aynı kurallar.</summary>
public static class MeterRule
{
    /// <summary>İleri-yön güncelleme: yeni &gt; mevcut ise ilerler (true), aksi halde dokunmaz (false).
    /// Geçmiş tarihli kayıt girişini ENGELLEMEZ (no-op). Bakım/yakıt modülleri bunu kullanır.</summary>
    public static bool ShouldAdvance(decimal current, decimal incoming) => incoming > current;

    /// <summary>Doğrudan sayaç düzenleme: yeni &lt; mevcut ise geçersiz (geriye gidemez).</summary>
    public static bool IsValidDirectSet(decimal current, decimal incoming) => incoming >= current;

    // ═══ FAZ 4.1 (2026-09-06) — ŞÜPHELİ SIÇRAMA UYARISI (önleme) ════════════════════════════════
    // Gerçek olay: kullanıcı yakıt fişine yanlışlıkla çok büyük bir sayaç yazdı (basamak hatası) ve
    // bu değer araca işlendi. Sayaç artık düzeltilebiliyor (VehicleMeterService), ama asıl doğru
    // olan HATAYI GİRİLMEDEN yakalamaktır: kaydetmeden önce kullanıcıya sorulur.
    //
    // Eşik bilinçli olarak İKİ koşullu: oransal tek başına küçük sayaçlarda (0 → 50) sürekli uyarır,
    // mutlak tek başına uzun süre kullanılmamış araçta haksız uyarır. İkisi birden gerekir.

    /// <summary>Oransal eşik: yeni değer mevcudun bu katından büyükse şüpheli sayılır.</summary>
    private const decimal OranEsigi = 1.5m;

    /// <summary>Mutlak eşik (km/saat): bu kadarlık artıştan azı hiçbir zaman şüpheli sayılmaz.</summary>
    private const decimal MutlakEsik = 10_000m;

    /// <summary>
    /// Girilen sayaç, mevcut sayaca göre <b>gerçekçi olmayacak kadar</b> büyük mü?
    /// Kaydı ENGELLEMEZ — arayüz bunu kullanıcıya onaylatmak için sorar (yanlışlıkla basamak
    /// eklenmesi bu şekilde yakalanır). Mevcut 0 ise (ilk kayıt) uyarı verilmez.
    /// </summary>
    public static bool SuspiciousJump(decimal current, decimal incoming)
        => current > 0 && incoming > current * OranEsigi && incoming - current > MutlakEsik;

    /// <summary>Kullanıcıya gösterilecek uyarı metni (teknik terim yok). Birim boş geçilebilir.</summary>
    public static string SuspiciousJumpMessage(decimal current, decimal incoming, string? unitLabel = null)
    {
        var birim = string.IsNullOrWhiteSpace(unitLabel) ? "" : " " + unitLabel!.Trim();
        return $"Girdiğiniz sayaç ({incoming:0.##}{birim}) aracın mevcut sayacından ({current:0.##}{birim}) " +
               $"çok yüksek görünüyor. Fazladan basamak yazmış olabilir misiniz?\n\nYine de kaydedilsin mi?";
    }
}

/// <summary>Sayaç doğrudan geriye alınmaya çalışıldığında fırlatılır.</summary>
public sealed class MeterBackwardException : Exception
{
    public MeterBackwardException(string message) : base(message) { }
}
