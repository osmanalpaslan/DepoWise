using System;
using System.Collections.Generic;
using System.Linq;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ FAZ 2 (ADR-221, 2026-09-05) — MENÜ RENK AİLESİ: TEK DOĞRU KAYNAK ═══
///
/// <b>Sorun.</b> Menü üç seviyeliydi (ÜST GRUP → ÜST MENÜ → EKRAN) ama hiyerarşi yalnız
/// <b>simge + kalınlık + girinti</b> ile anlatılıyordu. Renk hiyerarşiye hiç katılmıyordu:
/// masaüstünde 27 tema token'ı vardı, hiçbiri gruba bağlı değildi; web'de tek bir <c>primary</c>
/// rengi vardı. Kullanıcı "bu üst menü mü, ekran mı" ayrımını yalnız yazı kalınlığından çıkarıyordu.
///
/// <b>Çözüm.</b> Bu sınıf "hangi menü hangi RENK AİLESİNİ kullanır" sorusunun tek cevabıdır.
/// Platformlar yalnız <b>aile → kendi renk sistemi</b> çevirisini yapar
/// (masaüstü: Avalonia fırçası · web: CSS değişkeni). <see cref="MenuIcons"/> ile birebir aynı
/// desen — kanıtlanmış ve iki menünün ayrışmasını önlüyor.
///
/// <b>⭐ RENK EKRANA DEĞİL HİYERARŞİYE BAĞLIDIR.</b> Bir ekranın rengi yoktur; ekran, ait olduğu
/// üst menünün ailesini <b>miras alır</b>; üst menü de ait olduğu üst grubun ailesini. Bu yüzden
/// <c>AppScreens.All</c>'a yeni bir satır eklemek renk için YETERLİDİR — hiçbir yerde
/// <c>ScreenX = Blue</c> yazılmaz. Kalıcı kural budur.
///
/// <b>⭐ YENİ ÜST MENÜ DE RENKSİZ KALMAZ.</b> Eşlemede karşılığı olmayan bir başlık, adına göre
/// <b>belirlenimci</b> (deterministic) biçimde bir aileye düşer — rastgele değil, her koşuda aynı.
/// Böylece bugünkü 70 ekran için değil, ileride eklenecek her modül/menü/ekran için de çalışır.
///
/// <b>Neden aile sayısı AZ (6+1).</b> 24 üst menüye 24 ayrı renk vermek menüyü gökkuşağına çevirir;
/// kurumsal arayüz araştırması da bunun tersini söylüyor (soluk başlıklar, düşük görsel gürültü).
/// Aile <b>ÜST GRUPTAN</b> gelir: kullanıcı "Operasyon" bloğundaki tüm menüleri tek bakışta bir
/// arada görür. Kardeş menüler aynı aileyi paylaşır; onları birbirinden ayıran şey <b>simge ve
/// ad</b>dır — renk gruplama ipucudur, kimlik değil.
///
/// <b>⚠️ Renk TEK BAŞINA anlam taşımaz</b> (kullanıcı şartı ve erişilebilirlik gereği): aynı bilgiyi
/// simge, girinti, tipografi ve seçili durumu da taşır. Renk körlüğünde menü yine tam okunur.
///
/// <b>⚠️ Yetkiyle İLİŞKİSİ YOKTUR.</b> Bu sınıf hiçbir erişim kararı vermez ve
/// <see cref="AccessControl"/>'a dokunmaz. Renk, görünürlüğü belirlemez; görünürlük yetkiden gelir.
/// </summary>
public static class MenuPalette
{
    // ═══════════ AİLELER ═══════════
    // Anahtarlar platform-bağımsız STRING'dir: Application katmanı ne Avalonia'ya ne MudBlazor'a
    // bağımlı olur (MenuIcons ile aynı ilke).

    public const string Stock      = "stock";        // Malzeme ve Stok
    public const string Operations = "operations";   // Operasyon (araç, bakım, yakıt, iş emri…)
    public const string Finance    = "finance";      // Finans / ön muhasebe
    public const string Reports    = "reports";      // Raporlar
    public const string Corporate  = "corporate";    // Kurumsal yönetim (şube, personel, kullanıcı)
    public const string System     = "system";       // Sistem yönetimi (web yönetimi, yedek, çöp)
    public const string Neutral    = "neutral";      // Uyarılar gibi kesitsel öğeler

    /// <summary>Belirlenimci dağıtımda kullanılan aileler. <see cref="Neutral"/> BİLEREK dışarıdadır:
    /// nötr, "aile bulunamadı" anlamı taşır ve yeni menülere kendiliğinden verilmez.</summary>
    private static readonly string[] DagitilabilirAileler =
        { Stock, Operations, Finance, Reports, Corporate, System };

    /// <summary>Tüm aileler — platform çeviricilerinin eksiksizliği testle ölçülebilsin diye.</summary>
    public static IReadOnlyList<string> AllFamilies { get; } =
        DagitilabilirAileler.Concat(new[] { Neutral }).ToArray();

    // ═══════════ HİYERARŞİ SEVİYESİ ═══════════

    /// <summary>
    /// Menüdeki seviye. Renk AİLESİ hiyerarşiden gelir; <b>TON</b> bu seviyeden.
    /// Böylece "üst menü mü, ekran mı" sorusu renk yoğunluğuyla da yanıtlanır.
    /// </summary>
    public enum Level
    {
        /// <summary>ÜST GRUP (section) — en güçlü ton.</summary>
        Section = 0,
        /// <summary>ÜST MENÜ (group) — orta ton.</summary>
        Group = 1,
        /// <summary>EKRAN (screen) — en yumuşak ton.</summary>
        Screen = 2,
    }

    // ═══════════ ÜST GRUP → AİLE ═══════════
    private static readonly Dictionary<string, string> BySection = new(StringComparer.Ordinal)
    {
        ["Malzeme ve Stok"]  = Stock,
        ["Operasyon"]        = Operations,
        ["Finans"]           = Finance,
        ["Raporlar"]         = Reports,
        ["Kurumsal Yönetim"] = Corporate,
        ["Sistem Yönetimi"]  = System,
    };

    // ═══════════ ÜST GRUBU OLMAYAN ÜST MENÜLER ═══════════
    // Katalogda üç üst menü hiçbir üst gruba bağlı değildir (menünün en üstünde tek başına dururlar).
    // Miras alacakları bir üst seviye olmadığı için aileleri BURADA verilir.
    private static readonly Dictionary<string, string> BySectionlessGroup = new(StringComparer.Ordinal)
    {
        ["Uyarılar"] = Neutral,      // kesitsel: her modülden uyarı toplar, tek bir modüle ait değil
        ["Talepler"] = Operations,   // talep akışı operasyonun parçası
        ["Ayarlar"]  = System,       // sistem yönetimiyle aynı aile
    };

    // ═══════════ ÇÖZÜMLEYİCİLER ═══════════

    /// <summary>Üst grup (section) başlığı → aile.</summary>
    public static string ForSection(string? sectionTitle)
    {
        if (string.IsNullOrWhiteSpace(sectionTitle)) return Neutral;
        return BySection.TryGetValue(sectionTitle, out var aile) ? aile : Belirlenimci(sectionTitle);
    }

    /// <summary>
    /// Üst menü (group) başlığı → aile. <b>Miras:</b> menü kendi ailesini taşımaz, bağlı olduğu
    /// ÜST GRUBUN ailesini alır. Üst grubu yoksa yukarıdaki küçük eşlemeden, o da yoksa
    /// belirlenimci dağıtımdan gelir — yani sonuç ASLA boş olmaz.
    /// </summary>
    public static string ForGroup(string? groupTitle)
    {
        if (string.IsNullOrWhiteSpace(groupTitle)) return Neutral;

        var grup = AppScreens.Groups.FirstOrDefault(g =>
            string.Equals(g.Title, groupTitle, StringComparison.Ordinal));

        if (grup?.Section is { Length: > 0 } bolumAnahtari)
        {
            var bolum = AppScreens.Sections.FirstOrDefault(s =>
                string.Equals(s.Key, bolumAnahtari, StringComparison.Ordinal));
            if (bolum is not null) return ForSection(bolum.Title);
        }

        if (BySectionlessGroup.TryGetValue(groupTitle, out var acik)) return acik;
        return Belirlenimci(groupTitle);
    }

    /// <summary>
    /// ⭐ Ekran anahtarı → aile. <b>Ekranın kendi rengi YOKTUR</b>: ait olduğu üst menünün ailesini
    /// miras alır. Katalogda olmayan bir anahtar da grubu üzerinden çözülemezse nötr alır.
    ///
    /// Kalıcı sonuç: <c>AppScreens.All</c>'a yeni bir ekran satırı eklemek renk için YETERLİDİR.
    /// </summary>
    public static string ForScreen(string? screenKey)
    {
        if (string.IsNullOrWhiteSpace(screenKey)) return Neutral;
        var ekran = AppScreens.All.FirstOrDefault(s =>
            string.Equals(s.Key, screenKey, StringComparison.Ordinal));
        return ekran is null ? Neutral : ForGroup(ekran.Group);
    }

    /// <summary>
    /// Eşlemede karşılığı olmayan başlık için <b>belirlenimci</b> aile.
    ///
    /// Neden rastgele değil: aynı menü her açılışta aynı rengi almalı; aksi hâlde kullanıcı
    /// renkten hiçbir şey öğrenemez. Neden <see cref="string.GetHashCode()"/> DEĞİL: .NET'te bu
    /// değer süreçten sürece değişir (hash randomization) — menü renkleri uygulamayı her
    /// açtığında kayar ve nedeni bulunamaz. Bu yüzden karakter toplamı kullanılır: basit,
    /// sürümden sürüme sabit ve platformlar arası aynı.
    /// </summary>
    private static string Belirlenimci(string baslik)
    {
        unchecked
        {
            uint toplam = 2166136261;
            foreach (var ch in baslik)
            {
                toplam ^= ch;
                toplam *= 16777619;   // FNV-1a: kısa metinlerde iyi dağılır, tamamen belirlenimci
            }
            return DagitilabilirAileler[(int)(toplam % (uint)DagitilabilirAileler.Length)];
        }
    }

    /// <summary>
    /// Platformların kullandığı token adı: <c>menu-&lt;aile&gt;-&lt;seviye&gt;</c>.
    /// Tek biçimde üretilir ki masaüstü ile web isim üretiminde ayrışamasın.
    /// </summary>
    public static string TokenName(string family, Level level)
        => "menu-" + family + "-" + level switch
        {
            Level.Section => "section",
            Level.Group => "group",
            _ => "screen",
        };
}
