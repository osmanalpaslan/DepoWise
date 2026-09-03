using System;
using System.Collections.Generic;
using System.Linq;

namespace DepoWise.Application.Setup;

/// <summary>Tek bir ön-koşulun sonucu. <see cref="Ok"/> false ise kuruluma geçilmez.</summary>
public sealed record PrerequisiteResult(string Id, string Label, bool Ok, string? Detail = null);

/// <summary>
/// Makine bilgisini kurulum aracının UI'ından ayıran arayüz. Testte sahte değerlerle doldurulur;
/// gerçek uygulamada işletim sisteminden okunur. (Testler bu sayede gerçek diske/OS'a bağlı olmaz.)
/// </summary>
public interface ISystemProbe
{
    /// <summary>Windows derleme numarası (ör. Windows 10 1607 = 14393).</summary>
    int OsBuild { get; }
    /// <summary>İşlemci mimarisi: "X64", "Arm64", "X86" …</summary>
    string Architecture { get; }
    /// <summary>Hedef sürücüdeki boş alan (bayt). Bilinmiyorsa -1.</summary>
    long AvailableFreeBytes(string path);
    /// <summary>Hedef klasöre yazma izni var mı?</summary>
    bool CanWrite(string path);
    /// <summary>Sunucuya erişilebiliyor mu?</summary>
    bool NetworkAvailable { get; }
}

/// <summary>
/// ═══ SİSTEM ÖN-KOŞULLARI (2026-09-04) ═══
///
/// Analizde ampirik olarak doğrulandı: <b>Alpnex'in ayrıca kurulması gereken dış bağımlılığı YOK</b>
/// (paket self-contained; 253 dosyanın hiçbirinde VC++ Redistributable importu yok; WebView2
/// kullanılmıyor; <c>api-ms-win-crt-*</c> Windows 10+ ile birlikte gelir). Bu yüzden burada
/// "bağımlılık kurulumu" değil, makinenin karşılaması gereken <b>ön-koşullar</b> kontrol edilir.
///
/// Kontrol edilmesi gerekmeyen bir şey sırf liste dolsun diye EKLENMEZ (kullanıcı kuralı).
/// </summary>
public static class SetupPrerequisites
{
    /// <summary>Paketin desteklediği mimariler. win-x64 paketi ARM64'te emülasyonla çalışır; x86'da çalışmaz.</summary>
    private static readonly string[] DesteklenenMimariler = { "X64", "Arm64" };

    public static IReadOnlyList<PrerequisiteResult> Check(
        ISystemProbe probe, string installPath, IReadOnlyList<SetupRequirement> requirements)
    {
        var sonuc = new List<PrerequisiteResult>();

        // 1) Windows sürümü
        var minBuild = requirements.FirstOrDefault(r => r.Id == SetupManifestReader.ReqOsBuild)?.Value ?? 14393;
        var osLabel = requirements.FirstOrDefault(r => r.Id == SetupManifestReader.ReqOsBuild)?.Label
                      ?? "Windows 10 (1607) veya üzeri";
        sonuc.Add(new PrerequisiteResult("os", osLabel, probe.OsBuild >= minBuild,
            probe.OsBuild >= minBuild ? null
                : $"Bu bilgisayarın Windows sürümü Alpnex için çok eski. Gereken en düşük sürüm: {osLabel}."));

        // 2) Mimari
        var mimariOk = DesteklenenMimariler.Contains(probe.Architecture, StringComparer.OrdinalIgnoreCase);
        sonuc.Add(new PrerequisiteResult("arch", "64-bit Windows", mimariOk,
            mimariOk ? null : "Alpnex yalnız 64-bit Windows üzerinde çalışır."));

        // 3) Disk alanı
        var gerekli = requirements.FirstOrDefault(r => r.Id == SetupManifestReader.ReqDiskBytes)?.Value
                      ?? (400L * 1024 * 1024);
        var bos = probe.AvailableFreeBytes(installPath);
        var diskOk = bos < 0 || bos >= gerekli;   // ölçülemiyorsa engelleme (yanlış negatif üretme)
        sonuc.Add(new PrerequisiteResult("disk", $"En az {gerekli / 1024 / 1024} MB boş alan", diskOk,
            diskOk ? null
                : $"Kurulum için yeterli boş alan yok. Gereken: {gerekli / 1024 / 1024} MB, " +
                  $"boş: {Math.Max(0, bos) / 1024 / 1024} MB."));

        // 4) Yazma izni — yönetici gerektirmeyen bir konuma kurulduğu için normalde sağlanır
        var yazma = probe.CanWrite(installPath);
        sonuc.Add(new PrerequisiteResult("write", "Kurulum izinleri", yazma,
            yazma ? null : "Seçilen klasöre yazma izni yok. Farklı bir klasör seçin."));

        // 5) Ağ — paket sunucudan indiriliyor
        sonuc.Add(new PrerequisiteResult("network", "Ağ bağlantısı", probe.NetworkAvailable,
            probe.NetworkAvailable ? null
                : "İnternet bağlantısı bulunamadı. Kurulum paketi sunucudan indirileceği için bağlantı gerekir."));

        return sonuc;
    }

    /// <summary>Tümü sağlanıyor mu?</summary>
    public static bool AllOk(IReadOnlyList<PrerequisiteResult> results) => results.All(r => r.Ok);

    /// <summary>Kullanıcıya gösterilecek ilk engel (yoksa null).</summary>
    public static PrerequisiteResult? FirstBlocker(IReadOnlyList<PrerequisiteResult> results)
        => results.FirstOrDefault(r => !r.Ok);
}
