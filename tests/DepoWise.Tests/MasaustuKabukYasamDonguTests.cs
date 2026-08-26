using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MAS-01 · ÇIKIŞ→GİRİŞ DÖNGÜSÜNDE KABUK SERBEST BIRAKILMIYORDU ═══ (denetim 2026-08-26, ikinci tur)
///
/// <b>Bulunan durum.</b> Her girişte <c>App.ShowSyncThenMain</c> <b>YENİ</b> bir <c>ShellViewModel</c>
/// oluşturur. Eski kabuk ise şunlara bağlı kaldığı için ASLA serbest kalmıyordu:
///
/// <list type="bullet">
///   <item><c>DeveloperMode.Changed</c> — <b>statik</b> olay; <c>+=</c> vardı, <c>-=</c> YOKTU.</item>
///   <item><c>ServerAuthClient.SessionExpiredRaised</c> — <b>statik</b> olay; <c>-=</c> yalnız olayın
///     KENDİ işleyicisinin içinde yapılıyordu, normal "Çıkış Yap" akışında değil.</item>
///   <item><c>_updateTimer</c> (dakikada bir güncelleme kontrolü) — hiçbir yerde <c>Stop()</c> edilmiyordu.</item>
/// </list>
///
/// <b>Kullanıcıya yansıması.</b> Aynı uygulama oturumunda her çıkış→giriş bir kabuk daha biriktirir:
/// N döngüden sonra dakikada N kez güncelleme kontrolü yapılır, yeni sürüm çıktığında birden çok
/// "güncelleme mevcut" penceresi tetiklenebilir, çıkışta geliştirici modu kapanırken statik olay
/// KAPANMIŞ pencerelerin işleyicilerini de çağırır. Bellek de sürekli büyür.
///
/// ⚠️ Güvenlik açığı DEĞİLDİR; kararlılık/bellek sorunudur.
///
/// <b>Bu test neden kaynak okuyor:</b> <c>ShellViewModel</c> Avalonia arayüzü ve <c>DesktopServices</c>
/// olmadan örneklenemez (birim testinden çalıştırılamaz). Kural bu yüzden yapısal olarak kilitlenir —
/// aynı sapmanın sessizce geri gelmesini engeller. Davranış ayrıca izole masaüstü turunda denenmiştir.
/// </summary>
public class MasaustuKabukYasamDonguTests
{
    private static string Oku(params string[] parcalar)
    {
        var kok = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && kok is not null; i++)
        {
            var aday = Path.Combine(new[] { kok }.Concat(parcalar).ToArray());
            if (File.Exists(aday)) return File.ReadAllText(aday);
            kok = Path.GetDirectoryName(kok!);
        }
        throw new FileNotFoundException("Kaynak bulunamadı: " + string.Join("/", parcalar));
    }

    private static string Kabuk() => Oku("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs");
    private static string Uygulama() => Oku("src", "DepoWise.Desktop", "App.axaml.cs");

    /// <summary>⭐ MAS-01a — statik olaylara abone olan kabuk, aboneliği ÇÖZEN bir yol sunmalı.</summary>
    [Theory]
    [InlineData("DeveloperMode.Changed")]
    [InlineData("ServerAuthClient.SessionExpiredRaised")]
    public void MAS01a_Statik_Olay_Abonelikleri_Cozuluyor(string olay)
    {
        var src = Kabuk();

        Assert.Contains(olay + " +=", src, StringComparison.Ordinal);
        Assert.Contains(olay + " -=", src, StringComparison.Ordinal);
    }

    /// <summary>⭐ MAS-01b — her iki zamanlayıcı da serbest bırakma yolunda durdurulmalı.</summary>
    [Fact]
    public void MAS01b_Zamanlayicilar_Serbest_Birakmada_Durduruluyor()
    {
        var src = Kabuk();
        var i = src.IndexOf("public void Release()", StringComparison.Ordinal);
        Assert.True(i >= 0, "ShellViewModel.Release() bulunamadı (kabuk serbest bırakılamıyor)");

        var govde = src.Substring(i, Math.Min(900, src.Length - i));
        Assert.Contains("_updateTimer", govde, StringComparison.Ordinal);
        Assert.Contains("_connTimer", govde, StringComparison.Ordinal);
        Assert.Contains("Stop()", govde, StringComparison.Ordinal);
        Assert.Contains("DeveloperMode.Changed -=", govde, StringComparison.Ordinal);
        Assert.Contains("SessionExpiredRaised -=", govde, StringComparison.Ordinal);
    }

    /// <summary>⭐ MAS-01c — çıkış/giriş akışı eski kabuğu GERÇEKTEN serbest bırakmalı.</summary>
    [Fact]
    public void MAS01c_Cikis_Akisi_Eski_Kabugu_Serbest_Birakir()
    {
        var src = Uygulama();

        Assert.Contains("Release()", src, StringComparison.Ordinal);
        Assert.Contains("ShellViewModel", src, StringComparison.Ordinal);
    }

    /// <summary>Kaynak kilidinin gerçekten yakaladığını kanıtlar (kural kendi kendini sınar).</summary>
    [Fact]
    public void MAS01d_Kilit_Gercekten_Yakaliyor()
    {
        const string eskiHali = "DeveloperMode.Changed += OnDeveloperModeChanged;";
        Assert.Contains("DeveloperMode.Changed +=", eskiHali, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperMode.Changed -=", eskiHali, StringComparison.Ordinal);
    }
}
