using System;
using QRCoder;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// ═══ BAR-01 (ADR-177) — QR ETİKETİ ÜRETİCİSİ ═══
///
/// SALT-OKUNUR ve DURUMSUZ: veritabanına dokunmaz, hiçbir kayıt/senkron/audit satırı üretmez,
/// dosya saklamaz — yalnız verilen metni PNG'ye çevirir. QR içeriği DAİMA kaydın MEVCUT benzersiz
/// kodudur (PK-O4/madde 7): URL/JSON/metadata/firma-şube-fiyat bilgisi QR'a GİRMEZ; böylece USB
/// okuyucu, telefon kamerası ve global arama kutusu aynı düz metin değeri üzerinde çalışır.
/// Masaüstü doğrudan çağırır (çevrimdışı üretim); web <c>GET /api/qr/{entity}/{id}</c> ucundan alır.
/// </summary>
public static class QrLabelService
{
    /// <summary>Metni PNG QR görüntüsüne çevirir (ECC M). Boş metin → ArgumentException.</summary>
    public static byte[] Png(string code, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("QR içeriği boş olamaz.");
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(code.Trim(), QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    /// <summary>Koddan güvenli dosya adı ("QR_&lt;kod&gt;.png") — dosya sisteminde geçersiz karakterler _'ya döner.</summary>
    public static string FileName(string code)
        => "QR_" + System.Text.RegularExpressions.Regex.Replace(code.Trim(), @"[^\p{L}\p{Nd}\-_.]", "_") + ".png";
}
