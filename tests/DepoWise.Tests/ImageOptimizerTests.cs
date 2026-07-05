using DepoWise.Infrastructure.Files;
using SkiaSharp;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Foto optimizasyonu (SkiaSharp): büyük görsel küçültülür + JPEG'e sıkıştırılır; küçük/geçersiz dokunulmaz.</summary>
public class ImageOptimizerTests
{
    private static byte[] MakePng(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawCircle(w / 2f, h / 2f, Math.Min(w, h) / 3f, paint);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void BuyukGorsel_Kucultulur_VeJpegOlur()
    {
        var big = MakePng(3000, 2000);
        var (mime, bytes) = ImageOptimizer.Optimize(big, "image/png");

        Assert.Equal("image/jpeg", mime);
        Assert.True(bytes.Length < big.Length, "optimize edilen boyut orijinalden küçük olmalı");
        using var outBmp = SKBitmap.Decode(bytes);
        Assert.NotNull(outBmp);
        Assert.True(Math.Max(outBmp!.Width, outBmp.Height) <= ImageOptimizer.MaxDim);
        Assert.Equal(1600, outBmp.Width); // 3000x2000 → 1600x1067
    }

    [Fact]
    public void GecersizIcerik_Orijinali_Dondurur()
    {
        var junk = new byte[] { 1, 2, 3, 4, 5 };
        var (mime, bytes) = ImageOptimizer.Optimize(junk, "image/png");
        Assert.Equal("image/png", mime);
        Assert.Equal(junk, bytes);
    }
}
