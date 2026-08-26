using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ HIZ SINIRLAYICI · BELLEK VE DAVRANIŞ ═══ (denetim 2026-08-26)
///
/// <b>Bulunan durum:</b> <see cref="RateLimiter"/> anahtarları istemci IP'sinden üretir ve süresi dolmuş
/// pencereleri HİÇ TEMİZLEMİYORDU. Farklı IP'lerden gelen her istek kalıcı bir satır bırakıyordu; sunucu
/// bellek sınırı 207 MB olduğu için IP çeşitlendiren bir istek seli süreci düşürebilirdi (sınırsız önbellek).
///
/// <b>Kural DEĞİŞMEDİ:</b> atılan satırlar zaten "penceresi dolmuş" olanlardır — bir sonraki istekte
/// nasılsa sıfırlanacaklardı. Aşağıdaki testler hem temizliği hem de <b>kararların aynı kaldığını</b> ölçer.
/// </summary>
public class RateLimiterHardeningTests
{
    private sealed class Saat
    {
        public DateTimeOffset Simdi = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public DateTimeOffset Oku() => Simdi;
    }

    [Fact]
    public void HZ01_Suresi_Dolmus_Satirlar_Temizlenir()
    {
        var saat = new Saat();
        var rl = new RateLimiter(5, TimeSpan.FromMinutes(1), saat.Oku);

        // 6.000 farklı IP → eşik (5.000) aşılır.
        for (int i = 0; i < 6_000; i++) rl.Check("ip:" + i);
        Assert.True(rl.TrackedKeys > 5_000, "beklenen: sözlük dolu; gerçek: " + rl.TrackedKeys);

        // Pencere geçtikten sonra ilk istekte eskiler atılır.
        saat.Simdi = saat.Simdi.AddMinutes(2);
        rl.Check("yeni-ip");

        Assert.True(rl.TrackedKeys <= 2, "süresi dolmuş satırlar temizlenmedi; kalan: " + rl.TrackedKeys);
    }

    [Fact]
    public void HZ02_Temizlik_Karari_Degistirmez()
    {
        var saat = new Saat();
        var rl = new RateLimiter(3, TimeSpan.FromMinutes(1), saat.Oku);

        Assert.True(rl.Check("a").Allowed);
        Assert.True(rl.Check("a").Allowed);
        Assert.True(rl.Check("a").Allowed);
        Assert.False(rl.Check("a").Allowed);          // limit doldu

        // Sözlüğü eşiğin üstüne çıkar — "a" hâlâ AYNI pencerede olduğu için temizlenmemeli.
        for (int i = 0; i < 6_000; i++) rl.Check("dolgu:" + i);
        Assert.False(rl.Check("a").Allowed);          // karar değişmedi

        // Pencere dolunca yeniden açılır (eski davranış).
        saat.Simdi = saat.Simdi.AddMinutes(1);
        Assert.True(rl.Check("a").Allowed);
    }

    [Fact]
    public void HZ03_Esik_Altinda_Hicbir_Sey_Atilmaz()
    {
        var saat = new Saat();
        var rl = new RateLimiter(5, TimeSpan.FromMinutes(1), saat.Oku);

        for (int i = 0; i < 100; i++) rl.Check("ip:" + i);
        saat.Simdi = saat.Simdi.AddMinutes(5);
        rl.Check("ip:0");

        // Eşik (5.000) aşılmadığı için temizlik ÇALIŞMAZ → davranış eskisiyle birebir aynı.
        Assert.Equal(100, rl.TrackedKeys);
    }
}
