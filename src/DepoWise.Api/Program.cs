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

// #19 — canlı sunucu durumu için hafif istek sayacı.
_ = ServerMetrics.Start; // başlangıç anını sabitle
app.Use(async (ctx, next) => { System.Threading.Interlocked.Increment(ref ServerMetrics.Requests); await next(); });

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

// #19 — Canlı sunucu durumu (YALNIZ süper admin). Süreç + veri + canlılık metrikleri (web animasyonlu ekran poll eder).
app.MapGet("/api/server/status", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);

    var proc = System.Diagnostics.Process.GetCurrentProcess();
    const double MB = 1024d * 1024d;
    long dbBytes = 0, companies = 0, users = 0, machinesOnline = 0;
    try
    {
        using var conn = svc.Factory.Create();
        using (var c = conn.CreateCommand()) { c.CommandText = "PRAGMA page_count;"; var pc = Convert.ToInt64(c.ExecuteScalar()); c.CommandText = "PRAGMA page_size;"; var ps = Convert.ToInt64(c.ExecuteScalar()); dbBytes = pc * ps; }
        using (var c = conn.CreateCommand()) { c.CommandText = "SELECT COUNT(*) FROM companies WHERE is_deleted=0;"; companies = Convert.ToInt64(c.ExecuteScalar()); }
        using (var c = conn.CreateCommand()) { c.CommandText = "SELECT COUNT(*) FROM users WHERE is_deleted=0;"; users = Convert.ToInt64(c.ExecuteScalar()); }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM sync_devices WHERE last_seen_at IS NOT NULL AND last_seen_at > $t;";
            c.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 5 * 60 * 1000);
            machinesOnline = Convert.ToInt64(c.ExecuteScalar());
        }
    }
    catch { }
    string? latest = null; try { latest = svc.Releases.Latest()?.Version; } catch { }

    return Results.Ok(new
    {
        uptimeSeconds = (long)(DateTimeOffset.UtcNow - ServerMetrics.Start).TotalSeconds,
        workingSetMb = Math.Round(proc.WorkingSet64 / MB, 1),
        gcMemoryMb = Math.Round(GC.GetTotalMemory(false) / MB, 1),
        threadCount = proc.Threads.Count,
        dotnet = Environment.Version.ToString(),
        dbSizeMb = Math.Round(dbBytes / MB, 2),
        companies,
        users,
        machinesOnline,
        latestVersion = latest ?? "—",
        requestCount = System.Threading.Interlocked.Read(ref ServerMetrics.Requests),
        serverTimeUtc = DateTimeOffset.UtcNow,
    });
}).RequireAuthorization();

// ── Kimlik doğrulama → JWT ──
app.MapPost("/api/auth/login", (LoginDto dto) =>
{
    bool allBranches = string.Equals(dto.BranchId, BranchConstants.AllBranchesId, StringComparison.Ordinal);
    // Gerçek şube seçildiyse önce ŞUBE ŞİFRESİ doğrulanır (şubede şifre tanımlı değilse serbest). "Tüm Şubeler" sanal seçimdir.
    if (!allBranches && !string.IsNullOrWhiteSpace(dto.BranchId))
    {
        var co = string.IsNullOrWhiteSpace(dto.CompanyId) ? "DEPOWISE" : dto.CompanyId!;
        if (!svc.Branches.VerifyBranchPassword(co, dto.BranchId!, dto.BranchPassword))
            return Results.Json(new { error = "Şube şifresi hatalı." }, statusCode: 401);
    }
    // companyId verilmezse (web) firma-bağımsız giriş: kullanıcı adı hangi firmadaysa oraya girer.
    var res = string.IsNullOrWhiteSpace(dto.CompanyId)
        ? svc.Auth.LoginAnyCompany(dto.Username, dto.Password)
        : svc.Auth.Login(dto.CompanyId!, dto.Username, dto.Password);
    if (!res.Success || res.Session is null)
        return Results.Json(new { error = res.Locked ? $"Kilitli ({res.SecondsRemaining}sn)" : res.Error }, statusCode: 401);
    // "Tüm Şubeler" seçimi YALNIZ yetkili kullanıcıya açık.
    if (allBranches && !res.Session.CanViewAllBranches)
        return Results.Json(new { error = "Bu kullanıcının Tüm Şubeler yetkisi yok." }, statusCode: 403);
    var token = JwtTokens.Issue(jwtKey, res.Session.UserId, res.Session.CompanyId);
    // 2 aşamalı login: kullanıcının KENDİ firmasının adı + şubeleri döner (kullanıcı firma listesini görmez).
    var companyName = svc.Companies.GetName(res.Session.CompanyId);
    var branches = svc.Branches.ListForLogin(res.Session.CompanyId)
        .Select(b => new { id = b.Id, name = b.Name, code = b.Code, hasPassword = b.HasPassword });
    return Results.Ok(new { token, userId = res.Session.UserId, companyId = res.Session.CompanyId,
        companyName, branches, isSuperAdmin = res.Session.IsSuperAdmin, branchId = dto.BranchId,
        canViewAllBranches = res.Session.CanViewAllBranches });
});

// Masaüstü senkron girişi: yerel DB'de olmayan (web'te oluşturulan) kullanıcıyı sunucu doğrular ve
// tam paketini döndürür → masaüstü yerele yazıp giriş yapar. Geçersizse 401.
app.MapPost("/api/auth/sync-login", (LoginDto dto) =>
{
    var bundle = svc.Auth.ExportForSync(dto.CompanyId ?? "", dto.Username, dto.Password);
    return bundle is null
        ? Results.Json(new { error = "Kullanıcı adı veya parola hatalı." }, statusCode: 401)
        : Results.Ok(bundle);
});

// ── Login ekranı için PUBLIC firma + şube listesi (anonim; kod+şifre-var-mı) ──
app.MapGet("/api/public/companies", () =>
{
    using var conn = svc.Factory.Create();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name FROM companies WHERE is_deleted=0 ORDER BY name;";
    var list = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add(new { id = r.GetString(0), name = r.GetString(1) });
    return Results.Ok(list);
});
app.MapGet("/api/public/branches", (string companyId) =>
{
    if (string.IsNullOrWhiteSpace(companyId)) return Results.Ok(Array.Empty<object>());
    var rows = svc.Branches.ListForLogin(companyId);
    return Results.Ok(rows.Select(b => new { id = b.Id, name = b.Name, code = b.Code, hasPassword = b.HasPassword }));
});
app.MapPost("/api/public/verify-branch", (VerifyBranchDto d) =>
{
    var co = string.IsNullOrWhiteSpace(d.CompanyId) ? "DEPOWISE" : d.CompanyId!;
    return Results.Ok(new { ok = svc.Branches.VerifyBranchPassword(co, d.BranchId, d.BranchPassword) });
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

// İş verisi SNAPSHOT push (JWT) — masaüstü kendi firmasının iş tablolarını gönderir; sunucu upsert eder
// (company_id oturumdan zorlanır). Web adminleri bu veriyi görür. Faz 2 "güvenli web görünürlüğü".
app.MapPost("/api/sync/business-push", async (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(c.Request.Body);
    var res = svc.BusinessSync.Apply(s.CompanyId, doc.RootElement);
    return Results.Ok(new { upserted = res.Upserted, skipped = res.Skipped, errors = res.Errors });
}).RequireAuthorization();

// Çakışmalar — admin (tümü) / personel (görmediği, şube kapsamında)
app.MapGet("/api/sync/conflicts", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(svc.BusinessSync.ListConflicts(s.CompanyId)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/sync/conflicts/unseen", (HttpContext c, string? branchId) =>
    S(c) is { } s ? Results.Ok(svc.BusinessSync.ListUnseen(s.CompanyId, string.IsNullOrWhiteSpace(branchId) ? null : branchId)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/sync/conflicts/seen", (HttpContext c, ConflictSeenDto d) =>
    S(c) is { } s ? Results.Ok(new { marked = svc.BusinessSync.MarkSeen(s.CompanyId, string.IsNullOrWhiteSpace(d.BranchId) ? null : d.BranchId) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/sync/conflicts/{id}/resolve", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.BusinessSync.ResolveConflict(s.CompanyId, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Makine yönetimi (JWT — admin) ──
app.MapGet("/api/machines", (HttpContext ctx, string? companyId) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var rows = svc.Enrollment.ListDevices(s, companyId).Select(d => new
    {
        id = d.Id, name = d.Name, status = d.Status, statusText = d.StatusText,
        lastSeenText = d.LastSeenText, createdText = d.CreatedText, canActivate = d.CanActivate, isActive = d.IsActive,
        companyId = d.CompanyId, companyName = d.CompanyName, quota = d.Quota, branchName = d.BranchText,
        ip = d.IpText, ipv4 = d.Ip4Text, ipv6 = d.Ip6Text,
        online = d.LastSeenAt is long t && (now - t) <= 90_000, // son 90 sn içinde ping = çevrimiçi
    });
    return Results.Ok(rows);
}).RequireAuthorization();
// Firma makine kotası (yalnız süper admin)
app.MapPost("/api/companies/{id}/machine-quota", (HttpContext ctx, string id, QuotaDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Enrollment.SetQuota(s, id, d.Quota);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// Sıfır-sürtünmeli kayıt: masaüstü açılışta kendini 'pending' cihaz olarak kaydeder (auth gerekmez).
app.MapPost("/api/machines/register", (HttpContext ctx, MachineRegisterDto d) =>
    Results.Ok(svc.Enrollment.RegisterSelf(
        string.IsNullOrWhiteSpace(d.CompanyId) ? "DEPOWISE" : d.CompanyId!,
        string.IsNullOrWhiteSpace(d.MachineName) ? "Bilinmeyen Makine" : d.MachineName!,
        ClientIp(ctx), string.IsNullOrWhiteSpace(d.BranchId) ? null : d.BranchId)));
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
app.MapPost("/api/machines/{id}/reactivate", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Enrollment.Reactivate(s, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapDelete("/api/machines/{id}", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Enrollment.DeleteDevice(s, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── Kullanıcının menüsü/yetkileri (masaüstüyle AYNI AccessControl) → web menüyü buna göre çizer ──
// ── Kullanıcı yetki/şifre "imzası" (masaüstü değişiklik tespiti) ──
// Parola + roller + yetkiler + buton izinleri + aktiflik hash'i. Web'de değişince imza değişir → masaüstü uyarır.
app.MapGet("/api/me/authsig", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    using var conn = svc.Factory.Create();
    string ph = ""; int active = 1;
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT password_hash, is_active FROM users WHERE id=$u;";
        c.Parameters.AddWithValue("$u", s.UserId);
        using var r = c.ExecuteReader();
        if (r.Read()) { ph = r.GetString(0); active = r.GetInt32(1); }
    }
    var roles = new List<string>();
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$u ORDER BY r.role_key;";
        c.Parameters.AddWithValue("$u", s.UserId);
        using var r = c.ExecuteReader(); while (r.Read()) roles.Add(r.GetString(0));
    }
    var perms = new List<string>();
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT module_key,can_view,can_create,can_edit,can_delete FROM user_permissions WHERE user_id=$u ORDER BY module_key;";
        c.Parameters.AddWithValue("$u", s.UserId);
        using var r = c.ExecuteReader();
        while (r.Read()) perms.Add($"{r.GetString(0)}:{r.GetInt64(1)}{r.GetInt64(2)}{r.GetInt64(3)}{r.GetInt64(4)}");
    }
    var buttons = new List<string>();
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=$u ORDER BY button_key;";
        c.Parameters.AddWithValue("$u", s.UserId);
        using var r = c.ExecuteReader(); while (r.Read()) buttons.Add(r.GetString(0));
    }
    var raw = $"{ph}|{active}|{string.Join(",", roles)}|{string.Join(",", perms)}|{string.Join(",", buttons)}";
    var sig = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
    return Results.Ok(new { sig });
}).RequireAuthorization();

// ── Kullanıcı bazlı web tema tercihi (her kullanıcıya özel; cihazdan bağımsız) ──
app.MapGet("/api/me/theme", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var mode = svc.Settings.Get(s.CompanyId, $"web_theme_mode:{s.UserId}");
    var color = svc.Settings.Get(s.CompanyId, $"web_theme_color:{s.UserId}");
    var style = svc.Settings.Get(s.CompanyId, $"web_theme_style:{s.UserId}");
    return Results.Ok(new
    {
        mode = string.IsNullOrEmpty(mode) ? "dark" : mode,
        color = string.IsNullOrEmpty(color) ? "blue" : color,
        style = string.IsNullOrEmpty(style) ? "classic" : style,
    });
}).RequireAuthorization();
app.MapPost("/api/me/theme", (HttpContext ctx, UserThemeDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    if (!string.IsNullOrEmpty(d.Mode)) svc.Settings.Set(s.CompanyId, $"web_theme_mode:{s.UserId}", d.Mode, s.UserId);
    if (!string.IsNullOrEmpty(d.Color)) svc.Settings.Set(s.CompanyId, $"web_theme_color:{s.UserId}", d.Color, s.UserId);
    if (!string.IsNullOrEmpty(d.Style)) svc.Settings.Set(s.CompanyId, $"web_theme_style:{s.UserId}", d.Style, s.UserId);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

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
    return Results.Ok(new { isSuperAdmin = s.IsSuperAdmin, isAdmin = DepoWise.Application.Security.AccessControl.IsAdmin(s), modules = mods });
}).RequireAuthorization();

// ── Ana ekran: kritik uyarılar (bakım + muayene/sigorta + düşük stok + yakıt) ──
app.MapGet("/api/dashboard", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var sum = svc.Dashboard.GetSummary(s);
    return Results.Ok(new
    {
        alerts = sum.Alerts.Select(a => new
        {
            kind = a.Kind.ToString(), title = a.Title, detail = a.Detail,
            navigateKey = a.NavigateKey, isCritical = a.IsCritical, icon = a.Icon,
            key = a.Key, signature = a.Signature, read = a.Read, // #18
        }),
    });
}).RequireAuthorization();
// #18 — Uyarıyı kullanıcı için "okundu" işaretle (ana ekrandan gizlenir; hali değişince yeniden görünür).
app.MapPost("/api/alerts/read", (HttpContext ctx, AlertReadDto d) =>
    Session(ctx) is { } s ? Results.Ok(new { ok = Void(() => svc.Dashboard.MarkAlertRead(s, d.Key ?? "", d.Signature ?? "")) }) : Results.Unauthorized()).RequireAuthorization();

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
        dto.Name, dto.TaxNo, dto.TaxOffice, dto.Address, dto.Phone, dto.Email, dto.AuthorizedPerson, dto.MaxUsers));
    return Results.Ok(new { id });
}).RequireAuthorization();

app.MapPut("/api/companies/{id}", (HttpContext ctx, string id, NewCompanyDto dto) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Companies.Update(s, id, new DepoWise.Infrastructure.Organization.NewCompany(
        dto.Name, dto.TaxNo, dto.TaxOffice, dto.Address, dto.Phone, dto.Email, dto.AuthorizedPerson, dto.MaxUsers));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapDelete("/api/companies/{id}", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Companies.Delete(s, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── İş modülleri: liste (okuma) uçları — hepsi yetki korumalı (servis AccessControl.View) ──
DepoWise.Application.Common.PageRequest Page() => new() { Limit = 500 };
SessionContext? S(HttpContext ctx) => Session(ctx);
static string? Doc(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
static string? ClientIp(HttpContext c)
{
    var fly = c.Request.Headers["Fly-Client-IP"].ToString();
    if (!string.IsNullOrWhiteSpace(fly)) return fly;
    var xff = c.Request.Headers["X-Forwarded-For"].ToString();
    if (!string.IsNullOrWhiteSpace(xff)) return xff.Split(',')[0].Trim();
    return c.Connection.RemoteIpAddress?.ToString();
}
static bool Void(Action a) { a(); return true; }

app.MapGet("/api/users", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Users.ListUsers(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/branches", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Branches.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/personnel", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Personnel.List(s, Page()).Items) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/materials", (HttpContext c, string? search) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var rows = svc.Materials.List(s, Page(), search).Items.Select(m =>
    {
        var stock = svc.Stock.GetBalance(m.Id);
        var status = stock <= 0 ? "Stok Yok" : stock <= m.MinStock ? "Düşük Stok" : "Yeterli";
        return new { id = m.Id, code = m.Code, name = m.Name, type = m.Type, unitPrice = m.UnitPrice, currency = m.Currency, minStock = m.MinStock, stock, statusText = status };
    }).ToList();
    return Results.Ok(rows);
}).RequireAuthorization();
app.MapGet("/api/materials/{id}", (HttpContext c, string id) => S(c) is { } s ? Results.Ok(svc.Materials.GetDetail(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/materials/{id}", (HttpContext c, string id, NewMaterialDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.Materials.Update(s, id, new DepoWise.Infrastructure.Materials.UpdateMaterial(
        d.Code, d.Name, d.Type, d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, Doc(d.Description)));
    if (d.VehicleIds is not null) svc.Materials.SetCompatibleVehicles(s, id, d.VehicleIds);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapDelete("/api/materials/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Materials.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/materials/{id}/compatible-vehicles", (HttpContext c, string id, IdListDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Materials.SetCompatibleVehicles(s, id, d.Ids ?? new())) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/materials/{id}/equivalents", (HttpContext c, string id, IdDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Materials.AddEquivalent(s, id, d.Id)) }) : Results.Unauthorized()).RequireAuthorization();

// Malzeme fotoğrafları (file_records + disk storage)
app.MapGet("/api/materials/{id}/photos", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Files.GetPhotos(s, "material", id).Select(p => new { id = p.Id, url = $"/api/materials/{id}/photos/{p.Id}" })) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/materials/{id}/photos/{fileId}", (HttpContext c, string id, string fileId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var p = svc.Files.GetPhotos(s, "material", id).FirstOrDefault(x => x.Id == fileId);
    if (p is null) return Results.NotFound();
    return Results.File(svc.Storage.Read(p.StorageKey), p.Mime ?? "image/jpeg");
}).RequireAuthorization();
app.MapPost("/api/materials/{id}/photos", async (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var form = await ctx.Request.ReadFormAsync();
    int n = 0;
    foreach (var file in form.Files)
    {
        using var ms = new MemoryStream();
        await file.OpenReadStream().CopyToAsync(ms, ctx.RequestAborted);
        svc.Files.SavePhoto(s, "material", id, file.FileName, file.ContentType, ms.ToArray());
        n++;
    }
    return Results.Ok(new { added = n });
}).RequireAuthorization();
app.MapDelete("/api/materials/{id}/photos/{fileId}", (HttpContext c, string id, string fileId) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Files.DeletePhoto(s, fileId)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles", (HttpContext c, string? search) => S(c) is { } s ? Results.Ok(svc.Vehicles.List(s, search)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/stock", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Stock.RecentMovements(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/maintenance", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Maintenance.ListMaintenances(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/inspection", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Inspection.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/fuel", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Fuel.ListDistributions(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/daily", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.DailyActivity.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/requests", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Requests.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/lookups/{table}", (HttpContext c, string table) => S(c) is { } s ? Results.Ok(svc.Lookups.List(s, table)) : Results.Unauthorized()).RequireAuthorization();
// Araç markaları (brand_type=vehicle) — malzeme markalarından ayrı
app.MapGet("/api/lookups/vehicle_brands", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Lookups.ListBrands(s, "vehicle")) : Results.Unauthorized()).RequireAuthorization();
// Malzeme alt kategorileri (seçili kategorinin çocukları)
app.MapGet("/api/materials/subcategories", (HttpContext c, string? parentId) =>
    S(c) is { } s ? Results.Ok(svc.Lookups.ListCategories(s, string.IsNullOrWhiteSpace(parentId) ? null : parentId)) : Results.Unauthorized()).RequireAuthorization();

// Roller (kullanıcı oluşturma için)
app.MapGet("/api/roles", (HttpContext c) => S(c) is null ? Results.Unauthorized()
    : Results.Ok(RoleKeys.Seed.Where(r => r.Key != RoleKeys.SuperAdmin).Select(r => new { key = r.Key, name = r.Name }))).RequireAuthorization();

// ── Yazma (ekle/sil) uçları — servis AccessControl (Create/Delete) enforce eder ──
app.MapPost("/api/branches", (HttpContext c, BranchDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Branches.Create(s, new DepoWise.Infrastructure.Organization.NewBranch(d.Name, string.IsNullOrWhiteSpace(d.Kind) ? "branch" : d.Kind!, d.ParentId, Doc(d.Code), Doc(d.Password))) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/branches/{id}/users", (HttpContext c, string id) => S(c) is { } s ? Results.Ok(svc.Branches.GetUsers(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/personnel", (HttpContext c, PersonnelDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Personnel.Create(s, new DepoWise.Infrastructure.Org.NewPersonnel(d.FullName, d.Title, d.Phone, d.BranchId, d.IsActive)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/personnel/{id}", (HttpContext c, string id, PersonnelDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Personnel.Update(s, id, new DepoWise.Infrastructure.Org.NewPersonnel(d.FullName, d.Title, d.Phone, d.BranchId, d.IsActive))) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users", (HttpContext c, NewUserDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // Firma: YALNIZ süper admin seçebilir; diğerleri kendi firmasına bağlar (yetki yükseltme engeli).
    var companyId = s.IsSuperAdmin && !string.IsNullOrWhiteSpace(d.CompanyId) ? d.CompanyId! : s.CompanyId;
    var id = svc.Users.CreateUser(s, new DepoWise.Infrastructure.Security.NewUser(
        d.Username, d.Password, d.FullName, d.RoleKeys ?? new List<string>(), companyId, null, d.BranchId, d.CanViewAllBranches));
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapPost("/api/materials", (HttpContext c, NewMaterialDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var id = svc.Materials.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial(
        d.Code, d.Name, d.Type, d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, "TRY", Doc(d.Description)));
    if (d.VehicleIds is { Count: > 0 }) svc.Materials.SetCompatibleVehicles(s, id, d.VehicleIds);
    if (d.EquivalentIds is not null) foreach (var eq in d.EquivalentIds) svc.Materials.AddEquivalent(s, id, eq);
    if (d.OpeningStock > 0)
        svc.OpeningStock.RecordOpening(s, id, d.OpeningStock, Guid.NewGuid().ToString("N"), d.UnitPrice > 0 ? d.UnitPrice : null);
    return Results.Ok(new { id });
}).RequireAuthorization();

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
        "vehicle_brands" => svc.Lookups.AddVehicleBrand(s, d.Name),
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
// Alan adı değiştirme (ID korunur) — YALNIZ süper admin
app.MapPut("/api/lookups/{table}/{id}", (HttpContext c, string table, string id, NameDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Alan adı değişimi yalnız süper admin." }, statusCode: 403);
    svc.Lookups.Rename(s, table, id, d.Name);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// Tanım (lookup) senkronu: masaüstü giriş sonrası TÜM firma tanımlarını çeker → yerele yazar.
// Böylece web'te eklenen/yeniden adlandırılan tanımlar tüm makinelerde görünür (id korunur, ad güncellenir).
app.MapGet("/api/lookups/sync", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var company = s.CompanyId;
    using var conn = svc.Factory.Create();
    List<Dictionary<string, object?>> Rows(string sql)
    {
        var list = new List<Dictionary<string, object?>>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$c", company);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }
    return Results.Ok(new
    {
        companyId = company,
        units = Rows("SELECT id,name FROM units WHERE company_id=$c AND is_deleted=0;"),
        suppliers = Rows("SELECT id,name FROM suppliers WHERE company_id=$c AND is_deleted=0;"),
        vehicleTypes = Rows("SELECT id,name FROM vehicle_types WHERE company_id=$c AND is_deleted=0;"),
        vehicleCategories = Rows("SELECT id,name FROM vehicle_categories WHERE company_id=$c AND is_deleted=0;"),
        materialCategories = Rows("SELECT id,name,parent_id FROM material_categories WHERE company_id=$c AND is_deleted=0;"),
        brands = Rows("SELECT id,name,brand_type FROM brands WHERE company_id=$c AND is_deleted=0;"),
        vehicleModels = Rows("SELECT id,name,brand_id FROM vehicle_models WHERE company_id=$c AND is_deleted=0;"),
        branches = Rows("SELECT id,name,kind,parent_id FROM branches WHERE company_id=$c AND is_deleted=0;"),
    });
}).RequireAuthorization();

// TEST verisi temizleme — YALNIZ süper admin. İş/test kayıtlarını siler; auth (users/roles/
// permissions), companies, app_settings, app_releases, schema_migrations KORUNUR → giriş + sürümler bozulmaz.
app.MapPost("/api/admin/reset-data", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Bu işlem yalnız süper admin." }, statusCode: 403);

    var clearTables = new[]
    {
        // Malzeme + stok
        "materials","material_equivalents","material_compatible_vehicles",
        "stock_movements","stock_balances","stock_documents","stock_count_lines",
        // Araç + bakım
        "vehicles","vehicle_maintenances","maintenance_materials","vehicle_inspections",
        "vehicle_meter_logs","vehicle_templates","vehicle_template_materials",
        "maintenance_definitions","maintenance_definition_vehicles",
        // Personel + operasyon
        "personnel","fuel_depot_entries","fuel_distributions","daily_activities",
        // Talepler
        "material_requests","material_request_items","request_status_history",
        // Tanımlar (lookup)
        "material_categories","brands","units","suppliers",
        "vehicle_types","vehicle_categories","vehicle_models","fx_rates",
        // Dosyalar + loglar + oturum + sync (test makineleri/kayıtları)
        "file_records","audit_logs","login_attempts","sessions","user_scopes",
        "sync_devices","sync_outbox","sync_inbox","server_changes","sync_conflicts","enrollment_keys",
    };

    using var conn = svc.Factory.Create();
    using (var pragma = conn.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=OFF;"; pragma.ExecuteNonQuery(); }
    using var tx = conn.BeginTransaction();
    var cleared = new List<string>();
    foreach (var t in clearTables)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {t};";
            var n = cmd.ExecuteNonQuery();
            cleared.Add($"{t}:{n}");
        }
        catch (Exception ex) { cleared.Add($"{t}:HATA {ex.Message}"); }
    }
    tx.Commit();
    using (var pragma = conn.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=ON;"; pragma.ExecuteNonQuery(); }
    return Results.Ok(new { ok = true, cleared });
}).RequireAuthorization();

// Stok Sayım — fark kadar 'adjustment' hareketi
app.MapPost("/api/stock/count", (HttpContext c, StockCountDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var lines = (d.Lines ?? new()).Select(l => new DepoWise.Infrastructure.Materials.CountLine(l.MaterialId, l.CountedQuantity)).ToList();
    svc.Stock.Count(s, lines, string.IsNullOrWhiteSpace(d.Reason) ? "Sayım" : d.Reason!, Guid.NewGuid().ToString("N"), d.BranchId);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// Geliştirici Modu (app_settings.developer_mode) — kod 621875, admin
app.MapGet("/api/settings/developer", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(new { active = svc.Settings.Get(s.CompanyId, "developer_mode") == "1" }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/settings/developer", (HttpContext c, DeveloperDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!DepoWise.Application.Security.AccessControl.IsAdmin(s)) return Results.Json(new { error = "Yalnız admin." }, statusCode: 403);
    if (d.Active && d.Code != "621875") return Results.Json(new { error = "Geliştirici kodu hatalı." }, statusCode: 400);
    svc.Settings.Set(s.CompanyId, "developer_mode", d.Active ? "1" : "0", s.UserId);
    return Results.Ok(new { active = d.Active });
}).RequireAuthorization();

// ── Stok İşlemleri (Yeni Kayıt / Transfer / Depo Çıkışı + hareket iptali) — masaüstüyle birebir ──
app.MapGet("/api/stock/balance/{materialId}", (HttpContext c, string materialId) =>
    S(c) is null ? Results.Unauthorized() : Results.Ok(new { balance = svc.Stock.GetBalance(materialId) })).RequireAuthorization();

app.MapPost("/api/stock/receive", (HttpContext c, StockReceiveDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var code = d.Code?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Kod zorunlu.");
    if (string.IsNullOrWhiteSpace(d.Name)) throw new ArgumentException("Ad zorunlu.");
    if (d.Quantity < 0) throw new ArgumentException("Eklenecek stok negatif olamaz.");
    var found = svc.Materials.List(s, Page(), code).Items
        .FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
    var materialId = found?.Id ?? svc.Materials.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial(
        code, d.Name.Trim(), string.IsNullOrWhiteSpace(d.Type) ? null : d.Type,
        d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, 0m, d.UnitPrice, "TRY", Doc(d.Note)));
    if (d.Quantity > 0)
        svc.Stock.ReceiveIn(s,
            new[] { new DepoWise.Infrastructure.Materials.StockLine(materialId, d.Quantity, d.UnitPrice > 0 ? d.UnitPrice : null) },
            Guid.NewGuid().ToString("N"), d.BranchId, d.PersonnelId, d.VehicleId, Doc(d.Note),
            invoiceNo: Doc(d.InvoiceNo), orderSlipNo: Doc(d.OrderSlipNo), creditSlipNo: Doc(d.CreditSlipNo));
    return Results.Ok(new { id = materialId });
}).RequireAuthorization();

app.MapPost("/api/stock/issue", (HttpContext c, StockMoveDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.MaterialId)) throw new ArgumentException("Malzeme seçin.");
    if (d.Quantity <= 0) throw new ArgumentException("Miktar sıfırdan büyük olmalı.");
    svc.Stock.IssueOut(s,
        new[] { new DepoWise.Infrastructure.Materials.StockLine(d.MaterialId, d.Quantity) },
        Guid.NewGuid().ToString("N"), d.BranchId, d.PersonnelId, d.VehicleId, Doc(d.Note),
        invoiceNo: Doc(d.InvoiceNo), orderSlipNo: Doc(d.OrderSlipNo), creditSlipNo: Doc(d.CreditSlipNo));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapPost("/api/stock/transfer", (HttpContext c, StockTransferDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.MaterialId)) throw new ArgumentException("Malzeme seçin.");
    if (d.Quantity <= 0) throw new ArgumentException("Miktar sıfırdan büyük olmalı.");
    if (string.IsNullOrWhiteSpace(d.FromBranchId) || string.IsNullOrWhiteSpace(d.ToBranchId)) throw new ArgumentException("Kaynak ve hedef şube seçin.");
    svc.Stock.Transfer(s, d.MaterialId, d.Quantity, d.FromBranchId, d.ToBranchId, Guid.NewGuid().ToString("N"), Doc(d.Note),
        personnelId: d.PersonnelId, vehicleId: d.VehicleId,
        invoiceNo: Doc(d.InvoiceNo), orderSlipNo: Doc(d.OrderSlipNo), creditSlipNo: Doc(d.CreditSlipNo));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapPost("/api/stock/reverse", (HttpContext c, StockReverseDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.DocumentId)) throw new ArgumentException("Belge yok.");
    svc.Stock.ReverseDocument(s, d.DocumentId, string.IsNullOrWhiteSpace(d.Reason) ? "Kullanıcı iptali" : d.Reason);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── Modül kataloğu (yetki matrisi için) ──
app.MapGet("/api/modules", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // Süper-admin-özel modüller (firma vb.) yalnız süper admine gösterilir → yetki matrisinde başkası atayamaz.
    var mods = AppModules.All.Where(m => s.IsSuperAdmin || !AppModules.IsSuperAdminOnly(m.Key))
        .Select(m => new { key = m.Key, label = m.Label, adminOnly = AppModules.IsSuperAdminOnly(m.Key) });
    return Results.Ok(mods);
}).RequireAuthorization();

// Özel buton yetkileri kataloğu (yetki ağacı buton bölümü — web parity #15).
app.MapGet("/api/buttons", (HttpContext c) =>
    S(c) is null ? Results.Unauthorized()
        : Results.Ok(SpecialButtons.All.Select(b => new { key = b.Key, label = b.Label }))).RequireAuthorization();

// ── Raporlar (firma alanı yalnız süper admin; ResolveCompany fail-closed tenant izolasyonu) ──
app.MapGet("/api/reports/company-filter", (HttpContext c) => S(c) is { } s ? Results.Ok(new { showCompany = s.IsSuperAdmin }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/reports/scope", (HttpContext c, string? companyId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var cid = DepoWise.Application.Security.TenantAccessGuard.ResolveCompanyId(s, companyId); // süper admin başka firma seçebilir; diğerleri reddedilir
    var branches = new List<object>(); var vehicles = new List<object>();
    using var conn = svc.Factory.Create();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, name FROM branches WHERE company_id=$c AND is_deleted=0 ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) branches.Add(new { id = r.GetString(0), name = r.GetString(1) });
    }
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, internal_code, COALESCE(plate,'') FROM vehicles WHERE company_id=$c AND is_deleted=0 ORDER BY internal_code;";
        cmd.Parameters.AddWithValue("$c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) { var p = r.GetString(2); vehicles.Add(new { id = r.GetString(0), display = string.IsNullOrEmpty(p) ? r.GetString(1) : $"{r.GetString(1)} - {p}" }); }
    }
    return Results.Ok(new { branches, vehicles });
}).RequireAuthorization();
app.MapPost("/api/reports/{type}", (HttpContext c, string type, ReportReqDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var req = new DepoWise.Application.Reports.ReportRequest(true, d.FromDate, d.ToDate, d.BranchIds, d.VehicleIds, d.CompanyId);
    DepoWise.Application.Reports.TableModel tbl = type switch
    {
        "stock" => svc.Reports.StockStatus(s, req),
        "general" => svc.Reports.General(s, req),
        "maintenance" => svc.Reports.Maintenance(s, req),
        "fuel" => svc.Reports.FuelConsumption(s, req),
        "fuel-depot" => svc.Reports.FuelDepot(s, req),
        "stock-count" => svc.Reports.StockCount(s, req),
        "requests" => svc.Reports.Requests(s, req),
        _ => throw new ArgumentException("Bilinmeyen rapor tipi."),
    };
    return Results.Ok(new { title = tbl.Title, headers = tbl.Headers, rows = tbl.Rows });
}).RequireAuthorization();

// ── Bakım (Bakım Takibi) — masaüstüyle birebir ──
app.MapGet("/api/maintenance/definitions", (HttpContext c, string? parentDefId) =>
    S(c) is { } s ? Results.Ok(svc.MaintenanceDefinitions.List(s, parentDefId)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/maintenance/definitions", (HttpContext c, MaintDefDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.MaintenanceDefinitions.Create(s,
        new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition(d.Name, d.IntervalValue, string.IsNullOrWhiteSpace(d.IntervalUnit) ? "km" : d.IntervalUnit, d.ParentDefId, d.Description), d.VehicleIds) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/maintenance", (HttpContext c, MaintenanceDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var mats = d.Materials?.Select(m => new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(m.MaterialId, m.Quantity)).ToList();
    var id = svc.Maintenance.Save(s, new DepoWise.Infrastructure.Maintenance.NewMaintenance(
        d.VehicleId, d.DefinitionId, d.SubDefinitionId, d.TechnicianId, Doc(d.Description), Doc(d.SubDefinitionNote),
        d.PerformedKm, d.PerformedHour, d.PerformedDate, mats), Guid.NewGuid().ToString("N"));
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapPost("/api/maintenance/cancel", (HttpContext c, IdReasonDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Maintenance.Cancel(s, d.Id, string.IsNullOrWhiteSpace(d.Reason) ? "Kullanıcı iptali" : d.Reason)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/maintenance/definitions/{id}", (HttpContext c, string id, MaintDefDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.MaintenanceDefinitions.Update(s, id, new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition(
        d.Name, d.IntervalValue, string.IsNullOrWhiteSpace(d.IntervalUnit) ? "km" : d.IntervalUnit, d.ParentDefId, d.Description));
    if (d.VehicleIds is not null) svc.MaintenanceDefinitions.SetVehicles(s, id, d.VehicleIds);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapDelete("/api/maintenance/definitions/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.MaintenanceDefinitions.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/maintenance/definitions/{id}/vehicles", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.MaintenanceDefinitions.GetVehicleIds(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/maintenance/{id}/materials", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Maintenance.GetMaintenanceMaterials(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/maintenance/alerts", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var vmap = svc.Vehicles.List(s).ToDictionary(v => v.Id, v => v.Display);
    string LevelText(DepoWise.Application.Maintenance.AlertLevel l) => l switch
    {
        DepoWise.Application.Maintenance.AlertLevel.Approaching => "Bakım Yaklaşıyor",
        DepoWise.Application.Maintenance.AlertLevel.Critical => "Kritik",
        DepoWise.Application.Maintenance.AlertLevel.Overdue => "Bakım Gecikti",
        _ => "Normal",
    };
    var rows = svc.Maintenance.GetAlerts(s).Select(a => new
    {
        vehicleCode = vmap.TryGetValue(a.VehicleId, out var code) ? code : a.VehicleId,
        definition = a.DefinitionName,
        progressText = $"%{(int)(a.Progress * 100)}",
        consumedText = $"{a.Consumed:0.##} / {a.Interval:0.##}",
        levelText = LevelText(a.Level),
    });
    return Results.Ok(rows);
}).RequireAuthorization();

// ── Muayene / Sigorta ──
app.MapPost("/api/inspection", (HttpContext c, InspectionDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.Inspection.Save(s, new DepoWise.Infrastructure.Maintenance.NewInspection(
        d.VehicleId, d.DocType, d.LastDate, d.NextDate, Doc(d.Result), Doc(d.Place), Doc(d.Note))) }) : Results.Unauthorized()).RequireAuthorization();

// ── Yakıt ──
app.MapGet("/api/fuel/depot", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Fuel.ListDepotEntries(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/fuel/summary", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var received = svc.Fuel.ListDepotEntries(s).Sum(e => e.Liters);
    var distributed = svc.Fuel.ListDistributions(s).Sum(e => e.Liters);
    return Results.Ok(new
    {
        depotBalance = svc.Fuel.GetDepotBalance(s),
        currentPrice = svc.Fuel.GetCurrentFuelPrice(s),
        totalReceived = received,
        totalDistributed = distributed,
    });
}).RequireAuthorization();
app.MapPost("/api/fuel/distribute", (HttpContext c, DistributionDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.Fuel.Distribute(s, new DepoWise.Infrastructure.Operations.NewDistribution(
        d.VehicleId, d.Liters, d.CurrentMeter, d.UnitPrice, "TRY", d.PersonnelId, d.DistributionDate, Doc(d.Note)), Guid.NewGuid().ToString("N")) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/fuel/depot", (HttpContext c, DepotEntryDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.Fuel.AddDepotEntry(s, new DepoWise.Infrastructure.Operations.NewDepotEntry(
        d.Liters, d.UnitPrice, "TRY", d.SupplierId, Doc(d.InvoiceNo), Doc(d.Note), d.EntryDate), Guid.NewGuid().ToString("N")) }) : Results.Unauthorized()).RequireAuthorization();

// ── Günlük Faaliyet (Hareket + Bakım) ──
app.MapPost("/api/daily/movement", (HttpContext c, MovementDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.DailyActivity.SaveMovement(s, new DepoWise.Infrastructure.Operations.NewMovementActivity(
        string.IsNullOrWhiteSpace(d.MovementKind) ? "movement" : d.MovementKind, d.VehicleId, d.FromLocationId, d.ToLocationId,
        d.OperatorId, d.DurationDays, Doc(d.Description), d.ActivityDate), Guid.NewGuid().ToString("N")) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/daily/maintenance", (HttpContext c, MaintenanceDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var mats = d.Materials?.Select(m => new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(m.MaterialId, m.Quantity)).ToList();
    var id = svc.DailyActivity.SaveMaintenanceActivity(s, new DepoWise.Infrastructure.Maintenance.NewMaintenance(
        d.VehicleId, d.DefinitionId, d.SubDefinitionId, d.TechnicianId, Doc(d.Description), Doc(d.SubDefinitionNote),
        d.PerformedKm, d.PerformedHour, d.PerformedDate, mats), Guid.NewGuid().ToString("N"));
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapDelete("/api/daily/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.DailyActivity.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Araçlar (ekle/sil) ──
app.MapPost("/api/vehicles", (HttpContext c, NewVehicleDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.Vehicles.Create(s, new DepoWise.Infrastructure.Vehicles.NewVehicle(
        d.InternalCode, Doc(d.Plate), d.ProductionYear, d.CurrentMeter, string.IsNullOrWhiteSpace(d.MeterUnit) ? "km" : d.MeterUnit,
        d.BranchId, d.DriverPersonnelId, Doc(d.ChassisNo), Doc(d.EngineNo), string.IsNullOrWhiteSpace(d.Status) ? "active" : d.Status, Doc(d.StatusNote),
        d.VehicleTypeId, d.CategoryId, d.BrandId, d.VehicleModelId, d.TemplateId)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/vehicles/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Vehicles.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Vehicles.Get(s, id)) : Results.Unauthorized()).RequireAuthorization();
// Araç detay sekmeleri: uyumlu malzemeler + araç bakımları + hareketler
app.MapGet("/api/vehicles/{id}/materials", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Materials.MaterialsForVehicle(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles/{id}/maintenance", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Maintenance.ListMaintenances(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles/{id}/movements", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.DailyActivity.GetForVehicle(s, id, "movement")) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/vehicles/{id}", (HttpContext c, string id, NewVehicleDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.Vehicles.Update(s, id, new DepoWise.Infrastructure.Vehicles.UpdateVehicle(
        Doc(d.Plate), d.ProductionYear, string.IsNullOrWhiteSpace(d.Status) ? "active" : d.Status, Doc(d.StatusNote),
        Doc(d.ChassisNo), Doc(d.EngineNo), d.VehicleTypeId, d.CategoryId, d.BrandId, d.VehicleModelId, d.BranchId, d.DriverPersonnelId));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapGet("/api/vehicles/models/{brandId}", (HttpContext c, string brandId) =>
    S(c) is { } s ? Results.Ok(svc.Lookups.ListVehicleModels(s, brandId)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/vehicles/models", (HttpContext c, VehicleModelDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.Lookups.AddVehicleModel(s, d.BrandId, d.Name) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles/next-code", (HttpContext c, string baseCode) =>
    S(c) is { } s ? Results.Ok(new { code = svc.VehicleTemplates.GenerateNextInternalCode(s, baseCode) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicle-templates", (HttpContext c, string? search) =>
    S(c) is { } s ? Results.Ok(svc.VehicleTemplates.List(s, search)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/vehicle-templates", (HttpContext c, VehicleTemplateDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.VehicleTemplates.Create(s, new DepoWise.Infrastructure.Vehicles.NewVehicleTemplate(
        d.Name, Doc(d.InternalCode), d.VehicleTypeId, d.CategoryId, d.BrandId, d.VehicleModelId, d.ProductionYear), d.MaterialIds) }) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/vehicle-templates/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.VehicleTemplates.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicle-templates/{id}/materials", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.VehicleTemplates.GetMaterialRows(s, id)) : Results.Unauthorized()).RequireAuthorization();
// Araç uyarı özeti (satır BAKIM/MUAYENE kolonu): vehicleId -> metin
app.MapGet("/api/vehicles/alerts", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var map = new Dictionary<string, HashSet<string>>();
    void Add(string vid, string label) { if (!map.TryGetValue(vid, out var set)) { set = new(); map[vid] = set; } set.Add(label); }
    try { foreach (var a in svc.Maintenance.GetAlerts(s)) Add(a.VehicleId, "Bakım"); } catch { }
    try { foreach (var a in svc.Inspection.GetAlerts(s)) Add(a.VehicleId, "Muayene"); } catch { }
    return Results.Ok(map.Select(kv => new { vehicleId = kv.Key, text = string.Join(" / ", kv.Value) }));
}).RequireAuthorization();
// Araç fotoğrafları
app.MapGet("/api/vehicles/{id}/photos", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Files.GetPhotos(s, "vehicle", id).Select(p => new { id = p.Id, url = $"/api/vehicles/{id}/photos/{p.Id}" })) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles/{id}/photos/{fileId}", (HttpContext c, string id, string fileId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var p = svc.Files.GetPhotos(s, "vehicle", id).FirstOrDefault(x => x.Id == fileId);
    return p is null ? Results.NotFound() : Results.File(svc.Storage.Read(p.StorageKey), p.Mime ?? "image/jpeg");
}).RequireAuthorization();
app.MapPost("/api/vehicles/{id}/photos", async (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var form = await ctx.Request.ReadFormAsync(); int n = 0;
    foreach (var file in form.Files) { using var ms = new MemoryStream(); await file.OpenReadStream().CopyToAsync(ms, ctx.RequestAborted); svc.Files.SavePhoto(s, "vehicle", id, file.FileName, file.ContentType, ms.ToArray()); n++; }
    return Results.Ok(new { added = n });
}).RequireAuthorization();
app.MapDelete("/api/vehicles/{id}/photos/{fileId}", (HttpContext c, string id, string fileId) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Files.DeletePhoto(s, fileId)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Malzeme Talep (kalemli + onay akışı) ──
app.MapGet("/api/requests/{id}/items", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Requests.GetItems(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/requests", (HttpContext c, RequestDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var items = (d.Items ?? new()).Select(i => new DepoWise.Infrastructure.Requests.RequestItemInput(i.MaterialId, i.Quantity, i.VehicleId, Doc(i.Note))).ToList();
    var h = svc.Requests.Create(s, new DepoWise.Infrastructure.Requests.NewRequest(items, d.BranchId, d.RequesterId, d.WarehouseId, d.ApproverId, Doc(d.Description), d.RequestDate, d.SubmitImmediately));
    return Results.Ok(new { id = h.Id, docNo = h.DocNo });
}).RequireAuthorization();
app.MapGet("/api/requests/{id}/edit", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Requests.GetForEdit(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/requests/{id}", (HttpContext c, string id, RequestDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var items = (d.Items ?? new()).Select(i => new DepoWise.Infrastructure.Requests.RequestItemInput(i.MaterialId, i.Quantity, i.VehicleId, Doc(i.Note))).ToList();
    svc.Requests.Update(s, id, new DepoWise.Infrastructure.Requests.NewRequest(items, d.BranchId, d.RequesterId, d.WarehouseId, d.ApproverId, Doc(d.Description), d.RequestDate, d.SubmitImmediately));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapPost("/api/requests/{id}/approve", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Approve(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/requests/{id}/reject", (HttpContext c, string id, IdReasonDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Reject(s, id, string.IsNullOrWhiteSpace(d?.Reason) ? "Reddedildi" : d!.Reason!)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/requests/{id}/cancel", (HttpContext c, string id, IdReasonDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Cancel(s, id, d?.Reason)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/requests/{id}/history", (HttpContext c, string id) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    string Lbl(DepoWise.Application.Requests.RequestStatus st) => st switch
    {
        DepoWise.Application.Requests.RequestStatus.Draft => "Taslak",
        DepoWise.Application.Requests.RequestStatus.Pending => "Beklemede",
        DepoWise.Application.Requests.RequestStatus.Approved => "Onaylı",
        DepoWise.Application.Requests.RequestStatus.Rejected => "Reddedildi",
        _ => "İptal",
    };
    var rows = svc.Requests.GetHistory(id).Select(h =>
        $"{(h.From is null ? "—" : Lbl(h.From.Value))} → {Lbl(h.To)}" + (string.IsNullOrWhiteSpace(h.Reason) ? "" : $" ({h.Reason})"));
    return Results.Ok(rows);
}).RequireAuthorization();
app.MapGet("/api/requests/{id}/pdf", (HttpContext c, string id, bool? economic) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var d = svc.Requests.GetPdfData(s, id);
    string Lbl(DepoWise.Application.Requests.RequestStatus st) => st switch
    {
        DepoWise.Application.Requests.RequestStatus.Draft => "Taslak",
        DepoWise.Application.Requests.RequestStatus.Pending => "Beklemede",
        DepoWise.Application.Requests.RequestStatus.Approved => "Onaylı",
        DepoWise.Application.Requests.RequestStatus.Rejected => "Reddedildi",
        _ => "İptal",
    };
    var companyName = svc.Companies.List(s).FirstOrDefault(x => x.Id == s.CompanyId)?.Name ?? s.CompanyId;
    var model = new DepoWise.Application.Requests.RequestPdfModel(
        companyName, d.DocNo, DateTimeOffset.FromUnixTimeMilliseconds(d.RequestDate).LocalDateTime.ToString("dd.MM.yyyy"),
        Lbl(d.Status), d.BranchName, d.RequesterName, d.WarehouseName, d.ApproverName, d.Description,
        d.Items.Select(i => new DepoWise.Application.Requests.RequestPdfItem(i.Code, i.Name, i.Unit, i.Quantity, i.VehicleCode, i.VehicleChassis)).ToList());
    var bytes = svc.RequestPdf.Generate(model, economic == true);
    return Results.File(bytes, "application/pdf", $"{d.DocNo}{(economic == true ? "-ekonomik" : "")}.pdf");
}).RequireAuthorization();

// ── Personel (sil) + Şube/Şantiye (güncelle/sil) ──
app.MapDelete("/api/personnel/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Lookups.Delete(s, "personnel", id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/branches/{id}", (HttpContext c, string id, BranchDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Branches.Update(s, id, new DepoWise.Infrastructure.Organization.NewBranch(d.Name, string.IsNullOrWhiteSpace(d.Kind) ? "branch" : d.Kind!, d.ParentId, Doc(d.Code), Doc(d.Password)))) }) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/branches/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Branches.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Kullanıcılar (rol / aktif / şifre / sil) ──
app.MapGet("/api/users/{id}/roles", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Users.GetRoleKeys(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users/{id}/roles", (HttpContext c, string id, RolesDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.SetRoles(s, id, d.Roles ?? new())) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users/{id}/active", (HttpContext c, string id, ActiveDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.SetActive(s, id, d.Active)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users/{id}/password", (HttpContext c, string id, PasswordDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.ChangePassword(s, id, d.Password)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/users/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.DeleteUser(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users/{id}/branch", (HttpContext c, string id, IdDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Branches.AssignUser(s, id, string.IsNullOrWhiteSpace(d.Id) ? null : d.Id)) }) : Results.Unauthorized()).RequireAuthorization();
// "Tüm Şubeler" yetkisi — YALNIZ süper admin belirler.
app.MapPost("/api/users/{id}/all-branches", (HttpContext c, string id, ActiveDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.SetViewAllBranches(s, id, d.Active)) }) : Results.Unauthorized()).RequireAuthorization();
// Kota izleme (kullanıcı + admin kullanımı).
app.MapGet("/api/quota-monitor", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(svc.Users.GetQuotaMonitor(s)) : Results.Unauthorized()).RequireAuthorization();

// ── Yetkiler (kullanıcı bazlı modül matrisi) ──
app.MapGet("/api/permissions/{userId}", (HttpContext c, string userId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var data = svc.Permissions.GetForUser(s, userId);
    return Results.Ok(new { modules = data.Modules, buttons = data.Buttons });
}).RequireAuthorization();
app.MapPost("/api/permissions/{userId}", (HttpContext c, string userId, PermSaveDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var mods = (d.Modules ?? new()).Select(m => new ModulePermission(m.ModuleKey, m.CanView, m.CanCreate, m.CanEdit, m.CanDelete));
    svc.Permissions.SaveForUser(s, userId, mods, d.Buttons ?? new());
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── Yetki Şablonları ──
app.MapGet("/api/permission-templates", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.PermissionTemplates.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/permission-templates/{id}", (HttpContext c, string id) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var d = svc.PermissionTemplates.GetData(s, id);
    return Results.Ok(new { modules = d.Modules, buttons = d.Buttons, roleKey = d.RoleKey });
}).RequireAuthorization();
app.MapPost("/api/permission-templates", (HttpContext c, TemplateDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var mods = (d.Modules ?? new()).Select(m => new ModulePermission(m.ModuleKey, m.CanView, m.CanCreate, m.CanEdit, m.CanDelete));
    var id = svc.PermissionTemplates.Create(s, d.Name, d.RoleKey, mods, d.Buttons ?? new());
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapDelete("/api/permission-templates/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.PermissionTemplates.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Sistem Logu (audit) ──
app.MapGet("/api/audit", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.AuditLog.List(s)) : Results.Unauthorized()).RequireAuthorization();

// ── Sunucu veritabanı yedeği (Yedek Yönetimi'nin web karşılığı) ──
app.MapGet("/api/backup/list", (HttpContext c) =>
    S(c) is null ? Results.Unauthorized() : Results.Ok(svc.DbBackup.ListBackups().Select(b => new
    {
        fileName = Path.GetFileName(b.Path), sizeBytes = b.SizeBytes, createdAt = b.CreatedAt,
        dateText = DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm"),
        sizeText = $"{b.SizeBytes / 1024.0 / 1024.0:0.##} MB",
    }))).RequireAuthorization();
app.MapPost("/api/backup/create", (HttpContext c) =>
{
    var s = S(c); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    var path = svc.DbBackup.Backup();
    return Results.Ok(new { fileName = Path.GetFileName(path) });
}).RequireAuthorization();
app.MapGet("/api/backup/download/{name}", (HttpContext c, string name) =>
{
    var s = S(c); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    var safe = Path.GetFileName(name); // path traversal koruması
    var full = Path.Combine(svc.DbBackup.GetBackupFolder(), safe);
    return File.Exists(full) ? Results.File(full, "application/octet-stream", safe) : Results.NotFound();
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
record LoginDto(string? CompanyId, string Username, string Password, string? BranchId = null, string? BranchPassword = null);
record EnrollDto(string CompanyId, string Key, string DeviceName);
record PushDto(List<PushOp> Ops);
record PushOp(string OperationId, string EntityType, string EntityId, string PayloadJson, long? BaseVersion);
record NewCompanyDto(string Name, string? TaxNo, string? TaxOffice, string? Address, string? Phone, string? Email, string? AuthorizedPerson, int MaxUsers = 0);
record NameDto(string Name);
record PersonnelDto(string FullName, string? Title, string? Phone, string? BranchId, bool IsActive = true);
record NewUserDto(string Username, string Password, string? FullName, List<string>? RoleKeys, string? CompanyId, string? BranchId, bool CanViewAllBranches = false);
record MachineRegisterDto(string? CompanyId, string? MachineName, string? BranchId = null);
record QuotaDto(int Quota);
record VerifyBranchDto(string? CompanyId, string BranchId, string? BranchPassword);
record ConflictSeenDto(string? BranchId);
record UserThemeDto(string? Mode, string? Color, string? Style);
record NewMaterialDto(string Code, string Name, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId, decimal MinStock, decimal UnitPrice, string? Description, decimal OpeningStock, List<string>? VehicleIds, List<string>? EquivalentIds);
record IdListDto(List<string>? Ids);
record IdDto(string Id);
record AlertReadDto(string? Key, string? Signature);
record VehicleModelDto(string BrandId, string Name);
record ReportReqDto(long? FromDate, long? ToDate, List<string>? BranchIds, List<string>? VehicleIds, string? CompanyId);
record BranchDto(string Name, string? Kind, string? ParentId, string? Code = null, string? Password = null);
record CountLineDto(string MaterialId, decimal CountedQuantity);
record StockCountDto(string? Reason, string? BranchId, List<CountLineDto>? Lines);
record DeveloperDto(string? Code, bool Active);
record VehicleTemplateDto(string Name, string? InternalCode, string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId, int? ProductionYear, List<string>? MaterialIds);
record StockReceiveDto(string Code, string Name, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId,
    decimal Quantity, decimal UnitPrice, string? BranchId, string? PersonnelId, string? VehicleId, string? Note, string? InvoiceNo, string? OrderSlipNo, string? CreditSlipNo);
record StockMoveDto(string MaterialId, decimal Quantity, string? BranchId, string? PersonnelId, string? VehicleId, string? Note, string? InvoiceNo, string? OrderSlipNo, string? CreditSlipNo);
record StockTransferDto(string MaterialId, decimal Quantity, string? FromBranchId, string? ToBranchId, string? PersonnelId, string? VehicleId, string? Note, string? InvoiceNo, string? OrderSlipNo, string? CreditSlipNo);
record StockReverseDto(string DocumentId, string? Reason);
record IdReasonDto(string Id, string? Reason);
record MaintLineDto(string MaterialId, decimal Quantity);
record MaintenanceDto(string VehicleId, string DefinitionId, string? SubDefinitionId, string? TechnicianId, string? Description, string? SubDefinitionNote,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate, List<MaintLineDto>? Materials);
record MaintDefDto(string Name, decimal IntervalValue, string IntervalUnit, string? ParentDefId, string? Description, List<string>? VehicleIds);
record InspectionDto(string VehicleId, string DocType, long? LastDate, long? NextDate, string? Result, string? Place, string? Note);
record DepotEntryDto(decimal Liters, decimal UnitPrice, string? SupplierId, string? InvoiceNo, string? Note, long? EntryDate);
record DistributionDto(string VehicleId, decimal Liters, decimal CurrentMeter, decimal? UnitPrice, string? PersonnelId, long? DistributionDate, string? Note);
record MovementDto(string MovementKind, string? VehicleId, string? FromLocationId, string? ToLocationId, string? OperatorId, int? DurationDays, string? Description, long? ActivityDate);
record NewVehicleDto(string InternalCode, string? Plate, int? ProductionYear, decimal CurrentMeter, string? MeterUnit, string? BranchId, string? DriverPersonnelId,
    string? ChassisNo, string? EngineNo, string? Status, string? StatusNote, string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId, string? TemplateId);
record RequestItemDto(string MaterialId, decimal Quantity, string? VehicleId, string? Note);
record RequestDto(List<RequestItemDto>? Items, string? BranchId, string? RequesterId, string? WarehouseId, string? ApproverId, string? Description, long? RequestDate, bool SubmitImmediately);
record RolesDto(List<string>? Roles);
record ActiveDto(bool Active);
record PasswordDto(string Password);
record ModulePermDto(string ModuleKey, bool CanView, bool CanCreate, bool CanEdit, bool CanDelete);
record PermSaveDto(List<ModulePermDto>? Modules, List<string>? Buttons);
record TemplateDto(string Name, string? RoleKey, List<ModulePermDto>? Modules, List<string>? Buttons);

/// <summary>#19 — Canlı sunucu durumu sayaçları (süreç boyunca).</summary>
static class ServerMetrics
{
    public static long Requests;
    public static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;
}
