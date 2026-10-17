using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Desktop.Views;

/// <summary>Listedeki tek kart (ekran modeli).</summary>
public sealed class GonderilemeyenSatiri
{
    public string Anahtar { get; init; } = "";
    public string Baslik { get; init; } = "";
    public string Zaman { get; init; } = "";
    public string Neden { get; init; } = "";
    public string Ipucu { get; init; } = "";
    public bool YereldeVar { get; init; }
    public bool YenidenGonderilebilir { get; init; }
    public bool KodBildirimi { get; init; }
    public ICommand? YenidenGonderCommand { get; init; }
    public ICommand? YoksayCommand { get; init; }

    /// <summary>Kart modelini kayıttan üretir.</summary>
    public static GonderilemeyenSatiri Olustur(GonderilemeyenKayit k, ICommand? yeniden = null, ICommand? yoksay = null)
    {
        // Metinler ortak katmanda (GonderilemeyenKayit) üretilir → test edilebilir.
        return new GonderilemeyenSatiri
        {
            Anahtar = k.Anahtar,
            Baslik = k.Baslik,
            Zaman = k.ZamanMetni,
            Neden = k.NedenMetni,
            Ipucu = k.Ipucu,
            YereldeVar = k.YereldeVar,
            YenidenGonderilebilir = k.YenidenGonderilebilir,
            KodBildirimi = k.KodBildirimi,
            YenidenGonderCommand = yeniden,
            YoksayCommand = yoksay,
        };
    }
}

/// <summary>
/// ⭐ M-15 (AR-GE 2026-10-17) — GÖNDERİLEMEYEN KAYITLAR penceresi (yalnız masaüstü: web doğrudan sunucuya yazdığı için
/// "gönderilemeyen" kaydı yoktur). Veri yerel <c>sync_rejections</c> tablosundan okunur (<see cref="GonderimRedleriService"/>);
/// çevrimdışıyken de açılır.
/// </summary>
public partial class GonderilemeyenlerWindow : Window
{
    private readonly string _companyId = "";

    public GonderilemeyenlerWindow() => AvaloniaXamlLoader.Load(this);

    private GonderilemeyenlerWindow(string companyId) : this()
    {
        _companyId = companyId;
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();
        this.FindControl<Button>("RefreshBtn")!.Click += (_, _) => Yukle();
    }

    public static async Task GosterAsync(string companyId)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null) return;
        await Olustur(companyId).ShowDialog(d.MainWindow);
    }

    /// <summary>Pencereyi kurar ve listeyi yükler (gösterme çağırana ait — arayüz testleri de bunu kullanır).</summary>
    public static GonderilemeyenlerWindow Olustur(string companyId)
    {
        var win = new GonderilemeyenlerWindow(companyId);
        win.Yukle();
        return win;
    }

    private void Yukle()
    {
        var durum = this.FindControl<SelectableTextBlock>("StatusText")!;
        var liste = this.FindControl<ItemsControl>("List")!;
        var svc = new GonderimRedleriService(DesktopServices.Factory);
        var yeniden = new RelayCommand<GonderilemeyenSatiri>(s =>
        {
            if (s is null) return;
            var ok = svc.YenidenGonder(_companyId, s.Anahtar);
            durum.Text = ok ? "Kayıt yeniden gönderilmek üzere işaretlendi; birkaç saniye içinde gönderilir." : "Kayıt bu bilgisayarda bulunamadı.";
        });
        var yoksay = new RelayCommand<GonderilemeyenSatiri>(s =>
        {
            if (s is null) return;
            svc.Yoksay(_companyId, s.Anahtar);
            Yukle();
        });
        var satirlar = new List<GonderilemeyenSatiri>();
        try
        {
            foreach (var k in svc.Acik(_companyId)) satirlar.Add(GonderilemeyenSatiri.Olustur(k, yeniden, yoksay));
            var red = System.Linq.Enumerable.Count(satirlar, x => !x.KodBildirimi);
            durum.Text = satirlar.Count == 0 ? "✓ Gönderilemeyen kayıt yok — her şey sunucuya ulaştı."
                : red == satirlar.Count ? $"{red} kayıt gönderilemedi."
                : $"{red} kayıt gönderilemedi · {satirlar.Count - red} kaydın kodu değiştirildi (bilgi).";
        }
        catch (Exception ex) { durum.Text = "Liste okunamadı: " + ex.Message; }
        liste.ItemsSource = satirlar;
    }
}
