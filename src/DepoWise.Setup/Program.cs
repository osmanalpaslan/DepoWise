using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

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

        Text = "DepoWise Kurulum";
        Width = 560; Height = 320;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
        Font = new System.Drawing.Font("Segoe UI", 9.5f);
        ForeColor = System.Drawing.Color.White;

        _title = new Label { Text = "DepoWise Kurulum", Left = 24, Top = 20, Width = 500, Height = 30,
            Font = new System.Drawing.Font("Segoe UI", 16f, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(59, 130, 246) };
        _serverLbl = new Label { Text = "Sunucu: " + _server, Left = 24, Top = 56, Width = 500, Height = 20,
            ForeColor = System.Drawing.Color.FromArgb(148, 163, 184) };

        var lbl = new Label { Text = "Kurulum klasörü:", Left = 24, Top = 92, Width = 500, Height = 20 };
        _dir = new TextBox { Left = 24, Top = 114, Width = 400, Height = 26,
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "app") };
        _browse = new Button { Text = "Gözat…", Left = 432, Top = 113, Width = 90, Height = 28, FlatStyle = FlatStyle.Flat };
        _browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog() == DialogResult.OK) _dir.Text = Path.Combine(d.SelectedPath, "DepoWise");
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
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "app")
            : _dir.Text.Trim();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

            SetStatus("Sunucuya bağlanılıyor, sürüm bilgisi alınıyor…");
            var metaJson = await http.GetStringAsync($"{_server}/api/releases/latest");
            if (string.IsNullOrWhiteSpace(metaJson) || metaJson == "null")
                throw new Exception("Sunucuda kurulum paketi bulunamadı (yönetici henüz sürüm yayınlamamış).");

            using var doc = JsonDocument.Parse(metaJson);
            var root = doc.RootElement;
            var version = root.GetProperty("version").GetString() ?? "?";
            var downloadUrl = root.TryGetProperty("downloadUrl", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(downloadUrl)) throw new Exception("Paket indirme adresi yok.");
            if (downloadUrl.StartsWith("/")) downloadUrl = _server + downloadUrl;

            var tmpZip = Path.Combine(Path.GetTempPath(), $"depowise-{version}.zip");
            SetStatus($"Sürüm {version} indiriliyor…");
            await DownloadAsync(http, downloadUrl!, tmpZip);

            SetStatus("Kuruluyor…");
            Directory.CreateDirectory(installDir);
            ExtractWithProgress(tmpZip, installDir);
            try { File.Delete(tmpZip); } catch { }

            File.WriteAllText(Path.Combine(installDir, "serverurl.txt"), _server);

            var exe = Path.Combine(installDir, "DepoWise.Desktop.exe");
            if (File.Exists(exe)) TryCreateShortcut(exe, installDir);

            SetProgress(100);
            SetStatus($"Kurulum tamamlandı (sürüm {version}). Masaüstündeki 'DepoWise' kısayolundan açabilirsiniz.");
            MessageBox.Show(this, $"Kurulum tamamlandı (sürüm {version}).\nMasaüstündeki 'DepoWise' kısayolundan açabilirsiniz.",
                "DepoWise Kurulum", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _done = true;
            _install.Text = "Kapat";
            _install.BackColor = System.Drawing.Color.FromArgb(59, 130, 246);
            _install.Enabled = true;
        }
        catch (Exception ex)
        {
            SetStatus("HATA: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Kurulum Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true; _browse.Enabled = true; _dir.Enabled = true; _busy = false;
        }
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

    private static void TryCreateShortcut(string exePath, string workingDir)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var lnk = Path.Combine(desktop, "DepoWise.lnk");
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            var sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exePath;
            sc.WorkingDirectory = workingDir;
            sc.Description = "DepoWise";
            sc.Save();
        }
        catch { /* kısayol başarısız olsa da kurulum tamam */ }
    }
}
