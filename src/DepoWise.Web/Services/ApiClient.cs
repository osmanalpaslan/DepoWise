using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace DepoWise.Web.Services;

public sealed record LoginBranchDto(string Id, string Name, string? Code, bool HasPassword);
public sealed record LoginCompanyDto(string Id, string Name);
public sealed record LoginResponse(string Token, string UserId, string CompanyId, bool IsSuperAdmin, string? BranchId = null,
    string? CompanyName = null, bool CanViewAllBranches = false, List<LoginBranchDto>? Branches = null,
    List<LoginCompanyDto>? Companies = null, bool MustChangePassword = false);
public sealed record MachineDto(string Id, string Name, string Status, string StatusText, string LastSeenText, string CreatedText, bool CanActivate, bool IsActive, bool Online, string CompanyId = "", string CompanyName = "", int Quota = 3, string Ip = "", string Ipv4 = "", string Ipv6 = "", string BranchName = "", string BranchId = "", string Province = "");
public sealed record ReleaseDto(string Version, string? ReleaseNotes, bool Signed, string? DownloadUrl);
public sealed record ReleasePackageDto(string Version, string FileName, long SizeBytes, double SizeMb, DateTime ModifiedUtc, bool IsLatest);
public sealed record CompanyDto(string Id, string Name, string? TaxNo, string? Phone, string? Email, string? AuthorizedPerson, int UserCount, int MaxUsers = 0, int MaxAdmins = 0, int MachineQuota = 3);
public sealed record MenuModule(string Key, string Label, bool Create, bool Edit, bool Delete);
public sealed record RoleDto(string Key, string Name);
public sealed record MenuResponse(bool IsSuperAdmin, bool IsAdmin, List<MenuModule> Modules, bool IsRestrictedSuperAdmin = false,
    // DEN-F1: ozel buton yetkileri (sunucu CanUseButton sonucunu gonderir; admin bypass dahil).
    List<string>? Buttons = null);

/// <summary>
/// DepoWise.Api HTTP istemcisi (web arayüzü → API). Web hiçbir iş kuralı TAŞIMAZ; her şey API'de.
/// JWT AuthState'ten eklenir. Bu sınıf UI'ı API'ye bağlayan tek noktadır (Next.js'e geçişte de bu sözleşme aynı).
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly AuthState _auth;
    private readonly ILogger<ApiClient>? _log;

    public ApiClient(HttpClient http, AuthState auth, ILogger<ApiClient>? log = null)
    { _http = http; _auth = auth; _log = log; }

    /// <summary>
    /// WEB-01 (2026-08-10) — SUNUCU HATASINI KULLANICI DİLİNE ÇEVİRİR.
    ///
    /// Sorun: bu sınıf hata gövdesini AYRIŞTIRMADAN kullanıcı mesajına yapıştırıyordu; ekranda
    /// <c>Hata 409: {"error":"..."}</c> gibi süslü parantezli teknik metin görünüyordu. Yazılım bilgisi
    /// olmayan son kullanıcı için okunamaz.
    ///
    /// API sözleşmesi DEĞİŞMEDİ: sunucu zaten her hata kodunda <c>{"error":"&lt;Türkçe mesaj&gt;"}</c>
    /// döndürüyor (Program.cs ortak middleware) ve 500'de ham exception'ı SIZDIRMIYOR (loga yazıyor).
    /// Burada yapılan tek şey o alanı çıkarmaktır — desen zaten projede vardı
    /// (<see cref="UploadImportAsync"/>), yalnız 5 çağrı noktasında uygulanmamıştı.
    ///
    /// Gövde ayrıştırılamazsa (ağ katmanı hatası, HTML proxy sayfası vb.) duruma göre anlaşılır bir
    /// Türkçe karşılık üretilir. Teknik ayrıntı KAYBOLMAZ: durum kodu + ham gövde sunucu loguna yazılır.
    /// </summary>
    private async Task<string> ErrorMessageAsync(HttpResponseMessage r, string path)
    {
        var code = (int)r.StatusCode;
        string body = "";
        try { body = await r.Content.ReadAsStringAsync(); } catch { }

        // Teknik ayrıntı geliştirici tarafında kalır (kullanıcıya gösterilmez).
        _log?.LogWarning("API hata: {Status} {Path} → {Body}", code, path, body);

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var e) &&
                    e.ValueKind == System.Text.Json.JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(e.GetString()))
                    return e.GetString()!;
            }
            catch { /* JSON değil → aşağıdaki duruma göre karşılık */ }
        }

        return code switch
        {
            400 => "İstek geçersiz. Girdiğiniz bilgileri kontrol edip tekrar deneyin.",
            401 => "Oturumunuz sona ermiş görünüyor. Lütfen yeniden giriş yapın.",
            403 => "Bu işlem için yetkiniz yok.",
            404 => "Kayıt bulunamadı. Başka biri silmiş olabilir; listeyi yenileyin.",
            409 => "Bu kayıt siz işlem yaparken değişti. Listeyi yenileyip tekrar deneyin.",
            413 => "Gönderilen dosya çok büyük.",
            429 => "Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin.",
            >= 500 => "Sunucuda beklenmeyen bir hata oluştu. Sorun devam ederse yöneticinize bildirin.",
            _ => "İşlem tamamlanamadı. Lütfen tekrar deneyin.",
        };
    }

    /// <summary>Masaüstü kurulum aracının indirme adresi (API'den servis edilir).</summary>
    public string SetupDownloadUrl => new Uri(_http.BaseAddress!, "api/setup/download").ToString();

    /// <summary>Kullanıcının görebileceği menü + yetkileri (masaüstüyle aynı) çeker ve AuthState'e yazar.</summary>
    public async Task RefreshMenuAsync()
    {
        try
        {
            var resp = await _http.SendAsync(Req(HttpMethod.Get, "/api/me/menu"));
            if (!resp.IsSuccessStatusCode) return;
            var data = await resp.Content.ReadFromJsonAsync<MenuResponse>();
            if (data is not null) _auth.SetModules(data.Modules, data.IsAdmin, data.IsRestrictedSuperAdmin, data.Buttons);
        }
        catch { }

        // G5 — platform görünürlüğü menüyle AYNI anda tazelenir (ayrı bir yenileme yolu açılmadı).
        // Böylece yönetici bir ekranı web'de kapattığında, kullanıcının bir sonraki menü tazelemesinde
        // (giriş / sayfa yenileme / oturum tazeleme) etkili olur; bayat veri kalıcı olmaz.
        try
        {
            var vr = await _http.SendAsync(Req(HttpMethod.Get, "/api/screens/visibility"));
            if (!vr.IsSuccessStatusCode) return;
            using var doc = System.Text.Json.JsonDocument.Parse(await vr.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("screens", out var arr)) return;
            var kapali = new List<string>();
            foreach (var e in arr.EnumerateArray())
                if (e.TryGetProperty("web", out var w) && w.ValueKind == System.Text.Json.JsonValueKind.False)
                    kapali.Add(e.GetProperty("key").GetString() ?? "");
            _auth.SetScreenVisibility(kapali);

            // MNU: menü düzeni AYNI yanıtta gelir (ayrı tazeleme yolu yok). Ham tercihler taşınır;
            // sıra/ad çözümlemesini web ve masaüstü AYNI kodla (MenuLayout.Build) yapar.
            _auth.SetMenuLayout(ParseLayout(doc.RootElement));
        }
        catch { /* okunamazsa katalog varsayılanı geçerli kalır → ekranlar kapanmaz */ }
    }

    /// <summary>MNU — sunucudan gelen ham menü düzeni tercihlerini çözer. Alan yoksa BOŞ küme döner
    /// (eski sunucu / kayıt yok) → menü katalog varsayılanıyla çizilir.</summary>
    private static DepoWise.Application.Security.MenuLayoutSet ParseLayout(System.Text.Json.JsonElement root)
    {
        var screens = new Dictionary<string, DepoWise.Application.Security.ScreenLayoutOverride>(StringComparer.Ordinal);
        var groups = new Dictionary<string, DepoWise.Application.Security.GroupLayoutOverride>(StringComparer.Ordinal);
        if (!root.TryGetProperty("layout", out var layout) ||
            layout.ValueKind != System.Text.Json.JsonValueKind.Object)
            return DepoWise.Application.Security.MenuLayoutSet.Empty;

        if (layout.TryGetProperty("screens", out var sa) && sa.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var e in sa.EnumerateArray())
            {
                var key = Str(e, "key");
                if (key.Length == 0) continue;
                screens[key] = new DepoWise.Application.Security.ScreenLayoutOverride(
                    key, StrOrNull(e, "label"), StrOrNull(e, "groupKey"), IntOrNull(e, "sortOrder"));
            }

        if (layout.TryGetProperty("groups", out var ga) && ga.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var e in ga.EnumerateArray())
            {
                var key = Str(e, "key");
                if (key.Length == 0) continue;
                groups[key] = new DepoWise.Application.Security.GroupLayoutOverride(
                    key, StrOrNull(e, "title"), IntOrNull(e, "sortOrder"),
                    e.TryGetProperty("isCustom", out var ic) && ic.ValueKind == System.Text.Json.JsonValueKind.True,
                    StrOrNull(e, "parentGroupKey"));   // SEC: üst grup bağı (menünün 3. seviyesi)
            }

        return new DepoWise.Application.Security.MenuLayoutSet(screens, groups);

        static string Str(System.Text.Json.JsonElement e, string k) => StrOrNull(e, k) ?? "";
        static string? StrOrNull(System.Text.Json.JsonElement e, string k)
            => e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        static int? IntOrNull(System.Text.Json.JsonElement e, string k)
            => e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    }

    private HttpRequestMessage Req(HttpMethod m, string url)
    {
        var r = new HttpRequestMessage(m, url);
        if (_auth.Token is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        return r;
    }

    /// <summary>ADIM 1 (2 aşamalı giriş): kullanıcı adı+parola doğrular; oturuma HENÜZ girmez — kullanıcının
    /// firma adı + şubelerini döndürür (kullanıcı firma listesini görmez). Hata → (mesaj, null).</summary>
    public async Task<(string? Error, LoginResponse? Data)> AuthenticateAsync(string username, string password)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!resp.IsSuccessStatusCode)
        {
            try { var e = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                  if (e.TryGetProperty("error", out var m)) return (m.GetString() ?? "Giriş başarısız.", null); } catch { }
            return ("Kullanıcı adı veya parola hatalı.", null);
        }
        var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return data is null ? ("Sunucu yanıtı okunamadı.", null) : (null, data);
    }

    /// <summary>İLK GİRİŞ şifre belirleme: mustChangePassword kullanıcı yeni şifresini AYNI login ekranından
    /// belirler. Adım 1'deki token Bearer olarak eklenir; başarıda firma/şube akışına devam edilecek yanıt döner.</summary>
    public async Task<(string? Error, LoginResponse? Data)> ChangeInitialPasswordAsync(string step1Token, string newPassword)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-initial-password")
        {
            Content = JsonContent.Create(new { newPassword })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", step1Token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            try { var e = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                  if (e.TryGetProperty("error", out var m)) return (m.GetString() ?? "Şifre belirlenemedi.", null); } catch { }
            return ("Şifre belirlenemedi.", null);
        }
        var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return data is null ? ("Sunucu yanıtı okunamadı.", null) : (null, data);
    }

    /// <summary>ADIM 1b (YALNIZ süper admin): firma seçilince o firma bağlamında YENİ token + o firmanın şubelerini
    /// döndürür. Adım 1'de alınan token (henüz AuthState'e yazılmadı) Authorization olarak elle eklenir.</summary>
    public async Task<(string? Error, LoginResponse? Data)> SelectCompanyAsync(string step1Token, string companyId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/select-company")
        {
            Content = JsonContent.Create(new { companyId })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", step1Token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            try { var e = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                  if (e.TryGetProperty("error", out var m)) return (m.GetString() ?? "Firma seçilemedi.", null); } catch { }
            return ("Firma seçilemedi.", null);
        }
        var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return data is null ? ("Sunucu yanıtı okunamadı.", null) : (null, data);
    }

    /// <summary>Şube şifresi doğrular (anonim public uç). Şifre yoksa serbest.</summary>
    public async Task<bool> VerifyBranchAsync(string companyId, string branchId, string? branchPassword)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/api/public/verify-branch", new { companyId, branchId, branchPassword });
            if (!resp.IsSuccessStatusCode) return false;
            var e = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return e.TryGetProperty("ok", out var ok) && ok.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch { return false; }
    }

    /// <summary>ADIM 2: şube seçildi → oturumu tamamla (AuthState + menü).</summary>
    public async Task FinalizeSignInAsync(LoginResponse data, string? branchId, string? userName = null)
    {
        _auth.SignIn(data.Token, data.UserId, data.CompanyId, data.IsSuperAdmin, branchId, data.CompanyName, userName);
        await RefreshMenuAsync();
    }

    public async Task<List<MachineDto>> GetMachinesAsync(string? companyId = null, string? branchId = null, bool unassigned = false)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(companyId)) q.Add($"companyId={Uri.EscapeDataString(companyId)}");
        if (!string.IsNullOrWhiteSpace(branchId)) q.Add($"branchId={Uri.EscapeDataString(branchId)}");
        if (unassigned) q.Add("unassigned=true");
        var url = "/api/machines" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        var resp = await _http.SendAsync(Req(HttpMethod.Get, url));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<MachineDto>>() ?? new();
    }

    public Task<string?> SetMachineQuotaAsync(string companyId, int quota) =>
        PostAsync($"/api/companies/{companyId}/machine-quota", new { quota });

    /// <summary>Giriş yapan kullanıcının kayıtlı web tema tercihi (mod + renk). Hata olursa varsayılan.</summary>
    public async Task<(string Mode, string Color, string Style)> GetUserThemeAsync()
    {
        try
        {
            var doc = await GetObjectAsync("/api/me/theme");
            var mode = doc.TryGetProperty("mode", out var m) ? m.GetString() ?? "dark" : "dark";
            var color = doc.TryGetProperty("color", out var c) ? c.GetString() ?? "amber" : "amber";
            var style = doc.TryGetProperty("style", out var st) ? st.GetString() ?? "soft" : "soft";
            return (mode, color, style);
        }
        catch { return ("dark", "amber", "soft"); } // İlk açılış varsayılanı: Koyu / Kehribar / Yumuşak
    }

    public Task<string?> SaveUserThemeAsync(string mode, string color, string style) =>
        PostAsync("/api/me/theme", new { mode, color, style });

    /// <summary>Herhangi bir liste ucundan ham JSON dizi (genel tablo bileşeni için).</summary>
    public async Task<string?> PostAsync(string path, object body)
    {
        var req = Req(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        var r = await _http.SendAsync(req);
        return r.IsSuccessStatusCode ? null : await ErrorMessageAsync(r, path);   // WEB-01
    }

    /// <summary>POST edip dönen {id}'yi de verir (fotoğraf yükleme için oluşan kaydın id'si gerekir).</summary>
    public async Task<(string? Err, string? Id)> CreateAsync(string path, object body)
    {
        var req = Req(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        var r = await _http.SendAsync(req);
        if (!r.IsSuccessStatusCode) return (await ErrorMessageAsync(r, path), null);   // WEB-01
        try { var doc = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(); return (null, doc.TryGetProperty("id", out var v) ? v.GetString() : null); }
        catch { return (null, null); }
    }

    public async Task<string?> UploadFilesAsync(string path, IEnumerable<(string Name, byte[] Bytes, string Mime)> files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var f in files)
        {
            var content = new ByteArrayContent(f.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(f.Mime) ? "image/jpeg" : f.Mime);
            form.Add(content, "file", f.Name);
        }
        var req = Req(HttpMethod.Post, path);
        req.Content = form;
        var r = await _http.SendAsync(req);
        return r.IsSuccessStatusCode ? null : await ErrorMessageAsync(r, path);   // WEB-01 (eskiden yalnız kod)
    }

    /// <summary>
    /// Excel içe aktarım (İş #7): .xlsx dosyasını + hedef şubeyi POST eder, sonucu JSON olarak alır.
    /// Hata gövdesindeki <c>error</c> alanı çıkarılır → kullanıcıya ham JSON gösterilmez.
    /// </summary>
    public async Task<(string? Error, System.Text.Json.JsonElement? Data)> UploadImportAsync(
        string path, string fileName, byte[] bytes, string branchId)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", fileName);
        form.Add(new StringContent(branchId), "branchId");

        var req = Req(HttpMethod.Post, path);
        req.Content = form;
        var r = await _http.SendAsync(req);
        // WEB-01: ayrıştırma buradaki yerel kopyadan ORTAK yardımcıya taşındı (tek kaynak).
        if (!r.IsSuccessStatusCode) return (await ErrorMessageAsync(r, path), null);
        var text = await r.Content.ReadAsStringAsync();
        try { return (null, System.Text.Json.JsonDocument.Parse(text).RootElement.Clone()); }
        catch { return ("Sunucu yanıtı okunamadı.", null); }
    }

    /// <summary>Korumalı bir uçtan dosya (bytes) + dosya adı çeker (PDF/Excel indirme için).</summary>
    public async Task<(byte[]? Bytes, string FileName)> GetFileAsync(string path, string fallbackName)
    {
        var r = await _http.SendAsync(Req(HttpMethod.Get, path));
        if (!r.IsSuccessStatusCode) return (null, fallbackName);
        var name = r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName ?? fallbackName;
        name = name?.Trim('"') ?? fallbackName;
        return (await r.Content.ReadAsByteArrayAsync(), name);
    }

    /// <summary>JSON gövdesi POST edip dönen dosyayı (bytes + ad) verir. HTTP durum kodunu da döner
    /// (403 → "yetkiniz yok" ayrımı için). Rapor Excel dışa aktarma gibi POST-tabanlı indirmelerde kullanılır.</summary>
    public async Task<(byte[]? Bytes, string FileName, int Status)> PostFileAsync(string path, object body, string fallbackName)
    {
        var req = Req(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        var r = await _http.SendAsync(req);
        if (!r.IsSuccessStatusCode) return (null, fallbackName, (int)r.StatusCode);
        var name = r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName ?? fallbackName;
        name = name?.Trim('"') ?? fallbackName;
        return (await r.Content.ReadAsByteArrayAsync(), name, (int)r.StatusCode);
    }

    /// <summary>Korumalı bir görsel ucundan bytes çekip data URL üretir (img src için — Bearer başlığı gerektiğinden).</summary>
    public async Task<string?> GetImageDataUrlAsync(string path)
    {
        try
        {
            var r = await _http.SendAsync(Req(HttpMethod.Get, path));
            if (!r.IsSuccessStatusCode) return null;
            var mime = r.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var bytes = await r.Content.ReadAsByteArrayAsync();
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    public async Task<string?> PutAsync(string path, object body)
    {
        var req = Req(HttpMethod.Put, path);
        req.Content = JsonContent.Create(body);
        var r = await _http.SendAsync(req);
        return r.IsSuccessStatusCode ? null : await ErrorMessageAsync(r, path);   // WEB-01
    }

    /// <summary>POST edip dönen JSON gövdesini verir (rapor tablosu vb.).</summary>
    public async Task<System.Text.Json.JsonElement?> PostJsonAsync(string path, object body)
    {
        var req = Req(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        var r = await _http.SendAsync(req);
        if (!r.IsSuccessStatusCode) return null;
        return await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }

    public async Task<string?> DeleteAsync(string path)
    {
        var r = await _http.SendAsync(Req(HttpMethod.Delete, path));
        return r.IsSuccessStatusCode ? null : await ErrorMessageAsync(r, path);   // WEB-01
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/roles"));
        if (!r.IsSuccessStatusCode) return new();
        return await r.Content.ReadFromJsonAsync<List<RoleDto>>() ?? new();
    }

    public sealed record Opt(string Id, string Name);
    public sealed record LinkableUser(string Id, string Username, string? FullName, bool IsActive, string? BranchName = null, string? Display = null)
    {
        /// <summary>Gösterim: yalnız Ad Soyad + şube (kullanıcı adı gizli).</summary>
        public string Label => !string.IsNullOrWhiteSpace(Display) ? Display!
            : (string.IsNullOrWhiteSpace(FullName) ? Username : FullName!)
              + (string.IsNullOrWhiteSpace(BranchName) ? "" : $" — {BranchName}")
              + (IsActive ? "" : " (pasif)");
    }

    /// <summary>Bir personele bağlanabilir (henüz bağsız) mevcut kullanıcılar (Admin+).</summary>
    public async Task<List<LinkableUser>> GetLinkableUsersAsync()
    {
        try
        {
            var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/personnel/linkable-users"));
            if (!r.IsSuccessStatusCode) return new();
            return await r.Content.ReadFromJsonAsync<List<LinkableUser>>() ?? new();
        }
        catch { return new(); }
    }

    /// <summary>Bir liste ucundan (id,name) seçenekleri — dropdown'lar için. nameKey birden çok olabilir (ilk dolu olan).</summary>
    public async Task<List<Opt>> OptionsAsync(string path, string idKey = "id", params string[] nameKeys)
    {
        if (nameKeys.Length == 0) nameKeys = new[] { "name" };
        try
        {
            var arr = await GetArrayAsync(path);
            var list = new List<Opt>();
            foreach (var e in arr)
            {
                if (e.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var id = e.TryGetProperty(idKey, out var i) ? i.GetString() ?? "" : "";
                if (id == "") continue;
                string name = "";
                foreach (var nk in nameKeys)
                    if (e.TryGetProperty(nk, out var n) && n.ValueKind != System.Text.Json.JsonValueKind.Null)
                    { name = n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() ?? "" : n.ToString(); if (name != "") break; }
                list.Add(new Opt(id, name));
            }
            return list;
        }
        catch { return new(); }
    }

    public async Task<System.Text.Json.JsonElement[]> GetArrayAsync(string path)
    {
        var resp = await _http.SendAsync(Req(HttpMethod.Get, path));
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();
        return doc ?? Array.Empty<System.Text.Json.JsonElement>();
    }

    /// <summary>Tek JSON nesne dönen uçlar için (özet vb.).</summary>
    public async Task<System.Text.Json.JsonElement> GetObjectAsync(string path)
    {
        var resp = await _http.SendAsync(Req(HttpMethod.Get, path));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }

    /// <summary>Kolon bazlı filtre + numaralı sayfalama sonucu (Malzeme/Araç Listesi — kullanıcı isteği
    /// 2026-07-17). Hata olursa boş sayfa döner (ekran "kayıt yok" gösterir, çökmez).</summary>
    public sealed record GridPage(System.Text.Json.JsonElement[] Items, int TotalCount, int Page, int PageSize, int TotalPages);
    public async Task<GridPage> GetGridAsync(string path)
    {
        try
        {
            var obj = await GetObjectAsync(path);
            var items = obj.TryGetProperty("items", out var it) && it.ValueKind == System.Text.Json.JsonValueKind.Array
                ? it.EnumerateArray().ToArray() : Array.Empty<System.Text.Json.JsonElement>();
            int I(string k) => obj.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() : 0;
            return new GridPage(items, I("totalCount"), I("page") is 0 ? 1 : I("page"), I("pageSize") is 0 ? 50 : I("pageSize"), I("totalPages") is 0 ? 1 : I("totalPages"));
        }
        catch { return new GridPage(Array.Empty<System.Text.Json.JsonElement>(), 0, 1, 50, 1); }
    }

    /// <summary>Bu kullanıcının bir liste ekranı için kaydettiği kolon seçimi — hiç kaydetmediyse null
    /// (çağıran kendi varsayılan kolon listesine düşer).</summary>
    public async Task<List<string>?> GetListColumnsAsync(string listKey)
    {
        try
        {
            var obj = await GetObjectAsync($"/api/me/list-columns/{listKey}");
            if (obj.TryGetProperty("columns", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Array)
                return c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList();
            return null;
        }
        catch { return null; }
    }

    /// <summary>Bu kullanıcının kolon seçimini kaydeder — KİŞİSELDİR (başka kullanıcıda görünmez).</summary>
    public Task<string?> SaveListColumnsAsync(string listKey, List<string> columns) =>
        PostAsync($"/api/me/list-columns/{listKey}", new { columns });

    /// <summary>Kişisel sayfa boyutu + kolon genişlikleri (ADR-089). pageSize=null → ekran 25 kullanır.</summary>
    public async Task<(int? PageSize, Dictionary<string, int>? Widths)> GetListPrefsAsync(string listKey)
    {
        try
        {
            var obj = await GetObjectAsync($"/api/me/list-prefs/{listKey}");
            int? ps = obj.TryGetProperty("pageSize", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number ? p.GetInt32() : null;
            Dictionary<string, int>? w = null;
            if (obj.TryGetProperty("widths", out var wj) && wj.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                w = new();
                foreach (var kv in wj.EnumerateObject()) if (kv.Value.ValueKind == System.Text.Json.JsonValueKind.Number) w[kv.Name] = kv.Value.GetInt32();
            }
            return (ps, w);
        }
        catch { return (null, null); }
    }

    public Task<string?> SavePageSizeAsync(string listKey, int pageSize) =>
        PostAsync($"/api/me/list-prefs/{listKey}/page-size", new { pageSize });

    public Task<string?> SaveWidthsAsync(string listKey, Dictionary<string, int> widths) =>
        PostAsync($"/api/me/list-prefs/{listKey}/widths", new { widths });

    /// <summary>Birim 4 (ortak tablo bileşeni): bir ekranın TÜM kişisel tercihi TEK çağrıda — kolon
    /// sırası/seçimi + genişlik + sıralama. Ortak tablo ekran açılırken bir kez çağırır.</summary>
    public sealed record ListPrefsFull(List<string>? Columns, Dictionary<string, int>? Widths, string? SortKey, bool SortDesc);
    public async Task<ListPrefsFull> GetListPrefsFullAsync(string listKey)
    {
        try
        {
            var obj = await GetObjectAsync($"/api/me/list-prefs/{listKey}");
            List<string>? cols = null;
            if (obj.TryGetProperty("columns", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Array)
                cols = c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList();
            Dictionary<string, int>? w = null;
            if (obj.TryGetProperty("widths", out var wj) && wj.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                w = new();
                foreach (var kv in wj.EnumerateObject()) if (kv.Value.ValueKind == System.Text.Json.JsonValueKind.Number) w[kv.Name] = kv.Value.GetInt32();
            }
            string? sk = null; bool sd = false;
            if (obj.TryGetProperty("sort", out var sj) && sj.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                sk = sj.TryGetProperty("key", out var k) ? k.GetString() : null;
                sd = sj.TryGetProperty("desc", out var dd) && dd.ValueKind == System.Text.Json.JsonValueKind.True;
            }
            return new ListPrefsFull(cols is { Count: > 0 } ? cols : null, w is { Count: > 0 } ? w : null, sk, sd);
        }
        catch { return new ListPrefsFull(null, null, null, false); }
    }

    /// <summary>Kaydedilmiş varsayılan sıralama (Birim 4 altyapı — şimdilik yalnız hatırlanır).</summary>
    public Task<string?> SaveSortAsync(string listKey, string key, bool desc) =>
        PostAsync($"/api/me/list-prefs/{listKey}/sort", new { key, desc });

    public async Task<List<CompanyDto>> GetCompaniesAsync()
    {
        var resp = await _http.SendAsync(Req(HttpMethod.Get, "/api/companies"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<CompanyDto>>() ?? new();
    }

    public async Task<string?> CreateCompanyAsync(object dto)
    {
        var req = Req(HttpMethod.Post, "/api/companies");
        req.Content = JsonContent.Create(dto);
        var resp = await _http.SendAsync(req);
        return resp.IsSuccessStatusCode ? null : $"Hata {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}";
    }

    /// <summary>Pasife alınmış (silinmiş) firmalar — yeniden aktifleştirme ekranı için.</summary>
    public async Task<List<CompanyDto>> GetDeletedCompaniesAsync()
    {
        var resp = await _http.SendAsync(Req(HttpMethod.Get, "/api/companies/deleted"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<CompanyDto>>() ?? new();
    }

    public Task ApproveMachineAsync(string id) => _http.SendAsync(Req(HttpMethod.Post, $"/api/machines/{id}/approve"));
    public Task RevokeMachineAsync(string id) => _http.SendAsync(Req(HttpMethod.Post, $"/api/machines/{id}/revoke"));
    public Task ReactivateMachineAsync(string id) => _http.SendAsync(Req(HttpMethod.Post, $"/api/machines/{id}/reactivate"));
    public Task DeleteMachineAsync(string id) => _http.SendAsync(Req(HttpMethod.Delete, $"/api/machines/{id}"));
    /// <summary>Admin makineye şube atar (boş branchId → atama kaldırılır).</summary>
    public Task<string?> AssignMachineBranchAsync(string id, string? branchId) =>
        PostAsync($"/api/machines/{id}/branch", new { branchId });
    /// <summary>Süper admin makinenin firmasını değiştirir (şube ataması otomatik kalkar).</summary>
    public Task<string?> AssignMachineCompanyAsync(string id, string companyId) =>
        PostAsync($"/api/machines/{id}/company", new { companyId });
    /// <summary>ADR-085 — makinenin TÜM firmalardaki tanımını sıfırlar (veriye dokunmaz); masaüstü bir sonraki
    /// girişte yerel makine önbelleğini temizler ve login ekranına döner.</summary>
    public Task<string?> RequestMachineResetAsync(string machineName) =>
        PostAsync("/api/admin/machine-reset", new { machineName });
    /// <summary>Bir firmanın şubelerini seçenek olarak döndürür (makineye şube atama için).</summary>
    public async Task<List<Opt>> GetBranchOptionsAsync(string companyId)
    {
        try
        {
            var resp = await _http.SendAsync(Req(HttpMethod.Get, $"/api/public/branches?companyId={Uri.EscapeDataString(companyId)}"));
            if (!resp.IsSuccessStatusCode) return new();
            var arr = await resp.Content.ReadFromJsonAsync<List<LoginBranchDto>>();
            return arr is null ? new() : arr.Select(b => new Opt(b.Id, b.Name)).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Diskteki güncelleme paketleri (canlı sunucu ekranı, süper admin).</summary>
    public async Task<List<ReleasePackageDto>> GetReleasePackagesAsync()
    {
        try
        {
            var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/releases/packages"));
            if (!r.IsSuccessStatusCode) return new();
            return await r.Content.ReadFromJsonAsync<List<ReleasePackageDto>>() ?? new();
        }
        catch { return new(); }
    }

    /// <summary>Bir güncelleme paketini MANUEL siler (en güncel sürüm silinemez).</summary>
    public Task<string?> DeleteReleasePackageAsync(string version) =>
        DeleteAsync($"/api/releases/packages/{Uri.EscapeDataString(version)}");

    public async Task<ReleaseDto?> GetLatestReleaseAsync()
    {
        var resp = await _http.GetAsync("/api/releases/latest");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ReleaseDto>();
    }

    /// <summary>Sürüm yayınla: dosyanın SHA-256'sı otomatik hesaplanır; API'ye çok-parçalı gönderilir. Hata → mesaj, başarı → null.</summary>
    public async Task<string?> PublishReleaseAsync(string version, string? notes, string fileName, byte[] fileBytes)
    {
        var checksum = Convert.ToHexString(SHA256.HashData(fileBytes));
        using var form = new MultipartFormDataContent
        {
            { new StringContent(version), "version" },
            { new StringContent(checksum), "checksum" },
            { new StringContent(fileBytes.Length.ToString()), "sizeBytes" },
            { new StringContent("0.0.0"), "minSupportedVersion" },
            { new StringContent(notes ?? ""), "releaseNotes" },
            { new StringContent("0"), "signed" },
        };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);

        var req = Req(HttpMethod.Post, "/api/releases");
        req.Content = form;
        var resp = await _http.SendAsync(req);
        return resp.IsSuccessStatusCode ? null : $"Hata {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}";
    }
}
