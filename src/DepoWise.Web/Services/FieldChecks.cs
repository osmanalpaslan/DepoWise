using System.Text.RegularExpressions;

namespace DepoWise.Web.Services;

/// <summary>
/// Web arayüzü alan kontrolleri. Web projesi bağımsız (backend'e yalnız HTTP ile bağlanır) olduğundan
/// DepoWise.Application.Ui.FieldChecks buraya küçük bir kopya olarak taşındı. Kesin kurallar sunucuda da
/// zorlanır; buradakiler kullanıcıya erken/nazik uyarı içindir (yumuşak kontroller kullanıcı yine geçebilir).
/// </summary>
public static class FieldChecks
{
    /// <summary>"+" seçim standardı (kullanıcı isteği 2026-08-06, madde 5): Türkçe karakter-doğru karşılaştırma
    /// (İ/I/ı/i, Ç/Ğ/Ö/Ş/Ü). StringComparison.OrdinalIgnoreCase Türkçe büyük/küçük harf kurallarını (İ↔i, I↔ı)
    /// DOĞRU eşlemez — arama/tekrar-kontrolü yapan TÜM ekranlar bu TEK kaynağı kullanır.</summary>
    public static readonly System.Globalization.CompareInfo TrCompare = new System.Globalization.CultureInfo("tr-TR").CompareInfo;

    /// <summary>
    /// 🔴 GERÇEK GUI HATASININ TEK KAYNAKTAN ÇÖZÜMÜ (2026-08-12) — TARİH → UNIX MS.
    ///
    /// <b>Sorun:</b> ekranlar tarih alanını <c>DateTime.Today</c>/<c>DateTime.Now</c> ile başlattığında
    /// <c>Kind=Local</c> olur. <c>new DateTimeOffset(local, TimeSpan.Zero)</c> .NET'te
    /// <see cref="ArgumentException"/> atar ("UTC Offset ... does not match the offset argument") ve
    /// KAYIT/SORGU DÜŞER. GUI testinde önce Tahsilat/Ödeme'de, sonra RAPORLARDA bulundu
    /// (<c>Reports.ApplyDateDefault</c> içinde <c>_to = DateTime.Now</c> → tarih seçmeden "Sorgula"
    /// denen her RequiresDate raporu patlıyordu).
    ///
    /// <b>Çözüm:</b> gün bileşeni alınır, Kind NÖTRLENİR, UTC 00:00 olarak yorumlanır.
    /// <b>Neden <c>new DateTimeOffset(d)</c> değil:</b> o, yerel saat dilimini uygular (TR = UTC+3) →
    /// 00:00 yerel = 21:00 UTC ÖNCEKİ GÜN; tarih BİR GÜN KAYARDI.
    ///
    /// <paramref name="endOfDay"/>: bitiş tarihlerinde günün SONUNU (23:59:59.999) verir.
    /// </summary>
    public static long? ToUnixMs(DateTime? d, bool endOfDay = false)
    {
        if (d is null) return null;
        var gun = DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified);
        var an = endOfDay ? gun.AddDays(1).AddMilliseconds(-1) : gun;
        return new DateTimeOffset(an, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>Ortak seçim alanı davranışı (madde 3, kullanıcı isteği 2026-08-06): sunucu-taramalı (server
    /// search) alanlar (SearchVehicle/SearchMaterial vb.) arama boşken bu kadarla sınırlanır; arama başlayınca
    /// sınır kalkar (çağıran zaten yalnız arama BOŞKEN bu sınırı uygular).</summary>
    public const int MaxUnfilteredOptions = 25;

    /// <summary>Doğrudan stok değişikliği uyarı metni (madde 1.3) — sunucudaki
    /// StockChangeLogService.WarningMessage'ın AYNISI (web Infrastructure'a erişemez → yansı). Log'a bu metin
    /// yazılır (POST /api/stock/change-log warningText).</summary>
    public const string StockChangeWarning =
        "Stok miktarını doğrudan düzenlemeye çalışıyorsunuz. Stok hareketlerinin kayıt altına alınabilmesi için " +
        "işlemleri mümkün olduğunca Giriş/Çıkış ekranından gerçekleştirmeniz önerilir. Devam ederseniz bu " +
        "değişiklik bir stok düzeltmesi olarak kaydedilir ve loglanır.";

    public const int MinVehicleYear = 1950;
    public static int MaxVehicleYear => DateTimeOffset.UtcNow.Year + 1;
    public static bool YearInRange(int? year) => year is null || (year >= MinVehicleYear && year <= MaxVehicleYear);

    /// <summary>Türk plaka biçimi (34 ABC 123 vb.). Boş plaka bu kontrolü tetiklemez. İş makinesi/plakasız
    /// araçlar uymayabilir → çağıran YALNIZ uyarı verir, engellemez.</summary>
    public static bool PlateLooksTurkish(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return true;
        var p = plate.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        return Regex.IsMatch(p, "^[0-9]{2}[A-ZÇĞİÖŞÜ]{1,4}[0-9]{2,5}$");
    }

    /// <summary>Telefon "makul" mü (yalnız uyarı için): 7-15 hane + geçerli karakterler.</summary>
    public static bool PhoneLooksValid(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return true;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 7 || digits.Length > 15) return false;
        return Regex.IsMatch(phone.Trim(), @"^[0-9+\-()\s]+$");
    }

    public const decimal LargeValueThreshold = 1_000_000m;
    public static bool IsSuspiciouslyLarge(decimal value) => value >= LargeValueThreshold;
}
