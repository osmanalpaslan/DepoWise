namespace DepoWise.Application.Notifications;

/// <summary>
/// Uygulamanın çıkarabileceği sesler. Dosya adları bu adlardan türetilir (<see cref="DosyaAdi"/>),
/// böylece web ve masaüstü AYNI dosyayı çalar ve iki ortamda ses farkı oluşamaz.
/// </summary>
public enum SesTuru
{
    /// <summary>Buton uyarı/onay penceresi açıldı (Sil, Düzenle, İptal, Bilgi…).</summary>
    DugmeUyarisi,
    /// <summary>Sohbette YENİ bir mesaj geldi (karşı taraftan).</summary>
    MesajGelen,
    /// <summary>Sohbette mesaj gönderildi (kendi eylemim).</summary>
    MesajGiden,
    /// <summary>Uyarı ve duyurular (üst bardaki çan sayacı arttı).</summary>
    Bildirim,
}

/// <summary>Ses dosyası adları — TEK KAYNAK.</summary>
public static class SesDosyalari
{
    public static string DosyaAdi(SesTuru tur) => tur switch
    {
        SesTuru.DugmeUyarisi => "dugme-uyari",
        SesTuru.MesajGelen => "mesaj-gelen",
        SesTuru.MesajGiden => "mesaj-giden",
        SesTuru.Bildirim => "bildirim",
        _ => "bildirim",
    };

    /// <summary>Tüm ses adları (paket/derleme testleri bunu kullanır).</summary>
    public static readonly IReadOnlyList<string> Hepsi = new[]
    {
        "dugme-uyari", "mesaj-gelen", "mesaj-giden", "bildirim",
    };
}

/// <summary>
/// ═══ UYARI / DUYURU SESİNİN KARAR MANTIĞI (kullanıcı isteği 2026-09-07) ═══
///
/// <para><b>Kullanıcının şartı:</b> <i>"uyarı ve duyurular için eğer birden fazla veri aynı zamanda
/// geliyorsa sesi 1 kere çal. kısaca ses ard arda spamlanmasın. yoğun gelen veri aralığı çok kısaysa
/// tek ses uygula. spam olayının önüne geçilmesi durumu SADECE uyarılar (buton uyarıları hariç) ve
/// duyurular için olacak."</i></para>
///
/// <para><b>Bu yüzden bu sınıf YALNIZ uyarı/duyuru sesini yönetir.</b> Buton uyarı pencereleri ve
/// sohbet sesleri buradan GEÇMEZ — kullanıcı ikisinin karıştırılmamasını açıkça istedi. Buton
/// uyarısı zaten kullanıcının kendi tıklamasıyla ve teker teker çıkar; onu bastırmak yanlış olurdu.</para>
///
/// <para><b>İki ayrı karar var, karıştırılmamalı:</b>
/// <list type="number">
///   <item><b>Yeni bir şey var mı?</b> Okunmamış sayısı en son GÖRÜLEN sayıdan büyükse yenidir.
///     Sayı düşerse (kullanıcı okuduysa) yeni "taban" o olur — sonraki tek uyarı yine ses çıkarır.
///     Taban kalıcı saklandığı için <b>bir önceki oturumdan sonra gelenler</b> ilk girişte duyulur.</item>
///   <item><b>Ses çalınabilir mi?</b> Son çalmanın üzerinden <see cref="AsgariAralikMs"/> geçmediyse
///     ÇALINMAZ. Beş uyarı arka arkaya damlasa bile kullanıcı TEK ses duyar.</item>
/// </list></para>
///
/// <para><b>Saf sınıftır:</b> ses çalmaz, dosya okumaz, saat sormaz — zaman dışarıdan verilir.
/// Bu sayede davranışı doğrudan test edilebilir (bkz. <c>BildirimSesiTests</c>).</para>
/// </summary>
public sealed class BildirimSesiKarari
{
    /// <summary>İki uyarı sesi arasındaki en kısa süre. Kullanıcının "spamlanmasın" şartının sayısal karşılığı.</summary>
    public const long AsgariAralikMs = 8_000;

    private int _sonGorulenSayi;
    /// <summary>Son çalma anı. <b>null = hiç çalmadı.</b> Sentinel olarak <c>long.MinValue</c>
    /// KULLANILMAZ: "şimdi − sentinel" çıkarması taşar ve kalkanı rastgele davrandırır.</summary>
    private long? _sonCalmaMs;
    private bool _tabanKuruldu;

    /// <summary>En son görülen okunmamış sayısı (kalıcı saklanır — oturumlar arası taban).</summary>
    public int SonGorulenSayi => _sonGorulenSayi;

    /// <summary>
    /// Önceki oturumdan devralınan tabanı yükler. <paramref name="sayi"/> null ise
    /// (bu makinede ilk çalışma) taban kurulmamış sayılır ve ilk okuma SESSİZ olur —
    /// uygulamayı ilk kez açan birine "birikmiş 40 uyarı" sesi çalmak anlamsızdır.
    /// </summary>
    public void TabanYukle(int? sayi)
    {
        if (sayi is { } s && s >= 0) { _sonGorulenSayi = s; _tabanKuruldu = true; }
        else { _sonGorulenSayi = 0; _tabanKuruldu = false; }
    }

    /// <summary>
    /// Yeni okunmamış sayısı geldi. <b>true</b> dönerse ses ÇALINMALI.
    /// </summary>
    /// <param name="okunmamis">Sunucudan gelen güncel okunmamış uyarı/duyuru sayısı.</param>
    /// <param name="simdiMs">Şimdiki zaman (Unix ms).</param>
    public bool Geldi(int okunmamis, long simdiMs)
    {
        if (okunmamis < 0) okunmamis = 0;

        // İlk okuma ve taban yoksa: yalnız tabanı kur, ses çalma.
        if (!_tabanKuruldu)
        {
            _tabanKuruldu = true;
            _sonGorulenSayi = okunmamis;
            return false;
        }

        var artti = okunmamis > _sonGorulenSayi;
        _sonGorulenSayi = okunmamis;          // düşüş de dâhil: taban HER ZAMAN güncellenir
        if (!artti) return false;

        // Spam kalkanı: kısa aralıkta gelen yoğunlukta TEK ses.
        // BASTIRILAN ses zamanı İLERLETMEZ (aksi hâlde yoğun trafikte uygulama sonsuza kadar
        // sessiz kalırdı) — bu yüzden atama yalnız gerçekten çaldığımızda yapılır.
        if (_sonCalmaMs is { } son && simdiMs - son < AsgariAralikMs) return false;

        _sonCalmaMs = simdiMs;
        return true;
    }
}
