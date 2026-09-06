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
            Upsert(conn, tx, "material_categories", root, "materialCategories", companyId!, now, "parent_id");
            Upsert(conn, tx, "brands", root, "brands", companyId!, now, "brand_type");
            Upsert(conn, tx, "vehicle_models", root, "vehicleModels", companyId!, now, "brand_id");
            Upsert(conn, tx, "branches", root, "branches", companyId!, now, "kind", "parent_id");
            ApplyMenuConfig(conn, tx, root, companyId!, now);   // MNU-B1: ekran ayarlari yerele iner
            ApplyTeams(conn, tx, root, companyId!, now);        // ARA IS 5 / ALT FAZ 1: ekip aynasi
            ApplyHierarchy(conn, tx, root, companyId!, now);    // ARA IS 5 / ALT FAZ 2: hiyerarsi aynasi
            tx.Commit();
        }
        catch { /* senkron başarısızsa giriş akışı etkilenmez */ }
    }

    // ═══ H7 DÜZELTMESİ (kullanıcı bildirimi 2026-09-06) ═══════════════════════════════════════════
    //
    // KULLANICI: "webte ekip tanımı yaptım ama masaüstüne kayıt atmadı."
    //
    // ÖLÇÜM: sunucu ucu (/api/lookups/sync) ekibi DOĞRU gönderiyordu ve masaüstü aynası da onu
    // DOĞRU yazıyordu — giriş yapıldığında ekip yerele iniyor (uçtan uca doğrulandı). Eksik olan
    // ZAMANLAMAYDI: tanımlar YALNIZ girişte ve elle "Eşitle"de çekiliyordu. Program açıkken web'de
    // açılan ekip/tanım, kullanıcı çıkıp girene ya da Eşitle'ye basana kadar görünmüyordu.
    //
    // Şubeler için aynı sorun daha önce SNK-12'de çözülmüştü (BranchMirror.RefreshAsync senkron
    // turuna eklenmişti). Burada AYNI kanıtlanmış desen tanımların tamamına uygulanır:
    // senkron turu <see cref="RefreshAsync"/> çağırır, <see cref="MinInterval"/> ile kısılır ve
    // yanıt DEĞİŞMEDİYSE yerele hiç dokunulmaz (gereksiz yazma ve ekran yenilemesi olmaz).

    /// <summary>Otomatik turda en fazla bu sıklıkta çekilir (tanımlar küçük ve seyrek değişir).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(2);
    private static DateTimeOffset _sonYenileme = DateTimeOffset.MinValue;
    /// <summary>Son uygulanan yanıtın imzası — aynısı geldiyse yerele YAZILMAZ.</summary>
    private static string? _sonImza;

    /// <summary>
    /// Senkron turundan çağrılır. <b>true</b> yalnızca yerele GERÇEKTEN yeni tanım yazıldığında döner
    /// (çağıran o zaman açık ekranı yeniler). Çevrimdışıysa/değişiklik yoksa false — sessiz.
    /// </summary>
    public static async Task<bool> RefreshAsync(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _sonYenileme < MinInterval) return false;
        _sonYenileme = DateTimeOffset.UtcNow;
        return await CekVeUygulaAsync(null, yalnizDegistiyse: true);
    }

    /// <summary>ELLE Eşitle: saklı JWT ile sunucudan tanımları çekip yerele yazar; % ilerleme bildirir.
    /// Başarılıysa true. Token yoksa/çevrimdışıysa false.
    /// (Elle eşitlemede imza kontrolü YAPILMAZ: kullanıcı "yenile" dediyse yerel bozulmuş olabilir.)</summary>
    public static Task<bool> SyncNowAsync(Action<int>? progress = null)
        => CekVeUygulaAsync(progress, yalnizDegistiyse: false);

    private static async Task<bool> CekVeUygulaAsync(Action<int>? progress, bool yalnizDegistiyse)
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
            var govde = await resp.Content.ReadAsStringAsync();

            // H7: otomatik turda yanıt bir öncekiyle AYNIYSA yerele hiç dokunma. Böylece tanımlar
            // 2 dakikada bir kontrol edilir ama yalnız GERÇEKTEN değiştiğinde yazılır ve ekran yenilenir.
            var imza = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(govde)));
            if (yalnizDegistiyse && imza == _sonImza) return false;

            using var doc = JsonDocument.Parse(govde);
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
            Upsert(conn, tx, "material_categories", root, "materialCategories", companyId!, now, "parent_id");
            Upsert(conn, tx, "brands", root, "brands", companyId!, now, "brand_type");
            Upsert(conn, tx, "vehicle_models", root, "vehicleModels", companyId!, now, "brand_id");
            Upsert(conn, tx, "branches", root, "branches", companyId!, now, "kind", "parent_id");
            ApplyMenuConfig(conn, tx, root, companyId!, now);   // MNU-B1: ekran ayarlari yerele iner
            ApplyTeams(conn, tx, root, companyId!, now);        // ARA IS 5 / ALT FAZ 1: ekip aynasi
            ApplyHierarchy(conn, tx, root, companyId!, now);    // ARA IS 5 / ALT FAZ 2: hiyerarsi aynasi
            tx.Commit();
            _sonImza = imza;   // H7: ancak BAŞARIYLA yazıldıktan sonra imzalanır (yarım kalan tur tekrar denenir)
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

        // 2026-09-03 (kullanıcı isteği): alan zorunluluğu — screenVisibility ile AYNI kurallar
        // (sunucu otoriteli, replace; alan yanıtta yoksa/tablo yerelde yoksa DOKUNULMAZ).
        Replace("fieldRequirements", "field_requirements",
            new[] { "screen_key", "field_key", "required" },
            new[] { "screen_key", "field_key", "required" });

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
        DepoWise.Infrastructure.Organization.FieldRequirementService.Invalidate(companyId);   // 2026-09-03

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

    /// <summary>
    /// Sunucudan gelen tanim satirlarini yerele yazar.
    ///
    /// <b>TSN duzeltmesi (2026-08-27).</b> <paramref name="extra"/> artik JSON adi degil, VERITABANI
    /// SUTUN adidir (<c>brand_id</c>) ve <see cref="DepoWise.Application.Common.JsonAlan.AlanOku"/> ile
    /// toleransli okunur. Eskiden burada camelCase ad araniyordu (<c>brandId</c>); sunucu ise sozluk
    /// anahtarini sutun adiyla gonderiyor. <c>TryGetProperty</c> harf duyarli oldugu icin alan HIC
    /// bulunamiyor, "bos geldi" saniliyor ve asagidaki UPDATE sutunu <c>NULL</c>&apos;a cekiyordu:
    /// arac modeli markasini, alt kategori ustunu kaybediyordu. Kayip sonra push ile SUNUCUYA da
    /// tasiniyordu (yerel <c>updated_at</c> "simdi" damgalandigi icin LWW yerel satiri yeni sayar).
    /// </summary>
    /// <summary>
    /// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — EKİP AYNASINI YERELE İNDİR ═══
    ///
    /// Ekip verisi <b>sunucu otoritelidir</b>: masaüstü ekip/üyelik YAZMAZ, yalnız okur. Bu yüzden
    /// menü ayarlarındaki gibi <b>DEĞİŞTİRME (replace)</b> uygulanır — upsert değil: sunucuda silinen
    /// ekip ya da çıkarılan üye yerelde de düşmeli. LWW veya çakışma modeli KURULMAZ ve
    /// <c>sync_outbox</c>'a hiçbir şey yazılmaz.
    ///
    /// <b>Genel <see cref="Upsert"/> neden kullanılmıyor:</b> o yardımcı her satırda <c>name</c>
    /// kolonu bekler; <c>team_members</c>'ta <c>name</c> yoktur.
    ///
    /// <b>ÇEVRİMDIŞI / ESKİ SÜRÜM GÜVENLİĞİ:</b> alan yanıtta hiç YOKSA (eski sunucu) yerele
    /// DOKUNULMAZ; yerel veritabanında tablo YOKSA (Migration084 uygulanmamış eski istemci) işlem
    /// sessizce atlanır → eski istemci bozulmaz.
    ///
    /// <b>SIRA:</b> silme üyeden ebeveyne, yazma ebeveynden üyeye — <c>team_members → teams</c> FK'si
    /// yerelde <c>foreign_keys=ON</c> altında zorunludur. <c>user_id</c>/<c>lead_user_id</c> yerelde
    /// FK DEĞİLDİR (<c>users</c> masaüstüne inmez) → kullanıcı satırı olmadan da ayna tutarlıdır.
    /// </summary>
    private static void ApplyTeams(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        JsonElement root, string companyId, long now)
    {
        if (!root.TryGetProperty("teams", out var teams) || teams.ValueKind != JsonValueKind.Array) return;
        if (!DepoWise.Infrastructure.Database.DbIntrospect.TableExists(conn, tx, "teams")) return;
        if (!DepoWise.Infrastructure.Database.DbIntrospect.TableExists(conn, tx, "team_members")) return;

        Sil("team_members");
        Sil("teams");

        foreach (var row in teams.EnumerateArray())
        {
            var id = Str(row, "id");
            var name = Str(row, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
            try
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO teams(id, company_id, name, lead_user_id, is_active, " +
                    "created_at, updated_at, version, is_deleted) VALUES(@i,@c,@n,@l,@a,@now,@now,1,0);";
                ins.AddWithValue("@i", id);
                ins.AddWithValue("@c", companyId);
                ins.AddWithValue("@n", name);
                var lead = Str(row, "lead_user_id");
                ins.AddWithValue("@l", string.IsNullOrEmpty(lead) ? DBNull.Value : lead);
                ins.AddWithValue("@a", Bayrak(row, "is_active", varsayilan: 1L));
                ins.AddWithValue("@now", now);
                ins.ExecuteNonQuery();
            }
            catch { /* tek bozuk satır tüm aynayı düşürmesin */ }
        }

        if (!root.TryGetProperty("teamMembers", out var members) || members.ValueKind != JsonValueKind.Array) return;
        foreach (var row in members.EnumerateArray())
        {
            var id = Str(row, "id");
            var teamId = Str(row, "team_id");
            var userId = Str(row, "user_id");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(userId)) continue;
            try
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO team_members(id, company_id, team_id, user_id, is_lead, " +
                    "created_at, updated_at, version, is_deleted) VALUES(@i,@c,@t,@u,@l,@now,@now,1,0);";
                ins.AddWithValue("@i", id);
                ins.AddWithValue("@c", companyId);
                ins.AddWithValue("@t", teamId);
                ins.AddWithValue("@u", userId);
                ins.AddWithValue("@l", Bayrak(row, "is_lead", varsayilan: 0L));
                ins.AddWithValue("@now", now);
                ins.ExecuteNonQuery();
            }
            catch { /* ebeveyni düşmüş üyelik satırı atlanır */ }
        }

        void Sil(string table)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {table} WHERE company_id=@c;";
            del.AddWithValue("@c", companyId);
            del.ExecuteNonQuery();
        }

        static long Bayrak(JsonElement row, string key, long varsayilan)
        {
            if (!row.TryGetProperty(key, out var v)) return varsayilan;
            return v.ValueKind switch
            {
                JsonValueKind.True => 1L,
                JsonValueKind.False => 0L,
                JsonValueKind.Number => v.TryGetInt64(out var n) && n != 0 ? 1L : 0L,
                _ => varsayilan,
            };
        }
    }

    /// <summary>
    /// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187, PK-EK-02) — HİYERARŞİ AYNASINI YERELE İNDİR ═══
    ///
    /// Ekip aynasıyla AYNI sözleşme: <b>sunucu otoriteli</b>, masaüstü YAZMAZ, <b>replace</b> semantiği
    /// (sunucuda kaldırılan ilişki yerelde de düşer), sunucu kimlikleri korunur, tablo yoksa
    /// <b>sessizce atlanır</b> (Migration085 uygulanmamış eski istemci bozulmaz).
    ///
    /// <b>ONAY BURAYA GİRMEZ:</b> <c>approval_instance</c>/<c>approval_step</c> hiçbir senkron yoluna
    /// dâhil değildir — onay yalnız çevrimiçi ve yalnız sunucuda yürür (PK-EK-05 / İK-9).
    /// Hiyerarşi yalnız GÖRÜNÜRLÜK içindir; masaüstünde onay kararı üretmez.
    /// </summary>
    private static void ApplyHierarchy(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        JsonElement root, string companyId, long now)
    {
        if (!root.TryGetProperty("userHierarchy", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        if (!DepoWise.Infrastructure.Database.DbIntrospect.TableExists(conn, tx, "user_hierarchy")) return;

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM user_hierarchy WHERE company_id=@c;";
            del.AddWithValue("@c", companyId);
            del.ExecuteNonQuery();
        }

        foreach (var row in arr.EnumerateArray())
        {
            var id = Str(row, "id");
            var userId = Str(row, "user_id");
            var managerId = Str(row, "manager_user_id");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(managerId)) continue;
            try
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO user_hierarchy(id, company_id, user_id, manager_user_id, " +
                    "created_at, updated_at, version, is_deleted) VALUES(@i,@c,@u,@m,@now,@now,1,0);";
                ins.AddWithValue("@i", id);
                ins.AddWithValue("@c", companyId);
                ins.AddWithValue("@u", userId);
                ins.AddWithValue("@m", managerId);
                ins.AddWithValue("@now", now);
                ins.ExecuteNonQuery();
            }
            catch { /* tek bozuk satır tüm aynayı düşürmesin */ }
        }
    }

    private static void Upsert(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        string table, JsonElement root, string jsonKey, string companyId, long now, params string[] extra)
    {
        if (!root.TryGetProperty(jsonKey, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var row in arr.EnumerateArray())
        {
            try
            {
                var id = Str(row, "id"); var name = Str(row, "name");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

                // Ekstra kolon değerleri — alan adı, sunucunun gönderdiği yazımdan BAĞIMSIZ okunur.
                var extraCols = extra;
                object?[] extraVals = new object?[extraCols.Length];
                for (int i = 0; i < extraCols.Length; i++)
                {
                    var v = DepoWise.Application.Common.JsonAlan.AlanOku(row, extraCols[i]);
                    if (extraCols[i] == "kind" && string.IsNullOrEmpty(v)) v = "branch";
                    extraVals[i] = (object?)v ?? DBNull.Value;
                }

                // Önce id ile GÜNCELLE (ad değişimi propagate)
                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    var setExtra = "";
                    for (int i = 0; i < extraCols.Length; i++) setExtra += $", {extraCols[i]}=@e{i}";
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
                    for (int i = 0; i < extraCols.Length; i++) { cols += $", {extraCols[i]}"; vals += $",@e{i}"; }
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
