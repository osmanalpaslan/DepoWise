using DepoWise.Application.Notifications;
using Microsoft.JSInterop;

namespace DepoWise.Web.Services;

/// <summary>
/// ═══ SESLİ BİLDİRİM — WEB (kullanıcı isteği 2026-09-07) ═══
///
/// <para>Masaüstündeki <c>SesServisi</c>'nin web karşılığı. <b>Karar mantığı ortaktır</b>
/// (<see cref="BildirimSesiKarari"/>): "yeni bir şey var mı" ve "spam kalkanı izin veriyor mu"
/// soruları iki ortamda AYNI kodla yanıtlanır; ses dosyaları da aynıdır.</para>
///
/// <para><b>Devre güvenliği:</b> Blazor Server'da bileşen kodundan kaçan her hata devreyi
/// (circuit) kapatır ve sayfadaki hiçbir düğme çalışmaz — sohbetin bütün hastalığı buydu.
/// Bu yüzden her JS çağrısı sarmalanmıştır: <b>ses hiçbir koşulda sayfayı bozamaz.</b></para>
///
/// <para><b>Kapsam:</b> spam kalkanı YALNIZ uyarı/duyuru sesine uygulanır
/// (<see cref="BildirimGeldiAsync"/>). Buton uyarısı ve sohbet sesleri <see cref="CalAsync"/> ile
/// doğrudan çalar — kullanıcı ikisinin karıştırılmamasını açıkça istedi.</para>
/// </summary>
public sealed class WebSesServisi
{
    private readonly IJSRuntime _js;
    private string _kullaniciId = "";
    private bool _tabanYuklendi;

    public WebSesServisi(IJSRuntime js) => _js = js;

    /// <summary>Uyarı/duyuru sesinin karar mantığı (devre başına tek örnek).</summary>
    public BildirimSesiKarari Bildirim { get; } = new();

    /// <summary>Tek bir sesi çalar. Kalkan uygulanmaz.</summary>
    public async Task CalAsync(SesTuru tur)
    {
        try { await _js.InvokeVoidAsync("dwSes.cal", SesDosyalari.DosyaAdi(tur)); } catch { }
    }

    /// <summary>
    /// Girişten sonra: bu tarayıcıda saklanan "en son görülen okunmamış sayısı" yüklenir.
    /// Taban olmadan, önceki oturumdan sonra gelenler ayırt edilemezdi.
    /// </summary>
    public async Task BaslatAsync(string kullaniciId)
    {
        _kullaniciId = kullaniciId ?? "";
        int? taban = null;
        try { taban = await _js.InvokeAsync<int?>("dwSes.tabanOku", _kullaniciId); } catch { }
        Bildirim.TabanYukle(taban);
        _tabanYuklendi = true;
    }

    /// <summary>
    /// Uyarı/duyuru sayacı güncellendi. Ses YALNIZ yeni bir şey varsa ve kalkan izin veriyorsa çalar.
    /// Taban her çağrıda tarayıcıya yazılır (oturumlar arası hafıza).
    /// </summary>
    public async Task BildirimGeldiAsync(int okunmamis)
    {
        // Taban yüklenmeden karar verilirse ilk okumada yanlışlıkla ses çalabilirdi.
        if (!_tabanYuklendi) return;

        bool calsin;
        try { calsin = Bildirim.Geldi(okunmamis, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); }
        catch { return; }

        try { await _js.InvokeVoidAsync("dwSes.tabanYaz", _kullaniciId, okunmamis < 0 ? 0 : okunmamis); } catch { }
        if (calsin) await CalAsync(SesTuru.Bildirim);
    }
}
