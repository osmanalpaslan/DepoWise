using System.Runtime.CompilerServices;

namespace DepoWise.Tests;

/// <summary>
/// ═══ POSTGRESQL ADRESİNİN SÜREÇ BAŞINDAKİ FOTOĞRAFI (2026-09-07, ölçülerek bulundu) ═══
///
/// <para><b>Bulunan hata:</b> <see cref="ApiTestHost"/>, bellek-içi API'nin PostgreSQL'e bağlanmasını
/// engellemek için <c>DEPOWISE_PG_URL</c>'i <b>SÜREÇ GENELİNDE</b> siliyor
/// (<c>Environment.SetEnvironmentVariable(..., null)</c>) ve geri koymuyor. Bu koruma yerinde ve
/// gereklidir — ama ortam değişkeni tüm süreç için ortaktır: ilk API testi çalıştığı anda
/// <b>bütün PostgreSQL testleri</b> "adres yok" diye ATLANIR.</para>
///
/// <para><b>Ölçüm:</b> tam koşuda 59 PostgreSQL testinden <b>47'si atlandı</b>; geçen 12'si zaten
/// veritabanına hiç bağlanmayan kapı/lehçe testleriydi. Yani PostgreSQL <b>hiçbir tam koşuda
/// gerçekten denenmedi</b> — üstelik rapor "3855 geçti" diyordu.</para>
///
/// <para><b>Bedeli ölçüldü:</b> sohbetin konuşma sorgusu PostgreSQL'de her çağrıda patlıyordu
/// (<c>42P08</c>) ve bu, üretimde sohbeti tamamen kullanılamaz hâle getirmişti. Aynı boşluk Günlük
/// Faaliyet raporunun eskimiş sütun sözleşmesini de üç gün gizledi. Bu, <see cref="TestDbTemizlik"/>
/// ile kapatılan <c>ClearAllPools</c> hatasının AYNI SINIFIDIR: bir testin süreç genelinde yaptığı
/// değişiklik, başka testleri sessizce bozar.</para>
///
/// <para><b>Çözüm:</b> adres, <b>hiçbir test çalışmadan önce</b> (modül yükleme anında) bir kez
/// okunur. PostgreSQL testleri bu fotoğrafı kullanır; <see cref="ApiTestHost"/>'un silmesi onları
/// artık etkilemez ve API'nin PostgreSQL'e bağlanmama koruması aynen sürer.</para>
/// </summary>
internal static class PgTestOrtami
{
    /// <summary>Süreç başlarken görülen <c>DEPOWISE_PG_URL</c>. Sonradan silinse bile DEĞİŞMEZ.</summary>
    public static string? BaslangictakiUrl { get; private set; }

    /// <summary>
    /// Modül yükleyicisi: test derlemesi belleğe alındığında, <b>ilk testten önce</b> çalışır.
    /// Sıralamaya bağlı olmadığı için "hangi test önce koştu" sorusu ortadan kalkar.
    /// </summary>
    [ModuleInitializer]
    internal static void Yakala()
        => BaslangictakiUrl = Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");
}
