using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace DepoWise.Desktop.Theming;

/// <summary>
/// ⭐ TARİH ALANI YER TUTUCULARI TÜRKÇE (görsel QA 2026-09-06).
///
/// <para><b>Sorun.</b> Avalonia'nın <see cref="DatePicker"/> denetimi, tarih SEÇİLİ DEĞİLKEN kutunun
/// üç bölmesine İngilizce yer tutucu yazar: <c>day · month · year</c>. Uygulamanın tamamı Türkçe
/// olduğu için bu, kullanıcıya yabancı bir alan gibi görünür (Günlük Faaliyet, Stok Hareketleri,
/// Maliyet Merkezleri gibi tarih SÜZGECİ olan ekranlarda alan bilerek boş bırakılır → yer tutucu
/// hep görünür).</para>
///
/// <para><b>Neden stil ile çözülmedi.</b> Bu metinleri Avalonia'nın kendi kodu, şablon uygulandıktan
/// sonra <c>TextBlock.Text</c> üzerine YEREL DEĞER olarak yazar. Avalonia'da yerel değer, stil
/// setter'ını yener; bu yüzden <c>Selector="DatePicker /template/ TextBlock#PART_DayTextBlock"</c>
/// biçiminde bir stil hiçbir şey değiştirmez. Çözüm, aynı yerel değeri şablon uygulandıktan ve
/// seçili tarih değiştikten SONRA yeniden yazmaktır.</para>
///
/// <para><b>Kapsam.</b> Sınıf düzeyinde bir olay işleyicisidir; uygulama açılışında BİR KEZ kurulur ve
/// tüm <see cref="DatePicker"/>'lara (43 kullanım, 25 ekran) kendiliğinden uygulanır. Hiçbir görünüm
/// dosyası değişmez, hiçbir stil seçicisi bozulmaz. Şablon parçaları bulunamazsa (Avalonia sürümü
/// değişirse) sessizce hiçbir şey yapmaz — kırılmaz, yalnız eski İngilizce metne döner.</para>
/// </summary>
internal static class TarihYerTutucu
{
    private const string Gun = "gün";
    private const string Ay = "ay";
    private const string Yil = "yıl";

    private static bool _kuruldu;

    /// <summary>Uygulama açılışında bir kez çağrılır.</summary>
    public static void Kur()
    {
        if (_kuruldu) return;
        _kuruldu = true;

        // 1) Şablon uygulandığında: yer tutucular ilk kez yazılır.
        DatePicker.TemplateAppliedEvent.AddClassHandler<DatePicker>((secici, e) => Yaz(secici, e.NameScope));

        // 2) Seçili tarih temizlendiğinde: Avalonia yer tutucuları YENİDEN İngilizce yazar.
        DatePicker.SelectedDateProperty.Changed.AddClassHandler<DatePicker>((secici, _) => Yaz(secici, null));
    }

    private static void Yaz(DatePicker secici, INameScope? kapsam)
    {
        // Tarih seçiliyse bölmelerde gerçek gün/ay/yıl yazar — dokunma.
        if (secici.SelectedDate.HasValue) return;

        Ata(Bul(secici, kapsam, "PART_DayTextBlock"), Gun);
        Ata(Bul(secici, kapsam, "PART_MonthTextBlock"), Ay);
        Ata(Bul(secici, kapsam, "PART_YearTextBlock"), Yil);

        static void Ata(TextBlock? hedef, string metin)
        {
            if (hedef is not null) hedef.Text = metin;
        }
    }

    private static TextBlock? Bul(DatePicker secici, INameScope? kapsam, string ad)
    {
        // Şablon olayında ad kapsamı hazırdır; sonraki çağrılarda denetimin kendi kapsamından aranır.
        var bulunan = kapsam?.Find<TextBlock>(ad);
        if (bulunan is not null) return bulunan;
        return secici.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == ad);
    }
}
