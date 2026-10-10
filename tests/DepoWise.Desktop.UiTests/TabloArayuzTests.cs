using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DepoWise.Application.Ui;
using DepoWise.Desktop.Controls;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.UiTests;

/// <summary>
/// ⭐ 2026-10-10 — TABLO ARAYÜZ TESTLERİ (görünmez ekranda GERÇEK fare hareketiyle).
/// UIT1: rapor tablosunda sütun kenarı sürüklenince sütun genişler ve bırakınca kaydedilir
///       (2026-10-04'te kaynak taramasıyla doğrulanmıştı; ekranda doğrulanamamıştı).
/// UIT2: rapor tablosu başlık yazısı hücrenin ortasında.
/// UIT3: liste tablolarının ortak başlık stili yazıyı ortalar.
/// </summary>
public class TabloArayuzTests
{
    private static (Window Pencere, DataGridView Tablo, GridController Veri) RaporTablosu()
    {
        var g = new GridController();
        g.SetData(
            new[] { new ListColumn("arac", "Araç"), new ListColumn("tutar", "Tutar", IsNumeric: true) },
            new[]
            {
                (IReadOnlyList<GridCell>)new[] { new GridCell("KAM-01"), new GridCell("100", 100) },
                new[] { new GridCell("KAM-02"), new GridCell("250", 250) },
            });
        var tablo = new DataGridView { DataContext = g };
        var w = new Window { Width = 1000, Height = 500, Content = tablo };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, tablo, g);
    }

    [AvaloniaFact]
    public void UIT1_Rapor_Tablosunda_Sutun_Suruklenince_Genisler_Ve_Kaydedilir()
    {
        var (w, tablo, g) = RaporTablosu();
        IReadOnlyDictionary<string, int>? kaydedilen = null;
        g.PersistWidths = d => kaydedilen = d;

        var tutamak = tablo.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ColGrip"));
        var kolon = Assert.IsType<GridColumnVm>(tutamak.DataContext);
        var once = kolon.Width;

        var bas = tutamak.TranslatePoint(new Point(tutamak.Bounds.Width / 2, tutamak.Bounds.Height / 2), w)!.Value;
        w.MouseDown(bas, MouseButton.Left);
        w.MouseMove(bas + new Point(60, 0));
        w.MouseMove(bas + new Point(120, 0));
        w.MouseUp(bas + new Point(120, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(kolon.Width, once + 110, once + 130);          // ~120 px genişledi
        Assert.NotNull(kaydedilen);                                    // bırakınca kişisel tercihe yazıldı
        Assert.Equal((int)Math.Round(kolon.Width), kaydedilen![kolon.Key]);

        // Daraltma da çalışır (alt sınır 50 px).
        bas = tutamak.TranslatePoint(new Point(tutamak.Bounds.Width / 2, tutamak.Bounds.Height / 2), w)!.Value;
        w.MouseDown(bas, MouseButton.Left);
        w.MouseMove(bas + new Point(-1000, 0));
        w.MouseUp(bas + new Point(-1000, 0), MouseButton.Left);
        Assert.Equal(50, kolon.Width);
    }

    [AvaloniaFact]
    public void UIT2_Rapor_Tablosu_Basligi_Hucrenin_Ortasinda()
    {
        var (w, tablo, _) = RaporTablosu();
        var baslik = tablo.GetVisualDescendants().OfType<SelectableTextBlock>().First(t => t.Text == "Araç");
        var dugme = baslik.FindAncestorOfType<Button>()!;
        Assert.Equal(HorizontalAlignment.Center, dugme.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, dugme.VerticalContentAlignment);

        // Yazının (sıralama oku yığınıyla birlikte) yatay merkezi, düğmenin merkezine yakın olmalı.
        var yigin = baslik.GetVisualParent<StackPanel>()!;
        var yiginMerkez = yigin.TranslatePoint(new Point(yigin.Bounds.Width / 2, yigin.Bounds.Height / 2), dugme)!.Value;
        Assert.InRange(yiginMerkez.X, dugme.Bounds.Width / 2 - 3, dugme.Bounds.Width / 2 + 3);
        Assert.InRange(yiginMerkez.Y, dugme.Bounds.Height / 2 - 3, dugme.Bounds.Height / 2 + 3);
        w.Close();
    }

    [AvaloniaFact]
    public void UIT3_Liste_Tablosu_Ortak_Baslik_Stili_Ortalar()
    {
        var yazi = new SelectableTextBlock { Text = "Plaka", Width = 220 };
        var baslik = new Border { Classes = { "TableHeader" }, Child = new Grid { Children = { yazi } } };
        var w = new Window { Width = 400, Height = 200, Content = baslik };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(TextAlignment.Center, yazi.TextAlignment);
        Assert.Equal(VerticalAlignment.Center, yazi.VerticalAlignment);
        w.Close();
    }
}
