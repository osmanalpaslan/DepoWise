using System;
using System.Collections.Generic;
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

    /// <summary>Başlık başına bağlama durumu: aynı başlık iki kez bağlanmasın, şablon yenilenince eski abonelikler bırakılsın.</summary>
    private sealed class Durum
    {
        public ScrollViewer? Sv;
        public readonly List<IDisposable> Abonelikler = new();
        public bool SablonDinleniyor;
        /// <summary>Başlık ızgarasının ÖZGÜN kolon tanımları (liste boşalınca geri yüklenir).</summary>
        public GridLength[]? Ozgun;
        public void Birak() { foreach (var a in Abonelikler) a.Dispose(); Abonelikler.Clear(); Sv = null; }
    }

    private sealed class Eylem : IDisposable
    {
        private Action? _a;
        public Eylem(Action a) => _a = a;
        public void Dispose() { _a?.Invoke(); _a = null; }
    }

    /// <summary>
    /// ⭐ 2026-10-12 SAĞLAMLAŞTIRMA (3) — KOLON GENİŞLİKLERİ SATIRDAN KOPYALANIR.
    /// Aynı genişlik tek başına yetmiyordu (ölçüldü): satırlar yatay kaydırıcıda SINIRSIZ genişlikle ölçülür →
    /// yıldız (*) kolonlar içerik/MinWidth kadar olur (Personel: 180/150); başlık SINIRLI genişlikle ölçülür →
    /// yıldızlar eşit bölünür (165/165) → 15 px kayma. Ayrıca bazı ekranlarda başlık ve satır hücresinin MinWidth
    /// değerleri birbirinden farklı yazılmıştı (Kullanıcılar: 150↔140 → 10 px). Başlık ızgarası artık ilk görünür
    /// satırın GERÇEK kolon genişliklerini alır → tanım farkı ne olursa olsun kolonlar birebir aynı.
    /// Liste boşken / satır bulunamazken başlık ÖZGÜN tanımına döner.
    /// </summary>
    public static void KolonlariEsle(Grid baslik, Grid? satir, ref GridLength[]? ozgun)
    {
        var tanimlar = baslik.ColumnDefinitions;
        ozgun ??= tanimlar.Select(c => c.Width).ToArray();
        if (ozgun.Length != tanimlar.Count) return;   // tanım sonradan değişti → dokunma

        var esle = satir is not null && satir.ColumnDefinitions.Count == tanimlar.Count
                   && satir.ColumnDefinitions.Sum(c => c.ActualWidth) > 0;
        for (int k = 0; k < tanimlar.Count; k++)
        {
            // DOKUNULMAYAN kolonlar (özgün tanım kalır):
            //  • SharedSizeGroup taşıyan kolon: başlık + satırlar zaten AYNI grupla hizalı. Gruptaki bir üyeye sabit piksel
            //    yazmak tüm grubu o değere KİLİTLER (veri kısalınca kolon daralamaz) — iki ortam denetimi bulgusu.
            //  • SON kolon: genişliği hiçbir kolonun BAŞLANGICINI değiştirmez (yalnız sağ boşluğu). Satırda gizlenebilen
            //    son hücre (Stok Hareketleri "DURUM" yalnız ters kayıtta görünür) 0 px'e inince başlık yazısı kaybolmasın.
            var dokunma = k == tanimlar.Count - 1 || !string.IsNullOrEmpty(tanimlar[k].SharedSizeGroup);
            var hedef = esle && !dokunma ? new GridLength(satir!.ColumnDefinitions[k].ActualWidth, GridUnitType.Pixel) : ozgun[k];
            var su = tanimlar[k].Width;
            if (su.GridUnitType != hedef.GridUnitType || Math.Abs(su.Value - hedef.Value) > 0.5)
                tanimlar[k].Width = hedef;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, Durum> Durumlar = new();

    private static void Bagla(Border header, Control liste)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try { Uygula(header, liste); } catch { /* yalnız hizalama: ekranı asla düşürme */ }
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    private static void Uygula(Border header, Control liste)
    {
        var durum = Durumlar.GetValue(header, _ => new Durum());

        // ⭐ 2026-10-12 SAĞLAMLAŞTIRMA (1): liste ŞABLONU sonradan uygulanırsa ya da YENİDEN uygulanırsa
        // (ilk açılışta gizli tablo — IsVisible="{Binding HasDist}" — ölçülmediği için şablonu yoktur;
        // çalışırken tema değişince şablon yeniden kurulur ve eski ScrollViewer ölür) yeniden bağlan.
        // Eskiden ScrollViewer bulunamayınca sessizce vazgeçiliyordu → o tabloda başlık hiç hizalanmıyordu.
        if (!durum.SablonDinleniyor && liste is TemplatedControl tc)
        {
            durum.SablonDinleniyor = true;
            tc.TemplateApplied += (_, _) => Bagla(header, liste);
        }

        var sv = liste.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (sv is null || header.Child is not Layoutable icerik) return;
        if (ReferenceEquals(sv, durum.Sv)) return;   // zaten bu kaydırıcıya bağlı (çift abonelik yok)
        durum.Birak();
        durum.Sv = sv;

        // Başlık taşan kısmı göstermemeli: öteleme sonrası kenardan taşmasın.
        header.ClipToBounds = true;

        // İçerik kullanılabilir alandan GENİŞ olduğunda Avalonia onu ORTALAR → başlık sola kayar
        // (ölçüldü: 112 px). Sola sabitlemek, satırlarla aynı x'ten başlamasını garanti eder.
        icerik.HorizontalAlignment = HorizontalAlignment.Left;

        var oteleme = icerik.RenderTransform as TranslateTransform ?? new TranslateTransform();
        icerik.RenderTransform = oteleme;

        void Tazele()
        {
            var yatayBosluk = header.Padding.Left + header.Padding.Right;
            var satirVar = liste is ItemsControl ic ? ic.ItemCount > 0 : true;

            if (satirVar)
            {
                // (1) GENİŞLİK: başlık ızgarası, SATIR ızgarasıyla aynı genişlikte ölçülmelidir.
                //
                //     Extent, listenin toplam içerik genişliğidir ve satır hücrelerinin YATAY BOŞLUĞUNU
                //     da içerir. Başlıkta aynı boşluk Border.Padding olarak durur (Components.axaml:
                //     "Yatay padding 12 DEGISMEZ — baslik / filtre / veri hucrelerinin ayni x ten
                //     baslamasi buna bagli"). Bu yüzden başlığın İÇ genişliği Extent eksi kendi yatay
                //     padding i olmalıdır; aksi hâlde yıldız (*) kolon tam padding kadar (ölçüldü: 24 px)
                //     fazla alır ve sonraki kolonlar kayardı.
                var genislik = IcerikGenisligi(sv.Extent.Width, yatayBosluk);
                if (genislik > 0) icerik.MinWidth = genislik;
            }
            else
            {
                // ⭐ 2026-10-12 SAĞLAMLAŞTIRMA (2): LİSTE BOŞ → hizalanacak satır yok. Eskiden başlık pencereye
                // sıkışıyor, yıldız (*) kolon sıfıra inip başlıklar ÜST ÜSTE biniyordu (boş Cari/Kasa listesinde
                // "KOD ÜNVAN"). Artık başlık kendi DOĞAL genişliğini alır: sığmayan sağ kolonlar kırpılır ama
                // hiçbir başlık diğerinin üstüne binmez. Satır gelince yukarıdaki kural yeniden devreye girer.
                // Doğal genişlik sınırsız ölçümle bulunur; ölçüm canlı öğede yapıldığı için hemen ardından
                // InvalidateMeasure ile gerçek kısıtla yeniden ölçülmesi istenir (yerleşim tutarlı kalır).
                // Önce özgün kolon tanımları geri gelir (satırdan kopyalanmış eski genişliklerle ölçülmesin).
                if (icerik is Grid bosIzgara) KolonlariEsle(bosIzgara, null, ref durum.Ozgun);
                icerik.MinWidth = 0;
                icerik.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var dogal = icerik.DesiredSize.Width;
                icerik.InvalidateMeasure();
                var alan = header.Bounds.Width - yatayBosluk;
                icerik.MinWidth = dogal > alan && alan > 0 ? dogal : 0;
            }

            // (3) KAYDIRMA: liste sağa kaydıkça başlık aynı miktarda sola ötelenir.
            oteleme.X = satirVar ? -sv.Offset.X : 0;
        }

        // Yerleşim turunun İÇİNDEN gelen bildirimler (Bounds/ItemCount) ertelenerek işlenir: ölçüm, o tur bittikten
        // sonra yapılır (yerleşim sırasında yeniden ölçüm döngüsü riski yok). Aynı turda birden çok istek tek işe iner.
        var bekliyor = false;
        void TazeleErtele()
        {
            if (bekliyor) return;
            bekliyor = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                bekliyor = false;
                try { if (ReferenceEquals(durum.Sv, sv)) Tazele(); } catch { /* yalnız hizalama */ }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        durum.Abonelikler.Add(sv.GetObservable(ScrollViewer.OffsetProperty).Subscribe(new Gozlemci<Vector>(_ => Tazele())));
        durum.Abonelikler.Add(sv.GetObservable(ScrollViewer.ExtentProperty).Subscribe(new Gozlemci<Size>(_ =>
        {
            // Satır varken eski (ölçümsüz, anında) yol; boşken ölçüm gerektiği için ertelenir.
            if (liste is ItemsControl c && c.ItemCount == 0) TazeleErtele(); else Tazele();
        })));
        if (liste is ItemsControl kaynak)
            durum.Abonelikler.Add(kaynak.GetObservable(ItemsControl.ItemCountProperty).Subscribe(new Gozlemci<int>(_ => TazeleErtele())));
        // (4) KOLON GENİŞLİKLERİ: her yerleşim turu BİTTİKTEN sonra (LayoutUpdated) ilk görünür satırın kolonları
        //     başlığa kopyalanır. Değer değişmedikçe hiçbir şey yazılmaz → döngü yok (yazınca bir tur daha, sonra eşit).
        if (header.Child is Grid baslikIzgara)
        {
            void Esle(object? s, EventArgs e)
            {
                try
                {
                    if (!ReferenceEquals(durum.Sv, sv) || !liste.IsEffectivelyVisible) return;
                    var satirVar = liste is ItemsControl ic && ic.ItemCount > 0;
                    Grid? satirIzgara = null;
                    if (satirVar)
                        satirIzgara = liste.GetVisualDescendants().OfType<ListBoxItem>()
                                           .Where(i => i.IsEffectivelyVisible && i.Bounds.Width > 0)
                                           .Select(i => i.GetVisualDescendants().OfType<Grid>()
                                                         .FirstOrDefault(g => g.ColumnDefinitions.Count == baslikIzgara.ColumnDefinitions.Count))
                                           .FirstOrDefault(g => g is not null);
                    // Satır var ama henüz çizilmediyse (sanallaştırma) bekle — özgüne dönüp titreme yapma.
                    if (satirVar && satirIzgara is null) return;
                    KolonlariEsle(baslikIzgara, satirIzgara, ref durum.Ozgun);
                }
                catch { /* yalnız hizalama */ }
            }
            liste.LayoutUpdated += Esle;
            durum.Abonelikler.Add(new Eylem(() => liste.LayoutUpdated -= Esle));
        }

        // Boş listede "doğal genişlik > alan" kararı başlığın genişliğine bağlıdır → pencere boyutu değişince tazele.
        durum.Abonelikler.Add(header.GetObservable(Visual.BoundsProperty).Subscribe(new Gozlemci<Rect>(_ =>
        {
            if (liste is ItemsControl c && c.ItemCount == 0) TazeleErtele();
        })));
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
