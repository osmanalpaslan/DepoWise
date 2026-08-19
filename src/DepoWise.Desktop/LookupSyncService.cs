using DepoWise.Infrastructure.Database;
using System;
using System.IO;
using System.Linq;
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
            ApplyMenuConfig(conn, tx, root, companyId!, now);   // MNU-B1: ekran ayarlari yerele iner
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
            ApplyMenuConfig(conn, tx, root, companyId!, now);   // MNU-B1: ekran ayarlari yerele iner
            tx.Commit();
            progress?.Invoke(100);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// ═══ MNU-B1 DÜZELTMESİ (2026-08-18) — EKRAN AYARLARINI YERELE İNDİR ═══
    ///
    /// Ekran platform görünürlüğü ve menü düzeni SUNUCU OTORİTELİ yapılandırmadır: masaüstü bunları
    /// asla yazmaz. Bu yüzden burada <b>DEĞİŞTİRME (replace)</b> uygulanır — upsert değil: sunucuda
    /// KALDIRILAN bir ayar yerelde de düşmeli, yoksa bir kez kapatılan ekran bir daha açılamazdı.
    ///
    /// <b>ÇEVRİMDIŞI GÜVENLİĞİ:</b> alan yanıtta hiç YOKSA (eski sunucu) yerele DOKUNULMAZ — mevcut
    /// ayar korunur. Sunucuya hiç ulaşılamadığında zaten bu metoda gelinmez (çağıran sessizce atlar)
    /// → çevrimdışı masaüstü en son inen ayarla çalışmaya devam eder, hiç inmediyse katalog varsayılanı.
    /// </summary>
    private static void ApplyMenuConfig(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        JsonElement root, string companyId, long now)
    {
        Replace("screenVisibility", "screen_platform_visibility",
            new[] { "screen_key", "platform", "enabled" },
            new[] { "screen_key", "platform", "enabled" });

        Replace("menuLayoutScreens", "screen_menu_layout",
            new[] { "screen_key", "label_override", "group_key_override", "sort_order" },
            new[] { "screen_key", "label_override", "group_key_override", "sort_order" });

        // SEC (2026-08-19): parent_group_key = üst grup bağı (menünün 3. seviyesi). Kolon yalnız
        // Migration071 uygulanmış yerel veritabanında vardır; yoksa o alan atlanır (aşağıdaki
        // Replace, hedef tabloda bulunmayan kolonu yazmaya çalışmaz).
        Replace("menuLayoutGroups", "menu_group_layout",
            new[] { "group_key", "title_override", "sort_order", "is_custom", "parent_group_key" },
            new[] { "group_key", "title_override", "sort_order", "is_custom", "parent_group_key" });

        // Masaüstü süreç içi önbellekleri düşür → menü bir sonraki çiziminde yeni ayarı görür.
        DepoWise.Infrastructure.Organization.ScreenVisibilityService.Invalidate(companyId);
        DepoWise.Infrastructure.Organization.MenuLayoutService.Invalidate(companyId);

        void Replace(string jsonKey, string table, string[] allCols, string[] allJsonCols)
        {
            if (!root.TryGetProperty(jsonKey, out var arr) || arr.ValueKind != JsonValueKind.Array) return;

            // ⚠️ Yerel tabloda BULUNMAYAN kolonlar atlanır. Sunucu yeni bir alan göndermeye başladığında
            // (ör. SEC/parent_group_key) migration'ı henüz uygulanmamış eski bir yerel veritabanında
            // INSERT tümüyle patlar ve o firmanın TÜM menü ayarı sessizce kaybolurdu. Süzgeç bunu
            // engeller: bilinen kolonlar yazılır, bilinmeyen alan yok sayılır.
            var tutulan = Enumerable.Range(0, allCols.Length)
                .Where(i => DepoWise.Infrastructure.Database.DbIntrospect.ColumnExists(conn, tx, table, allCols[i]))
                .ToArray();
            if (tutulan.Length == 0) return;
            var cols = tutulan.Select(i => allCols[i]).ToArray();
            var jsonCols = tutulan.Select(i => allJsonCols[i]).ToArray();

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {table} WHERE company_id=@c;";
                var p = del.CreateParameter(); p.ParameterName = "@c"; p.Value = companyId; del.Parameters.Add(p);
                del.ExecuteNonQuery();
            }

            foreach (var row in arr.EnumerateArray())
            {
                try
                {
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    var names = string.Join(",", cols);
                    var holes = string.Join(",", cols.Select((_, i) => "@v" + i));
                    ins.CommandText = $"INSERT INTO {table}(id,company_id,{names},created_at,updated_at) " +
                                      $"VALUES(@id,@c,{holes},@now,@now);";
                    Add(ins, "@id", Guid.NewGuid().ToString("N"));
                    Add(ins, "@c", companyId);
                    for (int i = 0; i < cols.Length; i++)
                    {
                        object? v = null;
                        if (row.TryGetProperty(jsonCols[i], out var el))
                            v = el.ValueKind switch
                            {
                                JsonValueKind.String => el.GetString(),
                                JsonValueKind.Number => el.TryGetInt64(out var n) ? n : el.GetDouble(),
                                JsonValueKind.True => 1L,
                                JsonValueKind.False => 0L,
                                _ => null,
                            };
                        Add(ins, "@v" + i, v);
                    }
                    Add(ins, "@now", now);
                    ins.ExecuteNonQuery();
                }
                catch { /* tek bozuk satır tüm ayarı düşürmesin */ }
            }
        }

        static void Add(System.Data.Common.DbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
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
