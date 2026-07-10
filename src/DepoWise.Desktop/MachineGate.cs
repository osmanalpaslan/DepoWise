using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// Makine erişim kapısı. Sunucu bu makineyi 'revoked' (pasif) işaretlerse giriş engellenir.
/// Çevrimiçi: /api/machines/register durum döner + önbelleğe (machine_status.txt) yazılır.
/// Çevrimdışı: son bilinen durum kullanılır (pasif kaldıysa yine engel). Süper admin aktifleştirip
/// makine tekrar çevrimiçi olduğunda durum 'active/pending' döner ve giriş açılır.
/// </summary>
public static class MachineGate
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Makine durum + atanmış şube kontrolü sonucu.</summary>
    public sealed record MachineCheck(bool Allowed, string Reason, string? Status, string? BranchId, string? BranchName, bool Online);

    private static string StatusFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "machine_status.txt");
    private static string BranchFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "machine_branch.txt");

    public static async Task<MachineCheck> CheckAsync(string companyId)
    {
        var url = ResolveServerUrl();
        string? status = null, branchId = null, branchName = null;
        bool online = false;

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                // Makine şubesi ARTIK login şubesinden yazılmaz (admin atar) — payload'da göndermiyoruz.
                var json = JsonSerializer.Serialize(new { companyId, machineName = Environment.MachineName });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(url!.TrimEnd('/') + "/api/machines/register", content);
                if (resp.IsSuccessStatusCode)
                {
                    online = true;
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
                    branchId = root.TryGetProperty("branchId", out var bi) && bi.ValueKind != JsonValueKind.Null ? bi.GetString() : null;
                    branchName = root.TryGetProperty("branchName", out var bn) && bn.ValueKind != JsonValueKind.Null ? bn.GetString() : null;
                    if (status is not null) TryWrite(StatusFile, status);
                    TryWrite(BranchFile, $"{branchId}|{branchName}"); // çevrimdışı için önbelleğe al (boş da olabilir)
                }
            }
            catch { /* çevrimdışı → önbelleğe düş */ }
        }

        if (!online) // çevrimdışı: son bilinen durum + şube önbellekten
        {
            status = TryRead(StatusFile);
            var (cbId, cbName) = ReadBranchCache();
            branchId = cbId; branchName = cbName;
        }

        // Makine şubesini uygulama geneline yaz (ana ekran + çevrimdışı otomatik giriş kullanır).
        DesktopServices.MachineBranchId = string.IsNullOrWhiteSpace(branchId) ? null : branchId;
        DesktopServices.MachineBranchName = string.IsNullOrWhiteSpace(branchName) ? null : branchName;

        if (string.Equals(status, "revoked", StringComparison.OrdinalIgnoreCase))
            return new MachineCheck(false, "Bu makine pasife alınmış. Girişe kapalı. İnternete bağlanıp süper adminin makineyi aktifleştirmesi gerekir.", status, branchId, branchName, online);

        if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
            return new MachineCheck(false, "Bu makine firmanın makine kotasını aştığı için onay bekliyor. Süper adminin bu makineyi onaylaması (veya başka bir makineyi pasife alması) gerekir.", status, branchId, branchName, online);

        // status 'active' → izin. Çevrimdışı ve hiç durum yoksa (null) engelleme (ilk kurulum senaryosu).
        return new MachineCheck(true, "", status, branchId, branchName, online);
    }

    private static (string? BranchId, string? BranchName) ReadBranchCache()
    {
        var raw = TryRead(BranchFile);
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var i = raw.IndexOf('|');
        if (i < 0) return (raw, null);
        var id = raw[..i]; var name = raw[(i + 1)..];
        return (string.IsNullOrWhiteSpace(id) ? null : id, string.IsNullOrWhiteSpace(name) ? null : name);
    }

    private static void TryWrite(string path, string value)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); } catch { }
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch { return null; }
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(path))
            {
                var v = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }
        return "https://depowise-erp.fly.dev"; // serverurl.txt yoksa varsayılan bulut
    }
}
