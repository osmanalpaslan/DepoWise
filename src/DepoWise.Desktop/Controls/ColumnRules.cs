using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ MAS-04 — TABLO SÜTUN AYIRICI ÇİZGİLERİ ═══ (kullanıcı isteği 2026-08-26)
///
/// <b>Ne yapar.</b> Bir <see cref="Grid"/>'in HER kolonunun SAĞ kenarına 1 px'lik dikey bir çizgi koyar.
/// Başlık satırı, filtre satırı ve veri satırı aynı kolon düzenini kullandığı için üçüne de uygulanınca
/// tablo Excel gibi görünür: hangi değerin hangi sütuna ait olduğu tek bakışta anlaşılır.
///
/// <b>Neden ölçüm YAPMAZ (asıl tasarım kararı).</b> Çizgiler kolon sınırlarını <i>hesaplamaz</i>;
/// her çizgi ilgili kolonun İÇİNE, <see cref="HorizontalAlignment.Right"/> ile eklenir. Konumu
/// <see cref="Grid"/>'in kendisi belirler. Sonuç: kullanıcı kolon genişliğini sürüklediğinde,
/// kolon gizlendiğinde ya da tablo yatay kaydırıldığında çizgi <b>kendiliğinden</b> doğru yerde kalır —
/// senkronu bozulabilecek ikinci bir konum kaynağı YOKTUR.
///
/// <b>Gizli kolon.</b> Kolon gizlendiğinde o kolonun genişliği 0'a düşer. Çizgi görünür kalsaydı
/// kolon 1 px olarak durur ve tabloda ince bir kalıntı çizgi görünürdü. Bu yüzden her çizgi, KENDİ
/// kolonundaki diğer hücrelerin görünürlüğünü izler: hücre gizlenirse çizgi de gizlenir.
///
/// <b>Kolon genişliğini ETKİLEMEZ.</b> Çizginin istediği genişlik 1 px'dir; hücreler ise
/// <c>MinWidth = MaxWidth = kolon genişliği</c> ile sabitlenmiştir. <c>Auto</c> kolon daima
/// hücrenin genişliğini alır, çizgi bunu büyütemez.
///
/// <b>Son kolon.</b> Son kolonun sağına çizgi ÇİZİLMEZ — tablonun kendi dış kenarlığı zaten oradadır.
/// </summary>
public static class ColumnRules
{
    /// <summary>Bu <see cref="Grid"/>'in kolonlarına ayırıcı çizgi ekle.</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>("Enabled", typeof(ColumnRules));

    public static void SetEnabled(Grid grid, bool value) => grid.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Grid grid) => grid.GetValue(EnabledProperty);

    /// <summary>Eklenen çizgiler bu işaretle tanınır (tekrar eklenmesin, hücre sayılmasın).</summary>
    private static readonly AttachedProperty<bool> IsRuleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsRule", typeof(ColumnRules));

    static ColumnRules()
    {
        // ⚠️ Çizgiler HEMEN eklenemez: XAML çözümlenirken bu özellik, Grid'in hücreleri daha
        // eklenmeden ayarlanır — o anda kolonlarda hücre görünmez ve hiç çizgi çizilmezdi.
        // Görsel ağaca bağlanma anında hücreler kesinlikle yerindedir.
        EnabledProperty.Changed.AddClassHandler<Grid>((grid, e) =>
        {
            if (e.NewValue is not true) return;
            if (grid.IsAttachedToVisualTree()) { Sirala(grid); return; }
            void Baglandi(object? s, VisualTreeAttachmentEventArgs a)
            {
                grid.AttachedToVisualTree -= Baglandi;
                Sirala(grid);
            }
            grid.AttachedToVisualTree += Baglandi;
        });
    }

    /// <summary>
    /// Çizgileri, o anki yerleşim turu BİTTİKTEN sonra ekler.
    ///
    /// <b>Neden ertelenir.</b> <c>AttachedToVisualTree</c> yerleşim sırasında tetiklenir; tam o anda
    /// <c>Grid.Children</c>'a eklemek "koleksiyon değişti" hatasına yol açabilir. Kuyruğa alınca
    /// ekleme, güvenli bir anda yapılır ve bir sonraki turda çizilir.
    ///
    /// <b>Neden try/catch.</b> Bu özellik tamamen GÖRSELDİR (yalnız ayırıcı çizgi). Beklenmedik bir
    /// durumda çizgi çizilmemesi kabul edilebilir; ama çalışan bir ekranın çökmesi kabul edilemez.
    /// Bu yüzden hata yutulur — <b>iş mantığı burada YOKTUR</b>, gizlenen bir veri/işlem hatası olamaz.
    /// </summary>
    private static void Sirala(Grid grid)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try { Uygula(grid); } catch { /* yalnız süsleme: ekranı asla düşürme */ }
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    private static void Uygula(Grid grid)
    {
        // Şablon (DataTemplate) içindeki grid'ler her satır için yeniden kurulur; çizgi zaten varsa
        // ikinci kez eklenmemeli.
        if (grid.Children.Any(c => c.GetValue(IsRuleProperty))) return;

        var kolonSayisi = grid.ColumnDefinitions.Count;
        if (kolonSayisi < 2) return;

        for (int i = 0; i < kolonSayisi - 1; i++)   // son kolonun sağına çizilmez
        {
            var kolonHucreleri = grid.Children
                .Where(c => !c.GetValue(IsRuleProperty) && Grid.GetColumn(c) == i)
                .ToList();

            // O kolonda hiç hücre yoksa (ör. filtresi olmayan kolon) çizgi de olmamalı — aksi hâlde
            // boş kolon 1 px genişleyip hizayı bozardı.
            if (kolonHucreleri.Count == 0) continue;

            var cizgi = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = grid.TryFindResource("BorderSubtleBrush", out var fircaRes) && fircaRes is IBrush firca
                    ? firca : Brushes.Transparent,
                IsHitTestVisible = false,   // çizgi tıklamayı yutmaz: satır seçimi ve metin seçimi bozulmaz
            };
            cizgi.SetValue(IsRuleProperty, true);
            Grid.SetColumn(cizgi, i);
            grid.Children.Add(cizgi);

            GorunurluguIzle(kolonHucreleri, cizgi);
        }
    }

    /// <summary>Çizgi, kendi kolonundaki hücrelerden en az biri görünürken görünür.</summary>
    private static void GorunurluguIzle(List<Control> hucreler, Border cizgi)
    {
        void Tazele() => cizgi.IsVisible = hucreler.Any(h => h.IsVisible);
        foreach (var h in hucreler)
            h.GetObservable(Visual.IsVisibleProperty).Subscribe(new AnonimGozlemci(_ => Tazele()));
        Tazele();
    }

    private sealed class AnonimGozlemci : System.IObserver<bool>
    {
        private readonly System.Action<bool> _isle;
        public AnonimGozlemci(System.Action<bool> isle) => _isle = isle;
        public void OnCompleted() { }
        public void OnError(System.Exception error) { }
        public void OnNext(bool value) => _isle(value);
    }
}
