using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ TABLO BAŞLIĞI ↔ GÖVDE HİZALAMASI (FAZ 3b-6, 2026-09-05) ═══
///
/// <b>Çözdüğü gerçek hata (ölçülerek bulundu).</b> Projedeki tablolar iki AYRI <see cref="Grid"/>
/// kullanır: başlık (<c>Border.TableHeader</c>, <c>DockPanel.Dock="Top"</c>) ve satırlar
/// (<c>ListBox.Table</c> şablonu). İkisi aynı <c>ColumnDefinitions</c> ve aynı
/// <c>MinWidth/MaxWidth</c> değerlerini taşıdığı için normalde hizalıdırlar.
///
/// Ama <c>ListBox.Table</c> stili <c>ScrollViewer.HorizontalScrollBarVisibility="Auto"</c> taşır:
/// kolonların toplamı panele SIĞMADIĞINDA satırlar <b>doğal genişliğini</b> alır ve yatay kayar;
/// başlık ise <c>DockPanel</c>'in dar genişliğine <b>sıkışır</b>. Sonuç: iki grid farklı genişlikte
/// ölçülür, kolon genişlikleri ayrışır ve başlıklar veriyle hizasız kalır (Cari Hesaplar'da
/// ölçülen kayma: 100 px; "KOD" ile "ÜNVAN" üst üste biniyordu).
///
/// <b>Ne yapar.</b> Başlığı gövdeyle AYNI ölçüm ve kaydırma bağlamına sokar:
/// <list type="number">
///   <item><b>Genişlik:</b> başlık içeriğinin <c>MinWidth</c>'i, listenin yatay <c>Extent</c>
///     genişliğine eşitlenir → iki grid AYNI kullanılabilir genişlikte ölçülür → kolonlar birebir
///     aynı çıkar.</item>
///   <item><b>Kaydırma:</b> liste yatay kaydırıldığında başlık aynı miktarda ötelenir
///     (<see cref="TranslateTransform"/>) → kaydırma sonrasında da hizalı kalır.</item>
/// </list>
///
/// <b>Neden ölçüm/hesap yok.</b> Değerler listenin KENDİ <c>ScrollViewer</c>'ından okunur; ikinci
/// bir genişlik kaynağı üretilmez. Pencere yeniden boyutlandığında, kolon gizlendiğinde ya da kayıt
/// sayısı değiştiğinde <c>Extent</c> kendiliğinden güncellenir ve başlık onu izler.
///
/// <b>Gizli kolonla ilişkisi (FAZ 3b-5).</b> Alan yetkisi bir kolonu gizlediğinde hem başlık hem
/// satır hücresi <c>IsVisible=false</c> olur; <c>Auto</c> kolon sıfırlanır. Genişlik yine tek
/// kaynaktan geldiği için başlık ve gövde birlikte daralır — gizleme sonrası kayma OLUŞMAZ.
///
/// <b>Kullanım.</b> Başlık <c>Border</c>'ına:
/// <code>ctrl:TableHeaderSync.Source="{Binding #CariListe}"</code>
/// (<c>#CariListe</c> = satırları taşıyan <c>ListBox</c>'ın <c>x:Name</c>'i.)
///
/// <b>Salt görseldir.</b> İş mantığı yoktur; beklenmedik durumda hizalama yapılmaz ama ekran
/// düşmez (<c>try/catch</c>).
/// </summary>
public static class TableHeaderSync
{
    /// <summary>Başlığın hizalanacağı liste (satırları taşıyan <see cref="ListBox"/>).</summary>
    public static readonly AttachedProperty<Control?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Border, Control?>("Source", typeof(TableHeaderSync));

    public static void SetSource(Border header, Control? value) => header.SetValue(SourceProperty, value);
    public static Control? GetSource(Border header) => header.GetValue(SourceProperty);

    static TableHeaderSync()
    {
        SourceProperty.Changed.AddClassHandler<Border>((header, e) =>
        {
            if (e.NewValue is not Control liste) return;

            // ScrollViewer, ListBox'ın ŞABLONU uygulandıktan sonra oluşur. Görsel ağaca bağlanma
            // anında kesinlikle yerindedir; öncesinde aramak boşuna olurdu.
            if (header.IsAttachedToVisualTree() && liste.IsAttachedToVisualTree()) Bagla(header, liste);
            else
            {
                void Baglandi(object? s, VisualTreeAttachmentEventArgs a)
                {
                    header.AttachedToVisualTree -= Baglandi;
                    Bagla(header, liste);
                }
                header.AttachedToVisualTree += Baglandi;
            }
        });
    }

    private static void Bagla(Border header, Control liste)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try { Uygula(header, liste); } catch { /* yalnız hizalama: ekranı asla düşürme */ }
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    private static void Uygula(Border header, Control liste)
    {
        var sv = liste.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (sv is null || header.Child is not Layoutable icerik) return;

        // Başlık taşan kısmı göstermemeli: öteleme sonrası kenardan taşmasın.
        header.ClipToBounds = true;

        // İçerik kullanılabilir alandan GENİŞ olduğunda Avalonia onu ORTALAR → başlık sola kayar
        // (ölçüldü: 112 px). Sola sabitlemek, satırlarla aynı x'ten başlamasını garanti eder.
        icerik.HorizontalAlignment = HorizontalAlignment.Left;

        var oteleme = new TranslateTransform();
        icerik.RenderTransform = oteleme;

        void Tazele()
        {
            // (1) GENİŞLİK: başlık ızgarası, SATIR ızgarasıyla aynı genişlikte ölçülmelidir.
            //
            //     Extent, listenin toplam içerik genişliğidir ve satır hücrelerinin YATAY BOŞLUĞUNU
            //     da içerir. Başlıkta aynı boşluk Border.Padding olarak durur (Components.axaml:
            //     "Yatay padding 12 DEGISMEZ — baslik / filtre / veri hucrelerinin ayni x ten
            //     baslamasi buna bagli"). Bu yüzden başlığın İÇ genişliği Extent eksi kendi yatay
            //     padding i olmalıdır; aksi hâlde yıldız (*) kolon tam padding kadar (ölçüldü: 24 px)
            //     fazla alır ve sonraki kolonlar kayardı.
            var genislik = IcerikGenisligi(sv.Extent.Width, header.Padding.Left + header.Padding.Right);
            if (genislik > 0) icerik.MinWidth = genislik;

            // (2) KAYDIRMA: liste sağa kaydıkça başlık aynı miktarda sola ötelenir.
            oteleme.X = -sv.Offset.X;
        }

        sv.GetObservable(ScrollViewer.OffsetProperty).Subscribe(new Gozlemci<Vector>(_ => Tazele()));
        sv.GetObservable(ScrollViewer.ExtentProperty).Subscribe(new Gozlemci<Size>(_ => Tazele()));
        Tazele();
    }

    /// <summary>
    /// Başlık içeriğinin genişliği = listenin içerik genişliği − başlığın yatay boşluğu.
    ///
    /// Saf fonksiyon olarak ayrıldı ki KURAL test edilebilsin (görsel ağaç kurmadan). Ölçüm
    /// yerine kural sınanır: boşluk düşülmezse yıldız (*) kolon tam padding kadar fazla alır ve
    /// başlık satırdan kayar — FAZ 3b-6'da gerçek GUI'de 24 px olarak ölçülen hata buydu.
    ///
    /// Extent 0/negatif ise (liste boş, henüz ölçülmedi) 0 döner → çağıran başlığa DOKUNMAZ ve
    /// eski davranış sürer.
    /// </summary>
    internal static double IcerikGenisligi(double extentWidth, double yatayBosluk)
    {
        if (extentWidth <= 0) return 0;
        var g = extentWidth - yatayBosluk;
        return g > 0 ? g : 0;
    }

    private sealed class Gozlemci<T> : IObserver<T>
    {
        private readonly Action<T> _isle;
        public Gozlemci(Action<T> isle) => _isle = isle;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => _isle(value);
    }
}
