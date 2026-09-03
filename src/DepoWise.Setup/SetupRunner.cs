using System.IO.Compression;
using System.Net.Http.Headers;
using DepoWise.Application.Setup;

namespace DepoWise.Setup;

/// <summary>Kurulum akışındaki adımlar (UI adım listesini bundan üretir).</summary>
public enum SetupStep { SistemKontrolu, Indirme, Dogrulama, Kurulum, SonKontroller }

/// <summary>UI'a bildirilen durum. Kod-arkası yalnız bunu ekrana çizer.</summary>
public sealed record SetupState(SetupStep Step, string Message, int Percent, string? Detail = null);

/// <summary>
/// ═══ KURULUM AKIŞI — UI'DAN BAĞIMSIZ ═══
///
/// Arayüz (Avalonia) yalnız bu sınıfı çağırır ve <see cref="IProgress{T}"/> ile gelen durumu çizer.
/// Böylece iş mantığı kod-arkasında yaşamaz (<c>.claude/rules/desktop.md</c>) ve arayüz değişse de
/// (WinForms → Avalonia geçişinde olduğu gibi) akış aynı kalır.
///
/// Sıra bilinçlidir: <b>doğrulama kurulumdan ÖNCEDİR</b> ve atlanamaz.
/// </summary>
public sealed class SetupRunner
{
    private readonly string _server;
    private readonly HttpClient _http;

    public SetupRunner(string server, HttpClient http)
    {
        _server = server.TrimEnd('/');
        _http = http;
    }

    /// <summary>Kurulum tanımını alır (yeni manifest ucu; yoksa mevcut sürüm ucundan üretir).</summary>
    public async Task<SetupManifest> GetManifestAsync(CancellationToken ct)
    {
        try
        {
            var r = await _http.GetAsync($"{_server}/api/setup/manifest", ct);
            if (r.IsSuccessStatusCode)
                return SetupManifestReader.Parse(await r.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException) { /* uç yok → geri düşüş */ }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { /* zaman aşımı → geri düşüş */ }

        return SetupManifestReader.FromReleasesLatest(
            await _http.GetStringAsync($"{_server}/api/releases/latest", ct));
    }

    /// <summary>Ön-koşulları ölçer (ağ durumu manifest indirilebildiyse zaten bilinir).</summary>
    public IReadOnlyList<PrerequisiteResult> CheckPrerequisites(
        SetupManifest manifest, string installDir, bool networkKnownGood)
        => SetupPrerequisites.Check(new WindowsSystemProbe(networkKnownGood), installDir, manifest.Requirements);

    /// <summary>
    /// İndir → DOĞRULA → kur → sürüm durumunu yaz → kısayol. Herhangi bir adım başarısızsa
    /// <see cref="SetupVerificationException"/> atar ve yarım dosyaları temizler.
    /// </summary>
    public async Task InstallAsync(SetupManifest manifest, string installDir,
        IProgress<SetupState> progress, CancellationToken ct)
    {
        var pkg = manifest.Application;
        var indirmeUri = SetupUrlPolicy.ResolveDownloadUrl(_server, pkg.DownloadUrl);
        var tmpZip = Path.Combine(Path.GetTempPath(), $"alpnex-{pkg.Version}.zip");

        try
        {
            // ── 1) İNDİR (yeniden deneme + kaldığı yerden devam) ──
            progress.Report(new SetupState(SetupStep.Indirme, "İndiriliyor…", 0));
            var ip = new Progress<DownloadProgress>(d => progress.Report(new SetupState(
                SetupStep.Indirme, "İndiriliyor…", d.Percent, Detay(d))));
            await SetupDownloader.DownloadAsync(new SetupHttp(_http), indirmeUri, tmpZip, pkg.SizeBytes, ip, ct);

            // ── 2) DOĞRULA — FAIL-CLOSED, atlanamaz ──
            progress.Report(new SetupState(SetupStep.Dogrulama, "Paket doğrulanıyor…", 100));
            await Task.Run(() => SetupPackageVerifier.RequireVerifiedPackage(tmpZip, pkg.Sha256, pkg.SizeBytes), ct);

            // ── 3) KUR ──
            progress.Report(new SetupState(SetupStep.Kurulum, "Kuruluyor…", 0));
            Directory.CreateDirectory(installDir);
            await Task.Run(() => Ac(tmpZip, installDir, p => progress.Report(
                new SetupState(SetupStep.Kurulum, "Kuruluyor…", p))), ct);

            try { File.Delete(tmpZip); } catch { }

            var exe = Path.Combine(installDir, "DepoWise.Desktop.exe");
            if (!File.Exists(exe))
                throw new SetupVerificationException("PAKET_EKSIK",
                    "Kurulum paketi geçersiz: uygulama dosyası bulunamadı. Kurulum iptal edildi.");

            // ── 4) SON KONTROLLER: sunucu adresi + sürüm durumu (çift indirmeyi önler) ──
            progress.Report(new SetupState(SetupStep.SonKontroller, "Son kontroller…", 100));
            File.WriteAllText(Path.Combine(installDir, "serverurl.txt"), _server);
            SetupInstallState.WriteInstalledVersion(SetupInstallState.DefaultUpdateRoot(), pkg.Version);
            TryCreateShortcut(exe, installDir);
        }
        catch
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
            throw;
        }
    }

    private static string Detay(DownloadProgress d)
    {
        var mb = $"{d.BytesRead / 1024 / 1024} / {Math.Max(1, d.TotalBytes / 1024 / 1024)} MB";
        var hiz = d.BytesPerSecond >= 1024 * 1024
            ? $"{d.BytesPerSecond / 1024 / 1024:0.0} MB/sn"
            : d.BytesPerSecond > 0 ? $"{d.BytesPerSecond / 1024:0} KB/sn" : "";
        var kalan = d.Remaining is { } r && r.TotalSeconds >= 1
            ? (r.TotalMinutes >= 1 ? $"~{r.TotalMinutes:0} dk" : $"~{r.TotalSeconds:0} sn") : "";
        return string.Join("   ·   ", new[] { mb, hiz, kalan }.Where(s => s.Length > 0));
    }

    private static void Ac(string zipPath, string targetDir, Action<int> progress)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        int total = archive.Entries.Count, i = 0;
        var rootFull = Path.GetFullPath(targetDir);
        foreach (var entry in archive.Entries)
        {
            i++;
            var destPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;  // zip-slip
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destPath); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
            if (total > 0 && i % 5 == 0) progress((int)(i * 100L / total));
        }
        progress(100);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TryCreateShortcut(string exePath, string workingDir)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            var sc = shell.CreateShortcut(Path.Combine(desktop, "Alpnex.lnk"));
            sc.TargetPath = exePath;
            sc.WorkingDirectory = workingDir;
            sc.Description = "Alpnex";
            sc.Save();
        }
        catch { /* kısayol başarısız olsa da kurulum tamamdır */ }
    }

    /// <summary>HTTP soyutlamasının gerçek uygulaması (Range ile kaldığı yerden devam).</summary>
    private sealed class SetupHttp : ISetupHttp
    {
        private readonly HttpClient _http;
        public SetupHttp(HttpClient http) => _http = http;

        public async Task<long> GetLengthAsync(Uri url, CancellationToken ct)
        {
            using var r = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            r.EnsureSuccessStatusCode();
            return r.Content.Headers.ContentLength ?? -1L;
        }

        public async Task<Stream> OpenReadAsync(Uri url, long offset, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0) req.Headers.Range = new RangeHeaderValue(offset, null);
            var r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadAsStreamAsync(ct);
        }
    }

    /// <summary>Makineden ön-koşul verisi okur; saf karar mantığı <see cref="SetupPrerequisites"/>'tedir.</summary>
    private sealed class WindowsSystemProbe : ISystemProbe
    {
        private readonly bool _networkKnownGood;
        public WindowsSystemProbe(bool networkKnownGood) => _networkKnownGood = networkKnownGood;

        public int OsBuild => Environment.OSVersion.Version.Build;
        public string Architecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
        public bool NetworkAvailable => _networkKnownGood;

        public long AvailableFreeBytes(string path)
        {
            try
            {
                var kok = Path.GetPathRoot(Path.GetFullPath(path));
                return string.IsNullOrEmpty(kok) ? -1 : new DriveInfo(kok).AvailableFreeSpace;
            }
            catch { return -1; }
        }

        public bool CanWrite(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                var deneme = Path.Combine(path, ".alpnex_yazma_denemesi");
                File.WriteAllText(deneme, "x");
                File.Delete(deneme);
                return true;
            }
            catch { return false; }
        }
    }
}
