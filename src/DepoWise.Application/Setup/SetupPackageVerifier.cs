using System;
using System.IO;
using System.Security.Cryptography;

namespace DepoWise.Application.Setup;

/// <summary>Paket doğrulaması başarısız → kurulum YOK. Mesajı kullanıcıya gösterilebilir.</summary>
public sealed class SetupVerificationException : Exception
{
    /// <summary>Geliştirici logu için kısa kod (kullanıcı ekranında "Ayrıntılar" altında gösterilir).</summary>
    public string Code { get; }

    public SetupVerificationException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// ═══ KURULUM PAKETİ BÜTÜNLÜK KAPISI — FAIL-CLOSED (2026-09-04) ═══
///
/// <b>Kapatılan açık:</b> kurulum aracı (<c>AlpnexSetup.exe</c>) sunucudan indirdiği ~86 MB'lık zip'i
/// HİÇ doğrulamıyordu — sunucu SHA-256'yı veriyor ve yayında 64 hane hex olarak ZORUNLU kılıyor
/// (<c>ReleaseService.Publish</c>), ama kurulum aracı bu alanı okumuyordu bile. Yani "indirilen ne ise
/// onu aç ve çalıştır" davranışı: yarım/bozuk indirme ya da araya giren bir aktör doğrudan kod
/// çalıştırma yoluna dönüşebiliyordu.
///
/// Bu, uygulama içi güncelleyicide <b>2026-08-26 denetiminde bilinçli olarak kapatılan</b> açığın
/// (UPD-01, <c>UpdateService.RequireVerifiedPackage</c>) kurulum tarafındaki eşidir: aynı üründe bir
/// kapı kilitliydi, diğeri açıktı. Buradaki kural da AYNIDIR ve bilerek aynı sözcüklerle yazılmıştır:
/// <b>checksum yoksa "doğrulama yok" değil, "kurulum yok" demektir.</b>
///
/// <b>Mevcut yayınları bozmaz:</b> sunucu 64 hane hex'i zaten zorunlu kıldığı için yayındaki her
/// sürümün geçerli bir checksum'ı vardır (doğrulandı).
///
/// <b>Akış boyunca dosya belleğe ALINMAZ</b> — 86 MB'lık paket disk üzerinden akış (stream) ile
/// özetlenir; kurulum aracı düşük bellekli makinelerde de çalışır.
/// </summary>
public static class SetupPackageVerifier
{
    /// <summary>SHA-256 hex uzunluğu (32 bayt × 2).</summary>
    public const int ChecksumHexLength = 64;

    /// <summary>Beklenen checksum biçimsel olarak geçerli mi? (tam 64 hane, yalnız hex)</summary>
    public static bool IsValidChecksumFormat(string? checksum)
    {
        if (checksum is null || checksum.Length != ChecksumHexLength) return false;
        foreach (var c in checksum)
        {
            var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }

    /// <summary>Akıştan SHA-256 hesaplar (büyük harf hex). Akış baştan okunur ve TÜKETİLİR.</summary>
    public static string ComputeSha256(Stream stream)
    {
        if (stream.CanSeek) stream.Position = 0;
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    /// <summary>Dosyadan SHA-256 hesaplar (akış; dosya belleğe alınmaz).</summary>
    public static string ComputeFileSha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: false);
        return ComputeSha256(fs);
    }

    /// <summary>
    /// İndirilen paketi doğrular. Başarısızlıkta <see cref="SetupVerificationException"/> atar ve
    /// <b>dosyayı siler</b> (yarım/bozuk paket diskte kalıp sonra yanlışlıkla kullanılmasın).
    ///
    /// Sıra bilinçlidir: önce ucuz kontroller (dosya var mı, boyut), sonra pahalı olan (SHA-256).
    /// Böylece yarım inen 86 MB'lık dosya için gereksiz özet hesaplanmaz.
    /// </summary>
    /// <param name="filePath">İndirilmiş paket dosyası.</param>
    /// <param name="expectedSha256">Sunucunun bildirdiği checksum. Boş/geçersiz → KURULUM YOK.</param>
    /// <param name="expectedSizeBytes">Sunucunun bildirdiği boyut. 0/negatif ise boyut kontrolü atlanır.</param>
    public static void RequireVerifiedPackage(string filePath, string? expectedSha256, long expectedSizeBytes)
    {
        void Reddet(string code, string message)
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { /* silinemezse de kurulum yok */ }
            throw new SetupVerificationException(code, message);
        }

        if (!File.Exists(filePath))
            Reddet("DOSYA_YOK", "İndirilen kurulum dosyası bulunamadı. Kurulum iptal edildi.");

        // ⭐ FAIL-CLOSED: checksum bildirilmemişse ya da biçimi bozuksa kurulum YAPILMAZ.
        if (!IsValidChecksumFormat(expectedSha256))
            Reddet("CHECKSUM_YOK",
                "Sunucu paket imzasını (checksum) bildirmedi ya da geçersiz bildirdi. " +
                "Güvenlik gereği kurulum iptal edildi.");

        if (expectedSizeBytes > 0)
        {
            var actualSize = new FileInfo(filePath).Length;
            if (actualSize != expectedSizeBytes)
                Reddet("BOYUT_UYUSMADI",
                    "İndirilen dosya eksik ya da bozuk (beklenen boyutta değil). " +
                    "Dosya güvenlik nedeniyle kurulmadı.");
        }

        var actual = ComputeFileSha256(filePath);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            Reddet("CHECKSUM_UYUSMADI",
                "İndirilen dosyanın doğrulaması başarısız oldu. " +
                "Dosya güvenlik nedeniyle kurulmadı.");
    }
}
