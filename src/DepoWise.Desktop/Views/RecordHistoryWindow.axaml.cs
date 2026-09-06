using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Desktop.ViewModels;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop.Views;

/// <summary>
/// ⭐ FAZ 4.3 (kullanıcı isteği 2026-09-06) — ANLAŞILIR LOG PENCERESİ.
///
/// İki yerde kullanılır:
/// <list type="number">
///   <item><b>Ekran geçmişi</b> — aktif ekranın tüm kayıtları. Her satırdaki "Bu kaydın geçmişi"
///   bağlantısı, o kaydın KENDİ geçmişini açar (kullanıcının istediği "her kaydın kendi log ekranı").</item>
///   <item><b>Kayıt geçmişi</b> — tek bir kaydın tüm yaşam öyküsü; alan farkları burada KESİNDİR,
///   çünkü kaydın tüm geçmişi okunur.</item>
/// </list>
/// Yetki kapısı her iki yolda da SERVİSTEDİR (<see cref="AuditLogService"/>); bu pencere yalnız gösterir.
/// </summary>
public partial class RecordHistoryWindow : Window
{
    private string _duzMetin = "";

    public RecordHistoryWindow() => AvaloniaXamlLoader.Load(this);

    private RecordHistoryWindow(string baslik, string altBaslik, IReadOnlyList<AuditLogRow> satirlar,
        SessionContext? session, bool kayitBaglantisi) : this()
    {
        this.FindControl<TextBlock>("TitleText")!.Text = baslik;
        this.FindControl<TextBlock>("SubText")!.Text = altBaslik;
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();
        this.FindControl<Button>("CopyBtn")!.Click += async (_, _) =>
        {
            var clip = GetTopLevel(this)?.Clipboard;
            if (clip is not null)
            {
                await clip.SetTextAsync(_duzMetin);
                this.FindControl<TextBlock>("CopiedText")!.IsVisible = true;
            }
        };

        // Satırdaki bağlantı: o kaydın kendi geçmişini yeni pencerede açar.
        var komut = session is null ? null : new AsyncRelayCommand<LogSatiri>(async satir =>
        {
            if (satir is null || string.IsNullOrWhiteSpace(satir.EntityId)) return;
            await KayitGecmisiniGosterAsync(session, satir.EntityType, satir.EntityId, satir.Kayit, this);
        });

        var gunler = AuditDisplayBuilder.Gunlere(satirlar, komut, kayitBaglantisi && session is not null);
        this.FindControl<ItemsControl>("DaysList")!.ItemsSource = gunler;

        if (gunler.Count == 0)
        {
            var bos = this.FindControl<TextBlock>("EmptyText")!;
            bos.Text = "Bu seçim için henüz kayıt geçmişi yok.";
            bos.IsVisible = true;
        }
        _duzMetin = DuzMetin(baslik, gunler);
    }

    /// <summary>Kopyalanabilir düz metin (destek/kanıt için) — ekrandakiyle aynı bilgi.</summary>
    private static string DuzMetin(string baslik, List<LogGunu> gunler)
    {
        var sb = new StringBuilder();
        sb.AppendLine(baslik).AppendLine();
        foreach (var g in gunler)
        {
            sb.AppendLine($"═══ {g.Gun}  ({g.Ozet}) ═══");
            foreach (var s in g.Satirlar)
            {
                sb.AppendLine($"  {s.Saat}  ·  {s.Islem}  ·  {s.Kayit}  ·  {s.Kullanici}");
                foreach (var d in s.Degisiklikler) sb.AppendLine("      " + d);
                if (s.NotVar) sb.AppendLine("      " + s.Not);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Aktif EKRANIN kayıt geçmişini gösterir (satırlardan tek kayda inilebilir).</summary>
    public static async System.Threading.Tasks.Task EkranGecmisiniGosterAsync(SessionContext session,
        string modul, string ekranAdi, Window sahip)
    {
        try
        {
            var satirlar = DesktopServices.Audit.ForModule(session, modul, limit: 300);
            var win = new RecordHistoryWindow($"Kayıt Geçmişi — {ekranAdi}",
                "İşlemler güne göre gruplanır; her satırın altında hangi alanın neye döndüğü yazar. " +
                "Gösterilen zaman, kaydın sisteme GİRİLDİĞİ andır (işlem tarihinden bağımsız).",
                satirlar, session, kayitBaglantisi: true);
            await win.ShowDialog(sahip);
        }
        catch (System.Exception ex)
        {
            await ScreenInfoService.ShowAsync("Kayıt Geçmişi", "Geçmiş okunamadı: " + ex.Message);
        }
    }

    /// <summary>TEK KAYDIN kendi log ekranı — kullanıcının "her kaydın kendine ait log ekranı" isteği.</summary>
    public static async System.Threading.Tasks.Task KayitGecmisiniGosterAsync(SessionContext session,
        string entityType, string entityId, string? kayitAdi, Window sahip)
    {
        try
        {
            var satirlar = DesktopServices.Audit.ForEntity(session, entityType, entityId);
            var ad = string.IsNullOrWhiteSpace(kayitAdi)
                ? DepoWise.Application.Common.AuditFields.TipEtiket(entityType) : kayitAdi;
            var win = new RecordHistoryWindow($"Kayıt Geçmişi — {ad}",
                "Bu kaydın tüm geçmişi. Her satırın altında o işlemde hangi alanın neye döndüğü yazar.",
                satirlar, session, kayitBaglantisi: false);
            await win.ShowDialog(sahip);
        }
        catch (System.Exception ex)
        {
            await ScreenInfoService.ShowAsync("Kayıt Geçmişi", "Geçmiş okunamadı: " + ex.Message);
        }
    }

    /// <summary>Ana pencere sahipli kısa yol (ViewModel'lerden çağrılır).</summary>
    public static async System.Threading.Tasks.Task KayitGecmisiniGosterAsync(SessionContext session,
        string entityType, string entityId, string? kayitAdi)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null) return;
        await KayitGecmisiniGosterAsync(session, entityType, entityId, kayitAdi, d.MainWindow);
    }
}
