using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Kota İzleme "ONLINE" sütunu — sayım KULLANICI bazındadır, oturum/login bazında DEĞİL.
/// Kullanıcının şartı: "aynı kullanıcı hem webten hem masaüstünden login olmuş ise 1 online görünmeli;
/// yani anlık login durumunu değil kullanıcı online durumunu almalı."
/// </summary>
[Collection("presence")]   // statik durum paylaşıldığı için testler sırayla çalışır
public class ServerPresenceTests
{
    private const long T0 = 1_700_000_000_000;

    [Fact]
    public void AyniKullanici_WebVeMasaustunden_Girse_1_Online_Sayilir()
    {
        ServerPresence.ResetForTests();

        // Aynı kullanıcı (U1), aynı firma (A): önce web'den, sonra masaüstünden istek atıyor
        ServerPresence.Touch("U1", "A", T0);            // web
        ServerPresence.Touch("U1", "A", T0 + 1_000);    // masaüstü (aynı kişi)

        Assert.Equal(1, ServerPresence.TotalOnline(T0 + 2_000));                       // 2 DEĞİL, 1
        Assert.Equal(1, ServerPresence.OnlineByCompany(T0 + 2_000)["A"]);              // kota ekranındaki değer
    }

    [Fact]
    public void FarkliKullanicilar_Ayri_Sayilir()
    {
        ServerPresence.ResetForTests();

        ServerPresence.Touch("U1", "A", T0);
        ServerPresence.Touch("U2", "A", T0);
        ServerPresence.Touch("U3", "B", T0);   // başka firma

        var byCompany = ServerPresence.OnlineByCompany(T0 + 1_000);
        Assert.Equal(2, byCompany["A"]);       // A firmasında 2 farklı kişi
        Assert.Equal(1, byCompany["B"]);
        Assert.Equal(3, ServerPresence.TotalOnline(T0 + 1_000));
    }

    [Fact]
    public void Pencere_Disinda_Kalan_Kullanici_Online_Sayilmaz()
    {
        ServerPresence.ResetForTests();

        ServerPresence.Touch("U1", "A", T0);                                   // 5 dk'dan eski olacak
        ServerPresence.Touch("U2", "A", T0 + ServerPresence.WindowMs);         // taze

        var now = T0 + ServerPresence.WindowMs + 1_000;                        // U1 pencereden çıktı
        Assert.Equal(1, ServerPresence.TotalOnline(now));
        Assert.Equal(1, ServerPresence.OnlineByCompany(now)["A"]);
    }

    [Fact]
    public void AyniKullanici_IkiPlatformda_FarkliFirmada_Bile_TekKisi_Sayilir()
    {
        ServerPresence.ResetForTests();

        // Süper admin: web'de A firmasını seçmiş, masaüstünde kendi (B) firmasında
        ServerPresence.Touch("SU", "A", T0);
        ServerPresence.Touch("SU", "B", T0 + 1_000);   // en son B

        // Kişi TEK → toplamda 1; en son istek attığı firmada görünür
        Assert.Equal(1, ServerPresence.TotalOnline(T0 + 2_000));
        var byCompany = ServerPresence.OnlineByCompany(T0 + 2_000);
        Assert.Equal(1, byCompany["B"]);
        Assert.False(byCompany.ContainsKey("A"));      // çift sayım YOK
    }
}
