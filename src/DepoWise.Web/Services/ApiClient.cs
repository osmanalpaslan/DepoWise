using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace DepoWise.Web.Services;

public sealed record LoginResponse(string Token, string UserId, string CompanyId, bool IsSuperAdmin);
public sealed record MachineDto(string Id, string Name, string Status, string StatusText, string LastSeenText, string CreatedText, bool CanActivate, bool IsActive);
public sealed record ReleaseDto(string Version, string? ReleaseNotes, bool Signed, string? DownloadUrl);
public sealed record CompanyDto(string Id, string Name, string? TaxNo, string? Phone, string? Email, string? AuthorizedPerson, int UserCount);
public sealed record MenuModule(string Key, string Label, bool Create, bool Edit, bool Delete);
public sealed record RoleDto(string Key, string Name);
public sealed record MenuResponse(bool IsSuperAdmin, List<MenuModule> Modules);

/// <summary>
/// DepoWise.Api HTTP istemcisi (web arayüzü → API). Web hiçbir iş kuralı TAŞIMAZ; her şey API'de.
/// JWT AuthState'ten eklenir. Bu sınıf UI'ı API'ye bağlayan tek noktadır (Next.js'e geçişte de bu sözleşme aynı).
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly AuthState _auth;

    public ApiClient(HttpClient http, AuthState auth) { _http = http; _auth = auth; }

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
            if (data is not null) _auth.SetModules(data.Modules);
        }
        catch { }
    }

    private HttpRequestMessage Req(HttpMethod m, string url)
    {
        var r = new HttpRequestMessage(m, url);
        if (_auth.Token is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);
        return r;
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!resp.IsSuccessStatusCode) return "Kullanıcı adı veya parola hatalı.";
        var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        if (data is null) return "Sunucu yanıtı okunamadı.";
        _auth.SignIn(data.Token, data.UserId, data.CompanyId, data.IsSuperAdmin);
        await RefreshMenuAsync(); // yetkiye göre menü
        return null;
    }

    public async Task<List<MachineDto>> GetMachinesAsync()
    {
        var resp = await _http.SendAsync(Req(HttpMethod.Get, "/api/machines"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<MachineDto>>() ?? new();
    }

    /// <summary>Herhangi bir liste ucundan ham JSON dizi (genel tablo bileşeni için).</summary>
    public async Task<string?> PostAsync(string path, object body)
    {
        var req = Req(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        var r = await _http.SendAsync(req);
        return r.IsSuccessStatusCode ? null : $"Hata {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}";
    }

    public async Task<string?> DeleteAsync(string path)
    {
        var r = await _http.SendAsync(Req(HttpMethod.Delete, path));
        return r.IsSuccessStatusCode ? null : $"Hata {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}";
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/roles"));
        if (!r.IsSuccessStatusCode) return new();
        return await r.Content.ReadFromJsonAsync<List<RoleDto>>() ?? new();
    }

    public async Task<System.Text.Json.JsonElement[]> GetArrayAsync(string path)
    {
        var resp = await _http.SendAsync(Req(HttpMethod.Get, path));
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();
        return doc ?? Array.Empty<System.Text.Json.JsonElement>();
    }

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

    public Task ApproveMachineAsync(string id) => _http.SendAsync(Req(HttpMethod.Post, $"/api/machines/{id}/approve"));
    public Task RevokeMachineAsync(string id) => _http.SendAsync(Req(HttpMethod.Post, $"/api/machines/{id}/revoke"));

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
