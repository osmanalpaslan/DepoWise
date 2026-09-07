using Avalonia;
using Avalonia.Controls;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ SOHBET — YENİ MESAJA OTOMATİK KAYDIRMA (kullanıcı isteği 2026-09-07) ═══
///
/// <para><b>Neden var:</b> konuşma birkaç ekran boyunu geçince yeni mesaj listenin ALTINDA,
/// görünmeyen alanda oluşuyordu. Kullanıcı pencereye bakıp "mesaj gelmedi / gönderdiğim görünmüyor"
/// diyordu — oysa mesaj oradaydı, sadece aşağıdaydı.</para>
///
/// <para><b>Kural:</b> yalnız <b>içerik büyüdüğünde</b> ve kullanıcı <b>zaten dipteyken</b> kaydırılır.
/// Geçmişi okumak için yukarı kaydırmış bir kullanıcı zorla aşağı çekilmez — bu, mesajlaşma
/// uygulamalarında en can sıkıcı davranıştır.</para>
///
/// <para>Kullanımı (XAML): <c>&lt;ScrollViewer ctrl:SohbetKaydirma.AltaKilitli="True"&gt;</c></para>
/// </summary>
public static class SohbetKaydirma
{
    /// <summary>"Dipteyse dipte kal" davranışını açar.</summary>
    public static readonly AttachedProperty<bool> AltaKilitliProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("AltaKilitli", typeof(SohbetKaydirma));

    public static void SetAltaKilitli(ScrollViewer o, bool v) => o.SetValue(AltaKilitliProperty, v);
    public static bool GetAltaKilitli(ScrollViewer o) => o.GetValue(AltaKilitliProperty);

    /// <summary>"Dipte sayılma" toleransı (piksel). Bir mesaj balonu boyundan biraz fazlası.</summary>
    private const double DipToleransi = 48;

    static SohbetKaydirma()
    {
        AltaKilitliProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            sv.ScrollChanged -= Degisti;              // çift abonelik olmasın (şablon yeniden kurulabilir)
            if (e.GetNewValue<bool>()) sv.ScrollChanged += Degisti;
        });
    }

    private static void Degisti(object? gonderen, ScrollChangedEventArgs e)
    {
        if (gonderen is not ScrollViewer sv) return;
        if (e.ExtentDelta.Y <= 0) return;             // içerik büyümediyse ilgilenmiyoruz

        // Yeni mesaj EKLENMEDEN önce kullanıcı dipte miydi? Önceki uzunluk = şimdiki - artış.
        var oncekiUzunluk = sv.Extent.Height - e.ExtentDelta.Y;
        var gorunenAlt = sv.Offset.Y + sv.Viewport.Height;
        if (gorunenAlt >= oncekiUzunluk - DipToleransi)
            sv.ScrollToEnd();
    }
}
