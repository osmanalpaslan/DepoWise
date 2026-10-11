using System;
using System.IO;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// ⭐ 2026-10-11 (kullanıcı isteği): görev çubuğunda ARKA PLANSIZ simge, masaüstü kısayolunda ARKA PLANLI simge.
/// Exe simgesi artık şeffaf işaret olduğu için, simgesi belirtilmemiş eski masaüstü kısayolları da şeffaf görünürdü.
/// Kurulum (Setup) yeni kısayolları zaten arka planlı simgeyle oluşturur; bu sınıf MEVCUT kurulumlardaki
/// "Alpnex.lnk" kısayolunu bir kez düzeltir: hedefi BU uygulamaysa simgesini kurulum klasöründeki app-logo.ico yapar.
/// Yalnız masaüstündeki kendi kısayolumuza dokunur; başka hiçbir dosya değişmez. Hata uygulamayı ASLA etkilemez.
/// </summary>
public static class KisayolSimgesi
{
    public static void ArkaPlandaDuzelt()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = Task.Run(() => { try { Duzelt(); } catch { } });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Duzelt()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith("DepoWise.Desktop.exe", StringComparison.OrdinalIgnoreCase)) return;   // geliştirme (dotnet) değil
        var ico = Path.Combine(Path.GetDirectoryName(exe)!, "app-logo.ico");
        var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Alpnex.lnk");
        if (!File.Exists(ico) || !File.Exists(lnk)) return;

        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t is null) return;
        dynamic shell = Activator.CreateInstance(t)!;
        var sc = shell.CreateShortcut(lnk);
        string hedef = sc.TargetPath ?? "";
        string simge = sc.IconLocation ?? "";
        if (!string.Equals(Path.GetFullPath(hedef), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return;   // bizim kısayolumuz değil
        if (simge.StartsWith(ico, StringComparison.OrdinalIgnoreCase)) return;                                              // zaten doğru
        sc.IconLocation = ico + ",0";
        sc.Save();
    }
}
