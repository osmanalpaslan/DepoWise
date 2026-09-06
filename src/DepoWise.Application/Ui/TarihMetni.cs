using System;
using System.Globalization;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ TARİH METNİ — GG.AA.YYYY biçimlendirme, maskeleme ve GERÇEK TAKVİM doğrulaması ═══
/// (kullanıcı isteği 2026-09-06: kompakt tarih alanı.)
///
/// <para><b>Neden ayrı bir sınıf.</b> Bu mantık masaüstündeki <c>TarihKutusu</c> denetiminin içinde
/// dursaydı test edilemezdi: test projesi <c>DepoWise.Desktop</c>'a başvurmaz (yalnız Api ·
/// Infrastructure · Application · Domain). Kural olarak iş mantığı UI denetiminin içinde saklanmaz —
/// burada durunca hem masaüstü kullanır hem birim testi yazılır.</para>
///
/// <para><b>Gerçek takvim doğrulaması</b> (CLAUDE.md §5): 31.02.2026, 30.02.2024, 31.04.2026 gibi
/// var olmayan günler REDDEDİLİR. Kabul biçimi tektir: <c>GG.AA.YYYY</c> — iki haneli gün, iki haneli
/// ay, dört haneli yıl. Böylece 01.02.2026'nın "1 Şubat mı 2 Ocak mı" belirsizliği oluşmaz.</para>
/// </summary>
public static class TarihMetni
{
    /// <summary>Kullanıcının gördüğü tek biçim.</summary>
    public const string Bicim = "dd.MM.yyyy";

    private static readonly CultureInfo Kultur = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Tarihi kutuda gösterilecek metne çevirir. Tarih yoksa boş metin.</summary>
    public static string Bicimle(DateTimeOffset? tarih)
        => tarih is { } t ? t.DateTime.ToString(Bicim, Kultur) : "";

    /// <summary>
    /// Kullanıcının yazdığı ham metni maskeler: yalnız rakamlar alınır, noktalar KENDİLİĞİNDEN
    /// eklenir. "06092026" → "06.09.2026". Sekiz rakamdan fazlası yok sayılır.
    /// </summary>
    public static string Maskele(string? ham)
    {
        var rakamlar = new string((ham ?? "").Where(char.IsDigit).ToArray());
        if (rakamlar.Length > 8) rakamlar = rakamlar[..8];

        if (rakamlar.Length > 4) return rakamlar[..2] + "." + rakamlar[2..4] + "." + rakamlar[4..];
        if (rakamlar.Length > 2) return rakamlar[..2] + "." + rakamlar[2..];
        return rakamlar;
    }

    /// <summary>
    /// Metni tarihe çevirir.
    /// <list type="bullet">
    ///   <item>Boş/yalnız boşluk → <c>true</c> döner ve <paramref name="tarih"/> <c>null</c> olur
    ///         (süzgeç alanları bilerek boş bırakılabilir).</item>
    ///   <item>Geçerli GG.AA.YYYY → <c>true</c>, tarih dolu.</item>
    ///   <item>Eksik, bozuk ya da takvimde OLMAYAN gün → <c>false</c>; çağıran eski değeri korur.</item>
    /// </list>
    /// Saat bileşeni yoktur: değer daima günün başlangıcı ve <see cref="TimeSpan.Zero"/> ofsetlidir —
    /// mevcut ekranların <c>DateTimeOffset?</c> beklentisiyle birebir aynıdır.
    /// </summary>
    public static bool Coz(string? metin, out DateTimeOffset? tarih)
    {
        tarih = null;
        var s = (metin ?? "").Trim();
        if (s.Length == 0) return true;

        // TryParseExact takvimi de doğrular: 31.02.2026 buradan GEÇEMEZ.
        if (!DateTime.TryParseExact(s, Bicim, Kultur, DateTimeStyles.None, out var t)) return false;

        tarih = new DateTimeOffset(t.Date, TimeSpan.Zero);
        return true;
    }
}
