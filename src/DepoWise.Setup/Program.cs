using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using DepoWise.Application.Setup;

// ── DepoWise Kurulum Aracı (arayüzlü) ──
// Sunucuya bağlanır, en güncel paketi indirir (yüzdeli), seçilen klasöre kurar, sunucu adresini
// otomatik yazar, masaüstü kısayolu oluşturur. Elle bağlantı ayarı YOK. Uygulama self-contained.

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }
}

sealed class SetupForm : Form
{
    private readonly string _server;
    private readonly TextBox _dir;
    private readonly Button _browse, _install;
    private readonly ProgressBar _progress;
    private readonly Label _status, _title, _serverLbl;
    private bool _busy, _done;

    public SetupForm()
    {
        _server = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ServerUrl")?.Value?.TrimEnd('/')
            ?? "https://depowise-erp.fly.dev";

        Text = "Alpnex Kurulum";
        Width = 560; Height = 320;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
        Font = new System.Drawing.Font("Segoe UI", 9.5f);
        ForeColor = System.Drawing.Color.White;

        _title = new Label { Text = "Alpnex Kurulum", Left = 24, Top = 20, Width = 500, Height = 30,
            Font = new System.Drawing.Font("Segoe UI", 16f, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(59, 130, 246) };
        _serverLbl = new Label { Text = "Sunucu: " + _server, Left = 24, Top = 56, Width = 500, Height = 20,
            ForeColor = System.Drawing.Color.FromArgb(148, 163, 184) };

        var lbl = new Label { Text = "Kurulum klasörü:", Left = 24, Top = 92, Width = 500, Height = 20 };
        _dir = new TextBox { Left = 24, Top = 114, Width = 400, Height = 26,
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "app") };
        _browse = new Button { Text = "Gözat…", Left = 432, Top = 113, Width = 90, Height = 28, FlatStyle = FlatStyle.Flat };
        _browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog() == DialogResult.OK) _dir.Text = Path.Combine(d.SelectedPath, "Alpnex");
        };

        _progress = new ProgressBar { Left = 24, Top = 168, Width = 498, Height = 22, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
        _status = new Label { Text = "Kuruluma hazır.", Left = 24, Top = 196, Width = 498, Height = 40,
            ForeColor = System.Drawing.Color.FromArgb(203, 213, 225) };

        _install = new Button { Text = "İndir ve Kur", Left = 24, Top = 240, Width = 498, Height = 36,
            FlatStyle = FlatStyle.Flat, BackColor = System.Drawing.Color.FromArgb(34, 197, 94), ForeColor = System.Drawing.Color.White,
            Font = new System.Drawing.Font("Segoe UI", 10.5f, System.Drawing.FontStyle.Bold) };
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += OnInstallClick;

        Controls.AddRange(new Control[] { _title, _serverLbl, lbl, _dir, _browse, _progress, _status, _install });
    }

    private async void OnInstallClick(object? sender, EventArgs e)
    {
        if (_done) { Close(); return; }
        await InstallAsync();
    }

    private void SetStatus(string s) { if (InvokeRequired) BeginInvoke(() => _status.Text = s); else _status.Text = s; }
    private void SetProgress(int p) { if (InvokeRequired) BeginInvoke(() => _progress.Value = Math.Clamp(p, 0, 100)); else _progress.Value = Math.Clamp(p, 0, 100); }

    private async Task InstallAsync()
    {
        if (_busy) return;
        _busy = true; _install.Enabled = false; _browse.Enabled = false; _dir.Enabled = false;
        var installDir = string.IsNullOrWhiteSpace(_dir.Text)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "app")
            : _dir.Text.Trim();
        string? tmpZip = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

            // ── 1) Kurulum tanımı: önce yeni manifest ucu, yoksa MEVCUT sürüm ucu (geri düşüş) ──
            SetStatus("Sunucuya bağlanılıyor, sürüm bilgisi alınıyor…");
            var manifest = await ManifestAlAsync(http);
            var pkg = manifest.Application;

            // ── 2) Sistem ön-koşulları (kurulabilir bağımlılık YOK — bkz. SetupPrerequisites) ──
            SetStatus("Sisteminiz kontrol ediliyor…");
            // networkKnownGood: true — manifest bu satıra gelmeden ZATEN sunucudan indirildi.
            var onKosul = SetupPrerequisites.Check(new WindowsSystemProbe(true), installDir, manifest.Requirements);
            if (SetupPrerequisites.FirstBlocker(onKosul) is { } engel)
                throw new SetupVerificationException("ON_KOSUL:" + engel.Id, engel.Detail ?? engel.Label);

            // ── 3) İndirme adresi kapısı: yalnız HTTPS + yalnız kendi sunucumuzun host'u ──
            var indirmeUri = SetupUrlPolicy.ResolveDownloadUrl(_server, pkg.DownloadUrl);

            tmpZip = Path.Combine(Path.GetTempPath(), $"alpnex-{pkg.Version}.zip");
            SetStatus($"Sürüm {pkg.Version} indiriliyor…");
            await DownloadAsync(http, indirmeUri.ToString(), tmpZip);

            // ── 4) ⭐ BÜTÜNLÜK KAPISI — FAIL-CLOSED. Doğrulanmayan paket KURULMAZ. ──
            SetStatus("Paket doğrulanıyor…");
            SetupPackageVerifier.RequireVerifiedPackage(tmpZip, pkg.Sha256, pkg.SizeBytes);

            SetStatus("Kuruluyor…");
            Directory.CreateDirectory(installDir);
            ExtractWithProgress(tmpZip, installDir);
            try { File.Delete(tmpZip); } catch { }
            tmpZip = null;

            // Paket bütünlüğü: ana exe yoksa kurulum geçersiz (UpdateInstaller ile aynı guard).
            var exe = Path.Combine(installDir, "DepoWise.Desktop.exe");
            if (!File.Exists(exe))
                throw new SetupVerificationException("PAKET_EKSIK",
                    "Kurulum paketi geçersiz: uygulama dosyası bulunamadı. Kurulum iptal edildi.");

            File.WriteAllText(Path.Combine(installDir, "serverurl.txt"), _server);

            // ── 5) ⭐ ÇİFT İNDİRME DÜZELTMESİ: sürüm durumunu YAZ. ──
            // Yazılmazsa uygulama kendini 0.0.0 sanıp az önce kurulan ~86 MB'ı ilk açılışta
            // TEKRAR indirir. Yol/biçim UpdateInstaller ile birebir aynıdır (yeni mekanizma YOK).
            SetStatus("Son kontroller…");
            SetupInstallState.WriteInstalledVersion(SetupInstallState.DefaultUpdateRoot(), pkg.Version);

            TryCreateShortcut(exe, installDir);

            SetProgress(100);
            var version = pkg.Version;
            SetStatus($"Kurulum tamamlandı (sürüm {version}). Masaüstündeki 'Alpnex' kısayolundan açabilirsiniz.");
            MessageBox.Show(this, $"Kurulum tamamlandı (sürüm {version}).\nMasaüstündeki 'Alpnex' kısayolundan açabilirsiniz.",
                "Alpnex Kurulum", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _done = true;
            _install.Text = "Kapat";
            _install.BackColor = System.Drawing.Color.FromArgb(59, 130, 246);
            _install.Enabled = true;
        }
        catch (Exception ex)
        {
            // Yarım inen paket diskte bırakılmaz (iptal/hata sonrası temizlik).
            try { if (tmpZip is not null && File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }

            // Kullanıcıya SADE mesaj; teknik ayrıntı (hata kodu) ayrı satırda.
            var kullaniciMesaji = ex is SetupVerificationException sv ? sv.Message : ex.Message;
            var kod = ex is SetupVerificationException s2 ? s2.Code : ex.GetType().Name;

            SetStatus("HATA: " + kullaniciMesaji);
            MessageBox.Show(this, kullaniciMesaji + "\n\nHata kodu: " + kod,
                "Kurulum Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true; _browse.Enabled = true; _dir.Enabled = true; _busy = false;
        }
    }

    /// <summary>Önce yeni manifest ucunu dener; yoksa MEVCUT sürüm ucundan üretir (geri düşüş).
    /// Bu sayede kurulum aracı, sunucuya manifest ucu eklenmeden önce de çalışır.</summary>
    private async Task<SetupManifest> ManifestAlAsync(HttpClient http)
    {
        try
        {
            var r = await http.GetAsync($"{_server}/api/setup/manifest");
            if (r.IsSuccessStatusCode)
                return SetupManifestReader.Parse(await r.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException) { /* uç yok/erişilemedi → geri düşüş */ }
        catch (TaskCanceledException) { /* zaman aşımı → geri düşüş */ }

        return SetupManifestReader.FromReleasesLatest(
            await http.GetStringAsync($"{_server}/api/releases/latest"));
    }

    private async Task DownloadAsync(HttpClient http, string url, string dest)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var fs = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0; int n;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastMs = 0, lastBytes = 0; string speed = "";
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            var elapsed = sw.ElapsedMilliseconds;
            if (elapsed - lastMs >= 400)
            {
                var dSec = (elapsed - lastMs) / 1000.0;
                if (dSec > 0)
                {
                    var bps = (read - lastBytes) / dSec;
                    speed = bps >= 1024 * 1024 ? $"{bps / 1024 / 1024:0.0} MB/sn" : $"{bps / 1024:0} KB/sn";
                }
                lastMs = elapsed; lastBytes = read;
            }
            if (total > 0)
            {
                var pct = (int)(read * 100 / total);
                SetProgress(pct);
                SetStatus($"İndiriliyor… %{pct}  ({read / 1024 / 1024} / {total / 1024 / 1024} MB)" + (speed == "" ? "" : $"  •  {speed}"));
            }
            else SetStatus($"İndiriliyor… {read / 1024 / 1024} MB" + (speed == "" ? "" : $"  •  {speed}"));
        }
    }

    private void ExtractWithProgress(string zipPath, string targetDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        int total = archive.Entries.Count, i = 0;
        var rootFull = Path.GetFullPath(targetDir);
        foreach (var entry in archive.Entries)
        {
            i++;
            var destPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip koruması
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destPath); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
            if (total > 0 && i % 5 == 0)
            {
                var pct = (int)(i * 100L / total);
                SetProgress(pct);
                SetStatus($"Kuruluyor… %{pct}");
            }
        }
    }

    /// <summary>Gerçek makineden ön-koşul verisi okur. Saf mantık <see cref="SetupPrerequisites"/>'tedir;
    /// bu sınıf yalnız işletim sistemine dokunur → mantık testlerde sahte sorgulayıcıyla çalışabilir.</summary>
    private sealed class WindowsSystemProbe : ISystemProbe
    {
        private readonly bool _networkKnownGood;

        /// <param name="networkKnownGood">Kurulum tanımı sunucudan ZATEN indirildiyse true.
        /// Ayrıca bir "ağ var mı" isteği atılmaz: hem gereksiz, hem de sunucu HEAD'e 405 döndürdüğü
        /// için yanlış negatif üretme riski var (doğrulandı, 2026-09-04).</param>
        public WindowsSystemProbe(bool networkKnownGood) => _networkKnownGood = networkKnownGood;

        public int OsBuild => Environment.OSVersion.Version.Build;

        public string Architecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();

        public long AvailableFreeBytes(string path)
        {
            try
            {
                var kok = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(kok)) return -1;
                return new DriveInfo(kok).AvailableFreeSpace;
            }
            catch { return -1; }   // ölçülemiyorsa engelleme
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

        public bool NetworkAvailable => _networkKnownGood;
    }

    private static void TryCreateShortcut(string exePath, string workingDir)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var lnk = Path.Combine(desktop, "Alpnex.lnk");
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            var sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exePath;
            sc.WorkingDirectory = workingDir;
            sc.Description = "Alpnex";
            sc.Save();
        }
        catch { /* kısayol başarısız olsa da kurulum tamam */ }
    }
}
