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
    /// <summary>
    /// Üretimdeki <c>DepoWise.Web.Services.FieldChecks.ToUnixMs</c> AYNASI.
    /// ⚠️ Test projesi Web'e referans VEREMEZ (Web, Application'ı dosya-link ile kullanır;
    /// ProjectReference yoktur). Bu yüzden projenin mevcut "AYNASI" deseni uygulanır ve
    /// <see cref="Ayna_Uretim_Koduyla_Ayni"/> testi ikisinin AYNI kaldığını KANITLAR.
    /// </summary>
    private static long? Ms(DateTime? d) => d is null ? null
        : new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified), TimeSpan.Zero)
            .ToUnixTimeMilliseconds();

    private static long? MsSon(DateTime? d) => d is null ? null
        : new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified).AddDays(1).AddMilliseconds(-1), TimeSpan.Zero)
            .ToUnixTimeMilliseconds();

    /// <summary>
    /// 0 — ⭐ AYNA DENETİMİ: üretim dosyası hâlâ Kind-nötrleyen deseni kullanıyor mu?
    /// Biri değişip diğeri kalırsa bu test kırılır (sessiz ayrışma engellenir).
    /// </summary>
    [Fact]
    public void Ayna_Uretim_Koduyla_Ayni()
    {
        var yol = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "DepoWise.Web", "Services", "FieldChecks.cs");
        var kaynak = File.ReadAllText(Path.GetFullPath(yol));

        Assert.Contains("public static long? ToUnixMs(DateTime? d, bool endOfDay = false)", kaynak);
        Assert.Contains("DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified)", kaynak);
        Assert.Contains("new DateTimeOffset(an, TimeSpan.Zero).ToUnixTimeMilliseconds()", kaynak);

        // Üretimde ESKİ hatalı desen KALMAMALI (ham yerel tarihe sıfır offset).
        Assert.DoesNotContain("new DateTimeOffset(d.Value, TimeSpan.Zero)", kaynak);
    }

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

    /// <summary>
    /// 7 — 🔴 İKİNCİ GERÇEK HATA (2026-08-12, GUI): <c>Reports.ApplyDateDefault</c> bitiş tarihini
    /// <c>DateTime.Now</c> ile dolduruyordu (Kind=Local). Kullanıcı tarih seçmeden "Sorgula" derse
    /// <b>RequiresDate olan HER rapor patlıyordu</b> — G4-4'ün beş raporu ve mevcut Araç/Bakım/Yakıt/
    /// Stok Hareketleri raporları dahil. Bu test o senaryoyu birebir üretir.
    /// </summary>
    [Fact]
    public void Rapor_Varsayilan_Tarih_Araligi_Patlamaz()
    {
        var now = DateTime.Now;                       // Kind = Local (ApplyDateDefault ile aynı)
        var from = new DateTime(now.Year, now.Month, 1);
        var to = now;

        Assert.NotNull(Ms(from));
        Assert.NotNull(Ms(to));                       // eski desende ArgumentException atıyordu
    }

    /// <summary>8 — Gün sonu (bitiş tarihi) 23:59:59.999 verir ve gün kaymaz.</summary>
    [Fact]
    public void Gun_Sonu_Dogru()
    {
        var ms = MsSon(new DateTime(2026, 8, 12))!.Value;
        var geri = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        Assert.Equal(12, geri.Day);
        Assert.Equal(23, geri.Hour);
        Assert.Equal(59, geri.Minute);
    }

    /// <summary>
    /// 9 — Başlangıç &lt; bitiş: aynı gün seçilse bile aralık BOŞ olmaz (gün başı → gün sonu).
    /// </summary>
    [Fact]
    public void Ayni_Gun_Araligi_Bos_Olmaz()
    {
        var g = new DateTime(2026, 8, 12);
        Assert.True(MsSon(g) > Ms(g));
    }
}
