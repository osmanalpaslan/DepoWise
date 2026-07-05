using SkiaSharp;

namespace DepoWise.Infrastructure.Files;

/// <summary>
/// Fotoğraf optimizasyonu (SkiaSharp — ücretsiz; ImageSharp lisans maliyeti yok). En uzun kenar MaxDim'i aşarsa
/// küçültür ve JPEG (kalite Q) olarak yeniden kodlar. Çözemezse/optimizasyon işe yaramazsa ORİJİNALİ döndürür
/// (yükleme asla bozulmaz). Fly Linux'ta NativeAssets.Linux.NoDependencies ile çalışır (fontconfig gerektirmez).
/// </summary>
public static class ImageOptimizer
{
    public const int MaxDim = 1600;   // en uzun kenar (px)
    public const int Quality = 82;    // JPEG kalitesi

    /// <summary>Optimize eder; başarısızsa (mime, bytes) = orijinal. mime her zaman geçerli döner.</summary>
    public static (string Mime, byte[] Bytes) Optimize(byte[] input, string fallbackMime)
    {
        try
        {
            using var original = SKBitmap.Decode(input);
            if (original is null) return (fallbackMime, input); // çözülemedi → dokunma

            int w = original.Width, h = original.Height;
            double scale = (double)MaxDim / Math.Max(w, h);

            SKBitmap resized;
            bool ownResized = false;
            if (scale < 1.0)
            {
                int nw = Math.Max(1, (int)Math.Round(w * scale));
                int nh = Math.Max(1, (int)Math.Round(h * scale));
                resized = original.Resize(new SKImageInfo(nw, nh), SKFilterQuality.High);
                ownResized = true;
                if (resized is null) return (fallbackMime, input);
            }
            else
            {
                resized = original; // zaten küçük — yalnız yeniden JPEG kodla (boyut düşerse kullan)
            }

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, Quality);
            if (ownResized) resized.Dispose();
            if (data is null) return (fallbackMime, input);

            var bytes = data.ToArray();
            // Yeniden kodlama orijinalden büyük olduysa (zaten iyi sıkıştırılmış) orijinali koru.
            if (bytes.Length >= input.Length && scale >= 1.0) return (fallbackMime, input);
            return ("image/jpeg", bytes);
        }
        catch
        {
            return (fallbackMime, input); // herhangi bir hata → orijinal (yükleme bozulmasın)
        }
    }
}
