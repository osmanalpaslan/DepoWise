using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// 🔴 GUI TESTİNDE BULUNAN GERÇEK HATA (2026-08-12) — WEB TARİH DÖNÜŞÜMÜ.
///
/// <b>Belirti:</b> web Tahsilat/Ödeme ekranında Kaydet'e basınca
/// <c>"Kaydedilemedi: The UTC Offset of the local dateTime parameter does not match the offset
/// argument. (Parameter 'offset')"</c> — kayıt HİÇ oluşmuyordu. Aynı kök neden Fatura ve
/// Kasa/Banka (iç transfer) ekranlarında da vardı.
///
/// <b>Kök neden:</b> tarih alanları <c>DateTime.Today</c> ile başlatılıyor → <c>Kind=Local</c>.
/// <c>new DateTimeOffset(localDateTime, TimeSpan.Zero)</c> .NET'te <see cref="ArgumentException"/>
/// atar: Kind=Local olan bir tarihe sıfır offset verilemez.
///
/// <b>Neden servis/API testleri yakalayamadı:</b> dönüşüm YALNIZ UI katmanındadır; servis zaten
/// hazır <c>long</c> milisaniye alır. Bu hatayı ancak gerçek GUI akışı ortaya çıkarabilirdi.
///
/// <b>Neden basitçe <c>new DateTimeOffset(d)</c> yazılmadı:</b> o, tarihi YEREL saat dilimiyle
/// yorumlar (TR = UTC+3) → 00:00 yerel = 21:00 UTC ÖNCEKİ GÜN. Fatura/tahsilat tarihi BİR GÜN
/// KAYARDI. Doğru çözüm: gün bileşenini al, Kind'ı NÖTRLE, UTC 00:00 olarak yorumla.
/// </summary>
public class WebDateConversionTests
{
    /// <summary>Web ekranlarındaki <c>Ms(DateTime?)</c> yardımcısının BİREBİR aynısı.</summary>
    private static long? Ms(DateTime? d) => d is null ? null
        : new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified), TimeSpan.Zero)
            .ToUnixTimeMilliseconds();

    /// <summary>
    /// 1 — 🔴 HATANIN KENDİSİ: eski desen <c>Kind=Local</c> tarihte İSTİSNA ATAR.
    /// Bu test, hatanın gerçek olduğunu ve neden kaydın düştüğünü belgeler.
    /// </summary>
    [Fact]
    public void Eski_Desen_Local_Tarihte_Patlar()
    {
        var bugun = DateTime.Today;                       // Kind = Local (MudDatePicker varsayılanı böyle set ediliyordu)
        Assert.Equal(DateTimeKind.Local, bugun.Kind);

        Assert.Throws<ArgumentException>(() => new DateTimeOffset(bugun, TimeSpan.Zero));
    }

    /// <summary>2 — ⭐ DÜZELTME: yeni desen Kind=Local tarihte PATLAMAZ.</summary>
    [Fact]
    public void Yeni_Desen_Local_Tarihte_Patlamaz()
    {
        var ms = Ms(DateTime.Today);
        Assert.NotNull(ms);
    }

    /// <summary>3 — Diğer Kind değerlerinde de çalışır (Utc / Unspecified).</summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Her_Kind_Icin_Calisir(DateTimeKind kind)
    {
        var d = DateTime.SpecifyKind(new DateTime(2026, 8, 12, 0, 0, 0), kind);
        Assert.NotNull(Ms(d));
    }

    /// <summary>
    /// 4 — ⭐ GÜN KAYMASI YOK: 12 Ağustos 2026 seçilirse UTC'de de 12 Ağustos olmalı.
    /// (Yerel saat dilimi uygulansaydı TR'de 11 Ağustos 21:00 olurdu.)
    /// </summary>
    [Fact]
    public void Gun_Kaymasi_Olmaz()
    {
        var secilen = new DateTime(2026, 8, 12);
        var ms = Ms(secilen)!.Value;
        var geri = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

        Assert.Equal(2026, geri.Year);
        Assert.Equal(8, geri.Month);
        Assert.Equal(12, geri.Day);
        Assert.Equal(0, geri.Hour);      // UTC 00:00 — gün başı
    }

    /// <summary>5 — Saat bileşeni taşınmaz: seçilen tarihin saati ne olursa olsun gün başına düşer.</summary>
    [Fact]
    public void Saat_Bileseni_Tasinmaz()
    {
        var ogleden = new DateTime(2026, 8, 12, 15, 47, 33);
        Assert.Equal(Ms(new DateTime(2026, 8, 12)), Ms(ogleden));
    }

    /// <summary>6 — null → null (tarih seçilmemişse alan gönderilmez).</summary>
    [Fact]
    public void Null_Null_Doner() => Assert.Null(Ms(null));
}
