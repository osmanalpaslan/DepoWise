using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DepoWise.Application.Files;

namespace DepoWise.Desktop;

/// <summary>
/// Fotoğraf ekleme sırasında BİÇİM doğrulaması (2026-07-25, kullanıcı isteği). Sunucu kaydederken zaten
/// magic-byte ile doğruluyordu (<see cref="FileValidation"/>, yalnız JPEG/PNG) ama dosya seçici daha geniş
/// uzantılara izin verdiğinden (webp/bmp/…) kullanıcı seçince sessizce/şifreli hata alıyordu. Bu yardımcı,
/// seçilen her dosyayı EKLENIRKEN aynı kurala göre kontrol eder; uygun olmayanlar için desteklenen biçimleri
/// listeleyen bir uyarı gösterir ve yalnız GEÇERLİ dosyaları forma ekletir (Malzemeler + Araçlar ortak).
/// </summary>
public static class PhotoPickHelper
{
    /// <summary>Desteklenen biçimler (kullanıcıya gösterilen metin) — FileValidation.DetectImage ile birebir.</summary>
    public const string SupportedFormatsText = "JPEG (.jpg, .jpeg), PNG (.png)";

    /// <summary>Seçilen yolları doğrular; geçersiz olanlar için tek bir uyarı penceresi gösterir (dosya adlarıyla).
    /// Dönüş: yalnız GEÇERLİ (magic-byte + boyut uyumlu) yollar.</summary>
    public static async Task<IReadOnlyList<string>> ValidateAndWarnAsync(IReadOnlyList<string> pickedPaths)
    {
        var valid = new List<string>();
        var invalid = new List<(string Name, string Reason)>();
        foreach (var path in pickedPaths)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var result = FileValidation.ValidateImage(Path.GetFileName(path), null, bytes);
                if (result.Ok) valid.Add(path);
                else invalid.Add((Path.GetFileName(path), result.Error ?? "Geçersiz dosya."));
            }
            catch (System.Exception ex) { invalid.Add((Path.GetFileName(path), ex.Message)); }
        }

        if (invalid.Count > 0)
        {
            var list = string.Join("\n", invalid.ConvertAll(x => $"• {x.Name} — {x.Reason}"));
            await ConfirmService.AskAsync(
                $"Aşağıdaki dosyalar desteklenmeyen biçimde olduğu için eklenmedi:\n\n{list}\n\n" +
                $"Desteklenen biçimler: {SupportedFormatsText}",
                "Fotoğraf Biçimi Desteklenmiyor", "Tamam", "");
        }
        return valid;
    }
}
