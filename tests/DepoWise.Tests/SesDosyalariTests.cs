using DepoWise.Application.Notifications;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SES DOSYALARI — NÖBETÇİ (kullanıcı isteği 2026-09-07) ═══
///
/// <para>Ses çalmaz; <b>dosyaların gerçekten var ve geçerli olduğunu</b> doğrular. Sessiz kalan
/// bir bildirim, kullanıcı açısından "özellik yok" demektir ve derleme bunu yakalamaz.</para>
///
/// <para>Korunan noktalar:
/// <list type="number">
///   <item>Her <see cref="SesTuru"/> için dosya <b>hem web hem masaüstünde</b> var (biri unutulursa
///     iki ortam ayrışır — CLAUDE.md §4 işlevsel eşitlik).</item>
///   <item>Dosyalar <b>geçerli PCM WAV</b>: RIFF/WAVE başlığı, mono, 16-bit. Bozuk bir dosya
///     Windows'ta sessizce hiç çalmaz.</item>
///   <item>Ses <b>gerçekten dolu</b> (sessiz dosya değil) ve <b>kısa</b> — ofis uygulamasında uzun
///     ses rahatsız eder.</item>
///   <item>İki ortamdaki dosyalar <b>BAYT BAYT AYNI</b> — web'de bir sesi güncelleyip masaüstünü
///     unutmak imkânsız olsun.</item>
/// </list></para>
/// </summary>
public class SesDosyalariTests
{
    private const string WebKlasor = "src/DepoWise.Web/wwwroot/sounds";
    private const string MasaustuKlasor = "src/DepoWise.Desktop/Assets/Sounds";

    /// <summary>Depo kökünü bulur (test ikilisi bin/Debug altında koşar).</summary>
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.True(d is not null, "Depo kökü (DepoWise.sln) bulunamadı.");
        return d!.FullName;
    }

    private static string Yol(string klasor, string ad) => Path.Combine(Kok(), klasor.Replace('/', Path.DirectorySeparatorChar), ad + ".wav");

    public static TheoryData<string> Sesler()
    {
        var d = new TheoryData<string>();
        foreach (var ad in SesDosyalari.Hepsi) d.Add(ad);
        return d;
    }

    [Theory]
    [MemberData(nameof(Sesler))]
    public void SESD1_Her_Ses_Iki_Ortamda_Da_Var(string ad)
    {
        Assert.True(File.Exists(Yol(WebKlasor, ad)), $"WEB'de eksik: {ad}.wav");
        Assert.True(File.Exists(Yol(MasaustuKlasor, ad)), $"MASAÜSTÜNDE eksik: {ad}.wav");
    }

    [Theory]
    [MemberData(nameof(Sesler))]
    public void SESD2_Gecerli_Kisa_ve_Sessiz_Olmayan_PCM_WAV(string ad)
    {
        var b = File.ReadAllBytes(Yol(WebKlasor, ad));
        Assert.True(b.Length > 44, $"{ad}: dosya boş/eksik.");
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(b, 8, 4));

        Assert.Equal(1, BitConverter.ToUInt16(b, 20));          // 1 = PCM (sıkıştırmasız)
        var kanal = BitConverter.ToUInt16(b, 22);
        var oran = BitConverter.ToUInt32(b, 24);
        var bit = BitConverter.ToUInt16(b, 34);
        Assert.Equal(1, kanal);                                  // mono yeter; stereo boşuna yer kaplar
        Assert.Equal(16, bit);
        Assert.True(oran is >= 22050 and <= 48000, $"{ad}: beklenmeyen örnekleme oranı {oran}.");

        var veriByte = BitConverter.ToUInt32(b, 40);
        var saniye = veriByte / (double)(oran * kanal * (bit / 8));
        Assert.True(saniye > 0.03, $"{ad}: ses fazla kısa ({saniye:0.###} sn) — duyulmaz.");
        Assert.True(saniye < 1.5, $"{ad}: ses fazla uzun ({saniye:0.###} sn) — ofiste rahatsız eder.");

        // Sessiz dosya olmamalı: örneklerin tepe genliği makul aralıkta.
        short tepe = 0;
        for (var i = 44; i < b.Length - 1; i += 2)
        {
            var v = Math.Abs(BitConverter.ToInt16(b, i));
            if (v > tepe) tepe = (short)Math.Min(v, short.MaxValue);
        }
        Assert.True(tepe > 2000, $"{ad}: ses neredeyse sessiz (tepe {tepe}).");
        Assert.True(tepe < 30000, $"{ad}: ses fazla yüksek (tepe {tepe}) — irkiltir.");
    }

    [Theory]
    [MemberData(nameof(Sesler))]
    public void SESD3_Web_ve_Masaustu_Dosyalari_BIREBIR_AYNI(string ad)
        => Assert.Equal(File.ReadAllBytes(Yol(WebKlasor, ad)), File.ReadAllBytes(Yol(MasaustuKlasor, ad)));

    [Fact]
    public void SESD4_Fazladan_Ses_Dosyasi_Yok()
    {
        // Kullanılmayan bir ses dosyası pakete girip kafa karıştırmasın.
        foreach (var klasor in new[] { WebKlasor, MasaustuKlasor })
        {
            var d = Path.Combine(Kok(), klasor.Replace('/', Path.DirectorySeparatorChar));
            var adlar = Directory.GetFiles(d, "*.wav").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x);
            Assert.Equal(SesDosyalari.Hepsi.OrderBy(x => x), adlar);
        }
    }
}
