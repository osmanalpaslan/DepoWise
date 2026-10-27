using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DepoWise.Application.Update;

/// <summary>
/// ═══ M-01 (K-20, 2026-10-27) — GÜNCELLEME PAKETİ İMZASI ═══
///
/// <b>Kapatılan risk (04 G4-1, F-030):</b> paket bütünlüğü yalnız SHA-256 ile kontrol ediliyordu; özeti de paketi de
/// SUNUCU veriyor. Süper admin şifresini ele geçiren biri sahte paket + ona uyan özet yükleyip TÜM masaüstlerine kod
/// çalıştırabilirdi. Artık paket, yayın tarafında tutulan GİZLİ anahtarla imzalanır (ECDSA P-256 / SHA-256);
/// masaüstü, kurulum aracı ve sunucu imzayı programa GÖMÜLÜ açık anahtarla doğrular. Sunucuyu ele geçirmek imza üretmeye yetmez.
///
/// <b>İmzalanan metin</b> sürümü ve özeti birlikte bağlar (<see cref="ImzaMetni"/>): eski ama gerçek bir paket, yeni sürüm
/// etiketiyle yeniden yayınlanamaz (sürüm düşürme saldırısı).
///
/// <b>Gizli anahtarın yeri:</b> yayın bilgisayarı + kullanıcı onayıyla (2026-10-27) özel GitHub yedek deposu. Tek başına yetmez: sahte
/// paketi sunucuya yüklemek için ayrıca süper admin şifresi + iki adımlı giriş kodu gerekir.
///
/// <b>Anahtar değişimi:</b> yeni açık anahtar <see cref="Anahtarlar"/> listesine EKLENİR; eski anahtar, tüm masaüstleri yeni
/// listeyi taşıyan sürüme geçene kadar listede kalır. Gizli anahtar: <c>scripts/paket_imza_anahtari_olustur.mjs</c>.
/// </summary>
public static class PaketImzasi
{
    /// <summary>Geçerli imza açık anahtarları (SPKI DER, base64). Üretim anahtarı 2026-10-27.</summary>
    public static readonly IReadOnlyList<string> Anahtarlar = new[]
    {
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE3nl4qoNWMkF3oXBmJGFiPOiDV2Rf51uv8G4+Lhu6ipzBQ7eTISxIFmBGItJeufJg3VXxAL+JIkTc+/tktOBsTQ==",
    };

    /// <summary>YALNIZ test derlemesi ekler (InternalsVisibleTo): test anahtarıyla imzalı paketler test sunucusunda yayınlanabilsin.
    /// Üretim kodu bu listeye yazmaz; ortam değişkeniyle doldurulamaz (dışarıdan anahtar sokulamasın).</summary>
    internal static readonly List<string> EkAnahtarlar = new();

    /// <summary>Kurulum aracı (AlpnexSetup.exe) imzasında sürüm yerine kullanılan sabit etiket.</summary>
    public const string KurulumAraciEtiketi = "kurulum-araci";

    /// <summary>İmzalanan metin: biçim etiketi + sürüm + özet (büyük harf hex). Node imzalayıcı AYNISINI üretir.</summary>
    public static string ImzaMetni(string surum, string sha256Hex)
        => $"alpnex-paket-v1\n{surum.Trim()}\n{sha256Hex.Trim().ToUpperInvariant()}";

    /// <summary>İmza, sürüm+özet için geçerli mi? İmza yoksa/bozuksa/hiçbir anahtar tutmuyorsa false (fail-closed).</summary>
    public static bool Dogrula(string? surum, string? sha256Hex, string? imzaBase64)
    {
        if (string.IsNullOrWhiteSpace(surum) || string.IsNullOrWhiteSpace(sha256Hex) || string.IsNullOrWhiteSpace(imzaBase64))
            return false;
        byte[] imza;
        try { imza = Convert.FromBase64String(imzaBase64.Trim()); }
        catch (FormatException) { return false; }
        var veri = Encoding.UTF8.GetBytes(ImzaMetni(surum, sha256Hex));
        foreach (var anahtar in Anahtarlar.Concat(EkAnahtarlar))
        {
            try
            {
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(anahtar), out _);
                if (ec.VerifyData(veri, imza, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) return true;
            }
            catch (CryptographicException) { /* bozuk imza/anahtar → sıradaki anahtar */ }
        }
        return false;
    }

    /// <summary>Kullanıcıya gösterilecek ret metni (masaüstü, kurulum aracı, sunucu aynı cümle).</summary>
    public const string RetMetni =
        "Güncelleme paketinin imzası doğrulanamadı. Paket resmi yayın değil ya da bozulmuş; güvenlik gereği kurulmadı.";
}
