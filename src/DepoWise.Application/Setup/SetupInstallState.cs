using System;
using System.IO;
using System.Text;

namespace DepoWise.Application.Setup;

/// <summary>
/// ═══ TAZE KURULUM SÜRÜM DURUMU — ÇİFT İNDİRME DÜZELTMESİ (2026-09-04) ═══
///
/// <b>Sorun:</b> Kurulum aracı paketi kurup bitiyordu ama uygulamanın güncelleme durumunu
/// (<c>current.txt</c>) YAZMIYORDU. Uygulama ilk açıldığında <c>UpdateService</c> bu dosyayı bulamayıp
/// <c>"0.0.0"</c> olarak oluşturuyor, <c>Check()</c> "sunucudaki sürüm daha yeni" diyor ve
/// <b>az önce kurulan ~86 MB'lık paket bir kez daha indirilip yeniden kuruluyordu.</b>
/// Kullanıcı bunu "kurulum bitmedi mi?" olarak yaşıyordu.
///
/// <b>Çözüm:</b> kurulum aracı, kurulum BAŞARIYLA bittikten sonra sürümü mevcut mekanizmanın
/// beklediği YERE ve BİÇİME yazar. Yeni bir mekanizma kurulmaz (UPD-01 ile çelişmez):
///
/// <list type="bullet">
///   <item>Yol: <c>%LOCALAPPDATA%\Alpnex\update\current.txt</c> — <c>DesktopServices</c> içindeki
///         <c>UpdateService</c> kökü ve <c>UpdateInstaller</c>'ın yazdığı yol ile AYNI.</item>
///   <item>Biçim: yalnız sürüm metni, <b>satır sonu YOK</b>, UTF-8 — <c>UpdateInstaller</c>'ın
///         PowerShell yardımcısındaki <c>Set-Content -NoNewline -Encoding utf8</c> ile aynı.</item>
/// </list>
///
/// Okuma tarafı <c>File.ReadAllText(...).Trim()</c> kullandığı için BOM'lu/BOM'suz UTF-8 farkı
/// davranışı değiştirmez; yine de BOM'suz yazılır.
/// </summary>
public static class SetupInstallState
{
    /// <summary>Güncelleme durumunun tutulduğu kök: <c>%LOCALAPPDATA%\Alpnex\update</c>.</summary>
    public static string DefaultUpdateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "update");

    /// <summary>Sürüm dosyasının tam yolu.</summary>
    public static string CurrentVersionFile(string updateRoot) => Path.Combine(updateRoot, "current.txt");

    /// <summary>
    /// Kurulan sürümü yazar (klasör yoksa oluşturur). Kurulum BAŞARILI olduktan sonra çağrılır —
    /// erken çağrılırsa yarım kurulum "kurulu" görünür.
    /// </summary>
    public static void WriteInstalledVersion(string updateRoot, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Sürüm boş olamaz.", nameof(version));

        Directory.CreateDirectory(updateRoot);
        // satır sonu YOK + BOM YOK  → okuma tarafındaki Trim() ile birebir uyumlu
        File.WriteAllText(CurrentVersionFile(updateRoot), version.Trim(), new UTF8Encoding(false));
    }

    /// <summary>Yazılı sürümü okur; dosya yoksa/boşsa null.</summary>
    public static string? ReadInstalledVersion(string updateRoot)
    {
        var p = CurrentVersionFile(updateRoot);
        if (!File.Exists(p)) return null;
        var v = File.ReadAllText(p).Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
