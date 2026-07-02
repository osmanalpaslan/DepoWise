using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Update;
using Microsoft.AspNetCore.Authentication.JwtBearer;

// JWT "sub"/"company" claim adlarını KORU (.NET varsayılanı sub→NameIdentifier eşlemesini kapat)
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

var builder = WebApplication.CreateBuilder(args);

// Büyük güncelleme/kurulum paketleri (self-contained ~90-250MB) yüklenebilsin → istek boyutu sınırlarını yükselt.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_073_741_824); // 1 GB
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 1_073_741_824; // 1 GB
});

var dataDir = Environment.GetEnvironmentVariable("DEPOWISE_SERVER_DATA")
              ?? Path.Combine(builder.Environment.ContentRootPath, "data");
builder.Services.AddSingleton(new ServerServices(dataDir));

// JWT imza anahtarı: config > env > geliştirme varsayılanı (üretimde MUTLAKA gizli ayarla)
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? Environment.GetEnvironmentVariable("DEPOWISE_JWT_KEY")
             ?? "dev-only-change-me-please-32chars-minimum-secret-key";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = JwtTokens.ValidationParameters(jwtKey));
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
var svc = app.Services.GetRequiredService<ServerServices>();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Hata → doğru HTTP kodu (ForbiddenException 403, geçersiz istek 400, diğer 500)
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ForbiddenException ex) { await Write(ctx, 403, ex.Message); }
    catch (ArgumentException ex) { await Write(ctx, 400, ex.Message); }
    catch (InvalidOperationException ex) { await Write(ctx, 400, ex.Message); }
    catch (Exception ex) { await Write(ctx, 500, ex.Message); }
});
static Task Write(HttpContext ctx, int code, string msg)
{
    ctx.Response.StatusCode = code;
    ctx.Response.ContentType = "application/json";
    return ctx.Response.WriteAsJsonAsync(new { error = msg });
}

// Cihaz senkron token'ı (JWT değil) — ham Authorization
static string? DeviceToken(HttpRequest r)
{
    var h = r.Headers.Authorization.ToString();
    return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h["Bearer ".Length..].Trim() : null;
}
// Oturum: JWT claim'lerinden tam SessionContext'i SUNUCUDA yeniden kur
SessionContext? Session(HttpContext ctx)
{
    var userId = ctx.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
    var company = ctx.User.FindFirstValue(JwtTokens.CompanyClaim);
    return svc.SessionFor(company, userId);
}

// ── Sağlık ──
app.MapGet("/", () => Results.Ok(new { app = "DepoWise.Api", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

// ── Kimlik doğrulama → JWT ──
app.MapPost("/api/auth/login", (LoginDto dto) =>
{
    var res = svc.Auth.Login(dto.CompanyId ?? "DEPOWISE", dto.Username, dto.Password);
    if (!res.Success || res.Session is null)
        return Results.Json(new { error = res.Locked ? $"Kilitli ({res.SecondsRemaining}sn)" : res.Error }, statusCode: 401);
    var token = JwtTokens.Issue(jwtKey, res.Session.UserId, res.Session.CompanyId);
    return Results.Ok(new { token, userId = res.Session.UserId, companyId = res.Session.CompanyId, isSuperAdmin = res.Session.IsSuperAdmin });
});

// ── Senkron (cihaz token'ı) ──
app.MapPost("/sync/push", (HttpRequest req, PushDto dto) =>
{
    var token = DeviceToken(req); if (token is null) return Results.Unauthorized();
    var ops = dto.Ops.Select(o => new SyncOperation(o.OperationId, o.EntityType, o.EntityId, o.PayloadJson, o.BaseVersion)).ToList();
    // Kritik entity'ler sunucu-otoriteli doğrulanır (tenant + referans + tutarlılık); düşük-riskli LWW/version.
    return Results.Ok(svc.Sync.Push(token, ops, svc.SyncValidator.Validate));
});
app.MapGet("/sync/pull", (HttpRequest req, long after, int limit) =>
{
    var token = DeviceToken(req); if (token is null) return Results.Unauthorized();
    return Results.Ok(svc.Sync.Pull(token, after, limit <= 0 ? 100 : limit));
});
app.MapPost("/sync/enroll", (EnrollDto dto) => Results.Ok(svc.Enrollment.Enroll(dto.CompanyId, dto.Key, dto.DeviceName)));

// ── Makine yönetimi (JWT — admin) ──
app.MapGet("/api/machines", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Enrollment.ListDevices(s));
}).RequireAuthorization();
app.MapPost("/api/machines/{id}/approve", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Enrollment.ApproveDevice(s, id));
}).RequireAuthorization();
app.MapPost("/api/machines/{id}/revoke", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Enrollment.RevokeDevice(s, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── Kullanıcının menüsü/yetkileri (masaüstüyle AYNI AccessControl) → web menüyü buna göre çizer ──
app.MapGet("/api/me/menu", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var mods = DepoWise.Application.Security.AppModules.All
        .Where(m => DepoWise.Application.Security.AccessControl.CanSeeMenu(s, m.Key))
        .Select(m => new
        {
            key = m.Key,
            label = m.Label,
            create = DepoWise.Application.Security.AccessControl.Can(s, m.Key, PermissionAction.Create),
            edit = DepoWise.Application.Security.AccessControl.Can(s, m.Key, PermissionAction.Edit),
            delete = DepoWise.Application.Security.AccessControl.Can(s, m.Key, PermissionAction.Delete),
        }).ToList();
    return Results.Ok(new { isSuperAdmin = s.IsSuperAdmin, modules = mods });
}).RequireAuthorization();

// ── Firmalar (Süper Admin) ──
app.MapGet("/api/companies", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Companies.List(s));
}).RequireAuthorization();
app.MapPost("/api/companies", (HttpContext ctx, NewCompanyDto dto) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var id = svc.Companies.Create(s, new DepoWise.Infrastructure.Organization.NewCompany(
        dto.Name, dto.TaxNo, dto.TaxOffice, dto.Address, dto.Phone, dto.Email, dto.AuthorizedPerson));
    return Results.Ok(new { id });
}).RequireAuthorization();

// ── İş modülleri: liste (okuma) uçları — hepsi yetki korumalı (servis AccessControl.View) ──
DepoWise.Application.Common.PageRequest Page() => new() { Limit = 500 };
SessionContext? S(HttpContext ctx) => Session(ctx);

app.MapGet("/api/users", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Users.ListUsers(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/branches", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Branches.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/personnel", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Personnel.List(s, Page()).Items) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/materials", (HttpContext c, string? search) => S(c) is { } s ? Results.Ok(svc.Materials.List(s, Page(), search).Items) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles", (HttpContext c, string? search) => S(c) is { } s ? Results.Ok(svc.Vehicles.List(s, search)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/stock", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Stock.RecentMovements(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/maintenance", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Maintenance.ListMaintenances(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/inspection", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Inspection.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/fuel", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Fuel.ListDistributions(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/daily", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.DailyActivity.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/requests", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Requests.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/lookups/{table}", (HttpContext c, string table) => S(c) is { } s ? Results.Ok(svc.Lookups.List(s, table)) : Results.Unauthorized()).RequireAuthorization();

// Roller (kullanıcı oluşturma için)
app.MapGet("/api/roles", (HttpContext c) => S(c) is null ? Results.Unauthorized()
    : Results.Ok(RoleKeys.Seed.Where(r => r.Key != RoleKeys.SuperAdmin).Select(r => new { key = r.Key, name = r.Name }))).RequireAuthorization();

// ── Yazma (ekle/sil) uçları — servis AccessControl (Create/Delete) enforce eder ──
app.MapPost("/api/branches", (HttpContext c, NameDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Branches.Create(s, new DepoWise.Infrastructure.Organization.NewBranch(d.Name)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/personnel", (HttpContext c, PersonnelDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Personnel.Create(s, new DepoWise.Infrastructure.Org.NewPersonnel(d.FullName, d.Title, d.Phone, null)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users", (HttpContext c, NewUserDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Users.CreateUser(s, new DepoWise.Infrastructure.Security.NewUser(d.Username, d.Password, d.FullName, d.RoleKeys ?? new List<string>(), s.CompanyId)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/materials", (HttpContext c, NewMaterialDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Materials.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial(d.Code, d.Name, d.Type, d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice)) }) : Results.Unauthorized()).RequireAuthorization();

app.MapPost("/api/lookups/{table}", (HttpContext c, string table, NameDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var id = table switch
    {
        "units" => svc.Lookups.AddUnit(s, d.Name),
        "suppliers" => svc.Lookups.AddSupplier(s, d.Name),
        "material_categories" => svc.Lookups.AddCategory(s, d.Name),
        "brands" => svc.Lookups.AddBrand(s, d.Name, "material"),
        "vehicle_types" => svc.Lookups.AddVehicleType(s, d.Name),
        "vehicle_categories" => svc.Lookups.AddVehicleCategory(s, d.Name),
        _ => throw new ArgumentException("Bilinmeyen tanım tablosu."),
    };
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapDelete("/api/lookups/{table}/{id}", (HttpContext c, string table, string id) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.Lookups.Delete(s, table, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── Güncelleme (release) ──
app.MapGet("/api/releases/latest", () => Results.Ok(svc.Releases.Latest()));
app.MapPost("/api/releases", async (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var form = await ctx.Request.ReadFormAsync();
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
        await svc.ReleasePackages.SaveAsync(version, fs, ctx.RequestAborted);
        downloadUrl = $"/api/releases/{version}/download";
    }
    var id = svc.Releases.Publish(s, new NewRelease(version, checksum, size,
        string.IsNullOrWhiteSpace(min) ? "0.0.0" : min, string.IsNullOrWhiteSpace(notes) ? null : notes, signed, downloadUrl));
    return Results.Ok(new { id, downloadUrl });
}).RequireAuthorization();
// ── Masaüstü kurulum aracı (setup) indirme/yükleme ──
app.MapGet("/api/setup/download", () =>
{
    var path = Path.Combine(dataDir, "setup", "DepoWiseSetup.exe");
    return File.Exists(path)
        ? Results.File(path, "application/octet-stream", "DepoWiseSetup.exe")
        : Results.NotFound(new { error = "Kurulum aracı henüz yüklenmedi." });
});
app.MapPost("/api/setup", async (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files["file"]; if (file is null) return Results.BadRequest(new { error = "file yok" });
    var dir = Path.Combine(dataDir, "setup"); Directory.CreateDirectory(dir);
    await using var fs = File.Create(Path.Combine(dir, "DepoWiseSetup.exe"));
    await file.OpenReadStream().CopyToAsync(fs, ctx.RequestAborted);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapGet("/api/releases/{version}/download", (string version) =>
{
    var path = svc.ReleasePackages.PathFor(version);
    return path is null ? Results.NotFound() : Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

// ── Sunucu yedek (bulut) ──
app.MapPost("/api/backups", async (HttpRequest req) =>
{
    if (DeviceToken(req) is null) return Results.Unauthorized();
    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file is null) return Results.BadRequest(new { error = "file yok" });
    await using var fs = file.OpenReadStream();
    await svc.Backups.SaveAsync(form["company"].ToString(), form["machine"].ToString(), form["filename"].ToString(), fs, req.HttpContext.RequestAborted);
    return Results.Ok(new { ok = true });
});
app.MapGet("/api/backups", (HttpContext ctx, string company, DateOnly from, DateOnly to) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Backups.List(company, from, to));
}).RequireAuthorization();
app.MapDelete("/api/backups", (HttpContext ctx, string company, DateOnly from, DateOnly to) =>
{
    var s = Session(ctx); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    return Results.Ok(new { deleted = svc.Backups.DeleteRange(company, from, to) });
}).RequireAuthorization();

app.Run();

// ── İstek gövde tipleri ──
record LoginDto(string? CompanyId, string Username, string Password);
record EnrollDto(string CompanyId, string Key, string DeviceName);
record PushDto(List<PushOp> Ops);
record PushOp(string OperationId, string EntityType, string EntityId, string PayloadJson, long? BaseVersion);
record NewCompanyDto(string Name, string? TaxNo, string? TaxOffice, string? Address, string? Phone, string? Email, string? AuthorizedPerson);
record NameDto(string Name);
record PersonnelDto(string FullName, string? Title, string? Phone);
record NewUserDto(string Username, string Password, string? FullName, List<string>? RoleKeys);
record NewMaterialDto(string Code, string Name, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId, decimal MinStock, decimal UnitPrice);
