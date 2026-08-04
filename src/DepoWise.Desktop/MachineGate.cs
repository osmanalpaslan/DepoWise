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

    /// <summary>Makine durum + firma + atanmış şube kontrolü sonucu.</summary>
    public sealed record MachineCheck(bool Allowed, string Reason, string? Status, string? BranchId, string? BranchName, bool Online, string? CompanyId = null, string? CompanyName = null);

    private static string StatusFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "machine_status.txt");
    private static string BranchFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "machine_branch.txt");

    public static async Task<MachineCheck> CheckAsync(string companyId)
    {
        var url = ResolveServerUrl();
        string? status = null, branchId = null, branchName = null, mCompanyId = null, mCompanyName = null;
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
                    static string? Str(JsonElement r, string p) => r.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;
                    status = Str(root, "status");
                    branchId = Str(root, "branchId"); branchName = Str(root, "branchName");
                    mCompanyId = Str(root, "companyId"); mCompanyName = Str(root, "companyName");
                    if (status is not null) TryWrite(StatusFile, status);
                    // çevrimdışı için önbelleğe al: branchId|branchName|companyId|companyName
                    TryWrite(BranchFile, $"{branchId}|{branchName}|{mCompanyId}|{mCompanyName}");
                }
            }
            catch { /* çevrimdışı → önbelleğe düş */ }
        }

        if (!online) // çevrimdışı: son bilinen durum + şube/firma önbellekten
        {
            status = TryRead(StatusFile);
            (branchId, branchName, mCompanyId, mCompanyName) = ReadInfoCache();
        }

        // Makine firması + şubesini uygulama geneline yaz (ana ekran + çevrimdışı oto-giriş + süper admin seçenekleri).
        DesktopServices.MachineBranchId = string.IsNullOrWhiteSpace(branchId) ? null : branchId;
        DesktopServices.MachineBranchName = string.IsNullOrWhiteSpace(branchName) ? null : branchName;
        DesktopServices.MachineCompanyId = string.IsNullOrWhiteSpace(mCompanyId) ? null : mCompanyId;
        DesktopServices.MachineCompanyName = string.IsNullOrWhiteSpace(mCompanyName) ? null : mCompanyName;

        if (string.Equals(status, "revoked", StringComparison.OrdinalIgnoreCase))
            return new MachineCheck(false, "Bu makine pasife alınmış. Girişe kapalı. İnternete bağlanıp süper adminin makineyi aktifleştirmesi gerekir.", status, branchId, branchName, online, mCompanyId, mCompanyName);

        if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
            return new MachineCheck(false, "Bu makine firmanın makine kotasını aştığı için onay bekliyor. Süper adminin bu makineyi onaylaması (veya başka bir makineyi pasife alması) gerekir.", status, branchId, branchName, online, mCompanyId, mCompanyName);

        // status 'active' → izin. Çevrimdışı ve hiç durum yoksa (null) engelleme (ilk kurulum senaryosu).
        return new MachineCheck(true, "", status, branchId, branchName, online, mCompanyId, mCompanyName);
    }

    private static (string? BranchId, string? BranchName, string? CompanyId, string? CompanyName) ReadInfoCache()
    {
        var raw = TryRead(BranchFile);
        if (string.IsNullOrWhiteSpace(raw)) return (null, null, null, null);
        var parts = raw.Split('|');
        string? At(int i) => i < parts.Length && !string.IsNullOrWhiteSpace(parts[i]) ? parts[i] : null;
        return (At(0), At(1), At(2), At(3));
    }

    /// <summary>ADR-085 — makine tanımı sıfırlandığında yerel önbellek dosyalarını (durum + firma/şube) siler.
    /// Sunucudaki DesktopServices.MachineCompanyId/MachineBranchId alanları çağıran tarafça ayrıca null'lanır.</summary>
    public static void ClearCache()
    {
        try { File.Delete(StatusFile); } catch { }
        try { File.Delete(BranchFile); } catch { }
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
