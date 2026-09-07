using DepoWise.Application.Notifications;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SESLİ BİLDİRİM — KARAR MANTIĞI (kullanıcı isteği 2026-09-07) ═══
///
/// <para>Kullanıcının şartları birebir sınanır:
/// <list type="bullet">
///   <item>"birden fazla veri aynı zamanda geliyorsa sesi <b>1 kere</b> çal"</item>
///   <item>"ses ard arda <b>spamlanmasın</b>"</item>
///   <item>"bir önceki <b>login</b>den sonra gelen uyarı ve duyurular için"</item>
///   <item>spam kalkanı <b>yalnız</b> uyarı/duyuru için — buton uyarıları buradan geçmez</item>
/// </list></para>
///
/// <para>Testler davranışı ölçer: gerçek sayaç değerleri ve gerçek zaman damgaları verilir,
/// "ses çalınmalı mı" kararı okunur.</para>
/// </summary>
public class BildirimSesiTests
{
    private const long T0 = 1_700_000_000_000;
    private static BildirimSesiKarari Kurulu(int taban = 0)
    {
        var k = new BildirimSesiKarari();
        k.TabanYukle(taban);
        return k;
    }

    // ── 1) TEMEL: yalnız ARTIŞ ses çıkarır ────────────────────────────────────

    [Fact]
    public void SES1_Sayi_Artinca_Calar()
        => Assert.True(Kurulu(0).Geldi(1, T0));

    [Fact]
    public void SES2_Sayi_Ayni_Kalirsa_Calmaz()
    {
        var k = Kurulu(3);
        Assert.False(k.Geldi(3, T0));
        Assert.False(k.Geldi(3, T0 + 60_000));
    }

    [Fact]
    public void SES3_Sayi_Dusunce_Calmaz_Ama_Taban_Guncellenir()
    {
        var k = Kurulu(5);
        Assert.False(k.Geldi(0, T0));            // kullanıcı hepsini okudu
        Assert.Equal(0, k.SonGorulenSayi);
        // Artık TEK yeni uyarı bile ses çıkarmalı (taban 0'a indi).
        Assert.True(k.Geldi(1, T0 + BildirimSesiKarari.AsgariAralikMs));
    }

    // ── 2) SPAM KALKANI — kullanıcının asıl şartı ─────────────────────────────

    [Fact]
    public void SES4_Ayni_Anda_Gelen_Coklu_Uyari_TEK_Ses()
    {
        // Tek yoklamada 1 → 12'ye çıkması "aynı anda 11 uyarı geldi" demektir: TEK ses.
        var k = Kurulu(1);
        Assert.True(k.Geldi(12, T0));
    }

    [Fact]
    public void SES5_Kisa_Aralikla_Damlayan_Uyarilarda_Ses_SPAMLANMAZ()
    {
        var k = Kurulu(0);
        Assert.True(k.Geldi(1, T0));                    // ilk artış → çalar
        // Yoklama aralığı kısa; uyarılar arka arkaya damlıyor. Hiçbiri çalmamalı.
        Assert.False(k.Geldi(2, T0 + 1_000));
        Assert.False(k.Geldi(3, T0 + 2_500));
        Assert.False(k.Geldi(4, T0 + 5_000));
        Assert.False(k.Geldi(9, T0 + 7_999));           // sınırın 1 ms öncesi
    }

    [Fact]
    public void SES6_Asgari_Aralik_Gecince_Yeniden_Calar()
    {
        var k = Kurulu(0);
        Assert.True(k.Geldi(1, T0));
        Assert.False(k.Geldi(2, T0 + 3_000));
        Assert.True(k.Geldi(3, T0 + BildirimSesiKarari.AsgariAralikMs));   // tam sınır: çalar
    }

    [Fact]
    public void SES7_Bastirilan_Ses_Zamanlayiciyi_ILERLETMEZ()
    {
        // Bastırılan bir bildirim "son çalma" saatini ileri almamalı; aksi hâlde yoğun
        // trafikte ses SONSUZA KADAR bastırılırdı (sessiz uygulama = hata).
        var k = Kurulu(0);
        Assert.True(k.Geldi(1, T0));                     // çaldı (t=0)
        for (var t = 1_000; t < 8_000; t += 1_000) Assert.False(k.Geldi(k.SonGorulenSayi + 1, T0 + t));
        Assert.True(k.Geldi(k.SonGorulenSayi + 1, T0 + 8_000));   // 8 sn dolunca yine çalar
    }

    // ── 3) OTURUMLAR ARASI: "bir önceki login'den sonra gelenler" ─────────────

    [Fact]
    public void SES8_Onceki_Oturumdan_Sonra_Gelenler_Ilk_Girise_Ses_Cikarir()
    {
        // Önceki oturum 2 okunmamışla kapandı; kullanıcı yokken 3 tane daha geldi.
        var k = Kurulu(taban: 2);
        Assert.True(k.Geldi(5, T0));
    }

    [Fact]
    public void SES9_Onceki_Oturumdan_Beri_Yeni_Yoksa_Giriste_SESSIZ()
    {
        var k = Kurulu(taban: 4);
        Assert.False(k.Geldi(4, T0));
    }

    [Fact]
    public void SES10_Bu_Makinede_ILK_Calisma_Sessizdir()
    {
        // Taban yok (null): birikmiş 40 uyarıyla karşılaşan yeni kullanıcıya ses çalmak anlamsız.
        var k = new BildirimSesiKarari();
        k.TabanYukle(null);
        Assert.False(k.Geldi(40, T0));
        Assert.Equal(40, k.SonGorulenSayi);
        Assert.True(k.Geldi(41, T0 + BildirimSesiKarari.AsgariAralikMs));   // bundan SONRASI duyulur
    }

    // ── 4) SAĞLAMLIK ──────────────────────────────────────────────────────────

    [Fact]
    public void SES11_Negatif_Sayi_Sifir_Sayilir()
    {
        var k = Kurulu(0);
        Assert.False(k.Geldi(-5, T0));
        Assert.Equal(0, k.SonGorulenSayi);
    }

    // ── 5) DOSYA ADLARI — iki ortam AYNI sesi çalar ───────────────────────────

    [Fact]
    public void SES12_Her_Ses_Turunun_Tek_ve_Farkli_Dosya_Adi_Var()
    {
        var adlar = Enum.GetValues<SesTuru>().Select(SesDosyalari.DosyaAdi).ToList();
        Assert.Equal(adlar.Count, adlar.Distinct().Count());          // hiçbiri çakışmıyor
        Assert.All(adlar, a => Assert.False(string.IsNullOrWhiteSpace(a)));
        Assert.Equal(adlar.OrderBy(x => x), SesDosyalari.Hepsi.OrderBy(x => x));
    }
}
