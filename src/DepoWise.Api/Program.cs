using DepoWise.Api;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Update;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Environment.GetEnvironmentVariable("DEPOWISE_SERVER_DATA")
              ?? Path.Combine(builder.Environment.ContentRootPath, "data");
builder.Services.AddSingleton(new ServerServices(dataDir));

var app = builder.Build();
var svc = app.Services.GetRequiredService<ServerServices>();

static string? Bearer(HttpRequest r)
{
    var h = r.Headers.Authorization.ToString();
    return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : null;
}

// ── Sağlık ──
app.MapGet("/", () => Results.Ok(new { app = "DepoWise.Api", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

// ── Kimlik doğrulama (web/masaüstü oturumu) ──
app.MapPost("/api/auth/login", (LoginDto dto) =>
{
    var res = svc.Auth.Login(dto.CompanyId ?? "DEPOWISE", dto.Username, dto.Password);
    if (!res.Success || res.Session is null)
        return Results.Json(new { error = res.Locked ? $"Kilitli ({res.SecondsRemaining}sn)" : res.Error }, statusCode: 401);
    var token = svc.IssueToken(res.Session);
    return Results.Ok(new { token, userId = res.Session.UserId, companyId = res.Session.CompanyId, isSuperAdmin = res.Session.IsSuperAdmin });
});

// ── Senkron (cihaz token'ı ile) ──
app.MapPost("/sync/push", (HttpRequest req, PushDto dto) =>
{
    var token = Bearer(req); if (token is null) return Results.Unauthorized();
    var ops = dto.Ops.Select(o => new SyncOperation(o.OperationId, o.EntityType, o.EntityId, o.PayloadJson, o.BaseVersion)).ToList();
    // TODO(web): kritik entity'ler için sunucu-otoriteli doğrulayıcı. Şimdilik kabul (iskele).
    var outcomes = svc.Sync.Push(token, ops, op => (true, null));
    return Results.Ok(outcomes);
});

app.MapGet("/sync/pull", (HttpRequest req, long after, int limit) =>
{
    var token = Bearer(req); if (token is null) return Results.Unauthorized();
    return Results.Ok(svc.Sync.Pull(token, after, limit <= 0 ? 100 : limit));
});

app.MapPost("/sync/enroll", (EnrollDto dto) =>
{
    var r = svc.Enrollment.Enroll(dto.CompanyId, dto.Key, dto.DeviceName);
    return Results.Ok(r); // status = pending → Süper Admin onayı beklenir
});

// ── Makine yönetimi (Süper Admin oturum token'ı) ──
app.MapGet("/api/machines", (HttpRequest req) =>
{
    var s = svc.Resolve(Bearer(req)); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Enrollment.ListDevices(s));
});
app.MapPost("/api/machines/{id}/approve", (HttpRequest req, string id) =>
{
    var s = svc.Resolve(Bearer(req)); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Enrollment.ApproveDevice(s, id)); // { deviceId, token }
});
app.MapPost("/api/machines/{id}/revoke", (HttpRequest req, string id) =>
{
    var s = svc.Resolve(Bearer(req)); if (s is null) return Results.Unauthorized();
    svc.Enrollment.RevokeDevice(s, id);
    return Results.Ok(new { ok = true });
});

// ── Güncelleme (release) ──
app.MapGet("/api/releases/latest", () => Results.Ok(svc.Releases.Latest()));

app.MapPost("/api/releases", async (HttpRequest req) =>
{
    var s = svc.Resolve(Bearer(req)); if (s is null) return Results.Unauthorized();
    var form = await req.ReadFormAsync();
    var version = form["version"].ToString();
    var checksum = form["checksum"].ToString();
    var size = long.TryParse(form["sizeBytes"], out var sz) ? sz : 0;
    var min = form["minSupportedVersion"].ToString();
    var notes = form["releaseNotes"].ToString();
    var signed = form["signed"].ToString() == "1";
    string? downloadUrl = null;
    var file = form.Files["file"];
    if (file is not null)
    {
        await using var fs = file.OpenReadStream();
        await svc.ReleasePackages.SaveAsync(version, fs, req.HttpContext.RequestAborted);
        downloadUrl = $"/api/releases/{version}/download";
    }
    var id = svc.Releases.Publish(s, new NewRelease(version, checksum, size,
        string.IsNullOrWhiteSpace(min) ? "0.0.0" : min, string.IsNullOrWhiteSpace(notes) ? null : notes, signed, downloadUrl));
    return Results.Ok(new { id, downloadUrl });
});

app.MapGet("/api/releases/{version}/download", (string version) =>
{
    var path = svc.ReleasePackages.PathFor(version);
    return path is null ? Results.NotFound() : Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

// ── Sunucu yedek (bulut) ──
app.MapPost("/api/backups", async (HttpRequest req) =>
{
    if (Bearer(req) is null) return Results.Unauthorized();
    var form = await req.ReadFormAsync();
    var company = form["company"].ToString();
    var machine = form["machine"].ToString();
    var filename = form["filename"].ToString();
    var file = form.Files["file"];
    if (file is null) return Results.BadRequest(new { error = "file yok" });
    await using var fs = file.OpenReadStream();
    await svc.Backups.SaveAsync(company, machine, filename, fs, req.HttpContext.RequestAborted);
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/backups", (HttpRequest req, string company, DateOnly from, DateOnly to) =>
{
    var s = svc.Resolve(Bearer(req)); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Backups.List(company, from, to));
});

app.MapDelete("/api/backups", (HttpRequest req, string company, DateOnly from, DateOnly to) =>
{
    var s = svc.Resolve(Bearer(req)); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    return Results.Ok(new { deleted = svc.Backups.DeleteRange(company, from, to) });
});

app.Run();

// ── İstek gövde tipleri ──
record LoginDto(string? CompanyId, string Username, string Password);
record EnrollDto(string CompanyId, string Key, string DeviceName);
record PushDto(List<PushOp> Ops);
record PushOp(string OperationId, string EntityType, string EntityId, string PayloadJson, long? BaseVersion);
