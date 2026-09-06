using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Reporting;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ LİSTE YAZDIRMA — ekranlar için tek satırlık giriş noktası ═══ (kullanıcı isteği 2026-09-06)
///
/// <para>Her ekran zaten Excel çıktısını ortak <see cref="TableModel"/> ile üretiyor. Bu yardımcı,
/// AYNI modeli PDF'e çevirip kullanıcıya "nereye kaydedeyim" diye sorar. Böylece bir ekranı
/// yazdırılabilir yapmak için tek çağrı yeter ve iki çıktı (Excel/PDF) asla ayrışmaz.</para>
///
/// <para><b>Başlık bilgisi oturumdan gelir</b> — firma, şube ve kullanıcı adı kâğıda otomatik
/// yazılır. Kâğıt elden ele dolaştığı için "bu liste hangi firmanın, hangi şubenin, kim ne zaman
/// almış" bilgisi çıktının üzerinde olmalıdır.</para>
///
/// <para><b>Süzgeçler de yazılır.</b> Filtrelenmiş bir listenin "tam liste" sanılması, yazdırmada
/// en sık yapılan hatadır; bu yüzden uygulanan süzgeçler başlığın altında görünür.</para>
/// </summary>
public static class YazdirmaYardimcisi
{
    /// <summary>
    /// Tabloyu PDF olarak kaydeder. Kullanıcı kaydetme penceresini iptal ederse <c>null</c> döner
    /// (çağıran ekranda hiçbir mesaj göstermez — iptal bir hata değildir).
    /// </summary>
    /// <returns>Kaydedilen dosya yolu, ya da iptal edildiyse <c>null</c>.</returns>
    public static async Task<string?> YazdirAsync(TableModel tablo, SessionContext? oturum = null,
        IReadOnlyList<(string Etiket, string Deger)>? suzgecler = null, string? dosyaAdi = null)
    {
        // Firma/şube/kullanıcı adları KABUKTAN okunur: kabuk bunları açılışta bir kez çözer
        // (firma adı servisten, şube adı oturumdan) ve üst barda zaten gösterir — aynı kaynak kullanılır
        // ki kâğıttaki ile ekrandaki bilgi ayrışmasın. Kabuk yoksa (ilk açılış) alanlar boş geçilir.
        var kabuk = ViewModels.ShellViewModel.Current;
        var baslik = new PdfBaslik(
            CompanyName: kabuk?.ActiveCompanyName,
            BranchName: kabuk?.ActiveBranchName,
            UserName: kabuk?.DisplayName,
            Filters: suzgecler,
            LogoPath: LogoYolu());

        var ad = dosyaAdi ?? (DosyaAdinaUygun(tablo.Title) + ".pdf");
        var hedef = await FilePickerService.SavePdfAsync(ad);
        if (hedef is null) return null;

        var bayt = DesktopServices.TablePdf.Uret(tablo, baslik);
        await File.WriteAllBytesAsync(hedef, bayt);
        return hedef;
    }

    /// <summary>Firma logosu varsa yolu; yoksa <c>null</c> (başlık logosuz çizilir).</summary>
    private static string? LogoYolu()
    {
        try
        {
            // Marka ayarlarındaki logo (Talep Formu PDF'i ile AYNI kaynak — iki çıktı ayrışmasın).
            var yol = DesktopServices.Branding?.LogoPath;
            return string.IsNullOrWhiteSpace(yol) || !File.Exists(yol) ? null : yol;
        }
        catch { return null; }   // logo tamamen süstür: bulunamazsa yazdırma DURMAZ
    }

    /// <summary>Başlıktan dosya adı üretir: Windows'ta yasak karakterler ayıklanır.</summary>
    private static string DosyaAdinaUygun(string baslik)
    {
        var temiz = new System.Text.StringBuilder();
        foreach (var c in baslik)
            temiz.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        var s = temiz.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "Liste" : s;
    }
}
