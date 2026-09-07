using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Platform;
using DepoWise.Application.Notifications;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ SESLİ BİLDİRİM — MASAÜSTÜ (kullanıcı isteği 2026-09-07) ═══
///
/// <para><b>Neden ek kütüphane yok:</b> masaüstü yalnız Windows'ta çalışır (paket
/// <c>-r win-x64 --self-contained</c>). Windows'un kendi <c>winmm.dll</c> <c>PlaySound</c> API'si
/// WAV çalmaya yeter; NAudio/SoundPlayer gibi bir bağımlılık eklemek 86 MB'lık paketi büyütür ve
/// yeni bir güncelleme yüzeyi açardı.</para>
///
/// <para><b>Sesler nereden geliyor:</b> <c>Assets/Sounds/*.wav</c> Avalonia kaynağı olarak
/// uygulamanın İÇİNE gömülüdür (<c>AvaloniaResource</c>). Diske dosya yazılmaz, dışarıdan dosya
/// okunmaz → eksik/bozuk dosya diye bir durum oluşmaz. Sesler <c>scripts/ses_uret.mjs</c> ile
/// matematiksel olarak üretilmiştir; telif sorunu yoktur.</para>
///
/// <para><b>Bellek güvenliği:</b> <c>SND_ASYNC</c> ile Windows çalma bitene kadar tamponu okumaya
/// DEVAM eder. Yönetilen bir <c>byte[]</c> bu sırada çöp toplayıcı tarafından taşınabilirdi; bu
/// yüzden her ses bir kez YÖNETİLMEYEN belleğe kopyalanır ve uygulama boyunca orada kalır
/// (dördü toplam ~100 KB).</para>
///
/// <para><b>Ses ASLA uygulamayı çökertmez:</b> her çağrı sarmalanmıştır. Sesin çalmaması bir
/// rahatsızlık, uygulamanın kapanması felakettir.</para>
/// </summary>
public static class SesServisi
{
    private const uint SND_ASYNC = 0x0001;      // çalmayı bekleme, hemen dön
    private const uint SND_NODEFAULT = 0x0002;  // bulunamazsa Windows "ding" sesi ÇALMA
    private const uint SND_MEMORY = 0x0004;     // kaynak: bellekteki WAV

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool PlaySoundW(IntPtr veri, IntPtr modul, uint bayrak);

    private static readonly object _kilit = new();
    private static readonly Dictionary<SesTuru, IntPtr> _tampon = new();

    /// <summary>Sesler açık mı? Kullanıcı Ayarlar'dan kapatabilir; tercih bu makinede saklanır.</summary>
    public static bool Acik { get; private set; } = true;

    /// <summary>Uyarı/duyuru sesinin karar mantığı (spam kalkanı) — TEK örnek.</summary>
    public static BildirimSesiKarari Bildirim { get; } = new();

    private static string AyarYolu => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "ses.json");

    private sealed class Ayar
    {
        public bool Acik { get; set; } = true;
        /// <summary>Kullanıcı kimliği → en son görülen okunmamış sayısı (oturumlar arası taban).</summary>
        public Dictionary<string, int> Taban { get; set; } = new();
    }

    private static Ayar Oku()
    {
        try
        {
            if (File.Exists(AyarYolu))
                return JsonSerializer.Deserialize<Ayar>(File.ReadAllText(AyarYolu)) ?? new Ayar();
        }
        catch { }
        return new Ayar();
    }

    private static void Yaz(Ayar a)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AyarYolu)!);
            File.WriteAllText(AyarYolu, JsonSerializer.Serialize(a));
        }
        catch { /* tercih yazılamazsa ses yine çalışır; sessizce geç */ }
    }

    /// <summary>
    /// ⭐ Açılışta bir kez: tüm sesler gömülü kaynaktan belleğe alınır ve sonuç günlüğe yazılır.
    ///
    /// <para><b>İki faydası var.</b> (1) İlk sesin gecikmesi ortadan kalkar. (2) Asıl önemlisi:
    /// bir ses hiç yüklenemiyorsa bu, kullanıcı "ses gelmiyor" demeden ÖNCE günlüğe düşer.
    /// Sessiz başarısızlık, teşhisi imkânsız bir hata türüdür.</para>
    /// </summary>
    public static void Onyukle()
    {
        var eksik = new List<string>();
        foreach (var tur in Enum.GetValues<SesTuru>())
            if (Tampon(tur) == IntPtr.Zero) eksik.Add(SesDosyalari.DosyaAdi(tur));

        Gunlukle(eksik.Count == 0
            ? $"{Enum.GetValues<SesTuru>().Length} sesin tamamı yüklendi"
            : "YÜKLENEMEYEN sesler: " + string.Join(", ", eksik));
    }

    /// <summary>
    /// Girişten sonra çağrılır: ses tercihi ve <b>bir önceki oturumun</b> okunmamış tabanı yüklenir.
    /// Taban olmadan "önceki login'den sonra gelenler" ayırt edilemezdi.
    /// </summary>
    public static void Baslat(string kullaniciId)
    {
        var a = Oku();
        Acik = a.Acik;
        Bildirim.TabanYukle(a.Taban.TryGetValue(kullaniciId, out var t) ? t : null);
    }

    /// <summary>Okunmamış sayısını kalıcı yazar (uygulama kapansa da taban korunur).</summary>
    public static void TabanKaydet(string kullaniciId, int sayi)
    {
        var a = Oku();
        a.Taban[kullaniciId] = sayi < 0 ? 0 : sayi;
        Yaz(a);
    }

    /// <summary>Sesleri aç/kapat (Ayarlar ekranı). Tercih bu makinede saklanır.</summary>
    public static void AcikYap(bool acik)
    {
        Acik = acik;
        var a = Oku(); a.Acik = acik; Yaz(a);
    }

    /// <summary>
    /// Sesi çalar. <b>Spam kalkanı BURADA UYGULANMAZ</b> — kullanıcı kalkanın yalnız uyarı/duyuru
    /// için olmasını istedi. Uyarı/duyuru sesini <see cref="BildirimGeldi"/> üzerinden çalın.
    /// </summary>
    public static void Cal(SesTuru tur)
    {
        if (!Acik) return;
        try
        {
            var p = Tampon(tur);
            if (p != IntPtr.Zero) PlaySoundW(p, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch { /* ses hiçbir koşulda uygulamayı etkilemez */ }
    }

    /// <summary>
    /// Uyarı/duyuru sayacı güncellendi. Ses YALNIZ yeni bir şey varsa ve spam kalkanı izin
    /// veriyorsa çalar. Taban ayrıca kalıcı yazılır.
    /// </summary>
    public static void BildirimGeldi(string kullaniciId, int okunmamis)
    {
        bool calsin;
        try { calsin = Bildirim.Geldi(okunmamis, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); }
        catch { return; }
        try { TabanKaydet(kullaniciId, okunmamis); } catch { }
        if (calsin) Cal(SesTuru.Bildirim);
    }

    /// <summary>
    /// Ses YÜKLENEMEZSE bunu SESSİZCE geçmeyiz: kullanıcı "ses gelmiyor" der, sebebi hiçbir yerde
    /// yazmazsa aranacak yer kalmaz. Açılış günlüğüne tek satır düşülür (uygulama etkilenmez).
    /// </summary>
    private static void Gunlukle(string mesaj)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"),
                $"{DateTimeOffset.UtcNow:O}\tses\t{mesaj}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>WAV'ı gömülü kaynaktan bir kez okuyup yönetilmeyen belleğe kopyalar (bkz. sınıf notu).</summary>
    private static IntPtr Tampon(SesTuru tur)
    {
        lock (_kilit)
        {
            if (_tampon.TryGetValue(tur, out var mevcut)) return mevcut;

            var p = IntPtr.Zero;
            var ad = SesDosyalari.DosyaAdi(tur);
            try
            {
                var uri = new Uri($"avares://DepoWise.Desktop/Assets/Sounds/{ad}.wav");
                using var akis = AssetLoader.Open(uri);
                using var bellek = new MemoryStream();
                akis.CopyTo(bellek);
                var bayt = bellek.ToArray();
                if (bayt.Length > 0)
                {
                    p = Marshal.AllocHGlobal(bayt.Length);
                    Marshal.Copy(bayt, 0, p, bayt.Length);
                }
                else Gunlukle($"ses '{ad}' BOŞ okundu");
            }
            catch (Exception ex) { p = IntPtr.Zero; Gunlukle($"ses '{ad}' YÜKLENEMEDİ: {ex.GetType().Name} {ex.Message}"); }

            _tampon[tur] = p;      // başarısızlık da önbelleğe alınır: her seferinde denenmesin
            return p;
        }
    }
}
