using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Application.Security;

namespace DepoWise.Desktop;

/// <summary>
/// ⭐ 2026-10-11 — GÜNCELLEME SONRASI "Yenilikler" penceresi (kullanıcı isteği: güncellemeden sonra da notlar çıksın).
/// Uygulama yeni bir sürümle İLK KEZ açıldığında, o sürümün notu (<see cref="GuncellemeNotlari"/>) BİR KEZ gösterilir.
/// "Görüldü" bilgisi bu bilgisayarda tutulur (<c>%LOCALAPPDATA%\Alpnex\guncelleme_notu.json</c>) — kullanıcı/firma
/// ayarı değildir. Hata asla uygulamayı etkilemez (gösterilemezse sessizce geçilir).
/// </summary>
public static class GuncellemeNotuGosterici
{
    private static string Yol => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "guncelleme_notu.json");

    private sealed class Kayit { public string? GorulenSurum { get; set; } }

    /// <summary>Çalışan masaüstü sürümü (güncelleme denetimiyle AYNI kaynak).</summary>
    public static string? MevcutSurum()
    {
        try { return DesktopServices.Update.CurrentVersion(); } catch { return null; }
    }

    /// <summary>Gösterilecek not (yoksa ya da bu sürüm zaten görüldüyse null). Saf karar — test edilebilir.</summary>
    public static GuncellemeNotu? GosterilecekNot(string? mevcutSurum, string? gorulenSurum)
    {
        if (string.IsNullOrWhiteSpace(mevcutSurum)) return null;
        if (string.Equals(mevcutSurum, gorulenSurum, StringComparison.Ordinal)) return null;
        return GuncellemeNotlari.MasaustuSurumuIcin(mevcutSurum);
    }

    public static string? GorulenSurum()
    {
        try { return File.Exists(Yol) ? JsonSerializer.Deserialize<Kayit>(File.ReadAllText(Yol))?.GorulenSurum : null; }
        catch { return null; }
    }

    public static void GorulduIsaretle(string surum)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Yol)!);
            File.WriteAllText(Yol, JsonSerializer.Serialize(new Kayit { GorulenSurum = surum }));
        }
        catch { }
    }

    /// <summary>Ana pencere açıldıktan sonra çağrılır: bu sürümün notu görülmediyse pencereyi BİR KEZ açar.</summary>
    public static async Task GosterGerekirseAsync(Avalonia.Controls.Window? sahip)
    {
        try
        {
            var surum = MevcutSurum();
            var not = GosterilecekNot(surum, GorulenSurum());
            if (surum is null) return;
            if (not is null) { if (GorulenSurum() != surum) GorulduIsaretle(surum); return; }
            GorulduIsaretle(surum);   // pencere kapanmadan uygulama kapansa bile tekrar tekrar açılmasın
            if (sahip is null) return;
            await new Views.GuncellemeNotuWindow(not).ShowDialog(sahip);
        }
        catch { /* not gösterilemezse uygulama etkilenmez */ }
    }
}
