using DepoWise.Infrastructure.Database;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Application.Theming;

namespace DepoWise.Desktop;

/// <summary>
/// Tanım (lookup) senkronu: giriş sonrası sunucudan firmanın TÜM tanımlarını çekip yerele yazar
/// (id korunur → ad değişimi propagate olur; yeni tanımlar eklenir). Web'te oluşturulan/yeniden
/// adlandırılan tanımlar böylece tüm makinelerde görünür. Çevrimdışı/sunucusuz sessizce atlar.
/// </summary>
public static class LookupSyncService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Giriş bilgileriyle sunucudan tanımları çeker ve yerele upsert eder. Hata olursa sessiz.</summary>
    public static async Task PullAsync(string username, string password)
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var baseUrl = url!.TrimEnd('/');
            // 1) JWT al
            using var loginContent = new StringContent(
                JsonSerializer.Serialize(new { username, password }), Encoding.UTF8, "application/json");
            using var loginResp = await _http.PostAsync(baseUrl + "/api/auth/login", loginContent);
            if (!loginResp.IsSuccessStatusCode) return;
            using var loginDoc = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
            var token = loginDoc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(token)) return;

            // 2) Tanımları çek
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/lookups/sync");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var companyId = root.TryGetProperty("companyId", out var cid) ? cid.GetString() : null;
            if (string.IsNullOrWhiteSpace(companyId)) return;

            // 3) Yerele upsert (tek transaction)
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var conn = DesktopServices.Factory.Create();
            using var tx = conn.BeginTransaction();
            Upsert(conn, tx, "units", root, "units", companyId!, now);
            Upsert(conn, tx, "suppliers", root, "suppliers", companyId!, now);
            Upsert(conn, tx, "vehicle_types", root, "vehicleTypes", companyId!, now);
            Upsert(conn, tx, "vehicle_categories", root, "vehicleCategories", companyId!, now);
            Upsert(conn, tx, "material_categories", root, "materialCategories", companyId!, now, ("parent_id", "parentId"));
            Upsert(conn, tx, "brands", root, "brands", companyId!, now, ("brand_type", "brandType"));
            Upsert(conn, tx, "vehicle_models", root, "vehicleModels", companyId!, now, ("brand_id", "brandId"));
            Upsert(conn, tx, "branches", root, "branches", companyId!, now, ("kind", "kind"), ("parent_id", "parentId"));
            tx.Commit();
        }
        catch { /* senkron başarısızsa giriş akışı etkilenmez */ }
    }

    /// <summary>ELLE Eşitle: saklı JWT ile sunucudan tanımları çekip yerele yazar; % ilerleme bildirir.
    /// Başarılıysa true. Token yoksa/çevrimdışıysa false.</summary>
    public static async Task<bool> SyncNowAsync(Action<int>? progress = null)
    {
        var token = ServerAuthClient.Token;
        var baseUrl = ServerAuthClient.BaseUrl;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(baseUrl)) return false;
        try
        {
            progress?.Invoke(10);
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl!.TrimEnd('/') + "/api/lookups/sync");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return false;
            progress?.Invoke(35);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var companyId = root.TryGetProperty("companyId", out var cid) ? cid.GetString() : null;
            if (string.IsNullOrWhiteSpace(companyId)) return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var conn = DesktopServices.Factory.Create();
            using var tx = conn.BeginTransaction();
            Upsert(conn, tx, "units", root, "units", companyId!, now);
            Upsert(conn, tx, "suppliers", root, "suppliers", companyId!, now);
            Upsert(conn, tx, "vehicle_types", root, "vehicleTypes", companyId!, now);
            Upsert(conn, tx, "vehicle_categories", root, "vehicleCategories", companyId!, now);
            progress?.Invoke(65);
            Upsert(conn, tx, "material_categories", root, "materialCategories", companyId!, now, ("parent_id", "parentId"));
            Upsert(conn, tx, "brands", root, "brands", companyId!, now, ("brand_type", "brandType"));
            Upsert(conn, tx, "vehicle_models", root, "vehicleModels", companyId!, now, ("brand_id", "brandId"));
            Upsert(conn, tx, "branches", root, "branches", companyId!, now, ("kind", "kind"), ("parent_id", "parentId"));
            tx.Commit();
            progress?.Invoke(100);
            return true;
        }
        catch { return false; }
    }

    private static void Upsert(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        string table, JsonElement root, string jsonKey, string companyId, long now, params (string Col, string JKey)[] extra)
    {
        if (!root.TryGetProperty(jsonKey, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var row in arr.EnumerateArray())
        {
            try
            {
                var id = Str(row, "id"); var name = Str(row, "name");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

                // Ekstra kolon değerleri
                var extraCols = extra;
                object?[] extraVals = new object?[extraCols.Length];
                for (int i = 0; i < extraCols.Length; i++)
                {
                    var v = StrOrNull(row, extraCols[i].JKey);
                    if (extraCols[i].Col == "kind" && string.IsNullOrEmpty(v)) v = "branch";
                    extraVals[i] = (object?)v ?? DBNull.Value;
                }

                // Önce id ile GÜNCELLE (ad değişimi propagate)
                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    var setExtra = "";
                    for (int i = 0; i < extraCols.Length; i++) setExtra += $", {extraCols[i].Col}=@e{i}";
                    upd.CommandText = $"UPDATE {table} SET name=@n, is_deleted=0, updated_at=@now{setExtra} WHERE id=@id;";
                    upd.AddWithValue("@n", name);
                    upd.AddWithValue("@now", now);
                    upd.AddWithValue("@id", id);
                    for (int i = 0; i < extraCols.Length; i++) upd.AddWithValue($"@e{i}", extraVals[i]!);
                    if (upd.ExecuteNonQuery() > 0) continue; // güncellendi
                }

                // Yoksa EKLE (ad benzersizlik çakışması olursa yut → yerelde başka id ile zaten var)
                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    var cols = "id, company_id, name"; var vals = "@id,@c,@n";
                    for (int i = 0; i < extraCols.Length; i++) { cols += $", {extraCols[i].Col}"; vals += $",@e{i}"; }
                    cols += ", created_at, updated_at, version, is_deleted"; vals += ",@now,@now,1,0";
                    ins.CommandText = $"INSERT INTO {table}({cols}) VALUES({vals});";
                    ins.AddWithValue("@id", id);
                    ins.AddWithValue("@c", companyId);
                    ins.AddWithValue("@n", name);
                    ins.AddWithValue("@now", now);
                    for (int i = 0; i < extraCols.Length; i++) ins.AddWithValue($"@e{i}", extraVals[i]!);
                    try { ins.ExecuteNonQuery(); } catch { /* ad çakışması → atla */ }
                }
            }
            catch { /* tek satır hatası diğerlerini bozmaz */ }
        }
    }

    private static string Str(JsonElement row, string key)
        => row.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    private static string? StrOrNull(JsonElement row, string key)
    { var s = Str(row, key); return string.IsNullOrEmpty(s) ? null : s; }

    private static string? ResolveServerUrl()
    {
        try
        {
            var companyId = DesktopServices.DefaultCompanyId;
            var s = DesktopServices.Settings.Get(companyId, SettingKeys.UpdateServerUrl);
            if (!string.IsNullOrWhiteSpace(s)) return s;
            var path = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(path))
            {
                var v = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }
        return "https://depowise-erp.fly.dev";
    }
}
