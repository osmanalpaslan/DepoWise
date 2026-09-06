using DepoWise.Infrastructure.Update;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ İNDİRİLEN GÜNCELLEME PAKETİ DİSKTE SAKLANIR (kullanıcı bildirimi 2026-09-07) ═══
///
/// <para><b>Kullanıcı:</b> "her login oluşumda 'Güncelleme indiriliyor (sürüm X)…' karşıma
/// çıkıyor." Paket yalnız BELLEKTE tutulduğu için, kullanıcı "Ertele" dediğinde ya da uygulamayı
/// kapattığında 86 MB bir sonraki girişte YENİDEN iniyordu — üstelik indirme ana pencere
/// açılmadan önce yapıldığı için her seferinde bekleniyordu.</para>
///
/// <para>Artık paket diske yazılır ve sonraki girişte <b>checksum doğrulanarak</b> yeniden
/// kullanılır. Bu testler doğrulama sözleşmesini korur: bozuk/yarım dosya ASLA kuruluma
/// girmemelidir — güncelleme paketi uygulamanın kendisini değiştirir.</para>
/// </summary>
public class GuncellemeOnbellegiTests
{
    private static byte[] Paket(string icerik) => System.Text.Encoding.UTF8.GetBytes(icerik);

    private static string Ozet(byte[] b)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));

    [Fact]
    public void DogruPaket_Checksumu_Gecer()
    {
        var p = Paket("alpnex-paket-icerigi");
        Assert.True(UpdateService.VerifyChecksum(p, Ozet(p)));
        Assert.True(UpdateService.VerifyChecksum(p, Ozet(p).ToLowerInvariant()));   // büyük/küçük harf farkı sorun olmamalı
    }

    [Fact]
    public void BozukPaket_Checksumu_Gecmez()
    {
        var dogru = Paket("alpnex-paket-icerigi");
        var bozuk = Paket("alpnex-paket-icerigX");            // tek karakter değişti
        Assert.False(UpdateService.VerifyChecksum(bozuk, Ozet(dogru)));

        var yarim = dogru[..(dogru.Length - 3)];              // yarım inmiş dosya
        Assert.False(UpdateService.VerifyChecksum(yarim, Ozet(dogru)));
    }

    /// <summary>Checksum yoksa kurulum YAPILMAZ — imzasız paket kabul edilemez (UPD-01).</summary>
    [Fact]
    public void ChecksumYoksa_Kurulum_Reddedilir()
    {
        var p = Paket("alpnex");
        Assert.Throws<UpdateFailedException>(() => UpdateService.RequireVerifiedPackage(p, null));
        Assert.Throws<UpdateFailedException>(() => UpdateService.RequireVerifiedPackage(p, "   "));
        Assert.Throws<UpdateFailedException>(() => UpdateService.RequireVerifiedPackage(p, Ozet(Paket("baska"))));

        // Doğru checksum ile istisna ATILMAZ.
        UpdateService.RequireVerifiedPackage(p, Ozet(p));
    }

    /// <summary>Önbellek sözleşmesi: okuma checksum doğrular, yazma eski paketleri temizler.</summary>
    [Fact]
    public void OnbellekSozlesmesi_Kaynakta_Korunur()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        var s = File.ReadAllText(Path.Combine(d!.FullName, "src", "DepoWise.Desktop", "AutoUpdateService.cs"));

        Assert.Contains("OnbellektenOku", s);
        Assert.Contains("OnbellegeYaz", s);
        Assert.Contains("VerifyChecksum", s);      // okurken DAİMA doğrulanır
        Assert.Contains("File.Delete(yol)", s);    // bozuk dosya saklanmaz
        Assert.Contains("*.pkg", s);               // eski sürümlerin paketleri temizlenir
    }
}
