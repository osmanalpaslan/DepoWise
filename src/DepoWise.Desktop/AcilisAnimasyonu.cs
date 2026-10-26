using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace DepoWise.Desktop;

/// <summary>
/// ⭐ 2026-10-11 — AÇILIŞ ANİMASYONU (kullanıcının hazırladığı şeffaf arka planlı video, 10 sn).
///
/// <para><b>Neden video değil kare paketi:</b> Avalonia'da yerleşik video oynatıcı yok; video kütüphanesi (LibVLC vb.)
/// paketi onlarca MB büyütür. Video 854×480, 24 kare/sn WebP karelerine çevrildi (şeffaflık korunur) ve
/// <c>Assets/acilis-animasyonu.zip</c> içinde gömülü (~9,5 MB, sıkıştırılmış kareler bellekte; her kare gösterilirken
/// çözülür → bellek düşük). Kaynak: <c>Desktop\Yeni logo\açılış animasyonu</c> (ffmpeg ile, bkz. dönüşüm notu).</para>
///
/// <para><b>Davranış:</b> uygulama açılınca, giriş penceresinden ÖNCE ekranın ortasında çerçevesiz ve ŞEFFAF pencerede
/// bir kez oynar; tıklama ya da herhangi bir tuş animasyonu GEÇER. Kareler yüklenemezse ya da bir hata olursa animasyon
/// sessizce atlanır ve giriş ekranı açılır — açılış ASLA engellenmez.</para>
/// </summary>
public sealed class AcilisAnimasyonu : Window
{
    public const int Genislik = 854, Yukseklik = 480, KareHizi = 24;
    private readonly List<byte[]> _kareler;
    private readonly WriteableBitmap _tampon;
    private readonly Image _resim;
    private readonly DispatcherTimer _zaman;
    private readonly Action _bitince;
    private int _sira;
    private bool _bitti;

    private AcilisAnimasyonu(List<byte[]> kareler, Action bitince)
    {
        _kareler = kareler;
        _bitince = bitince;
        Title = "Alpnex";
        Width = Genislik; Height = Yukseklik;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DepoWise.Desktop/Assets/app-gorev.ico"))); } catch { }
        _tampon = new WriteableBitmap(new PixelSize(Genislik, Yukseklik), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        _resim = new Image { Source = _tampon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(_resim, BitmapInterpolationMode.HighQuality);
        Content = _resim;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Geçmek için tıklayın");

        _zaman = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / KareHizi) };
        _zaman.Tick += (_, _) => SonrakiKare();
        PointerPressed += (_, _) => Bitir();
        KeyDown += (_, _) => Bitir();
        Opened += (_, _) => { SonrakiKare(); _zaman.Start(); };
    }

    /// <summary>Animasyonu gösterir; bittiğinde (ya da geçilince / yüklenemezse) <paramref name="bitince"/> çağrılır.
    /// Dönüş: gösterilen pencere (masaüstü ömrünün MainWindow'u olarak atanmalı) ya da null (animasyon yok).</summary>
    public static Window? Goster(Action bitince)
    {
        try
        {
            var kareler = KareleriYukle();
            if (kareler.Count == 0) return null;
            var w = new AcilisAnimasyonu(kareler, bitince);
            w.Show();
            return w;
        }
        catch { return null; }
    }

    /// <summary>Gömülü paketteki kareler (sıralı, sıkıştırılmış WebP baytları). Test edilebilir.</summary>
    public static List<byte[]> KareleriYukle()
    {
        using var akis = AssetLoader.Open(new Uri("avares://DepoWise.Desktop/Assets/acilis-animasyonu.zip"));
        using var zip = new ZipArchive(akis, ZipArchiveMode.Read);
        var liste = new List<byte[]>();
        foreach (var e in zip.Entries.Where(x => x.Name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            using var s = e.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            liste.Add(m.ToArray());
        }
        return liste;
    }

    /// <summary>Tek kareyi doğrudan pencere tamponuna çözer (Skia — Avalonia'nın kendi çizim motoru).</summary>
    public static bool KareCoz(byte[] kare, WriteableBitmap hedef)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(kare));
        if (codec is null) return false;
        using var fb = hedef.Lock();
        var bilgi = new SKImageInfo(fb.Size.Width, fb.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.Info.Width != bilgi.Width || codec.Info.Height != bilgi.Height) return false;
        var sonuc = codec.GetPixels(bilgi, fb.Address, fb.RowBytes, new SKCodecOptions());
        return sonuc is SKCodecResult.Success or SKCodecResult.IncompleteInput;
    }

    private void SonrakiKare()
    {
        if (_bitti) return;
        if (_sira >= _kareler.Count) { Bitir(); return; }
        try
        {
            if (!KareCoz(_kareler[_sira++], _tampon)) { Bitir(); return; }
            _resim.InvalidateVisual();
        }
        catch { Bitir(); }
    }

    private void Bitir()
    {
        if (_bitti) return;
        _bitti = true;
        _zaman.Stop();
        _kareler.Clear();
        try { _bitince(); } catch { }
        // Pencereyi çağıran kapatır (ShowLogin eski MainWindow'u kapatır); kapatılmadıysa burada kapat.
        Dispatcher.UIThread.Post(() => { try { if (IsVisible) Close(); } catch { } });
    }
}
