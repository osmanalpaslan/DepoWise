using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ UIX-01 — "YAZIYA TIKLAYINCA SATIR SEÇİLMİYOR" HATASININ KAPSAM KİLİDİ (2026-09-04) ═══
///
/// <b>Geçmiş:</b> G3 (2026-08-12) kök nedeni çözdü — <c>SelectableTextBlock</c> tıklamayı tüketiyor,
/// bu yüzden <see cref="DepoWise.Desktop.Controls.TableRowSelect"/> olayı TÜNELLEME aşamasında
/// yakalıyor. Davranış ortak <c>ListBox.Table</c> stiline bağlandı.
///
/// <b>Kalan açık (bu testin kapattığı):</b> ortak stili KULLANMAYAN, ama tablo gibi davranan çıplak
/// <c>ListBox</c>'lar düzeltmenin dışında kaldı → o üç ekranda hata <b>hâlâ canlıydı</b>:
/// Bekleyen Onaylar · Ekip Listesi · Ekipman Bakım Kayıtları. Üçünde de seçim FONKSİYONELDİR
/// (<c>SelectedItem</c>'a bağlı) — satır seçilemeyince Onayla/Düzenle/Sil hiçbir şey yapmıyordu.
///
/// <b>Bu test ne yapar:</b> tüm masaüstü ekranlarını tarar; satır şablonunda <c>SelectableTextBlock</c>
/// olan ve <c>SelectedItem</c>'a bağlı HER <c>ListBox</c>, ya ortak <c>Classes="Table"</c> stilini
/// kullanmalı ya da davranışı açıkça bağlamalıdır. Yeni bir ekran aynı hataya düşerse test KIRILIR —
/// yani hata bir daha sessizce geri gelemez.
/// </summary>
public class TabloSatirSecimiKapsamTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static IEnumerable<string> ViewDosyalari()
        => Directory.GetFiles(Path.Combine(RepoKok(), "src", "DepoWise.Desktop"), "*.axaml", SearchOption.AllDirectories);

    /// <summary>Bir <c>&lt;ListBox ...&gt;</c> açılış etiketinin tamamını (çok satırlı olabilir) yakalar.</summary>
    private static readonly Regex ListBoxEtiketi = new(@"<ListBox\b[^>]*>", RegexOptions.Singleline);

    [Fact]
    public void UIX1_Tablo_Gibi_Davranan_Her_Liste_Satir_Secme_Davranisini_Alir()
    {
        var eksik = new List<string>();

        foreach (var dosya in ViewDosyalari())
        {
            var metin = File.ReadAllText(dosya);
            foreach (Match m in ListBoxEtiketi.Matches(metin))
            {
                var etiket = m.Value;

                // Yalnız SEÇİMİ ANLAMLI olan listeler: seçili satır bir şeye bağlıysa, satırın
                // seçilebiliyor olması işlevsel bir gerekliliktir (yalnız görsel bir incelik değil).
                if (!etiket.Contains("SelectedItem", StringComparison.Ordinal)) continue;

                // Ortak tablo stili → davranışı Components.axaml'den zaten alır.
                if (Regex.IsMatch(etiket, @"Classes\s*=\s*""[^""]*\bTable\b")) continue;

                // Ya da davranış açıkça bağlanmış.
                if (etiket.Contains("TableRowSelect.Enabled", StringComparison.Ordinal)) continue;

                // Satır şablonunda SelectableTextBlock var mı? Yoksa tıklama zaten yutulmuyor demektir.
                var kalan = metin[m.Index..];
                var kapanis = kalan.IndexOf("</ListBox>", StringComparison.Ordinal);
                var govde = kapanis > 0 ? kalan[..kapanis] : kalan;
                if (!govde.Contains("SelectableTextBlock", StringComparison.Ordinal)) continue;

                var satir = metin[..m.Index].Count(c => c == '\n') + 1;
                eksik.Add($"{Path.GetFileName(dosya)}:{satir}");
            }
        }

        Assert.True(eksik.Count == 0,
            "Satırındaki YAZIYA tıklanınca seçilmeyecek listeler (SelectableTextBlock tıklamayı yutar). "
            + "Çözüm: ListBox'a Classes=\"Table\" ya da ctrl:TableRowSelect.Enabled=\"True\" ekleyin → "
            + string.Join(", ", eksik));
    }

    /// <summary>Düzeltilen üç ekran açıkça kilitlenir — biri geri alınırsa yukarıdaki genel tarama
    /// yeniden yakalar, ama burada hangi ekranın kaybolduğu ADIYLA görünür.</summary>
    [Theory]
    [InlineData("ApprovalsView.axaml")]
    [InlineData("TeamsView.axaml")]
    [InlineData("MaintenanceView.axaml")]
    public void UIX2_Duzeltilen_Ekranlar_Davranisi_Kaybetmez(string dosyaAdi)
    {
        var yol = Path.Combine(RepoKok(), "src", "DepoWise.Desktop", "Views", dosyaAdi);
        Assert.Contains("ctrl:TableRowSelect.Enabled=\"True\"", File.ReadAllText(yol));
    }
}
