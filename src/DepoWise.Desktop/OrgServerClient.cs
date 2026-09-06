using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Application.Security;      // ModulePermission
using DepoWise.Infrastructure.Security;   // UserRow

namespace DepoWise.Desktop;

/// <summary>
/// Şube ve KULLANICI SUNUCU-OTORİTELİ işlemleri (2026-07-25, veri kaybı düzeltmesi). Bu iki tablo masaüstü
/// iş senkronuna DAHİL DEĞİLDİR (kod/şifre/hash taşır) ve her girişte sunucudan aynalanır → masaüstünde yalnız
/// YERELE yazılan şube/kullanıcı sonraki girişte kaybolur. Çözüm: masaüstü ÇEVRİMİÇİYKEN bu işlemleri
/// doğrudan SUNUCU API'sine yapar (web ile aynı uç) → sunucu-otoriteli olur, aynalama korur. Çevrimdışıysa
/// çağıran uyarır (bu işlem çevrimiçi gerektirir); yerele-yaz yapılmaz (aksi halde yine kaybolurdu).
///
/// Result: Offline=true → sunucuya ulaşılamadı (token yok / ağ yok) → "çevrimiçi gerektirir" uyarısı.
///         Error!=null → sunucu reddetti (yetki/validasyon) → mesajı göster. Ok=true → başarılı (Id set).
/// </summary>
public static class OrgServerClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary><paramref name="Status"/> = sunucunun HTTP kodu (0 = ulaşılamadı). 409 → DÜZENLEME KİLİDİ:
    /// kayıt biz formu açtıktan sonra değişti. Sona eklendi → mevcut çağrılar bozulmaz.</summary>
    public sealed record Result(bool Ok, bool Offline, string? Error, string? Id, int Status = 0);
    private static Result OfflineResult => new(false, true, null, null);

    // ── Belgeler (EVR-01 / ADR-165): SUNUCU-OTORİTELİ — belge içeriği senkron paketinde taşınmaz. ──
    public sealed record DocumentItem(string Id, string EntityType, string EntityTypeDisplay, string EntityId,
        string EntityLabel, string Title, string? DocType, long? ValidFrom, long? ValidUntil,
        string? Description, string FileName, string? Mime, long? SizeBytes, long CreatedAt, long Version);

    public static async Task<List<DocumentItem>?> ListDocumentsAsync(string? entityType, string? search)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(entityType)) q.Add("entityType=" + Uri.EscapeDataString(entityType!));
        if (!string.IsNullOrWhiteSpace(search)) q.Add("search=" + Uri.EscapeDataString(search!.Trim()));
        using var doc = await GetJsonAsync("/api/documents" + (q.Count > 0 ? "?" + string.Join("&", q) : ""));
        if (doc is null) return null;
        var list = new List<DocumentItem>();
        foreach (var e in doc.RootElement.EnumerateArray())
            list.Add(new DocumentItem(Str(e, "id"), Str(e, "entityType"), Str(e, "entityTypeDisplay"),
                Str(e, "entityId"), Str(e, "entityLabel"), Str(e, "title"), NullS(e, "docType"),
                Num(e, "validFrom"), Num(e, "validUntil"), NullS(e, "description"), Str(e, "fileName"),
                NullS(e, "mime"), Num(e, "sizeBytes"), Num(e, "createdAt") ?? 0, Num(e, "version") ?? 0));
        return list;
    }

    /// <summary>Genel amaçlı seçenek listesi (id + ad alanı) — belge ekranının "bağlı kayıt" seçicileri için.</summary>
    public static async Task<List<(string Id, string Name)>?> OptionListAsync(string path, string idField, string nameField)
    {
        using var doc = await GetJsonAsync(path);
        if (doc is null) return null;
        var list = new List<(string, string)>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = Str(e, idField);
            if (!string.IsNullOrEmpty(id)) list.Add((id, Str(e, nameField)));
        }
        return list;
    }

    /// <summary>Belge yükleme (multipart form). Meta alanları form alanı olarak gider; boşlar atlanır.</summary>
    public static async Task<Result> UploadDocumentAsync(string fileName, string? mime, byte[] bytes,
        IReadOnlyDictionary<string, string?> fields)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return OfflineResult;
        try
        {
            using var form = new System.Net.Http.MultipartFormDataContent();
            var content = new System.Net.Http.ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime!);
            form.Add(content, "file", fileName);
            foreach (var (k, v) in fields)
                if (!string.IsNullOrEmpty(v)) form.Add(new System.Net.Http.StringContent(v), k);
            using var req = new HttpRequestMessage(HttpMethod.Post, url + "/api/documents") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return new Result(false, false, ExtractError(text, (int)resp.StatusCode), null, (int)resp.StatusCode);
            return new Result(true, false, null, null);
        }
        catch { return OfflineResult; }
    }

    /// <summary>Belge içeriğini indirir (null = çevrimdışı/başarısız).</summary>
    public static async Task<byte[]?> DownloadDocumentAsync(string id)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url + $"/api/documents/{id}/download");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    public static Task<Result> UpdateDocumentMetaAsync(string id, object body)
        => SendOkAsync(HttpMethod.Put, $"/api/documents/{id}", body);
    public static Task<Result> DeleteDocumentAsync(string id)
        => SendOkAsync(HttpMethod.Delete, $"/api/documents/{id}", null);

    // ═══ FOTOĞRAFLAR — SUNUCU OTORİTELİ (ADR-182 · ARA İŞ 2 / S5, PK-F1=A) ═══
    //
    // 🔴 Kapatılan hata: masaüstü fotoğrafı YALNIZ kendi diskine + kendi yerel file_records'una yazıyordu.
    // `file_records` iş senkronunda YOKTUR ve ikili içerik hiçbir pakette taşınmaz → A makinesinde eklenen
    // fotoğrafı B makinesi ve web ASLA göremiyordu (üç ayrı silo). Belgeler (EVR-01) için doğru desen
    // zaten kurulmuştu; fotoğraflar o karardan ÖNCE yazıldığı için yerel yolda kalmıştı.
    //
    // Çözüm: fotoğraf da belgeler gibi SUNUCUDA durur; masaüstü web ile AYNI uçları çağırır. Yeni tablo,
    // yeni migration ve senkron sözleşmesi değişikliği GEREKMEZ.
    // <paramref name="entity"/>: "materials" | "vehicles" (API yol parçası).

    /// <summary>Sunucudaki fotoğraf künyeleri. <c>null</c> = ÇEVRİMDIŞI (hata değil; çağıran yerele düşer).</summary>
    public static async Task<List<RemotePhoto>?> ListPhotosAsync(string entity, string entityId)
    {
        using var doc = await GetJsonAsync($"/api/{entity}/{entityId}/photos");
        if (doc is null) return null;
        var list = new List<RemotePhoto>();
        foreach (var e in doc.RootElement.EnumerateArray())
            list.Add(new RemotePhoto(Str(e, "id"), NullS(e, "sha256")));
        return list;
    }

    /// <summary>Fotoğraf baytları. <c>null</c> = çevrimdışı/başarısız.</summary>
    public static async Task<byte[]?> DownloadPhotoAsync(string entity, string entityId, string fileId)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url + $"/api/{entity}/{entityId}/photos/{fileId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    /// <summary>Fotoğraf yükler (multipart — belge yüklemenin aynısı). Doğrulama/optimizasyon SUNUCUDA yapılır
    /// (7 MB sınırı, JPEG/PNG sihirli bayt kontrolü, yeniden boyutlandırma) → iki platform aynı kuralı uygular.</summary>
    public static async Task<Result> UploadPhotoAsync(string entity, string entityId, string fileName, string? mime, byte[] bytes)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return OfflineResult;
        try
        {
            using var form = new MultipartFormDataContent();
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(mime) ? "image/jpeg" : mime!);
            form.Add(content, "file", fileName);
            using var req = new HttpRequestMessage(HttpMethod.Post, url + $"/api/{entity}/{entityId}/photos") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return new Result(false, false, ExtractError(text, (int)resp.StatusCode), null, (int)resp.StatusCode);
            return new Result(true, false, null, null);
        }
        catch { return OfflineResult; }
    }

    /// <summary>Fotoğrafı sunucudan siler. Yetki kapısı SUNUCUDADIR (files/materials/vehicles → Delete).</summary>
    public static Task<Result> DeletePhotoAsync(string entity, string entityId, string fileId)
        => SendOkAsync(HttpMethod.Delete, $"/api/{entity}/{entityId}/photos/{fileId}", null);

    /// <summary>Sunucudaki bir fotoğrafın kimliği + içerik özeti (mükerrer taşımayı önlemek için).</summary>
    public sealed record RemotePhoto(string Id, string? Sha256);

    private static long? Num(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    // ── Arama (ARA-01 / ADR-174): sunucu-otoriteli Proje+Evrak sonuçları — çevrimdışıysa null
    //    (çağıran "çevrimiçi gerekli" notu gösterir). Yerel kaynaklar buradan GEÇMEZ. ──
    public static async Task<List<DepoWise.Infrastructure.Search.SearchGroup>?> SearchRemoteAsync(string q)
    {
        using var doc = await GetJsonAsync("/api/search?q=" + Uri.EscapeDataString(q) + "&sources=projects,documents");
        if (doc is null) return null;   // çevrimdışı / yetkisiz → çağıran uyarır
        var list = new List<DepoWise.Infrastructure.Search.SearchGroup>();
        foreach (var g in doc.RootElement.EnumerateArray())
        {
            var hits = new List<DepoWise.Infrastructure.Search.SearchHit>();
            if (g.TryGetProperty("hits", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var h in arr.EnumerateArray())
                    hits.Add(new DepoWise.Infrastructure.Search.SearchHit(
                        "", Str(g, "moduleDisplay"), Str(h, "id"), Str(h, "label"), NullS(h, "subLabel"), Str(h, "navigateKey")));
            list.Add(new DepoWise.Infrastructure.Search.SearchGroup(Str(g, "moduleDisplay"), Str(g, "navigateKey"), hits,
                g.TryGetProperty("hasMore", out var hm) && hm.ValueKind == JsonValueKind.True));
        }
        return list;
    }

    // ── Bildirim (BLD-01 / ADR-172): EVRAK bildirimleri sunucu-otoriteli (belgeler masaüstünde yerel değil) —
    //    çevrimdışıysa null (çağıran "çevrimiçi gerekli" notu gösterir). Sunucu, files yetkisi ve kapsamı
    //    KENDİ tarafında süzer (yan kapı yok). Diğer kaynaklar YERELDİR, buradan geçmez. ──
    public sealed record DocumentAlertItem(string Title, string Detail, bool IsCritical, string? EntityId);

    public static async Task<List<DocumentAlertItem>?> ListDocumentAlertsAsync()
    {
        using var doc = await GetJsonAsync("/api/dashboard");
        if (doc is null) return null;   // çevrimdışı / yetkisiz → çağıran uyarır
        var list = new List<DocumentAlertItem>();
        if (doc.RootElement.TryGetProperty("alerts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (Str(e, "kind") == "Document")
                    list.Add(new DocumentAlertItem(Str(e, "title"), Str(e, "detail"),
                        e.TryGetProperty("isCritical", out var c) && c.ValueKind == JsonValueKind.True,
                        NullS(e, "entityId")));
        return list;
    }

    // ── Takvim (TKV-01 / ADR-171): evrak+proje kaynakları sunucu-otoriteli — çevrimdışıysa null (çağıran
    //    "çevrimiçi gerekli" notu gösterir). Yerel kaynaklar (iş emri/muayene/bakım/el ile kayıt) buradan GEÇMEZ. ──
    public sealed record CalendarRemoteItem(string Source, string SourceDisplay, string Title,
        long StartDate, long? EndDate, string DateDisplay, string BranchName, string ResponsibleName, string? Detail);

    public static async Task<List<CalendarRemoteItem>?> ListCalendarAsync(long from, long to, string source)
    {
        using var doc = await GetJsonAsync($"/api/calendar?from={from}&to={to}&source={Uri.EscapeDataString(source)}");
        if (doc is null) return null;   // çevrimdışı / yetkisiz → çağıran uyarır
        var list = new List<CalendarRemoteItem>();
        foreach (var e in doc.RootElement.EnumerateArray())
            list.Add(new CalendarRemoteItem(Str(e, "source"), Str(e, "sourceDisplay"), Str(e, "title"),
                NumN(e, "startDate") ?? 0, NumN(e, "endDate"), Str(e, "dateDisplay"),
                Str(e, "branchName"), Str(e, "responsibleName"), NullS(e, "detail")));
        return list;
    }

    // ── Projeler (PRJ-01 / ADR-164): şubeler gibi SUNUCU-OTORİTELİ — çevrimdışıysa Offline döner. ──
    public sealed record ProjectItem(string Id, string Name, string Status, string StatusDisplay,
        long? StartDate, long? EndDate, string? ManagerPersonnelId, string ManagerName,
        string? Location, string? Description, List<string> BranchIds, string BranchDisplay, long Version);

    public static async Task<List<ProjectItem>?> ListProjectsAsync(string? search, string? status)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) q.Add("search=" + Uri.EscapeDataString(search!.Trim()));
        if (!string.IsNullOrWhiteSpace(status)) q.Add("status=" + Uri.EscapeDataString(status!));
        using var doc = await GetJsonAsync("/api/projects" + (q.Count > 0 ? "?" + string.Join("&", q) : ""));
        if (doc is null) return null;   // çevrimdışı / yetkisiz → çağıran uyarır
        var list = new List<ProjectItem>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var branchIds = new List<string>();
            if (e.TryGetProperty("branchIds", out var b) && b.ValueKind == JsonValueKind.Array)
                foreach (var x in b.EnumerateArray()) if (x.GetString() is { } id) branchIds.Add(id);
            list.Add(new ProjectItem(Str(e, "id"), Str(e, "name"), Str(e, "status"), Str(e, "statusDisplay"),
                NumN(e, "startDate"), NumN(e, "endDate"), NullS(e, "managerPersonnelId"), Str(e, "managerName"),
                NullS(e, "location"), NullS(e, "description"), branchIds, Str(e, "branchDisplay"),
                e.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0));
        }
        return list;
    }

    public static Task<Result> CreateProjectAsync(object body) => PostIdAsync("/api/projects", body);
    public static Task<Result> UpdateProjectAsync(string id, object body) => SendOkAsync(HttpMethod.Put, $"/api/projects/{id}", body);
    public static Task<Result> DeleteProjectAsync(string id) => SendOkAsync(HttpMethod.Delete, $"/api/projects/{id}", null);

    private static long? NumN(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    // ── Şubeler ──
    public static Task<Result> CreateBranchAsync(string name, string kind, string? parentId, string? code, string? password, string? companyId)
        => PostIdAsync("/api/branches", new { name, kind, parentId, code, password, companyId });

    /// <param name="version">DÜZENLEME KİLİDİ: formun açıldığı andaki şube sürümü (BranchRow.Version).
    /// null = kontrol yok (geriye uyumlu).</param>
    public static Task<Result> UpdateBranchAsync(string id, string name, string kind, string? parentId, string? code, string? password, string? companyId, long? version = null)
        => SendOkAsync(HttpMethod.Put, $"/api/branches/{id}", new { name, kind, parentId, code, password, companyId, version });

    public static Task<Result> DeleteBranchAsync(string id)
        => SendOkAsync(HttpMethod.Delete, $"/api/branches/{id}", null);

    // ── Kullanıcılar ──
    public static Task<Result> CreateUserAsync(string username, string password, string? fullName, List<string> roleKeys,
        string? companyId, string? branchId, bool canViewAllBranches, string? personnelId,
        // ⭐ 2026-09-06 (kullanıcı isteği): iletişim alanları
        string? email = null, string? phone = null, string? title = null, string? notes = null)
        => PostIdAsync("/api/users", new { username, password, fullName, roleKeys, companyId, branchId, canViewAllBranches, personnelId, email, phone, title, notes });

    /// <summary>⭐ Kullanıcı profilini SUNUCUDA günceller (ad-soyad + iletişim alanları). 2026-09-06.</summary>
    public static Task<Result> UpdateUserProfileAsync(string userId, string? fullName, string? email, string? phone, string? title, string? notes)
        => SendOkAsync(HttpMethod.Put, $"/api/users/{userId}/profile", new { fullName, email, phone, title, notes });

    // ── Kullanıcı LİSTE + yetki (masaüstü çevrimiçiyken sunucu-otoriteli görünürlük/düzenleme, 2026-07-25) ──
    // null döndürenler = çevrimdışı/erişilemedi → çağıran YEREL veriye düşer (salt-okuma).

    /// <summary>Firmanın kullanıcı listesini SUNUCUDAN çeker (masaüstü Kullanıcı Tanım + Yetkiler dropdown).
    /// null = çevrimdışı. Sunucu, admin olmayan aktöre rolü gizler (task 2, ListUsers sınırlı liste).</summary>
    public static async Task<List<UserRow>?> ListUsersAsync()
    {
        using var doc = await GetJsonAsync("/api/users");
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var list = new List<UserRow>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            list.Add(new UserRow(Str(e, "id"), Str(e, "username"), NullS(e, "fullName"), Bool(e, "isActive"),
                Str(e, "roles"), NullS(e, "branchId"), NullS(e, "branchName"),
                Bool(e, "canViewAllBranches"), Bool(e, "isAdmin"), NullS(e, "personnelId"), NullS(e, "personnelName"),
                NullS(e, "email"), NullS(e, "phone"), NullS(e, "title"), NullS(e, "notes")));
        }
        return list;
    }

    /// <summary>Bir kullanıcının rol anahtarlarını SUNUCUDAN çeker (yetki ağacını hedefe göre kurmak için). null = çevrimdışı.</summary>
    public static async Task<List<string>?> GetUserRolesAsync(string userId)
    {
        using var doc = await GetJsonAsync($"/api/users/{userId}/roles");
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var e in doc.RootElement.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString() ?? "");
        return list;
    }

    /// <summary>Kullanıcının kayıtlı yetkilerini SUNUCUDAN çeker (Yetkiler ekranı matrisini doldurur). null = çevrimdışı.</summary>
    /// <summary>Yetkileri sunucudan okur. <c>Version</c> = KLT-01c düzenleme kilidi jetonu;
    /// kaydederken <see cref="SavePermissionsAsync"/>'e geri verilmelidir.</summary>
    public static async Task<(List<ModulePermission> Modules, List<string> Buttons, long Version)?> GetPermissionsAsync(string userId)
    {
        using var doc = await GetJsonAsync($"/api/permissions/{userId}");
        if (doc is null) return null;
        var mods = new List<ModulePermission>();
        if (doc.RootElement.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array)
            foreach (var e in m.EnumerateArray())
                mods.Add(new ModulePermission(Str(e, "moduleKey"), Bool(e, "canView"), Bool(e, "canCreate"), Bool(e, "canEdit"), Bool(e, "canDelete")));
        var btns = new List<string>();
        if (doc.RootElement.TryGetProperty("buttons", out var b) && b.ValueKind == JsonValueKind.Array)
            foreach (var e in b.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) btns.Add(e.GetString() ?? "");
        long version = 0;
        if (doc.RootElement.TryGetProperty("version", out var v) && v.TryGetInt64(out var vv)) version = vv;
        return (mods, btns, version);
    }

    /// <summary>Yetkileri SUNUCUYA kaydeder (kullanıcı-otoriteli → hedef kullanıcı bir sonraki girişte alır).
    /// <paramref name="version"/> okunan sürümdür (KLT-01c); arada başkası kaydettiyse sunucu 409 döner.</summary>
    public static async Task<(string Mode, string ModeText, List<string> ScopeBranchIds, List<(string Id, string Name)> Assignable)?>
        GetBranchScopeAsync(string userId)
    {
        using var doc = await GetJsonAsync($"/api/permissions/{userId}/branch-scope");
        if (doc is null) return null;
        var root = doc.RootElement;
        var scope = new List<string>();
        if (root.TryGetProperty("scopeBranchIds", out var s) && s.ValueKind == JsonValueKind.Array)
            foreach (var e in s.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) scope.Add(e.GetString() ?? "");
        var atanabilir = new List<(string, string)>();
        if (root.TryGetProperty("assignable", out var a) && a.ValueKind == JsonValueKind.Array)
            foreach (var e in a.EnumerateArray()) atanabilir.Add((Str(e, "id"), Str(e, "name")));
        return (Str(root, "mode"), Str(root, "modeText"), scope, atanabilir);
    }

    /// <summary>GUI-05 — şube kapsamını SUNUCUYA kaydeder (kapsam sunucu-otoriteli; yetkilerle aynı yol).</summary>
    public static Task<Result> SaveBranchScopeAsync(string userId, IEnumerable<string> branchIds)
        => SendOkAsync(HttpMethod.Put, $"/api/permissions/{userId}/branch-scope", new { branchIds });

    public static Task<Result> SavePermissionsAsync(string userId, IEnumerable<ModulePermission> modules, IEnumerable<string> buttons,
        long version = 0)
        => SendOkAsync(HttpMethod.Post, $"/api/permissions/{userId}", new
        {
            modules = modules.Select(x => new { moduleKey = x.ModuleKey, canView = x.CanView, canCreate = x.CanCreate, canEdit = x.CanEdit, canDelete = x.CanDelete }),
            buttons,
            version
        });

    /// <summary>
    /// G1a (2026-08-12) — YETKİ SIFIRLAMA. Kullanıcının tüm modül/buton izinlerini SUNUCUDA siler.
    /// Yetkiler sunucu-otoriteli olduğu için yalnız yerele yazmak yetmez (hedef kullanıcı başka makinede).
    /// Sunucu, kaydetmeyle AYNI kapılardan geçirir; sürüm çakışmasında 409 döner.
    /// </summary>
    public static Task<Result> ResetPermissionsAsync(string userId, long version = 0)
        => SendOkAsync(HttpMethod.Post, $"/api/permissions/{userId}/reset", new { version });

    /// <summary>G1a — YETKİ ÖZETİ (salt okuma): hedefin ETKİN yetkileri, okunabilir satırlar hâlinde.
    /// Ham izin satırı değildir — admin bypass'ı ve rol kilitleri uygulanmış hâlidir.</summary>
    public static async Task<(string SourceText, List<(string Label, string Actions)> Modules, List<string> Buttons)?>
        GetPermissionSummaryAsync(string userId)
    {
        using var doc = await GetJsonAsync($"/api/permissions/{userId}/summary");
        if (doc is null) return null;
        var root = doc.RootElement;
        var source = Str(root, "sourceText");
        var mods = new List<(string, string)>();
        if (root.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array)
            foreach (var e in m.EnumerateArray()) mods.Add((Str(e, "label"), Str(e, "actionsText")));
        var btns = new List<string>();
        if (root.TryGetProperty("buttons", out var b) && b.ValueKind == JsonValueKind.Array)
            foreach (var e in b.EnumerateArray()) btns.Add(Str(e, "label"));
        return (source, mods, btns);
    }

    // ── Yetki Şablonları (G6-01, 2026-08-11) — SUNUCU-OTORİTELİ ────────────────────────────────
    // Şablonlar da tıpkı kullanıcı/yetki gibi masaüstü iş senkronuna DAHİL DEĞİLDİR
    // (BusinessSyncService.Tables listesinde yok). Masaüstü YERELE yazdığı için web'de ve diğer
    // makinelerde görünmüyordu; üstelik şablonla oluşturulan kullanıcının yetkileri sunucuya hiç
    // ulaşmıyordu. Çözüm kullanıcı/yetkideki KANITLANMIŞ desendir: çevrimiçiyken doğrudan aynı API
    // ucuna git; çevrimdışıysa çağıran uyarır, yerele yazılmaz. Yeni senkron mekanizması KURULMADI.

    /// <summary>Şablon YÖNETİM listesi (süper admin — tüm firmalar). null = çevrimdışı.</summary>
    public static Task<List<PermissionTemplateRow>?> ListTemplatesAsync()
        => TemplateRowsAsync("/api/permission-templates");

    /// <summary>Kullanıcı OLUŞTURMA için görünür şablonlar (kendi firması + tüm-firma). null = çevrimdışı.</summary>
    public static Task<List<PermissionTemplateRow>?> ListTemplatesForUserAsync()
        => TemplateRowsAsync("/api/permission-templates/for-user");

    private static async Task<List<PermissionTemplateRow>?> TemplateRowsAsync(string path)
    {
        using var doc = await GetJsonAsync(path);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var list = new List<PermissionTemplateRow>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            list.Add(new PermissionTemplateRow(Str(e, "id"), Str(e, "name"), Str(e, "companyId"),
                NullS(e, "companyName"), Bool(e, "scopeAll")));
        }
        return list;
    }

    /// <summary>Şablon içeriği (modüller + butonlar + rol). null = çevrimdışı ya da erişim reddedildi.</summary>
    public static async Task<PermissionTemplateData?> GetTemplateDataAsync(string templateId)
    {
        using var doc = await GetJsonAsync($"/api/permission-templates/{templateId}");
        if (doc is null) return null;
        var mods = new List<ModulePermission>();
        if (doc.RootElement.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array)
            foreach (var e in m.EnumerateArray())
                mods.Add(new ModulePermission(Str(e, "moduleKey"), Bool(e, "canView"), Bool(e, "canCreate"),
                    Bool(e, "canEdit"), Bool(e, "canDelete")));
        var btns = new List<string>();
        if (doc.RootElement.TryGetProperty("buttons", out var b) && b.ValueKind == JsonValueKind.Array)
            foreach (var e in b.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) btns.Add(e.GetString() ?? "");
        string? role = doc.RootElement.TryGetProperty("roleKey", out var rk) && rk.ValueKind == JsonValueKind.String
            ? rk.GetString() : null;
        return new PermissionTemplateData(mods, btns, role);
    }

    public static Task<Result> CreateTemplateAsync(string name, string? roleKey,
        IEnumerable<ModulePermission> modules, IEnumerable<string> buttons, string? companyId, bool scopeAll)
        => PostIdAsync("/api/permission-templates", new
        {
            name, roleKey,
            modules = modules.Select(x => new { moduleKey = x.ModuleKey, canView = x.CanView, canCreate = x.CanCreate, canEdit = x.CanEdit, canDelete = x.CanDelete }),
            buttons, companyId, scopeAll,
        });

    public static Task<Result> DeleteTemplateAsync(string templateId)
        => SendOkAsync(HttpMethod.Delete, $"/api/permission-templates/{templateId}", null);

    public static Task<Result> SetRolesAsync(string userId, IEnumerable<string> roles)
        => SendOkAsync(HttpMethod.Post, $"/api/users/{userId}/roles", new { roles });

    public static Task<Result> SetActiveAsync(string userId, bool active)
        => SendOkAsync(HttpMethod.Post, $"/api/users/{userId}/active", new { active });

    public static Task<Result> DeleteUserAsync(string userId)
        => SendOkAsync(HttpMethod.Delete, $"/api/users/{userId}", null);

    public static Task<Result> AssignBranchAsync(string userId, string? branchId)
        => SendOkAsync(HttpMethod.Post, $"/api/users/{userId}/branch", new { id = branchId ?? "" });

    public static Task<Result> SetAllBranchesAsync(string userId, bool active)
        => SendOkAsync(HttpMethod.Post, $"/api/users/{userId}/all-branches", new { active });

    /// <summary>Şifre sıfırla (geçici=kullanıcı adı). Offline=true → çevrimdışı; aksi halde TempPassword döner.</summary>
    public static async Task<(bool Offline, string? Error, string? TempPassword)> ResetPasswordAsync(string userId)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return (true, null, null);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url + $"/api/users/{userId}/reset-password");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, ExtractError(text, (int)resp.StatusCode), null);
            try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.TryGetProperty("tempPassword", out var v)) return (false, null, v.GetString()); } catch { }
            return (false, null, null);
        }
        catch { return (true, null, null); }
    }

    // ═══════════ UYGULAMA İÇİ SOHBET (kullanıcı isteği 2026-09-06) ═══════════
    //
    // Sohbet SENKRON DIŞIDIR: yerel SQLite'a hiçbir şey yazılmaz, doğrudan sunucu okunur/yazılır.
    // Bu yüzden "çevrimdışı" bir davranış YOKTUR — bağlantı yoksa çağrılar boş/başarısız döner ve
    // arayüz sohbeti kapalı gösterir (bkz. Migration096 gerekçesi).

    /// <summary>Firmadaki kullanıcılar + çevrimiçi durumu + okunmamış sayısı. null = ulaşılamadı.</summary>
    public static async Task<List<DepoWise.Infrastructure.Chat.ChatKisi>?> ChatKisilerAsync()
    {
        using var doc = await GetJsonAsync("/api/chat/users");
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var liste = new List<DepoWise.Infrastructure.Chat.ChatKisi>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            liste.Add(new DepoWise.Infrastructure.Chat.ChatKisi(
                Str(e, "userId"), Str(e, "username"), NullS(e, "fullName"), NullS(e, "title"),
                Bool(e, "online"), Int(e, "unread"), NullLong(e, "lastSeenAt")));
        }
        return liste;
    }

    /// <summary>Bir kişiyle olan konuşma. <paramref name="since"/> verilirse yalnız SONRAKİ mesajlar.</summary>
    public static async Task<List<DepoWise.Infrastructure.Chat.ChatMesaj>?> ChatKonusmaAsync(string karsiUserId, long? since = null)
    {
        var yol = $"/api/chat/messages?withUserId={Uri.EscapeDataString(karsiUserId)}" + (since is { } t ? $"&since={t}" : "");
        using var doc = await GetJsonAsync(yol);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var liste = new List<DepoWise.Infrastructure.Chat.ChatMesaj>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            liste.Add(new DepoWise.Infrastructure.Chat.ChatMesaj(
                Str(e, "id"), Str(e, "senderId"), Str(e, "recipientId"), Str(e, "body"),
                Long(e, "createdAt"), NullLong(e, "readAt"), Bool(e, "mine")));
        }
        return liste;
    }

    /// <summary>Mesaj gönderir.</summary>
    public static Task<Result> ChatGonderAsync(string aliciUserId, string govde)
        => PostIdAsync("/api/chat/messages", new { toUserId = aliciUserId, body = govde });

    /// <summary>Bir kişiden gelenleri okundu işaretler.</summary>
    public static Task<Result> ChatOkunduAsync(string karsiUserId)
        => SendOkAsync(HttpMethod.Post, "/api/chat/seen", new { withUserId = karsiUserId });

    private static async Task<JsonDocument?> GetJsonAsync(string path)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    private static string Str(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string? NullS(JsonElement o, string k) { var s = Str(o, k); return string.IsNullOrEmpty(s) ? null : s; }
    private static bool Bool(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n != 0));

    private static int Int(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    private static long Long(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0L;
    private static long? NullLong(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static async Task<Result> PostIdAsync(string path, object body)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return OfflineResult;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url + path)
            { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return new(false, false, ExtractError(text, (int)resp.StatusCode), null);
            string? id = null;
            try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.TryGetProperty("id", out var v)) id = v.GetString(); } catch { }
            return new(true, false, null, id);
        }
        catch { return OfflineResult; }   // ağ hatası → çevrimdışı gibi
    }

    private static async Task<Result> SendOkAsync(HttpMethod method, string path, object? body)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return OfflineResult;
        try
        {
            using var req = new HttpRequestMessage(method, url + path);
            if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return new(true, false, null, null, (int)resp.StatusCode);
            return new(false, false, ExtractError(await resp.Content.ReadAsStringAsync(), (int)resp.StatusCode), null, (int)resp.StatusCode);
        }
        catch { return OfflineResult; }
    }

    private static async Task<(string? Url, string? Token)> ResolveAsync()
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return (null, null);
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        return string.IsNullOrWhiteSpace(token) ? (null, null) : (url!.TrimEnd('/'), token);
    }

    private static string ExtractError(string body, int status)
    {
        try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? $"Sunucu hatası ({status})."; }
        catch { }
        return $"Sunucu hatası ({status}).";
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(path)) { var v = File.ReadAllText(path).Trim(); if (!string.IsNullOrWhiteSpace(v)) return v; }
        }
        catch { }
        return "https://depowise-erp.fly.dev";
    }
}
