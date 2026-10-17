namespace DepoWise.Infrastructure.Sync;

/// <summary>
/// ═══ M-80 (AR-GE 2026-10-17, B1 adım 4) — SUNUCU "VERİ DÖNEMİ" DEĞİŞİMİ (yedekten geri dönüş) ═══
///
/// <b>Sorun (05 F-176).</b> Sunucu veritabanı yedekten geri yüklenirse, yedekten SONRA masaüstlerinden gönderilmiş kayıtlar
/// sunucudan gider; ama masaüstünün gönderme imleci ileride kaldığı için o kayıtlar bir daha GÖNDERİLMEZ → felakette
/// masaüstündeki günlerin verisi kaybolur. Çekme imleci de yeni sunucuda anlamsızdır.
///
/// <b>Tespit (iki bağımsız işaret).</b> (1) Dönem kimliği: sunucu her çekme yanıtında veritabanının ve <c>companies</c>
/// tablosunun iç kimliklerinden oluşan bir <c>epoch</c> verir; dökümden geri yükleme / yeni sunucu bu kimlikleri değiştirir.
/// (2) Ufuk geriledi: ufuk normalde hiç azalmaz; bir an önceki noktaya geri sarma (PITR) onu istemcinin imlecinin gerisine
/// düşürür. Herhangi biri → masaüstü TAM yeniden gönderir (kimlik + operation_id sayesinde tekrar zararsız) ve TAM çeker.
/// </summary>
public static class SunucuDonemi
{
    public enum Karar
    {
        /// <summary>Normal: paketi uygula, imleci ilerlet.</summary>
        Devam,
        /// <summary>İlk kez görülen dönem: kaydet, normal devam.</summary>
        IlkKayit,
        /// <summary>Sunucu değişti/geri sarıldı: bu paketi uygulama (delta anlamsız), tam gönder + tam çek.</summary>
        SunucuDegisti,
    }

    /// <param name="eskiEpoch">Bu makinenin son gördüğü dönem (yoksa boş).</param>
    /// <param name="yeniEpoch">Yanıttaki dönem (eski sunucuda boş).</param>
    /// <param name="after">İstekte gönderilen imleç (0 = tam çekme).</param>
    /// <param name="nextAfter">Yanıttaki yeni ufuk.</param>
    public static Karar Degerlendir(string? eskiEpoch, string? yeniEpoch, long after, long nextAfter)
    {
        var eski = eskiEpoch ?? ""; var yeni = yeniEpoch ?? "";
        if (eski.Length > 0 && yeni.Length > 0 && !string.Equals(eski, yeni, StringComparison.Ordinal)) return Karar.SunucuDegisti;
        if (after > 0 && nextAfter < after) return Karar.SunucuDegisti;   // ufuk asla azalmaz → geri sarma
        if (eski.Length == 0 && yeni.Length > 0) return Karar.IlkKayit;
        return Karar.Devam;
    }
}
