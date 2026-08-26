namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ FORM / LİSTE YÜKSEKLİK PAYLAŞIMI ═══ (denetim 2026-08-26 · MAS-03)
///
/// <b>ÇÖZDÜĞÜ GERÇEK SORUN.</b> Masaüstü "Malzeme Giriş-Çıkış" ekranının kök yerleşimi
/// <c>RowDefinitions="Auto,Auto,*,Auto"</c> idi: form <b>Auto</b> satırındaydı ve daima kendi
/// <i>istediği</i> boyu alıyordu. Bu formda 44 alan + 130 px arama paneli + 180 px sepet +
/// 44 px not kutusu var → istenen boy ~700 px. Pencere 947 px iken listeye (<c>*</c> satırı)
/// yalnız <b>artan</b> kalıyordu: ~50 px. Yani kayıtlar <b>geliyordu</b> ama görülemiyordu
/// (ekran altındaki "N hareket" sayacı dolu olduğu hâlde tablo bir şerit hâlindeydi).
///
/// <b>NEDEN SABİT PİKSEL DEĞİL.</b> "Formun boyunu 420 px'e sabitle" türü bir çözüm iki yönde de
/// kırılırdı: küçük ekranda (768 px) satırlar taşar, büyük ekranda (1440 px) form gereksiz yere
/// kırpılıp boşuna kaydırma çubuğu çıkardı. Bunun yerine form, <b>kapsayıcının yüksekliğinin
/// bir oranıyla</b> sınırlanır; kalan alan listeye gider. Böylece pencere büyüyünce tablo da büyür,
/// küçülünce form kendi içinde kaydırılır ve tablo görünür kalır.
///
/// <b>NEDEN BURADA (Application).</b> Avalonia arayüzü bu projede otomatize edilemiyor. Karar
/// mantığı saf bir fonksiyon olarak burada durursa <b>gerçek sayılarla</b> test edilebilir;
/// masaüstü tarafında yalnız ince bir <c>IValueConverter</c> kabuğu kalır. Aynı yaklaşım
/// <see cref="DepoWise.Application.Common.BaglantiIzleyici"/> ile web'de de kullanıldı.
/// </summary>
public static class FormListeOrani
{
    /// <summary>Formun kapsayıcıdan alabileceği en büyük pay. Kalan alan listeye gider.</summary>
    public const double VarsayilanOran = 0.55;

    /// <summary>
    /// Liste için her koşulda ayrılan taban yükseklik (px). Kabaca başlık + 4 satır eder;
    /// "tablo var ama kullanılamıyor" durumuna geri düşmeyi imkânsız kılar.
    /// </summary>
    public const double ListeTabanYukseklik = 180;

    /// <summary>
    /// Kapsayıcı yüksekliğine göre forma verilecek ÜST SINIR.
    ///
    /// <para>Ölçü henüz bilinmiyorsa (ilk yerleşim turunda <c>Bounds.Height</c> 0'dır) veya değer
    /// anlamsızsa <see cref="double.PositiveInfinity"/> döner — yani <b>sınırlama uygulanmaz</b>.
    /// Bu bilinçlidir: sınır koyamadığımız durumda formu 0 px'e ezip ekranı boş bırakmaktansa
    /// eski davranışa düşmek güvenlidir.</para>
    /// </summary>
    public static double FormUstSiniri(double kapsayiciYuksekligi, double oran = VarsayilanOran)
    {
        if (double.IsNaN(kapsayiciYuksekligi) || double.IsInfinity(kapsayiciYuksekligi)) return double.PositiveInfinity;
        if (kapsayiciYuksekligi <= 0) return double.PositiveInfinity;
        if (double.IsNaN(oran) || oran <= 0 || oran >= 1) return double.PositiveInfinity;

        var sinir = kapsayiciYuksekligi * oran;

        // Liste tabanını her hâlükârda koru: çok kısa pencerede oran hesabı listeyi ezmesin.
        var listeyeKalan = kapsayiciYuksekligi - sinir;
        if (listeyeKalan < ListeTabanYukseklik)
            sinir = kapsayiciYuksekligi - ListeTabanYukseklik;

        // Pencere listenin tabanından bile kısaysa forma sınır koymak anlamsız (her şey taşar);
        // eski davranışa dön, kullanıcı pencereyi büyütsün.
        return sinir <= 0 ? double.PositiveInfinity : sinir;
    }
}
