using DepoWise.Infrastructure.Database;
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

// JWT imza anahtarı: config > env. Üretimde ZORUNLU — yoksa uygulama açılmaz (bilinen dev anahtarıyla
// token üretilip tüm firmalara girilebileceği için fallback yalnız Development'ta çalışır).
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? Environment.GetEnvironmentVariable("DEPOWISE_JWT_KEY");
if (string.IsNullOrWhiteSpace(jwtKey))
{
    if (builder.Environment.IsDevelopment())
        jwtKey = "dev-only-change-me-please-32chars-minimum-secret-key";
    else
        throw new InvalidOperationException(
            "DEPOWISE_JWT_KEY tanımlı değil. Üretimde JWT imza anahtarı zorunludur. " +
            "Örnek: fly secrets set DEPOWISE_JWT_KEY=<rastgele-64-karakter>");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = JwtTokens.ValidationParameters(jwtKey));
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
var svc = app.Services.GetRequiredService<ServerServices>();

// Gözlemlenebilirlik: açılış özeti (canlıda "sunucu nasıl başladı" tek bakışta). Sır DEĞİL — yalnız durum.
Console.WriteLine($"[START] {DateTimeOffset.UtcNow:O} DepoWise.Api env={app.Environment.EnvironmentName} " +
                  $"dataDir={dataDir} jwtKey={(string.IsNullOrEmpty(jwtKey) ? "YOK" : "var")}");

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// #19 — canlı sunucu durumu için hafif istek sayacı.
_ = ServerMetrics.Start; // başlangıç anını sabitle
app.Use(async (ctx, next) =>
{
    System.Threading.Interlocked.Increment(ref ServerMetrics.Requests);
    // #4 — Online kullanıcı izleme: kimliği doğrulanmış her istekte kullanıcıyı "görüldü" işaretle (bellek-içi, ücretsiz).
    var uid = ctx.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!string.IsNullOrEmpty(uid))
        ServerPresence.Touch(uid, ctx.User!.FindFirstValue(JwtTokens.CompanyClaim) ?? "");
    await next();
});

// Gözlemlenebilirlik: her istek için tek satır erişim logu (metot/yol/durum/süre). Fly.io bunu toplar → canlıda
// ne olup bittiği + hangi istek yavaş/hatalı görünür. Yüksek-frekanslı yoklamalar (health/status) loglanmaz (gürültü).
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    bool noisy = path == "/health" || path == "/" || path.StartsWith("/api/server/status");
    var sw = noisy ? null : System.Diagnostics.Stopwatch.StartNew();
    await next();
    if (sw is null) return;
    sw.Stop();
    var code = ctx.Response.StatusCode;
    var tag = code >= 500 ? "ERR" : code >= 400 ? "WRN" : (sw.ElapsedMilliseconds > 1500 ? "SLW" : "REQ");
    var line = $"[{tag}] {DateTimeOffset.UtcNow:O} {ctx.Request.Method} {path} {code} {sw.ElapsedMilliseconds}ms";
    if (code >= 500) Console.Error.WriteLine(line); else Console.WriteLine(line);
});

// Hata → doğru HTTP kodu (ForbiddenException 403, geçersiz istek 400, diğer 500)
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ForbiddenException ex) { await Write(ctx, 403, ex.Message); }
    // DÜZENLEME KİLİDİ: kayıt, kullanıcı formu açtıktan sonra değişti → 409 (üzerine yazılmadı).
    catch (ConcurrencyException ex) { await Write(ctx, 409, ex.Message); }
    // İŞ KURALI istisnaları (QA bulgusu 2026-07-22, çok-makineli simülasyon): bunlar tanınmadığı için
    // 500 "Sunucuda beklenmeyen bir hata oluştu" dönüyordu. Kural DOĞRU çalışıyordu (negatif stok/sayaç
    // geri alma engelleniyordu) ama kullanıcı sebebi göremiyordu. Mesajları iş metnidir, güvenle gösterilir.
    catch (DepoWise.Infrastructure.Materials.NegativeStockException ex) { await Write(ctx, 400, ex.Message); }
    // Faz 3-Ön: aynı malzemede eşzamanlı işlem — 3 tekrardan sonra vazgeçildi. Mesaj teknik değildir;
    // gerçek teknik sebep [stock-cas] etiketiyle sunucu logundadır (kullanıcı kararı K-5).
    catch (DepoWise.Infrastructure.Materials.StockBusyException ex) { await Write(ctx, 409, ex.Message); }
    catch (DepoWise.Application.Common.MeterBackwardException ex) { await Write(ctx, 400, ex.Message); }
    catch (ArgumentException ex) { await Write(ctx, 400, ex.Message); }
    catch (InvalidOperationException ex) { await Write(ctx, 400, ex.Message); }
    catch (Exception ex)
    {
        // Ham exception mesajı client'a SIZDIRILMAZ (dosya yolu/SQL detayı içerebilir) — sunucu loguna yazılır.
        Console.Error.WriteLine($"[500] {DateTimeOffset.UtcNow:O} {ctx.Request.Method} {ctx.Request.Path} → {ex}");
        await Write(ctx, 500, "Sunucuda beklenmeyen bir hata oluştu. Sorun devam ederse yöneticinize bildirin.");
    }
});
static Task Write(HttpContext ctx, int code, string msg)
{
    ctx.Response.StatusCode = code;
    ctx.Response.ContentType = "application/json";
    return ctx.Response.WriteAsJsonAsync(new { error = msg });
}

// IP bazlı giriş sınırı (brute-force / PBKDF2 DoS koruması). Kullanıcı-adı bazlı kilit AuthService'te zaten
// var; bu sayaç farklı kullanıcı adlarıyla taramayı keser. NAT arkasındaki ofisler (aynı IP'den çok kullanıcı)
// kilitlenmesin diye pencere gevşek tutuldu: 30 istek / 5 dk / IP.
var loginLimiter = new RateLimiter(30, TimeSpan.FromMinutes(5));
// Anonim liste uçları (firma/şube listesi) için gevşek sınır — normal girişi etkilemez, bot taramasını (scraping) keser.
var publicLimiter = new RateLimiter(120, TimeSpan.FromMinutes(1));

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
        using (var c = conn.CreateCommand())
        {
            if (conn is Npgsql.NpgsqlConnection)   // PG: PRAGMA yok → doğrudan veritabanı boyutu
            {
                c.CommandText = "SELECT pg_database_size(current_database());";
                dbBytes = Convert.ToInt64(c.ExecuteScalar());
            }
            else                                    // SQLite: sayfa sayısı × sayfa boyutu
            {
                c.CommandText = "PRAGMA page_count;"; var pc = Convert.ToInt64(c.ExecuteScalar());
                c.CommandText = "PRAGMA page_size;"; var ps = Convert.ToInt64(c.ExecuteScalar());
                dbBytes = pc * ps;
            }
        }
        using (var c = conn.CreateCommand()) { c.CommandText = "SELECT COUNT(*) FROM companies WHERE is_deleted=0;"; companies = Convert.ToInt64(c.ExecuteScalar()); }
        using (var c = conn.CreateCommand()) { c.CommandText = "SELECT COUNT(*) FROM users WHERE is_deleted=0;"; users = Convert.ToInt64(c.ExecuteScalar()); }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM sync_devices WHERE last_seen_at IS NOT NULL AND last_seen_at > @t;";
            c.AddWithValue("@t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 5 * 60 * 1000);
            machinesOnline = Convert.ToInt64(c.ExecuteScalar());
        }
    }
    catch { }
    string? latest = null; try { latest = svc.Releases.Latest()?.Version; } catch { }

    // Bellek limiti (container/cgroup) — .NET GC bunu görür; yoksa 256 MB (Fly.io makine boyutu) varsay.
    var gcInfo = GC.GetGCMemoryInfo();
    double memLimitMb = gcInfo.TotalAvailableMemoryBytes > 0 ? gcInfo.TotalAvailableMemoryBytes / MB : 256d;
    double wsMb = proc.WorkingSet64 / MB;
    double memPercent = memLimitMb > 0 ? Math.Round(Math.Clamp(wsMb / memLimitMb * 100.0, 0, 100), 1) : 0;

    // DİSK (Fly.io kalıcı disk /data) — canlı doluluk. Disk dolunca SQLite yazamaz → tam kesinti (ADR-070).
    var disk = svc.ReleasePackages.GetDiskInfo();
    double diskPercent = disk.TotalBytes > 0 ? Math.Round((double)disk.UsedBytes / disk.TotalBytes * 100.0, 1) : 0;

    return Results.Ok(new
    {
        diskTotalMb = Math.Round(disk.TotalBytes / MB, 1),
        diskFreeMb = Math.Round(disk.FreeBytes / MB, 1),
        diskUsedMb = Math.Round(disk.UsedBytes / MB, 1),
        diskPercent,
        packagesMb = Math.Round(disk.PackagesBytes / MB, 1),
        packageCount = disk.PackageCount,
        uptimeSeconds = (long)(DateTimeOffset.UtcNow - ServerMetrics.Start).TotalSeconds,
        workingSetMb = Math.Round(wsMb, 1),
        gcMemoryMb = Math.Round(GC.GetTotalMemory(false) / MB, 1),
        cpuPercent = ServerMetrics.SampleCpuPercent(),
        memPercent,
        memLimitMb = Math.Round(memLimitMb, 0),
        threadCount = proc.Threads.Count,
        dotnet = Environment.Version.ToString(),
        dbSizeMb = Math.Round(dbBytes / MB, 2),
        companies,
        users,
        machinesOnline,
        usersOnline = ServerPresence.TotalOnline(),
        latestVersion = latest ?? "—",
        requestCount = System.Threading.Interlocked.Read(ref ServerMetrics.Requests),
        serverTimeUtc = DateTimeOffset.UtcNow,
    });
}).RequireAuthorization();

// ── Kimlik doğrulama → JWT ──
app.MapPost("/api/auth/login", (HttpContext http, LoginDto dto) =>
{
    var rl = loginLimiter.Check("login:" + (ClientIp(http) ?? "unknown"));
    if (!rl.Allowed)
        return Results.Json(new { error = $"Çok fazla giriş denemesi. {rl.RetrySeconds} sn sonra tekrar deneyin." }, statusCode: 429);
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
    // "Tüm Şubeler" artık admin + süper admin'de DAİMA açık (rapor için); ayrıca özel yetki (flag) verilmiş kullanıcıda.
    bool effAllBranches = res.Session.CanViewAllBranches || res.Session.IsSuperAdmin || res.Session.IsCompanyAdmin;
    if (allBranches && !effAllBranches)
        return Results.Json(new { error = "Bu kullanıcının Tüm Şubeler yetkisi yok." }, statusCode: 403);
    var token = JwtTokens.Issue(jwtKey, res.Session.UserId, res.Session.CompanyId);
    // 2 aşamalı login: kullanıcının KENDİ firmasının adı + şubeleri döner (kullanıcı firma listesini görmez).
    // Süper admin: firma seçebilsin diye tüm firmalar da döner (Adım 1b: /api/auth/select-company ile firma seçilir).
    var companyName = svc.Companies.GetName(res.Session.CompanyId);
    var branches = svc.Branches.ListForLogin(res.Session.CompanyId)
        .Select(b => new { id = b.Id, name = b.Name, code = b.Code, hasPassword = b.HasPassword });
    object? companies = null;
    if (res.Session.IsSuperAdmin)
    {
        using var cc = svc.Factory.Create();
        using var cmd = cc.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM companies WHERE is_deleted=0 ORDER BY name;";
        var cl = new List<object>();
        using var rr = cmd.ExecuteReader();
        while (rr.Read()) cl.Add(new { id = rr.GetString(0), name = rr.GetString(1) });
        companies = cl;
    }
    return Results.Ok(new { token, userId = res.Session.UserId, companyId = res.Session.CompanyId,
        companyName, branches, isSuperAdmin = res.Session.IsSuperAdmin, branchId = dto.BranchId,
        canViewAllBranches = effAllBranches, companies,
        mustChangePassword = res.MustChangePassword });   // true → istemci ilk giriş şifre ekranını gösterir
});

// ── İLK GİRİŞ şifre belirleme: parolası doğru ama şifre değiştirmesi zorunlu kullanıcı, AYNI login ekranından
// yeni şifresini belirler. Bearer token (login'den) ile kimlik doğrulanır; kendi şifresini değiştirir. ──
app.MapPost("/api/auth/change-initial-password", (HttpContext c, ChangeInitialPwDto d) =>
{
    var s = Session(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.NewPassword) || d.NewPassword!.Length < 4)
        return Results.Json(new { error = "Yeni şifre en az 4 karakter olmalı." }, statusCode: 400);
    try { svc.Users.ChangeOwnPassword(s, d.NewPassword!); }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
    // Şifre belirlendi → normal login akışına devam (firma/şube seçimi). Taze token + firma bağlamı döner.
    var token = JwtTokens.Issue(jwtKey, s.UserId, s.CompanyId);
    var companyName = svc.Companies.GetName(s.CompanyId);
    var branches = svc.Branches.ListForLogin(s.CompanyId)
        .Select(b => new { id = b.Id, name = b.Name, code = b.Code, hasPassword = b.HasPassword });
    object? companies = null;
    if (s.IsSuperAdmin)
    {
        using var cc = svc.Factory.Create();
        using var cmd = cc.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM companies WHERE is_deleted=0 ORDER BY name;";
        var cl = new List<object>();
        using var rr = cmd.ExecuteReader();
        while (rr.Read()) cl.Add(new { id = rr.GetString(0), name = rr.GetString(1) });
        companies = cl;
    }
    bool effAll = s.IsSuperAdmin || s.IsCompanyAdmin || s.CanViewAllBranches;
    return Results.Ok(new { token, userId = s.UserId, companyId = s.CompanyId, companyName, branches,
        isSuperAdmin = s.IsSuperAdmin, canViewAllBranches = effAll, companies, mustChangePassword = false });
}).RequireAuthorization();

// ── Adım 1b (YALNIZ süper admin): firma seç → o firma bağlamında YENİ token + o firmanın şubeleri ──
// Süper admin seçtiği firmayı o firmanın admini gibi yönetir (tüm ekranlar/kayıtlar). Token firmayı taşır;
// sonraki isteklerde SessionFor süper admin için çapraz-firma oturumu kurar (AuthService.CreateSessionForUser).
app.MapPost("/api/auth/select-company", (HttpContext c, SelectCompanyDto d) =>
{
    var s = Session(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin firma seçebilir." }, statusCode: 403);
    if (string.IsNullOrWhiteSpace(d.CompanyId)) return Results.Json(new { error = "Firma seçilmedi." }, statusCode: 400);
    var name = svc.Companies.GetName(d.CompanyId!);
    if (string.IsNullOrEmpty(name)) return Results.Json(new { error = "Firma bulunamadı." }, statusCode: 404);
    var token = JwtTokens.Issue(jwtKey, s.UserId, d.CompanyId!);
    var branches = svc.Branches.ListForLogin(d.CompanyId!)
        .Select(b => new { id = b.Id, name = b.Name, code = b.Code, hasPassword = b.HasPassword });
    return Results.Ok(new { token, userId = s.UserId, companyId = d.CompanyId, companyName = name,
        branches, isSuperAdmin = true, canViewAllBranches = true });
}).RequireAuthorization();

// ── Token yenileme: geçerli JWT ile taze JWT al (kayan oturum) ──
// Masaüstü, token süresi dolmadan bunu çağırır → 12 saatten uzun oturumda sync sessizce durmaz.
// Yetkiler token'dan değil DB'den; kullanıcı hâlâ geçerli/aktif mi diye oturum yeniden kurulur.
app.MapPost("/api/auth/refresh", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized(); // süresi dolmuş/geçersiz token → 401
    var token = JwtTokens.Issue(jwtKey, s.UserId, s.CompanyId);
    return Results.Ok(new { token, expiresInSeconds = JwtTokens.ExpiryHours * 3600 });
}).RequireAuthorization();

// Masaüstü senkron girişi: yerel DB'de olmayan (web'te oluşturulan) kullanıcıyı sunucu doğrular ve
// tam paketini döndürür → masaüstü yerele yazıp giriş yapar. Geçersizse 401.
app.MapPost("/api/auth/sync-login", (HttpContext http, LoginDto dto) =>
{
    var rl = loginLimiter.Check("login:" + (ClientIp(http) ?? "unknown"));
    if (!rl.Allowed)
        return Results.Json(new { error = $"Çok fazla giriş denemesi. {rl.RetrySeconds} sn sonra tekrar deneyin." }, statusCode: 429);
    var bundle = svc.Auth.ExportForSync(dto.CompanyId ?? "", dto.Username, dto.Password);
    return bundle is null
        ? Results.Json(new { error = "Kullanıcı adı veya parola hatalı." }, statusCode: 401)
        : Results.Ok(bundle);
});

// ── Login ekranı için PUBLIC firma + şube listesi (anonim; kod+şifre-var-mı) ──
app.MapGet("/api/public/companies", (HttpContext http) =>
{
    if (!publicLimiter.Check("pub:" + (ClientIp(http) ?? "?")).Allowed) return Results.StatusCode(429);
    using var conn = svc.Factory.Create();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name FROM companies WHERE is_deleted=0 ORDER BY name;";
    var list = new List<object>();
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add(new { id = r.GetString(0), name = r.GetString(1) });
    return Results.Ok(list);
});
app.MapGet("/api/public/branches", (HttpContext http, string companyId) =>
{
    if (!publicLimiter.Check("pub:" + (ClientIp(http) ?? "?")).Allowed) return Results.StatusCode(429);
    if (string.IsNullOrWhiteSpace(companyId)) return Results.Ok(Array.Empty<object>());
    var rows = svc.Branches.ListForLogin(companyId);
    return Results.Ok(rows.Select(b => new { id = b.Id, name = b.Name, code = b.Code, hasPassword = b.HasPassword }));
});
app.MapPost("/api/public/verify-branch", (HttpContext http, VerifyBranchDto d) =>
{
    // Şube ŞİFRESİ doğrulaması → brute-force koruması (login ile aynı sıkı sınır: 30/5dk/IP).
    var rl = loginLimiter.Check("branch:" + (ClientIp(http) ?? "?"));
    if (!rl.Allowed) return Results.Json(new { error = $"Çok fazla deneme. {rl.RetrySeconds} sn sonra." }, statusCode: 429);
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
    // Yetki-farkında: kullanıcının yazamadığı modüllerin tabloları uygulanmaz + içerik doğrulaması yapılır.
    var res = svc.BusinessSync.Apply(s, doc.RootElement);
    // Senkron 2b: hareketler uygulandıktan sonra stok bakiyesini SUNUCU yeniden hesaplar (birleşik, otoriteli).
    try { svc.Stock.RecomputeBalances(s.CompanyId); } catch (Exception ex) { Console.Error.WriteLine($"[recompute-balances] {DateTimeOffset.UtcNow:O} {ex.Message}"); }
    return Results.Ok(new { upserted = res.Upserted, skipped = res.Skipped, errors = res.Errors });
}).RequireAuthorization();

// İş verisi GERİ-ÇEKME (server → masaüstü): firmanın iş tablolarını snapshot olarak döndürür → masaüstü
// diğer makinelerin verisini görür (çok makineli görünürlük). Oturumdaki firma zorlanır (tenant güvenli).
app.MapGet("/api/sync/business-pull", (HttpContext c, long? since) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // DELTA: since>0 ise yalnız updated_at>since satırlar döner (rutin eşitleme küçük olsun — 2508 kayıtta
    // tam snapshot zaman aşımına uğruyordu). since yok/0 → tam snapshot (ilk kurulum / manuel tam eşitleme).
    var snapshot = svc.BusinessSync.BuildSnapshot(s.CompanyId, "server", since ?? 0);
    return Results.Content(snapshot, "application/json");
}).RequireAuthorization();

// İş verisi SÜRÜMÜ (ucuz): firmanın tüm iş tablolarındaki en büyük updated_at. Masaüstü sık yoklar; sürüm
// değişmediyse tam snapshot ÇEKMEZ (kullanıcı isteği 2026-07-19: anlık ama bant israfsız).
app.MapGet("/api/sync/business-version", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(new { version = svc.BusinessSync.CompanyVersion(s.CompanyId) }) : Results.Unauthorized()).RequireAuthorization();

// Çakışmalar — admin (tümü) / personel (görmediği, şube kapsamında)
app.MapGet("/api/sync/conflicts", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(svc.BusinessSync.ListConflicts(s.CompanyId)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/sync/conflicts/unseen", (HttpContext c, string? branchId) =>
    S(c) is { } s ? Results.Ok(svc.BusinessSync.ListUnseen(s.CompanyId, string.IsNullOrWhiteSpace(branchId) ? null : branchId)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/sync/conflicts/seen", (HttpContext c, ConflictSeenDto d) =>
    S(c) is { } s ? Results.Ok(new { marked = svc.BusinessSync.MarkSeen(s.CompanyId, string.IsNullOrWhiteSpace(d.BranchId) ? null : d.BranchId) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/sync/conflicts/{id}/resolve", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.BusinessSync.ResolveConflict(s.CompanyId, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Makine yönetimi (JWT — admin) ── firma+şube filtresi VEYA kayıtsız (şubesiz) makineler (firma bağımsız)
app.MapGet("/api/machines", (HttpContext ctx, string? companyId, string? branchId, bool? unassigned) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var rows = svc.Enrollment.ListDevices(s,
        string.IsNullOrWhiteSpace(companyId) ? null : companyId,
        string.IsNullOrWhiteSpace(branchId) ? null : branchId,
        unassigned == true).Select(d => new
    {
        id = d.Id, name = d.Name, status = d.Status, statusText = d.StatusText,
        lastSeenText = d.LastSeenText, createdText = d.CreatedText, canActivate = d.CanActivate, isActive = d.IsActive,
        companyId = d.CompanyId, companyName = d.CompanyName, quota = d.Quota, branchName = d.BranchText, branchId = d.BranchId,
        ip = d.IpText, ipv4 = d.Ip4Text, ipv6 = d.Ip6Text, province = GeoIp.Province(d.Ip4, d.Ip6),
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
// Admin makineye ŞUBE atar (otoriter). branchId boş → atama kaldırılır.
app.MapPost("/api/machines/{id}/branch", (HttpContext ctx, string id, AssignBranchDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Enrollment.AssignBranch(s, id, string.IsNullOrWhiteSpace(d.BranchId) ? null : d.BranchId);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// SÜPER ADMIN makinenin FİRMASINI değiştirir (çapraz-firma; şube ataması otomatik kalkar).
app.MapPost("/api/machines/{id}/company", (HttpContext ctx, string id, AssignCompanyDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Enrollment.AssignCompany(s, id, d.CompanyId ?? "");
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// İLK KURULUM oto-atama (masaüstü, onay sonrası): makinenin şubesi henüz yoksa giriş yapan kullanıcı
// kendi firması+şubesini makineye tanımlar. Zaten atanmışsa dokunmaz (admin otoriter).
app.MapPost("/api/machines/self-assign", (HttpContext ctx, SelfAssignDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.MachineName) || string.IsNullOrWhiteSpace(d.BranchId))
        return Results.Json(new { error = "Makine adı ve şube gerekli." }, statusCode: 400);
    var assigned = svc.Enrollment.SelfAssignBranchIfUnset(s, d.MachineName!, d.BranchId!);
    return Results.Ok(new { assigned });
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
        c.CommandText = "SELECT password_hash, is_active FROM users WHERE id=@u;";
        c.AddWithValue("@u", s.UserId);
        using var r = c.ExecuteReader();
        if (r.Read()) { ph = r.GetString(0); active = r.GetInt32(1); }
    }
    var roles = new List<string>();
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u ORDER BY r.role_key;";
        c.AddWithValue("@u", s.UserId);
        using var r = c.ExecuteReader(); while (r.Read()) roles.Add(r.GetString(0));
    }
    var perms = new List<string>();
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT module_key,can_view,can_create,can_edit,can_delete FROM user_permissions WHERE user_id=@u ORDER BY module_key;";
        c.AddWithValue("@u", s.UserId);
        using var r = c.ExecuteReader();
        while (r.Read()) perms.Add($"{r.GetString(0)}:{r.GetInt64(1)}{r.GetInt64(2)}{r.GetInt64(3)}{r.GetInt64(4)}");
    }
    var buttons = new List<string>();
    using (var c = conn.CreateCommand())
    {
        c.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=@u ORDER BY button_key;";
        c.AddWithValue("@u", s.UserId);
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
        // İLK AÇILIŞ varsayılanı (kayıt yoksa): Koyu / Yumuşak / Kehribar. Kullanıcı değiştirince kaydı bu ezer.
        mode = string.IsNullOrEmpty(mode) ? "dark" : mode,
        color = string.IsNullOrEmpty(color) ? "amber" : color,
        style = string.IsNullOrEmpty(style) ? "soft" : style,
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

// Liste ekranı kolon tercihi — KİŞİSEL (kullanıcı isteği 2026-07-17): her kullanıcı yalnız KENDİ tercihini
// görür/kaydeder (user_id oturumdan gelir, istekten ASLA). columns=null → çağıran ekranın kendi varsayılanı.
app.MapGet("/api/me/list-columns/{listKey}", (HttpContext ctx, string listKey) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    return Results.Ok(new { columns = svc.ListPrefs.GetColumns(s, listKey) });
}).RequireAuthorization();
app.MapPost("/api/me/list-columns/{listKey}", (HttpContext ctx, string listKey, ListColumnsDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.ListPrefs.SaveColumns(s, listKey, d.Columns ?? new List<string>());
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// Liste ekranı sayfa boyutu + kolon genişlikleri — KİŞİSEL (kullanıcı isteği 2026-07-18). pageSize=null →
// ekran 25 kullanır; widths=null → otomatik genişlik. user_id oturumdan gelir.
app.MapGet("/api/me/list-prefs/{listKey}", (HttpContext ctx, string listKey) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    // Birim 4: TEK sorguda tüm kişisel tercih (kolon sırası/seçimi + genişlik + sayfa boyutu + pinned + sıralama).
    // Ortak tablo bileşeni ekran açılırken bir kez çağırır (performans kuralı: her işlemde tekrar okunmaz).
    var p = svc.ListPrefs.GetAll(s, listKey);
    return Results.Ok(new
    {
        columns = p.Columns,
        pageSize = p.PageSize,
        widths = p.Widths,
        pinned = p.Pinned,                                   // gelecekte aktif; şimdilik yalnız taşınır
        sort = p.Sort is null ? null : new { key = p.Sort.Key, desc = p.Sort.Desc },
    });
}).RequireAuthorization();
// Kaydedilmiş varsayılan sıralama (Birim 4 altyapı — UI'da henüz aktif değil, ama uç hazır).
app.MapPost("/api/me/list-prefs/{listKey}/sort", (HttpContext ctx, string listKey, SortPrefDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.Key)) return Results.BadRequest();
    svc.ListPrefs.SaveSort(s, listKey, d.Key!, d.Desc);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapPost("/api/me/list-prefs/{listKey}/page-size", (HttpContext ctx, string listKey, PageSizeDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.ListPrefs.SavePageSize(s, listKey, d.PageSize);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapPost("/api/me/list-prefs/{listKey}/widths", (HttpContext ctx, string listKey, WidthsDto d) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.ListPrefs.SaveWidths(s, listKey, d.Widths ?? new Dictionary<string, int>());
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
    return Results.Ok(new { isSuperAdmin = s.IsSuperAdmin, isRestrictedSuperAdmin = s.IsRestrictedSuperAdmin,
        isAdmin = DepoWise.Application.Security.AccessControl.IsAdmin(s), modules = mods });
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
        // A2 (Aurora): ana ekran KPI sayıları. AYNI GetSummary → aynı tenant/şube/yetki kapsamı
        // (uyarılarla birebir). Yeni alan; eski davranış bozulmaz (summary yoksa web türetmeye düşer).
        summary = new
        {
            vehicleCount = sum.VehicleCount,
            materialCount = sum.MaterialCount,
            lowStockCount = sum.LowStockCount,
            pendingRequestCount = sum.PendingRequestCount,
            personnelCount = sum.PersonnelCount,
        },
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
    // dto.Id: masaüstünün ÇEVRİMDIŞI ürettiği firma id'si (kuyruk işlenirken gelir) → aynı id ile oluşturulur,
    // tekrar gönderimde idempotent (hata vermez). Web'den gelen normal istekte null'dır → sunucu id üretir.
    var id = svc.Companies.Create(s, new DepoWise.Infrastructure.Organization.NewCompany(
        dto.Name, dto.TaxNo, dto.TaxOffice, dto.Address, dto.Phone, dto.Email, dto.AuthorizedPerson, dto.MaxUsers, dto.MaxAdmins, dto.MachineQuota), dto.Id);
    return Results.Ok(new { id });
}).RequireAuthorization();

app.MapPut("/api/companies/{id}", (HttpContext ctx, string id, NewCompanyDto dto) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Companies.Update(s, id, new DepoWise.Infrastructure.Organization.NewCompany(
        dto.Name, dto.TaxNo, dto.TaxOffice, dto.Address, dto.Phone, dto.Email, dto.AuthorizedPerson, dto.MaxUsers, dto.MaxAdmins, dto.MachineQuota));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapDelete("/api/companies/{id}", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Companies.Delete(s, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// Pasife alınmış firmalar + yeniden aktifleştirme (sözleşme yenileme) — yalnız süper admin.
app.MapGet("/api/companies/deleted", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    return Results.Ok(svc.Companies.ListDeleted(s));
}).RequireAuthorization();
app.MapPost("/api/companies/{id}/reactivate", (HttpContext ctx, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var reactivated = svc.Companies.Reactivate(s, id);
    return Results.Ok(new { ok = true, reactivatedUsers = reactivated });
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
/// G6-02 (2026-08-11): "vehicle_brands" API düzeyinde bir TAKMA ADdır — araç markaları malzeme markalarıyla
/// AYNI fiziksel tabloda (brands) durur, yalnız brand_type sütunuyla ayrılır. Listeleme (özel GET rotası) ve
/// ekleme (POST switch) bu takma adı zaten çeviriyordu; yeniden adlandır/sil/kilitle uçları çevirmediği için
/// LookupService "Bilinmeyen tanım tablosu: vehicle_brands" diyerek 400 dönüyordu (web'de araç markası
/// düzeltilemiyor/silinemiyordu; masaüstü doğrudan "brands" kullandığı için çalışıyordu).
/// Beyaz liste GEVŞETİLMEZ: çeviriden sonra tablo adı yine LookupService'in whitelist'inden geçer.
static string LookupTable(string table) => table == "vehicle_brands" ? "brands" : table;
// Araç sınır kuralları (madde 8+1): şantiye/şube zorunlu + makul üretim yılı.
static void RequireVehicleFields(string? branchId, int? productionYear)
{
    if (string.IsNullOrWhiteSpace(branchId))
        throw new ArgumentException("Araç için şantiye/şube seçimi zorunludur.");
    if (!DepoWise.Application.Ui.FieldChecks.YearInRange(productionYear))
        throw new ArgumentException($"Üretim yılı {DepoWise.Application.Ui.FieldChecks.MinVehicleYear}–{DepoWise.Application.Ui.FieldChecks.MaxVehicleYear} aralığında olmalı.");
}

app.MapGet("/api/users", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Users.ListUsers(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/branches", (HttpContext c, string? companyId) => S(c) is { } s ? Results.Ok(svc.Branches.List(s, companyId)) : Results.Unauthorized()).RequireAuthorization();
// Firma seçicileri için tenant-kapsamlı liste: süper admin tümü, diğerleri YALNIZ kendi firması.
app.MapGet("/api/companies/options", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(svc.Companies.Selectable(s).Select(x => new { id = x.Id, name = x.Name })) : Results.Unauthorized()).RequireAuthorization();
// İş A (2026-08-09): "search" eklendi. Bu uç SAYFALIDIR; personel seçicileri aramasız yüklediğinde
// sınırın ötesindeki personel seçilemiyordu. /api/materials ve /api/vehicles ile AYNI desen.
app.MapGet("/api/personnel", (HttpContext c, string? search) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var acc = svc.Users.AccountsByPersonnel(s.CompanyId); // #6: personel → bağlı kullanıcı rozeti (döngü ÖNCESİ tek sorgu)
    var rows = svc.Personnel.List(s, Page(), search: Doc(search)).Items.Select(p =>
    {
        acc.TryGetValue(p.Id, out var a);
        return new
        {
            p.Id, p.CompanyId, p.BranchId, p.FullName, p.Title, p.Phone, p.IsActive, p.CreatedAt,
            isFieldStaff = p.IsFieldStaff,   // Fikir B: "Saha personeli" kutucuğu
            version = p.Version,             // DÜZENLEME KİLİDİ: düzenlemede geri gönderilir
            hasAccount = a is not null, userId = a?.UserId, username = a?.Username,
            accountActive = a?.IsActive ?? false, accountAdmin = a?.IsAdmin ?? false,
        };
    });
    return Results.Ok(rows);
}).RequireAuthorization();

// Fikir B — Unvan SABİT TANIM listesi (personel formunda seçilir, "+" ile yeni eklenir).
app.MapGet("/api/personnel-titles", (HttpContext c) =>
    S(c) is { } s ? Results.Ok(svc.PersonnelTitles.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/personnel-titles", (HttpContext c, TitleDto d) =>
    S(c) is { } s ? Results.Ok(svc.PersonnelTitles.Create(s, d.Name)) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/personnel-titles/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.PersonnelTitles.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
// #6 — Olası aynı kişi (mükerrer) sorgusu: kayıt öncesi uyarı için.
app.MapGet("/api/personnel/duplicates", (HttpContext c, string? fullName, string? phone, string? excludeId) =>
    S(c) is { } s ? Results.Ok(svc.Personnel.FindDuplicates(s, fullName ?? "", phone, excludeId)) : Results.Unauthorized()).RequireAuthorization();
// #6 — Personele uygulama hesabı aç (kullanıcı oluştur + bağla). Admin+.
app.MapPost("/api/personnel/{id}/account", (HttpContext c, string id, AccountDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var p = svc.Personnel.Get(s, id) ?? throw new InvalidOperationException("Personel bulunamadı.");
    var roles = new[] { string.IsNullOrWhiteSpace(d.RoleKey) ? "role-staff" : d.RoleKey! };
    // Adım 6: personele hesap açarken de şube zorunlu (personelin şubesi ya da seçilen şube).
    svc.Users.ValidateBranchForNewUser(s, s.CompanyId, roles, d.BranchId ?? p.BranchId);
    var uid = svc.Users.CreateUser(s, new DepoWise.Infrastructure.Security.NewUser(
        d.Username, d.Password, p.FullName, roles, CompanyId: s.CompanyId, BranchId: d.BranchId ?? p.BranchId, PersonnelId: id));
    return Results.Ok(new { userId = uid });
}).RequireAuthorization();
// #6 (revize) — Personele BAĞLANABİLİR mevcut kullanıcılar (henüz hiçbir personele bağlı olmayan). Admin+.
app.MapGet("/api/personnel/linkable-users", (HttpContext c) =>
    S(c) is { } s
        ? Results.Ok(svc.Users.ListLinkableUsers(s).Select(u => new { id = u.Id, username = u.Username, fullName = u.FullName, isActive = u.IsActive, branchName = u.BranchName, display = u.Display }))
        : Results.Unauthorized()).RequireAuthorization();
// #6 (revize) — MEVCUT kullanıcıyı personele bağla (YENİ hesap açmaz; kullanıcılar "Kullanıcılar" ekranında açılır). Admin+.
app.MapPost("/api/personnel/{id}/link-user", (HttpContext c, string id, LinkUserDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.UserId)) return Results.Json(new { error = "Bağlanacak kullanıcı seçilmedi." }, statusCode: 400);
    svc.Users.LinkPersonnel(s, d.UserId!, id);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// #6 — Personelin hesabını çöz (kullanıcıyı silmez, bağı kaldırır). Admin+.
app.MapDelete("/api/personnel/{id}/account", (HttpContext c, string id) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!svc.Users.AccountsByPersonnel(s.CompanyId).TryGetValue(id, out var a))
        return Results.Json(new { error = "Bu personele bağlı hesap yok." }, statusCode: 400);
    svc.Users.LinkPersonnel(s, a.UserId, null);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapGet("/api/materials", (HttpContext c, string? search) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var items = svc.Materials.List(s, Page(), search).Items;
    // Faz S / İş #11: bakiyeler TEK sorguda okunur. Eskiden satır başına ayrı sorgu atılıyordu
    // (sayfa başına 200'e kadar) — sunucu PostgreSQL'e (ağ üzerinden) geçtiği için her biri bir
    // gidiş-dönüştü. Bu uç, diğer ekranlardaki hızlı-arama seçicisidir; sık çağrılır.
    var balances = svc.Stock.GetBalances(s, items.Select(m => m.Id).ToList());
    var rows = items.Select(m =>
    {
        var stock = balances.TryGetValue(m.Id, out var q) ? q : 0m;   // bakiyesi yoksa 0 (eski davranışla aynı)
        var status = stock <= 0 ? "Stok Yok" : stock <= m.MinStock ? "Düşük Stok" : "Yeterli";
        return new { id = m.Id, code = m.Code, name = m.Name, type = m.Type, unitPrice = m.UnitPrice, currency = m.Currency, minStock = m.MinStock, stock, statusText = status };
    }).ToList();
    return Results.Ok(rows);
}).RequireAuthorization();
// Malzeme LİSTE ekranı: kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17). Eski
// "/api/materials" (search) YUKARIDA — başka ekranlardaki (Stok, Talep, Bakım…) hızlı-arama seçicileri onu
// kullanır, DOKUNULMADI. Bu uç yalnız Malzeme Listesi ekranı içindir.
app.MapGet("/api/materials/grid", (HttpContext c,
    string? code, string? name, string? type, string? category, string? unit, string? brand, string? supplier,
    string? unitPrice, string? currency, string? minStock, string? stock, string? status, string? description,
    string? compatibleVehicles, string? equivalents, int page, int pageSize, string? sort, bool? desc,
    bool criticalOnly = false) =>   // A1 (Aurora): yalnız kritik (stok<=min); varsayilan false = eski davranis
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var filter = new DepoWise.Infrastructure.Materials.MaterialGridFilter(
        code, name, type, category, unit, brand, supplier, unitPrice, currency, minStock, stock, status,
        description, compatibleVehicles, equivalents);
    var res = svc.Materials.SearchGrid(s, filter, page <= 0 ? 1 : page, pageSize <= 0 ? 25 : pageSize,
        string.IsNullOrWhiteSpace(sort) ? null : sort, desc == true, criticalOnly);
    return Results.Ok(new
    {
        items = res.Items, totalCount = res.TotalCount, page = res.Page, pageSize = res.PageSize, totalPages = res.TotalPages,
    });
}).RequireAuthorization();
// Malzeme Listesi — "Excel'e Aktar" (kullanıcı isteği 2026-07-19): AKTİF FİLTRELERLE eşleşen TÜM sonuçları
// (sayfalama sınırı olmadan) .xlsx olarak indirir. Aynı filtre/sıralama parametrelerini kullanır.
app.MapGet("/api/materials/grid/export", (HttpContext c,
    string? code, string? name, string? type, string? category, string? unit, string? brand, string? supplier,
    string? unitPrice, string? currency, string? minStock, string? stock, string? status, string? description,
    string? compatibleVehicles, string? equivalents, string? sort, bool? desc,
    bool criticalOnly = false) =>   // A1 (Aurora): ekranla aynı "yalnız kritik" filtresi
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    DepoWise.Application.Security.AccessControl.Require(s, "export", DepoWise.Application.Security.PermissionAction.View);   // dışa aktarım yetkisi (2026-07-26)
    var filter = new DepoWise.Infrastructure.Materials.MaterialGridFilter(
        code, name, type, category, unit, brand, supplier, unitPrice, currency, minStock, stock, status,
        description, compatibleVehicles, equivalents);
    var rows = svc.Materials.SearchGridAll(s, filter, string.IsNullOrWhiteSpace(sort) ? null : sort, desc == true, criticalOnly);
    var bytes = svc.Excel.Export(DepoWise.Infrastructure.Materials.MaterialService.ToTableModel(rows));
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Malzemeler.xlsx");
}).RequireAuthorization();
app.MapGet("/api/materials/{id}", (HttpContext c, string id) => S(c) is { } s ? Results.Ok(svc.Materials.GetDetail(s, id)) : Results.Unauthorized()).RequireAuthorization();
// A3 (Aurora): malzeme kartı "Son Hareketler" — tek malzemenin son N hareketi. Yetki: malzeme okuma + firma
// kapsamı (RecentForMaterial içinde Require(materials,View) + EnsureMaterialOwned). take verilmezse 10.
app.MapGet("/api/materials/{id}/movements", (HttpContext c, string id, int? take) =>
    S(c) is { } s ? Results.Ok(svc.Stock.RecentForMaterial(s, id, take is > 0 ? take.Value : 10)) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/materials/{id}", (HttpContext c, string id, NewMaterialDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.Materials.Update(s, id, new DepoWise.Infrastructure.Materials.UpdateMaterial(
        d.Code, d.Name, d.Type, d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, Doc(d.Description),
        TemplateId: d.TemplateId),
        expectedVersion: d.Version); // düzenleme kilidi
    if (d.VehicleIds is not null) svc.Materials.SetCompatibleVehicles(s, id, d.VehicleIds);
    // G2-03: muadil listesi. VehicleIds ile AYNI semantik — `null` = "dokunma" (hızlı düzenleme
    // pencereleri bu alanı göndermez, mevcut muadiller korunur), boş liste = "hepsini kaldır".
    // Update'ten SONRA çağrılır: düzenleme kilidi 409 verirse buraya hiç gelinmez (G2-02 korunur).
    if (d.EquivalentIds is not null) svc.Materials.SetEquivalents(s, id, d.EquivalentIds);
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

// ── Şablon fotoğrafları (malzeme + araç şablonları) — genel foto altyapısını yeniden kullanır ──
static string TplEntity(string kind) => kind == "vehicle" ? "vehicle_template" : "material_template";
app.MapGet("/api/templates/{kind}/{id}/photos", (HttpContext c, string kind, string id) =>
    S(c) is { } s ? Results.Ok(svc.Files.GetPhotos(s, TplEntity(kind), id).Select(p => new { id = p.Id, url = $"/api/templates/{kind}/{id}/photos/{p.Id}" })) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/templates/{kind}/{id}/photos/{fileId}", (HttpContext c, string kind, string id, string fileId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var p = svc.Files.GetPhotos(s, TplEntity(kind), id).FirstOrDefault(x => x.Id == fileId);
    if (p is null) return Results.NotFound();
    return Results.File(svc.Storage.Read(p.StorageKey), p.Mime ?? "image/jpeg");
}).RequireAuthorization();
app.MapPost("/api/templates/{kind}/{id}/photos", async (HttpContext ctx, string kind, string id) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    var form = await ctx.Request.ReadFormAsync();
    int n = 0;
    foreach (var file in form.Files)
    {
        using var ms = new MemoryStream();
        await file.OpenReadStream().CopyToAsync(ms, ctx.RequestAborted);
        svc.Files.SavePhoto(s, TplEntity(kind), id, file.FileName, file.ContentType, ms.ToArray());
        n++;
    }
    return Results.Ok(new { added = n });
}).RequireAuthorization();
app.MapDelete("/api/templates/{kind}/{id}/photos/{fileId}", (HttpContext c, string kind, string id, string fileId) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Files.DeletePhoto(s, fileId)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/vehicles", (HttpContext c, string? search) => S(c) is { } s ? Results.Ok(svc.Vehicles.List(s, search)) : Results.Unauthorized()).RequireAuthorization();
// Araç LİSTE ekranı: kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17). Eski
// "/api/vehicles" (search) YUKARIDA — başka ekranlardaki hızlı-arama seçicileri onu kullanır, DOKUNULMADI.
app.MapGet("/api/vehicles/grid", (HttpContext c,
    string? internalCode, string? plate, string? productionYear, string? meter, string? status, string? statusNote,
    string? vehicleType, string? category, string? brand, string? model, string? branch, string? driver,
    string? chassisNo, string? engineNo, int page, int pageSize, string? sort, bool? desc) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var filter = new DepoWise.Infrastructure.Vehicles.VehicleGridFilter(
        internalCode, plate, productionYear, meter, status, statusNote, vehicleType, category, brand, model,
        branch, driver, chassisNo, engineNo);
    var res = svc.Vehicles.SearchGrid(s, filter, page <= 0 ? 1 : page, pageSize <= 0 ? 25 : pageSize,
        string.IsNullOrWhiteSpace(sort) ? null : sort, desc == true);
    return Results.Ok(new
    {
        items = res.Items, totalCount = res.TotalCount, page = res.Page, pageSize = res.PageSize, totalPages = res.TotalPages,
    });
}).RequireAuthorization();
// Araç Listesi — "Excel'e Aktar" (kullanıcı isteği 2026-07-19) — bkz. materials/grid/export (aynı desen).
app.MapGet("/api/vehicles/grid/export", (HttpContext c,
    string? internalCode, string? plate, string? productionYear, string? meter, string? status, string? statusNote,
    string? vehicleType, string? category, string? brand, string? model, string? branch, string? driver,
    string? chassisNo, string? engineNo, string? sort, bool? desc) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    DepoWise.Application.Security.AccessControl.Require(s, "export", DepoWise.Application.Security.PermissionAction.View);   // dışa aktarım yetkisi (2026-07-26)
    var filter = new DepoWise.Infrastructure.Vehicles.VehicleGridFilter(
        internalCode, plate, productionYear, meter, status, statusNote, vehicleType, category, brand, model,
        branch, driver, chassisNo, engineNo);
    var rows = svc.Vehicles.SearchGridAll(s, filter, string.IsNullOrWhiteSpace(sort) ? null : sort, desc == true);
    var bytes = svc.Excel.Export(DepoWise.Infrastructure.Vehicles.VehicleService.ToTableModel(rows));
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Araclar.xlsx");
}).RequireAuthorization();
// Araç seçici (uyumlu araçlar vb. çoklu seçim için): id + görünen ad (iç kod - plaka).
app.MapGet("/api/vehicles/options", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var opts = new List<object>();
    using var conn = svc.Factory.Create();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, internal_code, COALESCE(plate,'') FROM vehicles WHERE company_id=@c AND is_deleted=0 ORDER BY internal_code;";
    cmd.AddWithValue("@c", s.CompanyId);
    using var r = cmd.ExecuteReader();
    while (r.Read()) { var p = r.GetString(2); opts.Add(new { id = r.GetString(0), display = string.IsNullOrEmpty(p) ? r.GetString(1) : $"{r.GetString(1)} - {p}" }); }
    return Results.Ok(opts);
}).RequireAuthorization();
app.MapGet("/api/stock", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Stock.RecentMovements(s)) : Results.Unauthorized()).RequireAuthorization();
// Stok Hareketleri ekranı (kullanıcı isteği 2026-08-05): tarih aralığı (from/to Unix ms) + metin araması (q).
app.MapGet("/api/stock/movements", (HttpContext c, long? from, long? to, string? q) =>
    S(c) is { } s ? Results.Ok(svc.Stock.SearchMovements(s, from, to, q, 1000)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/maintenance", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Maintenance.ListMaintenances(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/inspection", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Inspection.List(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/fuel", (HttpContext c, bool? includeCancelled) => S(c) is { } s ? Results.Ok(svc.Fuel.ListDistributions(s, 200, includeCancelled == true)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/daily", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.DailyActivity.List(s)) : Results.Unauthorized()).RequireAuthorization();
// Günlük Faaliyet LİSTE ekranı: kolon bazlı filtre + sayfalama + sıralama (kullanıcı isteği 2026-07-19 —
// Malzemeler/Araçlar'a yapılan geliştirmenin AYNISI, bkz. ADR-087/088/089). Eski "/api/daily" (yukarıda)
// dokunulmadı. "Tarih" filtre almaz — yalnız sıralanır.
app.MapGet("/api/daily/grid", (HttpContext c,
    string? type, string? vehicle, string? route, string? operatorText, string? duration, string? description,
    int page, int pageSize, string? sort, bool? desc, bool? includeCancelled) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var filter = new DepoWise.Infrastructure.Operations.DailyActivityGridFilter(type, vehicle, route, operatorText, duration, description);
    // K3 (2026-08-09): iptal edilen faaliyetler varsayılan GİZLİ; yalnız "İptal edilenleri göster" kutusu ile gelir.
    var res = svc.DailyActivity.SearchGrid(s, filter, page <= 0 ? 1 : page, pageSize <= 0 ? 25 : pageSize,
        string.IsNullOrWhiteSpace(sort) ? null : sort, desc == true, includeCancelled == true);
    return Results.Ok(new
    {
        items = res.Items, totalCount = res.TotalCount, page = res.Page, pageSize = res.PageSize, totalPages = res.TotalPages,
    });
}).RequireAuthorization();
// Günlük Faaliyet Listesi — "Excel'e Aktar" (kullanıcı isteği 2026-07-19) — bkz. materials/grid/export (aynı desen).
app.MapGet("/api/daily/grid/export", (HttpContext c,
    string? type, string? vehicle, string? route, string? operatorText, string? duration, string? description,
    string? sort, bool? desc, bool? includeCancelled) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    DepoWise.Application.Security.AccessControl.Require(s, "export", DepoWise.Application.Security.PermissionAction.View);   // dışa aktarım yetkisi (2026-07-26)
    var filter = new DepoWise.Infrastructure.Operations.DailyActivityGridFilter(type, vehicle, route, operatorText, duration, description);
    // Excel ekrandaki AYNI kümeyi verir: "İptal edilenleri göster" işaretliyse iptaller de dışa aktarılır.
    var rows = svc.DailyActivity.SearchGridAll(s, filter, string.IsNullOrWhiteSpace(sort) ? null : sort, desc == true, includeCancelled == true);
    var bytes = svc.Excel.Export(DepoWise.Infrastructure.Operations.DailyActivityService.ToTableModel(rows));
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "GunlukFaaliyet.xlsx");
}).RequireAuthorization();
// B-1 (PRT-01 Grup 4, 2026-08-10): durum/arama/limit artık SUNUCUYA ulaşıyor.
// Eskiden uç parametresizdi (List(s)) → en yeni 200 kayıt dönüyor, web bunun İÇİNDE istemci tarafında
// süzüyordu; 200'den fazla talebi olan firmada eski talepler web'de hiç bulunamıyordu. Masaüstü bu
// parametreleri servise zaten geçiyordu, o yüzden yalnız HTTP hattı eksikti.
// GERİYE UYUMLU: üç parametre de opsiyonel; hiçbiri verilmezse davranış AYNEN eskisi gibidir (limit 200).
// Servis sorgusu parametreli (@st/@like/@lim) — enjeksiyon yüzeyi açılmaz.
app.MapGet("/api/requests", (HttpContext c, string? status, string? search, int? limit) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();

    // Bilinmeyen durum SESSİZCE "draft"a düşmemeli (RequestStatusMachine.FromDb öyle yapar) → açık 400.
    DepoWise.Application.Requests.RequestStatus? st = null;
    if (!string.IsNullOrWhiteSpace(status))
    {
        st = status!.Trim().ToLowerInvariant() switch
        {
            "draft" => DepoWise.Application.Requests.RequestStatus.Draft,
            "pending" => DepoWise.Application.Requests.RequestStatus.Pending,
            "approved" => DepoWise.Application.Requests.RequestStatus.Approved,
            "rejected" => DepoWise.Application.Requests.RequestStatus.Rejected,
            "cancelled" => DepoWise.Application.Requests.RequestStatus.Cancelled,
            _ => null,
        };
        if (st is null) return Results.Json(new { error = "Geçersiz talep durumu." }, statusCode: 400);
    }

    // İstemci sınırsız veri isteyemez: üst sınır 1000, geçersiz/eksik değer varsayılana (200) düşer.
    const int DefaultLimit = 200, MaxLimit = 1000;
    var lim = limit is > 0 ? Math.Min(limit.Value, MaxLimit) : DefaultLimit;

    return Results.Ok(svc.Requests.List(s, st, string.IsNullOrWhiteSpace(search) ? null : search!.Trim(), lim));
}).RequireAuthorization();
app.MapGet("/api/lookups/{table}", (HttpContext c, string table) => S(c) is { } s ? Results.Ok(svc.Lookups.List(s, table)) : Results.Unauthorized()).RequireAuthorization();
// Araç markaları (brand_type=vehicle) — malzeme markalarından ayrı
app.MapGet("/api/lookups/vehicle_brands", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Lookups.ListBrands(s, "vehicle")) : Results.Unauthorized()).RequireAuthorization();
// G6-20 (2026-08-11): MALZEME markaları (brand_type=material) — yukarıdaki araç rotasının simetriği.
// Önceden bu istek genel "/api/lookups/{table}" rotasına düşüyor ve LookupService.List türü SÜZMEDİĞİ için
// ARAÇ markalarını da döndürüyordu; masaüstü ise aynı ekranda ListBrands(s, "material") kullanıyordu
// → web/masaüstü paritesi kırıktı (malzeme marka listesinde araç markaları görünüyordu).
// Bu ucun TÜM tüketicileri malzeme tarafıdır (Tanım Düzenle → Malzemeler/Marka, Malzeme formu,
// Malzeme hızlı düzenleme, Malzeme şablonları); araç ekranlarının hepsi vehicle_brands rotasını kullanır
// → araç tarafı ETKİLENMEZ. Yazma uçları zaten "material" ile ekliyordu (POST /api/lookups/brands).
app.MapGet("/api/lookups/brands", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Lookups.ListBrands(s, "material")) : Results.Unauthorized()).RequireAuthorization();
// Malzeme alt kategorileri (seçili kategorinin çocukları)
app.MapGet("/api/materials/subcategories", (HttpContext c, string? parentId) =>
    S(c) is { } s ? Results.Ok(svc.Lookups.ListCategories(s, string.IsNullOrWhiteSpace(parentId) ? null : parentId)) : Results.Unauthorized()).RequireAuthorization();
// Alt kategori EKLE — seçili KATEGORİYE bağlı (parent_id). Dedup: aynı üst altında aynı ad tek Tanım ID.
app.MapPost("/api/materials/subcategories", (HttpContext c, SubCategoryDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.ParentId)) return Results.Json(new { error = "Önce bir kategori seçin." }, statusCode: 400);
    try { return Results.Ok(new { id = svc.Lookups.AddCategory(s, d.Name, d.ParentId) }); }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
}).RequireAuthorization();

// Roller (kullanıcı oluşturma için)
app.MapGet("/api/roles", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // Süper Admin ve Kısıtlı Süper Admin rolleri YALNIZ süper admin'e listelenir (yalnız o atayabilir).
    var roles = RoleKeys.Seed.Where(r => (r.Key != RoleKeys.SuperAdmin && r.Key != RoleKeys.RestrictedSuperAdmin) || s.IsSuperAdmin);
    return Results.Ok(roles.Select(r => new { key = r.Key, name = r.Name }));
}).RequireAuthorization();

// ── Yazma (ekle/sil) uçları — servis AccessControl (Create/Delete) enforce eder ──
app.MapPost("/api/branches", (HttpContext c, BranchDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Branches.Create(s, new DepoWise.Infrastructure.Organization.NewBranch(d.Name, string.IsNullOrWhiteSpace(d.Kind) ? "branch" : d.Kind!, d.ParentId, Doc(d.Code), Doc(d.Password)), d.CompanyId) }) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/branches/{id}/users", (HttpContext c, string id) => S(c) is { } s ? Results.Ok(svc.Branches.GetUsers(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/personnel", (HttpContext c, PersonnelDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Personnel.Create(s, new DepoWise.Infrastructure.Org.NewPersonnel(d.FullName, d.Title, d.Phone, d.BranchId, d.IsActive, d.IsFieldStaff)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/personnel/{id}", (HttpContext c, string id, PersonnelDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Personnel.Update(s, id, new DepoWise.Infrastructure.Org.NewPersonnel(d.FullName, d.Title, d.Phone, d.BranchId, d.IsActive, d.IsFieldStaff), expectedVersion: d.Version)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users", (HttpContext c, NewUserDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // Firma: YALNIZ süper admin seçebilir; diğerleri kendi firmasına bağlar (yetki yükseltme engeli).
    var companyId = s.IsSuperAdmin && !string.IsNullOrWhiteSpace(d.CompanyId) ? d.CompanyId! : s.CompanyId;
    // Adım 6: operasyonel (personel) kullanıcıda şube/şantiye zorunlu (süper/kısıtlı-süper admin + admin muaf).
    svc.Users.ValidateBranchForNewUser(s, d.CompanyId, d.RoleKeys ?? new List<string>(), d.BranchId);
    var id = svc.Users.CreateUser(s, new DepoWise.Infrastructure.Security.NewUser(
        d.Username, d.Password, d.FullName, d.RoleKeys ?? new List<string>(), companyId, null, d.BranchId, d.CanViewAllBranches,
        string.IsNullOrWhiteSpace(d.PersonnelId) ? null : d.PersonnelId));   // Fikir B: "Personel seç" ile bağla
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapPost("/api/materials", (HttpContext c, NewMaterialDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var id = svc.Materials.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial(
        d.Code, d.Name, d.Type, d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, "TRY", Doc(d.Description),
        TemplateId: d.TemplateId));
    if (d.VehicleIds is { Count: > 0 }) svc.Materials.SetCompatibleVehicles(s, id, d.VehicleIds);
    if (d.EquivalentIds is not null) foreach (var eq in d.EquivalentIds) svc.Materials.AddEquivalent(s, id, eq);
    if (d.OpeningStock != 0)   // ADR-086: negatif açılış (devralınan eksik stok) de kaydedilir
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
    try { svc.Lookups.Delete(s, LookupTable(table), id); return Results.Ok(new { ok = true }); }
    catch (ForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
}).RequireAuthorization();
// Tanımı kilitle/kilit aç ("sabit tanım" — kullanıcı isteği 2026-07-19). Yalnız admin.
app.MapPut("/api/lookups/{table}/{id}/lock", (HttpContext c, string table, string id, LockDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    try { svc.Lookups.SetLocked(s, LookupTable(table), id, d.Locked); return Results.Ok(new { ok = true }); }
    catch (ForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
}).RequireAuthorization();
// Alan adı değiştirme (ID korunur). Yetki: "definitions"/Edit (Ekle/Sil ile aynı model — kullanıcı isteği
// 2026-07-18: "tanım düzenle ekranında düzenleme de olmalı"). Rename servisi Edit yetkisini zaten zorlar;
// tenant güvenli (yalnız kendi firmasının satırı). Süper-admin kısıtı KALDIRILDI.
app.MapPut("/api/lookups/{table}/{id}", (HttpContext c, string table, string id, NameDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    try { svc.Lookups.Rename(s, LookupTable(table), id, d.Name); return Results.Ok(new { ok = true }); }
    catch (ForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
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
        cmd.AddWithValue("@c", company);
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
        units = Rows("SELECT id,name FROM units WHERE company_id=@c AND is_deleted=0;"),
        suppliers = Rows("SELECT id,name FROM suppliers WHERE company_id=@c AND is_deleted=0;"),
        vehicleTypes = Rows("SELECT id,name FROM vehicle_types WHERE company_id=@c AND is_deleted=0;"),
        vehicleCategories = Rows("SELECT id,name FROM vehicle_categories WHERE company_id=@c AND is_deleted=0;"),
        materialCategories = Rows("SELECT id,name,parent_id FROM material_categories WHERE company_id=@c AND is_deleted=0;"),
        brands = Rows("SELECT id,name,brand_type FROM brands WHERE company_id=@c AND is_deleted=0;"),
        vehicleModels = Rows("SELECT id,name,brand_id FROM vehicle_models WHERE company_id=@c AND is_deleted=0;"),
        branches = Rows("SELECT id,name,kind,parent_id FROM branches WHERE company_id=@c AND is_deleted=0;"),
    });
}).RequireAuthorization();

// TEST verisi temizleme — YALNIZ süper admin. İş/test kayıtlarını siler; auth (users/roles/
// permissions), companies, app_settings, app_releases, schema_migrations KORUNUR → giriş + sürümler bozulmaz.
app.MapPost("/api/admin/reset-data", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Bu işlem yalnız süper admin." }, statusCode: 403);
    // TÜM firmaların iş verisini siler → üretimde varsayılan KAPALI. Bilinçli açmak için
    // DEPOWISE_ALLOW_RESET=1 ortam değişkeni gerekir (Development'ta serbest).
    if (!app.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("DEPOWISE_ALLOW_RESET") != "1")
        return Results.Json(new { error = "Veri sıfırlama üretim sunucusunda kapalıdır (DEPOWISE_ALLOW_RESET=1 gerekli)." }, statusCode: 403);

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
    var cleared = new List<string>();

    if (conn is Npgsql.NpgsqlConnection)
    {
        // PostgreSQL: FK kapatılamaz (Neon owner yetkisi yok) → yalnız VAR OLAN tabloları FK-güvenli
        // sırada (savepoint+retry) sil. Eksik tabloları önceden ele (SQLite'taki try/catch toleransının karşılığı).
        var existing = new HashSet<string>(
            DepoWise.Infrastructure.Database.DbIntrospect.ListTables(conn), StringComparer.OrdinalIgnoreCase);
        using var ptx = conn.BeginTransaction();
        var done = DepoWise.Infrastructure.Database.DialectPurge.RunFkSafe(
            conn, ptx, clearTables.Where(existing.Contains).Select(t => $"DELETE FROM \"{t}\";"));
        ptx.Commit();
        foreach (var (sql, n) in done) cleared.Add($"{sql}:{n}");
        return Results.Ok(new { ok = true, cleared });
    }

    // --- SQLite yolu: DEĞİŞMEDİ ---
    using (var pragma = conn.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=OFF;"; pragma.ExecuteNonQuery(); }
    using var tx = conn.BeginTransaction();
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
    // G1-05(a): istemcinin jetonu varsa kullanılır (tekrar gönderimde çift belge olmaz); yoksa eski davranış.
    svc.Stock.Count(s, lines, string.IsNullOrWhiteSpace(d.Reason) ? "Sayım" : d.Reason!,
        string.IsNullOrWhiteSpace(d.OperationId) ? Guid.NewGuid().ToString("N") : d.OperationId!, d.BranchId);
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

// ── TEMİZ TEST ORTAMI: tüm operasyonel/tenant kayıtlarını siler; YALNIZ çağıran süper admini + firmasını +
// sistem rollerini korur (giriş korunur). Süper admin + parola yeniden doğrulama zorunlu. GERİ ALINAMAZ. ──
// ══════════════════ ÖZEL KOD + FİRMA KALICI SİLME (ADR-083) ══════════════════
// ⚠️ CLAUDE.md §4 "operasyonel kaydı fiziksel silme" kuralının BİLİNÇLİ istisnası (kullanıcı talebi 2026-07-16).
// Firma Tanım PASİFE ALIR; burası GERİ ALINAMAZ SİLER. Koruma: süper admin + şifre + özel kod + kendi firması yasak.

/// Özel kod durumu — web login "ilk defa oluştur" ekranını buna göre gösterir.
app.MapGet("/api/auth/special-code/status", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // Süper admin DEĞİLSE özel kod kavramı hiç yok → giriş akışı değişmez (required=false).
    return Results.Ok(new { required = s.IsSuperAdmin, hasCode = s.IsSuperAdmin && svc.SpecialCode.HasCode(s.UserId) });
}).RequireAuthorization();

/// Özel kod belirle. İLK kez ise şifre istenmez (kullanıcı zaten giriş yapmış); DEĞİŞTİRİRKEN şifre zorunlu
/// (unutulursa şifreyle sıfırlanabilsin — kullanıcı kararı 2026-07-16).
app.MapPost("/api/auth/special-code", (HttpContext c, SpecialCodeDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Özel kod yalnız süper adminde bulunur." }, statusCode: 403);
    var exists = svc.SpecialCode.HasCode(s.UserId);
    // G6-05: firma-filtresiz doğrulama (bkz. /api/admin/purge-company yanındaki açıklama).
    if (exists && !svc.Auth.VerifyUserPassword(s.UserId, d.Password ?? ""))
        return Results.Json(new { error = "Özel kodu değiştirmek için şifrenizi doğru girin." }, statusCode: 403);
    svc.SpecialCode.SetCode(s, d.Code ?? "");
    return Results.Ok(new { ok = true, replaced = exists });
}).RequireAuthorization();

/// Kalıcı Silme ekranının kilidi: özel kodu doğrular. Kod yoksa daima false (fail-closed).
app.MapPost("/api/auth/special-code/verify", (HttpContext c, SpecialCodeDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    return svc.SpecialCode.Verify(s, d.Code ?? "")
        ? Results.Ok(new { ok = true })
        : Results.Json(new { error = "Özel kod hatalı." }, statusCode: 403);
}).RequireAuthorization();

/// Kalıcı silinmiş firmaların künyeleri (ekranda "ne zaman ne silindi" listesi).
app.MapGet("/api/admin/purges", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    return Results.Ok(svc.CompanyPurge.ListPurges(s).Select(p => new
    {
        companyId = p.CompanyId, companyName = p.CompanyName, purgedBy = p.PurgedBy,
        purgedAt = DateTimeOffset.FromUnixTimeMilliseconds(p.PurgedAt).ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
    }));
}).RequireAuthorization();

/// Masaüstü eşitleme adımı: "benim firmam kalıcı silindi mi?" → evetse yerel veriyi siler, login'e döner.
/// Kimliği doğrulanmış her kullanıcı KENDİ firmasını sorabilir (başkasınınkini değil — tenant sızıntısı olmasın).
app.MapGet("/api/sync/purge-status", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var p = svc.CompanyPurge.GetPurge(s.CompanyId);
    return Results.Ok(new { purged = p is not null, companyName = p?.CompanyName, purgedAt = p?.PurgedAt });
}).RequireAuthorization();

/// FİRMA KALICI SİLME — geri alınamaz. Süper admin + şifre + özel kod + firma adı teyidi.
app.MapPost("/api/admin/purge-company", (HttpContext c, PurgeCompanyDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    // G6-05: parola YALNIZ userId ile doğrulanır (firma filtresiz) — süper admin başka firma bağlamındayken
    // ("Firma Seç" → başka firma) kendi kullanıcı kaydı EV firmasındadır; firma-filtreli sürüm doğru parolayı
    // da "Parola hatalı" sayardı. Aynı düzeltme /api/admin/reset-company-business'ta zaten uygulanmıştı.
    if (!svc.Auth.VerifyUserPassword(s.UserId, d.Password ?? ""))
        return Results.Json(new { error = "Parola hatalı." }, statusCode: 403);
    if (!svc.SpecialCode.Verify(s, d.SpecialCode ?? ""))
        return Results.Json(new { error = "Özel kod hatalı." }, statusCode: 403);

    var companyId = d.CompanyId ?? "";
    var name = svc.CompanyPurge.FindName(companyId);
    if (name is null) return Results.Json(new { error = "Firma bulunamadı." }, statusCode: 404);
    // Yanlış firmayı silmeye karşı SON kilit: kullanıcı firma adını birebir yazmalı.
    if (!string.Equals((d.ConfirmName ?? "").Trim(), name, StringComparison.Ordinal))
        return Results.Json(new { error = $"Doğrulama başarısız: firma adını birebir yazın ({name})." }, statusCode: 400);

    DepoWise.Infrastructure.Organization.PurgeResult res;
    try { res = svc.CompanyPurge.Purge(s, companyId); }
    catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }

    // Diskteki fotoğraflar (files/{companyId}) + makine yedekleri (backups/{companyId}) de silinir —
    // "tamamen siler" (kullanıcı kararı) + disk dolması geçmişte tüm sistemi düşürmüştü (ADR-070).
    int dirsDeleted = 0;
    foreach (var sub in new[] { "files", "backups" })
    {
        try
        {
            var dir = System.IO.Path.Combine(dataDir, sub, companyId);
            if (System.IO.Directory.Exists(dir)) { System.IO.Directory.Delete(dir, true); dirsDeleted++; }
        }
        catch { /* dosya silinemese de DB purge'ü geçerli; künye yazıldı */ }
    }
    return Results.Ok(new { ok = true, companyName = res.CompanyName, tablesTouched = res.TablesTouched, rowsDeleted = res.RowsDeleted, dirsDeleted });
}).RequireAuthorization();

// ══════════════════ FİRMA İŞ VERİSİNİ SIFIRLAMA (kullanıcı talebi 2026-07-19) ══════════════════
// Kalıcı Silme'den (ADR-083) FARKI: firma + şubeler + KULLANICILAR KORUNUR. Yalnız o firmanın iş verisi
// (malzeme/araç/stok/bakım/yakıt/talep + tanımlar) silinir → temiz iş verisiyle sıfırdan başlanır.
// Aynı güvenlik: süper admin + parola + özel kod + firma adını birebir yazma. Ardından firmanın makineleri
// için YEREL SIFIRLAMA (ADR-084) tetiklenir → makineler bir sonraki girişte yerel iş verisini temizleyip
// boş sunucudan çeker (aksi halde çevrimiçi makine yerel kopyasını geri push edip veriyi diriltir).
app.MapPost("/api/admin/reset-company-business", (HttpContext c, PurgeCompanyDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    // Parola: yalnız userId ile doğrula (firma-filtresiz) — süper admin başka firma bağlamındayken bile KENDİ
    // parolasını doğrulayabilsin (kullanıcı bulgusu 2026-07-20: oturum OZE iken "Parola hatalı" veriyordu).
    if (!svc.Auth.VerifyUserPassword(s.UserId, d.Password ?? ""))
        return Results.Json(new { error = "Parola hatalı." }, statusCode: 403);
    if (!svc.SpecialCode.Verify(s, d.SpecialCode ?? ""))
        return Results.Json(new { error = "Özel kod hatalı." }, statusCode: 403);

    var companyId = d.CompanyId ?? "";
    var name = svc.CompanyPurge.FindName(companyId);
    if (name is null) return Results.Json(new { error = "Firma bulunamadı." }, statusCode: 404);
    // Yanlış firmayı sıfırlamaya karşı SON kilit: firma adını yazmalı (büyük/küçük harf duyarsız + boşluk kırpılır).
    if (!string.Equals((d.ConfirmName ?? "").Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = $"Doğrulama başarısız: firma adını yazın ({name})." }, statusCode: 400);

    DepoWise.Infrastructure.Organization.PurgeResult res;
    try { res = svc.CompanyPurge.ResetBusinessData(s, companyId); }
    catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }

    // İŞ verisi fotoğrafları da temizlenir (materyal/araç foto = files/{companyId}); makine yedekleri KALIR.
    int dirsDeleted = 0;
    try
    {
        var dir = System.IO.Path.Combine(dataDir, "files", companyId);
        if (System.IO.Directory.Exists(dir)) { System.IO.Directory.Delete(dir, true); dirsDeleted++; }
    }
    catch { /* dosya silinemese de DB sıfırlaması geçerli */ }

    // Makineler boş sunucudan yeniden dolsun (yerel iş verisini bir kez temizle + tekrar çek).
    long resetAt = 0;
    try { resetAt = svc.CompanyLocalReset.RequestReset(s, companyId).RequestedAt; } catch { }

    return Results.Ok(new { ok = true, companyName = res.CompanyName, tablesTouched = res.TablesTouched, rowsDeleted = res.RowsDeleted, dirsDeleted, machineResetRequestedAt = resetAt });
}).RequireAuthorization();

// ══════════════════ FİRMA YEREL SIFIRLAMA (ADR-084) ══════════════════
// Kalıcı Silme'den (ADR-083) FARKI: firma sunucuda durur, erişim engellenmez — yalnız o firmanın
// makineleri bir sonraki (çevrimiçi) girişte yerel kopyalarını bir kez temizler ve sıfırdan yeniden doldurur.

/// Süper admin bir firma için yerel sıfırlama isteği bırakır. Makine o an kapalı/çevrimdışı olsa da isteği
/// sunucuda BEKLER; makine aktif olup çevrimiçi giriş yaptığında algılanır (bkz. /api/sync/local-reset-status).
app.MapPost("/api/admin/company-local-reset", (HttpContext c, LocalResetDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    try
    {
        var res = svc.CompanyLocalReset.RequestReset(s, d.CompanyId ?? "");
        return Results.Ok(new { ok = true, requestedAt = res.RequestedAt });
    }
    catch (ForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
    catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
}).RequireAuthorization();

/// Masaüstü eşitleme adımı: "firmam için bekleyen bir yerel sıfırlama isteği var mı?" Yalnız KENDİ firmasını
/// sorar (companyId istekten değil oturumdan) — tenant sızıntısı olmaz.
app.MapGet("/api/sync/local-reset-status", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var st = svc.CompanyLocalReset.GetStatus(s.CompanyId);
    return Results.Ok(new { requestedAt = st?.RequestedAt });
}).RequireAuthorization();

// ══════════════════ MAKİNE TANIMI SIFIRLAMA (ADR-085) ══════════════════
// Bir fiziksel makineyi TÜM firmalardan koparır (o makinenin sync_devices satırları silinir) + künye
// bırakır. İş verisine dokunmaz. Masaüstü bir sonraki (çevrimiçi) girişte künyeyi görüp yerel makine
// önbelleğini (firma/şube) temizler ve login ekranına döner — bir sonraki giriş makineyi yeniden tanımlar.

/// Süper admin bir makine adı için tanım sıfırlama isteği bırakır. Makine adı firmalar arası bir anahtardır
/// (bkz. Migration046) — bu yüzden yalnız süper admin (MachineResetService.RequestReset zaten bunu zorlar).
app.MapPost("/api/admin/machine-reset", (HttpContext c, MachineResetDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    try
    {
        var res = svc.MachineReset.RequestReset(s, d.MachineName ?? "");
        return Results.Ok(new { ok = true, requestedAt = res.RequestedAt });
    }
    catch (ForbiddenException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
    catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
}).RequireAuthorization();

/// Masaüstü eşitleme adımı: "bu makine için bekleyen bir tanım sıfırlama isteği var mı?" Firma bağımsız —
/// makine adı sorguya parametre olarak verilir (künye firmalar arası tutulur).
app.MapGet("/api/sync/machine-reset-status", (HttpContext c, string? machineName) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var st = svc.MachineReset.GetStatus(machineName ?? "");
    return Results.Ok(new { requestedAt = st?.RequestedAt });
}).RequireAuthorization();

app.MapPost("/api/admin/reset-test-data", (HttpContext c, ReauthDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    // G6-05: firma-filtresiz doğrulama (bkz. /api/admin/purge-company yanındaki açıklama).
    if (!svc.Auth.VerifyUserPassword(s.UserId, d.Password ?? ""))
        return Results.Json(new { error = "Parola hatalı." }, statusCode: 403);

    // Korunan tablolar: migration geçmişi, sqlite iç tabloları, sistem rolleri. Özel işlenenler: users/companies/user_roles.
    var keepWhole = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "schema_migrations", "sqlite_sequence", "roles" };
    using var conn = svc.Factory.Create();
    var tables = DepoWise.Infrastructure.Database.DbIntrospect.ListTables(conn);   // lehçe-duyarlı (SQLite/PG)

    // Aktörün kendi kaydı KORUNUR (id<>@me / id<>@co); gerisi tamamen silinir.
    var stmts = new List<string>();
    foreach (var t in tables)
    {
        if (keepWhole.Contains(t)) continue;
        stmts.Add(t.ToLowerInvariant() switch
        {
            "users" => "DELETE FROM users WHERE id <> @me;",
            "companies" => "DELETE FROM companies WHERE id <> @co;",
            "user_roles" => "DELETE FROM user_roles WHERE user_id <> @me;",
            "user_permissions" => "DELETE FROM user_permissions WHERE user_id <> @me;",
            "user_button_permissions" => "DELETE FROM user_button_permissions WHERE user_id <> @me;",
            _ => $"DELETE FROM \"{t}\";",
        });
    }
    void Bind(System.Data.Common.DbCommand cmd)
    {
        if (cmd.CommandText.Contains("@me")) cmd.AddWithValue("@me", s.UserId);
        if (cmd.CommandText.Contains("@co")) cmd.AddWithValue("@co", s.CompanyId);
    }

    if (conn is Npgsql.NpgsqlConnection)
    {
        // PostgreSQL: FK kapatılamaz → FK-güvenli sırada (savepoint+retry) sil.
        using var tx = conn.BeginTransaction();
        DepoWise.Infrastructure.Database.DialectPurge.RunFkSafe(conn, tx, stmts, Bind);
        tx.Commit();
    }
    else
    {
        // --- SQLite yolu: DEĞİŞMEDİ (FK kapat + sırayla sil) ---
        using (var off = conn.CreateCommand()) { off.CommandText = "PRAGMA foreign_keys=OFF;"; off.ExecuteNonQuery(); }
        using var tx = conn.BeginTransaction();
        foreach (var sql in stmts)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx; del.CommandText = sql;
            Bind(del);
            del.ExecuteNonQuery();
        }
        tx.Commit();
        using (var on = conn.CreateCommand()) { on.CommandText = "PRAGMA foreign_keys=ON;"; on.ExecuteNonQuery(); }
    }

    // Diskteki fotoğraf ve makine yedeklerini de temizle (yer kaplamasın; ADR-070).
    int filesDeleted = 0;
    try
    {
        foreach (var sub in new[] { "files", "backups" })
        {
            var dir = System.IO.Path.Combine(dataDir, sub);
            if (System.IO.Directory.Exists(dir))
            {
                foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
                { try { System.IO.File.Delete(f); filesDeleted++; } catch { } }
            }
        }
    }
    catch { }
    return Results.Ok(new { ok = true, tablesCleared = tables.Count, filesDeleted, keptUser = s.UserId, keptCompany = s.CompanyId });
}).RequireAuthorization();

// ── Stok İşlemleri (Yeni Kayıt / Transfer / Depo Çıkışı + hareket iptali) — masaüstüyle birebir ──
app.MapGet("/api/stock/balance/{materialId}", (HttpContext c, string materialId) =>
    S(c) is { } s ? Results.Ok(new { balance = svc.Stock.GetBalance(s, materialId) }) : Results.Unauthorized()).RequireAuthorization();

app.MapPost("/api/stock/receive", (HttpContext c, StockReceiveDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.PersonnelId)) throw new ArgumentException("Personel (işlemi yapan) zorunludur."); // madde 8
    if (d.Quantity < 0) throw new ArgumentException("Eklenecek stok negatif olamaz.");

    string materialId;
    if (!string.IsNullOrWhiteSpace(d.MaterialId))
    {
        // madde 1.1 (kullanıcı isteği 2026-08-06): mevcut malzemeye giriş — Kod/Ad/Tür/Birim/Kategori/Alt
        // Kategori/Marka DEĞİŞTİRİLMEZ. Tedarikçi değiştiyse (kullanıcı kararı 2026-08-07) malzeme kartı
        // güncellenir; materials:edit yetkisi yoksa ya da kayıt arada değiştiyse stok girişi zaten
        // TAMAMLANMIŞ olur — bu ikincil güncelleme sessizce atlanır.
        materialId = d.MaterialId;
        var detail = svc.Materials.GetDetail(s, materialId);
        if (d.SupplierId != detail.SupplierId)
        {
            try
            {
                svc.Materials.Update(s, materialId, new DepoWise.Infrastructure.Materials.UpdateMaterial(
                    detail.Code, detail.Name, detail.Type, detail.CategoryId, detail.UnitId, detail.BrandId,
                    d.SupplierId, detail.MinStock, detail.UnitPrice, detail.Description, detail.TemplateId), detail.Version);
            }
            catch { }
        }
    }
    else
    {
        var code = d.Code?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Kod zorunlu.");
        if (string.IsNullOrWhiteSpace(d.Name)) throw new ArgumentException("Ad zorunlu.");
        var found = svc.Materials.List(s, Page(), code).Items
            .FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        materialId = found?.Id ?? svc.Materials.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial(
            code, d.Name.Trim(), string.IsNullOrWhiteSpace(d.Type) ? null : d.Type,
            d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, 0m, d.UnitPrice, "TRY", Doc(d.Note)));
    }

    if (d.Quantity > 0)
        svc.Stock.ReceiveIn(s,
            new[] { new DepoWise.Infrastructure.Materials.StockLine(materialId, d.Quantity, d.UnitPrice > 0 ? d.UnitPrice : null) },
            // G1-05(a): istemci jetonu varsa kullanılır; yoksa eski davranış.
            string.IsNullOrWhiteSpace(d.OperationId) ? Guid.NewGuid().ToString("N") : d.OperationId!,
            d.BranchId, d.PersonnelId, d.VehicleId, Doc(d.Note),
            invoiceNo: Doc(d.InvoiceNo), orderSlipNo: Doc(d.OrderSlipNo), creditSlipNo: Doc(d.CreditSlipNo));
    return Results.Ok(new { id = materialId });
}).RequireAuthorization();

// İş #8 (2026-08-09): ÇOK malzemeli stok işlemi. Yeni istemci "lines" gönderir; eski istemci
// tek malzeme (materialId + quantity) gönderir → ikisi de aynı doğrulamadan geçer, davranış aynı kalır.
static IReadOnlyList<DepoWise.Infrastructure.Materials.StockLine> StockLines(
    List<StockLineDto>? lines, string? materialId, decimal quantity)
{
    var src = lines is { Count: > 0 }
        ? lines
        : new List<StockLineDto> { new(materialId ?? "", quantity) };
    if (src.Any(l => string.IsNullOrWhiteSpace(l.MaterialId))) throw new ArgumentException("Malzeme seçin.");
    if (src.Any(l => l.Quantity <= 0)) throw new ArgumentException("Miktar sıfırdan büyük olmalı.");
    // Aynı malzeme iki kez eklenmişse tek satırda toplanır: iki ayrı hareket yerine doğru tek hareket
    // (aksi halde bakiye doğru ama hareket defteri kullanıcıya kafa karıştırıcı görünürdü).
    return src.GroupBy(l => l.MaterialId, StringComparer.Ordinal)
        .Select(g => new DepoWise.Infrastructure.Materials.StockLine(g.Key, g.Sum(x => x.Quantity)))
        .ToList();
}

app.MapPost("/api/stock/issue", (HttpContext c, StockMoveDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.PersonnelId)) throw new ArgumentException("Personel (işlemi yapan) zorunludur."); // madde 8
    var lines = StockLines(d.Lines, d.MaterialId, d.Quantity);
    svc.Stock.IssueOut(s, lines,
        // G1-05(a): istemci jetonu varsa kullanılır; yoksa eski davranış.
        string.IsNullOrWhiteSpace(d.OperationId) ? Guid.NewGuid().ToString("N") : d.OperationId!,
        d.BranchId, d.PersonnelId, d.VehicleId, Doc(d.Note),
        invoiceNo: Doc(d.InvoiceNo), orderSlipNo: Doc(d.OrderSlipNo), creditSlipNo: Doc(d.CreditSlipNo));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapPost("/api/stock/transfer", (HttpContext c, StockTransferDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.FromBranchId) || string.IsNullOrWhiteSpace(d.ToBranchId)) throw new ArgumentException("Kaynak ve hedef şube seçin.");
    var lines = StockLines(d.Lines, d.MaterialId, d.Quantity);
    // G1-05(a): istemci jetonu varsa kullanılır; yoksa eski davranış.
    svc.Stock.Transfer(s, lines, d.FromBranchId, d.ToBranchId,
        string.IsNullOrWhiteSpace(d.OperationId) ? Guid.NewGuid().ToString("N") : d.OperationId!, Doc(d.Note),
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
app.MapGet("/api/modules", (HttpContext c, string? userId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();

    // HEDEF-KULLANICI bazlı ağaç: bir kullanıcı seçiliyse, ağaç YALNIZ o kullanıcıya gerçekten VERİLEBİLECEK
    // ekranları gösterir — verilemeyecek olanlar kilitle DEĞİL, TAMAMEN gizli (kullanıcı isteği).
    IReadOnlySet<string> roleBlocked = new HashSet<string>();
    IReadOnlyList<string> targetRoles = System.Array.Empty<string>();
    if (!string.IsNullOrWhiteSpace(userId))
    {
        roleBlocked = svc.Permissions.BlockedModulesForUser(s, userId!); // Rol Yetki Kontrol: role kapalı → gizli
        targetRoles = svc.Users.GetRoleKeys(s, userId!);
    }
    bool targetCanReceiveSuperOnly = targetRoles.Contains(RoleKeys.RestrictedSuperAdmin) || targetRoles.Contains(RoleKeys.SuperAdmin);
    bool hasTarget = !string.IsNullOrWhiteSpace(userId);

    var mods = AppModules.All
        .Where(m => AccessControl.CanGrantModule(s, m.Key)          // aktörün delegasyon tavanı
                    && !roleBlocked.Contains(m.Key)                  // hedefin rolüne kapalı → gizli
                    // Süper-admin-only ekran: hedef seçiliyse yalnız (Kısıtlı) Süper Admin'e görünür; hedef yoksa
                    // (yeni kullanıcı/şablon) süper admine devir için görünür kalır.
                    && !(hasTarget && AppModules.IsSuperAdminOnly(m.Key) && !targetCanReceiveSuperOnly))
        .Select(m => new { key = m.Key, label = m.Label, adminOnly = AppModules.IsSuperAdminOnly(m.Key), restricted = AppModules.IsAdminRestricted(m.Key) });
    return Results.Ok(mods);
}).RequireAuthorization();

// Özel buton yetkileri kataloğu (yetki ağacı buton bölümü — web parity #15). Delegasyon tavanı uygulanır.
app.MapGet("/api/buttons", (HttpContext c) =>
    S(c) is { } s
        ? Results.Ok(SpecialButtons.All.Where(b => AccessControl.CanGrantButton(s, b.Key)).Select(b => new { key = b.Key, label = b.Label }))
        : Results.Unauthorized()).RequireAuthorization();

// Çöp Kutusu — silinen master-data'yı listeler/geri yükler. Yeniden kimlik doğrulama (parola) ister.
app.MapPost("/api/trash", (HttpContext c, ReauthDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // G6-05: firma-filtresiz doğrulama (bkz. /api/admin/purge-company yanındaki açıklama).
    if (!svc.Auth.VerifyUserPassword(s.UserId, d.Password ?? ""))
        return Results.Json(new { error = "Parola hatalı." }, statusCode: 403);
    return Results.Ok(svc.Trash.List(s, reauthenticated: true));
}).RequireAuthorization();
app.MapPost("/api/trash/restore", (HttpContext c, TrashRestoreDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // G6-05: firma-filtresiz doğrulama (bkz. /api/admin/purge-company yanındaki açıklama).
    if (!svc.Auth.VerifyUserPassword(s.UserId, d.Password ?? ""))
        return Results.Json(new { error = "Parola hatalı." }, statusCode: 403);
    return Results.Ok(new { ok = Void(() => svc.Trash.Restore(s, d.Table ?? "", d.Id ?? "", reauthenticated: true)) });
}).RequireAuthorization();

// #6 — Firma Yetki Kontrol (yalnız süper admin, yalnız web): firma bazında verilebilir/verilemez modüller.
app.MapGet("/api/company-permissions/{companyId}", (HttpContext c, string companyId) =>
    S(c) is { } s ? Results.Ok(svc.CompanyGrants.GetControl(s, companyId)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/company-permissions/{companyId}", (HttpContext c, string companyId, GrantLevelDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.CompanyGrants.SetLevels(s, companyId, d.Levels ?? new())) }) : Results.Unauthorized()).RequireAuthorization();

// Rol Yetki Kontrol (yalnız süper admin): ekran x rol matrisi. Kapatılan ekran o rolde yetki ağacında
// görünmez, verilemez ve (verilmişse) oturumda düşürülür — admin bypass'ı dahil.
app.MapGet("/api/role-permissions", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var rows = svc.RoleGrants.GetControl(s).Select(r => new
    {
        moduleKey = r.ModuleKey,
        label = r.Label,
        cells = r.Cells.Select(x => new { roleKey = x.RoleKey, blocked = x.Blocked, hard = x.Hard }),
    });
    return Results.Ok(new
    {
        roles = DepoWise.Infrastructure.Organization.RoleGrantService.ManagedRoles.Select(r => new { key = r.Key, name = r.Name }),
        modules = rows,
    });
}).RequireAuthorization();

app.MapPost("/api/role-permissions", (HttpContext c, RoleGrantDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var map = (d.Blocked ?? new()).ToDictionary(
        kv => kv.Key,
        kv => (IReadOnlyList<string>)(kv.Value ?? new List<string>()),
        StringComparer.Ordinal);
    return Results.Ok(new { ok = Void(() => svc.RoleGrants.SetMatrix(s, map)) });
}).RequireAuthorization();

// ── Raporlar (firma alanı yalnız süper admin; ResolveCompany fail-closed tenant izolasyonu) ──
app.MapGet("/api/reports/company-filter", (HttpContext c) => S(c) is { } s
    ? Results.Ok(new { showCompany = s.IsSuperAdmin, showBranchSelect = AccessControl.CanUseButton(s, SpecialButtons.BranchSelect) })
    : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/reports/scope", (HttpContext c, string? companyId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var cid = DepoWise.Application.Security.TenantAccessGuard.ResolveCompanyId(s, companyId); // süper admin başka firma seçebilir; diğerleri reddedilir
    var branches = new List<object>(); var vehicles = new List<object>(); var vehicleTypes = new List<object>();
    var maintenanceDefs = new List<object>(); var technicians = new List<object>(); var suppliers = new List<object>();
    using var conn = svc.Factory.Create();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, name FROM branches WHERE company_id=@c AND is_deleted=0 ORDER BY name;";
        cmd.AddWithValue("@c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) branches.Add(new { id = r.GetString(0), name = r.GetString(1) });
    }
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT id, internal_code, COALESCE(plate,'') FROM vehicles WHERE company_id=@c AND is_deleted=0 ORDER BY internal_code;";
        cmd.AddWithValue("@c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) { var p = r.GetString(2); vehicles.Add(new { id = r.GetString(0), display = string.IsNullOrEmpty(p) ? r.GetString(1) : $"{r.GetString(1)} - {p}" }); }
    }
    using (var cmd = conn.CreateCommand())   // Araç Türü filtresi (Araç Raporu)
    {
        cmd.CommandText = "SELECT id, name FROM vehicle_types WHERE company_id=@c AND is_deleted=0 ORDER BY name;";
        cmd.AddWithValue("@c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) vehicleTypes.Add(new { id = r.GetString(0), name = r.GetString(1) });
    }
    using (var cmd = conn.CreateCommand())   // Bakım Tanımı filtresi (Bakım Raporu) — yalnız ANA tanımlar (parent_def_id NULL)
    {
        cmd.CommandText = "SELECT id, name FROM maintenance_definitions WHERE company_id=@c AND is_deleted=0 AND parent_def_id IS NULL ORDER BY name;";
        cmd.AddWithValue("@c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) maintenanceDefs.Add(new { id = r.GetString(0), name = r.GetString(1) });
    }
    using (var cmd = conn.CreateCommand())   // Teknisyen filtresi (Bakım Raporu) — personel listesi
    {
        cmd.CommandText = "SELECT id, full_name FROM personnel WHERE company_id=@c AND is_deleted=0 ORDER BY full_name;";
        cmd.AddWithValue("@c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) technicians.Add(new { id = r.GetString(0), name = r.GetString(1) });
    }
    using (var cmd = conn.CreateCommand())   // Tedarikçi filtresi (Depo Girişi)
    {
        cmd.CommandText = "SELECT id, name FROM suppliers WHERE company_id=@c AND is_deleted=0 ORDER BY name;";
        cmd.AddWithValue("@c", cid);
        using var r = cmd.ExecuteReader();
        while (r.Read()) suppliers.Add(new { id = r.GetString(0), name = r.GetString(1) });
    }
    // Talep durumları (Talep Raporu) — DB tanımı DEĞİL, sabit liste; TEK doğru kaynak Application katmanındadır
    // (masaüstü aynı listeyi doğrudan kullanır → iki platform aynı değerleri gösterir). Sorgu gerektirmez.
    var requestStatuses = DepoWise.Application.Reports.RequestStatusOptions.All
        .Select(x => new { id = x.Key, name = x.Label });
    // Not: "Talep Eden" filtresi ayrı bir liste ÇEKMEZ — yukarıdaki personel listesini (technicians) kullanır.
    return Results.Ok(new { branches, vehicles, vehicleTypes, maintenanceDefs, technicians, suppliers, requestStatuses });
}).RequireAuthorization();
// Rapor tipi → TableModel: ORTAK yürütme (ReportService.Run) — katalog dispatch + tarih varsayılanı (Bu Ay) +
// maksimum kayıt koruması. Maks satır Sistem Ayarları'ndan okunur (yoksa varsayılan).
DepoWise.Application.Reports.TableModel BuildReport(DepoWise.Application.Security.SessionContext s, string type, DepoWise.Application.Reports.ReportRequest req)
{
    var max = DepoWise.Application.Reports.ReportLimits.Resolve(k => svc.Settings.Get(s.CompanyId, k));
    return svc.Reports.Run(s, type, req, max);
}
// Yönetici raporu mu? Excel yetkisi buna göre ayrışır (katalog tek doğru kaynak).
static bool IsManagerReport(string type)
    => DepoWise.Application.Reports.ReportCatalog.ByKey(type)?.IsManager ?? false;

// Ortak rapor kataloğu (madde 2/10): web UI filtre/kolon/yetki'yi buradan sürer.
app.MapGet("/api/reports/catalog", (HttpContext c) =>
    S(c) is null ? Results.Unauthorized() : Results.Ok(DepoWise.Application.Reports.ReportCatalog.All.Select(d => new
    {
        key = d.Key, name = d.Name, description = d.Description, group = d.Group.ToString(),
        category = d.Category.ToString(), categoryLabel = DepoWise.Application.Reports.ReportCatalog.CategoryLabel(d.Category),
        usesDate = d.UsesDate, usesBranch = d.UsesBranch, usesVehicle = d.UsesVehicle,
        usesVehicleType = d.UsesVehicleType, usesMaintenanceDef = d.UsesMaintenanceDef, usesTechnician = d.UsesTechnician,
        usesSupplier = d.UsesSupplier, usesRequester = d.UsesRequester, usesStatus = d.UsesStatus,
        requiresDate = d.RequiresDate, manager = d.IsManager,
        infoNote = d.InfoNote
    }))).RequireAuthorization();

// Rapor hücresi serileştirme: NumCell → {n:ham değer, t:görüntü} (istemci sayısal davranışı korur); diğer → olduğu gibi.
static object? ReportCell(object? cell)
    => cell is DepoWise.Application.Reports.NumCell n ? new { n = n.Value, t = n.Display } : cell;

app.MapPost("/api/reports/{type}", (HttpContext c, string type, ReportReqDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var req = new DepoWise.Application.Reports.ReportRequest(true, d.FromDate, d.ToDate, d.BranchIds, d.VehicleIds, d.CompanyId, d.VehicleTypeIds, d.MaintenanceDefIds, d.TechnicianIds, d.SupplierIds, d.RequesterIds, d.Statuses);
    var tbl = BuildReport(s, type, req);
    return Results.Ok(new
    {
        title = tbl.Title,
        headers = tbl.Headers,
        numeric = tbl.Numeric,
        rows = tbl.Rows.Select(r => r.Select(ReportCell).ToArray()),
        totalRow = tbl.TotalRow?.Select(ReportCell).ToArray()
    });
}).RequireAuthorization();

// Rapor Excel dışa aktarma — özel buton yetkisi ZORUNLU (yoksa 403; UI "yetkiniz yok" gösterir).
app.MapPost("/api/reports/{type}/export", (HttpContext c, string type, ReportReqDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    AccessControl.RequireButton(s, IsManagerReport(type)
        ? SpecialButtons.ExportManagerReports : SpecialButtons.ExportReports);
    var req = new DepoWise.Application.Reports.ReportRequest(true, d.FromDate, d.ToDate, d.BranchIds, d.VehicleIds, d.CompanyId, d.VehicleTypeIds, d.MaintenanceDefIds, d.TechnicianIds, d.SupplierIds, d.RequesterIds, d.Statuses);
    var tbl = BuildReport(s, type, req);
    var bytes = svc.Excel.Export(tbl);
    var fn = System.Text.RegularExpressions.Regex.Replace(tbl.Title, @"[^\p{L}\p{Nd}]+", "_").Trim('_') + ".xlsx";
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
}).RequireAuthorization();

// ════════════════════ EXCEL İÇE AKTARIM (İş #7, 2026-08-09) ════════════════════
// Masaüstünde zaten vardı (ImportExportViewModel), web'de HİÇ YOKTU. Aynı import servisleri kullanılır →
// iki platform BİREBİR aynı doğrulamayı ve iş kurallarını uygular; yeni iş kuralı YAZILMADI.
//
// Akış masaüstüyle aynı: şablon indir → dosya seç → ÖN KONTROL (dry-run, hiç yazmaz) → onay → aktar.
// Hedef şube ZORUNLU (masaüstü kuralı 2026-07-26): işlem kayıtları bu şubeyle etiketlenir.
// "__all__" → Tüm Şubeler (firma geneli, şubesiz).

/// <summary>Web'in sabit kodlamaması için: içe aktarılabilir kayıt türleri (masaüstüyle aynı liste).</summary>
app.MapGet("/api/import/entities", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    AccessControl.Require(s, "import_export", PermissionAction.View);
    return Results.Ok(ImportEntityKeys().Select(k => new { key = k, label = ImportEntityLabel(k) }));
}).RequireAuthorization();

app.MapGet("/api/import/{entity}/template", (HttpContext c, string entity) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    AccessControl.Require(s, "import_export", PermissionAction.View);
    var headers = ImportHeaders(svc, entity);
    var label = ImportEntityLabel(entity);
    var bytes = svc.Excel.Template(label + " Şablon", headers);
    var fn = System.Text.RegularExpressions.Regex.Replace(label, @"[^\p{L}\p{Nd}]+", "_").Trim('_') + "_sablon.xlsx";
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
}).RequireAuthorization();

// ÖN KONTROL: veritabanına HİÇBİR ŞEY yazmaz. Kullanıcı hatalı satırları aktarımdan ÖNCE görür.
app.MapPost("/api/import/{entity}/preview", async (HttpContext ctx, string entity) =>
{
    var (s, rows, err) = await ReadImportAsync(ctx, svc, entity);
    if (err is not null) return err;
    var res = ImportDryRun(svc, entity, s!, rows!);
    return Results.Ok(ImportPayload(res, System.Array.Empty<string>()));
}).RequireAuthorization();

app.MapPost("/api/import/{entity}/commit", async (HttpContext ctx, string entity) =>
{
    var (s, rows, err) = await ReadImportAsync(ctx, svc, entity);
    if (err is not null) return err;
    var (res, created) = ImportCommit(svc, entity, s!, rows!);
    return Results.Ok(ImportPayload(res, created));
}).RequireAuthorization();

// ── İçe aktarım yardımcıları (yukarıdaki 4 uç bunları kullanır) ──

/// <summary>
/// Yüklenen .xlsx'i okur ve içe aktarım oturumunu kurar. Ortak kapı: yetki, dosya, boyut ve
/// HEDEF ŞUBE doğrulaması tek yerde yapılır (önizleme ile aktarım arasında fark olmasın).
///
/// Şube: form alanı <c>branchId</c>. Boş/"__all__" → Tüm Şubeler (firma geneli, şubesiz).
/// Aksi halde şube kullanıcının KAPSAMINDA olmalı (fail-closed; ScopeResolver).
/// </summary>
async Task<(SessionContext? Session, IReadOnlyList<DepoWise.Application.Reports.ImportRow>? Rows, IResult? Error)>
    ReadImportAsync(HttpContext ctx, ServerServices services, string entity)
{
    var s = Session(ctx);
    if (s is null) return (null, null, Results.Unauthorized());
    AccessControl.Require(s, "import_export", PermissionAction.View);
    ImportEntityLabel(entity);   // bilinmeyen tür → 400 (ArgumentException, ortak hata katmanı çevirir)

    if (!ctx.Request.HasFormContentType)
        return (null, null, Results.Json(new { error = "Excel dosyası gönderilmedi." }, statusCode: 400));
    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return (null, null, Results.Json(new { error = "Excel dosyası gönderilmedi." }, statusCode: 400));
    const long MaxBytes = 20 * 1024 * 1024;   // 20 MB — şablon tabanlı liste dosyaları için fazlasıyla yeterli
    if (file.Length > MaxBytes)
        return (null, null, Results.Json(new { error = "Dosya çok büyük (en fazla 20 MB)." }, statusCode: 400));

    // Hedef şube ZORUNLU seçilir; masaüstündeki kuralın aynısı (2026-07-26).
    var rawBranch = form["branchId"].ToString();
    if (string.IsNullOrWhiteSpace(rawBranch))
        return (null, null, Results.Json(new { error = "Lütfen önce içe aktarılacak ŞUBEYİ seçin (zorunlu). Tüm şubelerde görünmesi için 'Tüm Şubeler' seçin." }, statusCode: 400));
    var branchId = rawBranch == "__all__" ? null : rawBranch;
    services.Scopes.EnsureBranchAllowed(s, branchId);   // kapsam dışı şube → 403

    using var ms = new MemoryStream();
    await file.OpenReadStream().CopyToAsync(ms, ctx.RequestAborted);
    IReadOnlyList<DepoWise.Application.Reports.ImportRow> rows;
    try { rows = services.Excel.ReadRows(ms.ToArray()); }
    catch { return (null, null, Results.Json(new { error = "Dosya okunamadı. Geçerli bir .xlsx dosyası seçin." }, statusCode: 400)); }
    if (rows.Count == 0)
        return (null, null, Results.Json(new { error = "Dosyada veri satırı bulunamadı." }, statusCode: 400));

    // Seçilen şubeyle oturum kopyası — masaüstündeki ImportSession ile birebir aynı.
    var importSession = new SessionContext(s.UserId, s.CompanyId, s.RoleKeys, s.Permissions, s.CanViewAllBranches)
    {
        OperatingBranchId = branchId,
        BlockedModules = s.BlockedModules,
    };
    return (importSession, rows, null);
}

static string[] ImportEntityKeys() => new[]
    { "materials", "vehicles", "personnel", "maintenance", "inspection", "fuel", "fuel-depot" };

static string ImportEntityLabel(string key) => key switch
{
    "vehicles" => "Araçlar",
    "personnel" => "Personel",
    "maintenance" => "Bakım",
    "inspection" => "Muayene / Sigorta",
    "fuel" => "Yakıt Dağıtım",
    "fuel-depot" => "Yakıt Depo Girişi",
    "materials" => "Malzemeler",
    _ => throw new ArgumentException($"Bilinmeyen içe aktarım türü: {key}"),
};

static IReadOnlyList<string> ImportHeaders(ServerServices svc, string entity) => entity switch
{
    "vehicles" => svc.VehicleImport.SampleHeaders(),
    "personnel" => svc.PersonnelImport.SampleHeaders(),
    "maintenance" => svc.MaintenanceImport.SampleHeaders(),
    "inspection" => svc.InspectionImport.SampleHeaders(),
    "fuel" => svc.FuelImport.SampleHeaders(),
    "fuel-depot" => svc.FuelDepotImport.SampleHeaders(),
    "materials" => svc.MaterialImport.SampleHeaders(),
    _ => throw new ArgumentException($"Bilinmeyen içe aktarım türü: {entity}"),
};

static DepoWise.Application.Reports.ImportResult ImportDryRun(ServerServices svc, string entity,
    SessionContext s, IReadOnlyList<DepoWise.Application.Reports.ImportRow> rows) => entity switch
{
    "vehicles" => svc.VehicleImport.DryRun(s, rows),
    "personnel" => svc.PersonnelImport.DryRun(s, rows),
    "maintenance" => svc.MaintenanceImport.DryRun(s, rows),
    "inspection" => svc.InspectionImport.DryRun(s, rows),
    "fuel" => svc.FuelImport.DryRun(s, rows),
    "fuel-depot" => svc.FuelDepotImport.DryRun(s, rows),
    "materials" => svc.MaterialImport.DryRun(s, rows),
    _ => throw new ArgumentException($"Bilinmeyen içe aktarım türü: {entity}"),
};

/// <summary>Masaüstündeki switch ile BİREBİR aynı: hangi servisin oluşturduğu yeni tanımları raporladığı dahil.</summary>
static (DepoWise.Application.Reports.ImportResult Result, IReadOnlyList<string> CreatedLookups) ImportCommit(
    ServerServices svc, string entity, SessionContext s, IReadOnlyList<DepoWise.Application.Reports.ImportRow> rows) => entity switch
{
    "vehicles" => svc.VehicleImport.CommitWithLookups(s, rows),
    "personnel" => svc.PersonnelImport.CommitWithLookups(s, rows),
    "maintenance" => svc.MaintenanceImport.CommitWithLookups(s, rows),
    "inspection" => (svc.InspectionImport.Commit(s, rows), System.Array.Empty<string>()),
    "fuel" => (svc.FuelImport.Commit(s, rows), System.Array.Empty<string>()),
    "fuel-depot" => (svc.FuelDepotImport.Commit(s, rows), System.Array.Empty<string>()),
    "materials" => svc.MaterialImport.CommitWithLookups(s, rows),
    _ => throw new ArgumentException($"Bilinmeyen içe aktarım türü: {entity}"),
};

static object ImportPayload(DepoWise.Application.Reports.ImportResult r, IReadOnlyList<string> created) => new
{
    dryRun = r.DryRun, total = r.Total, valid = r.Valid, added = r.Added, updated = r.Updated, failed = r.Failed,
    errors = r.Errors.Select(e => new { rowNumber = e.RowNumber, message = e.Message }),
    createdLookups = created,
};

// ── Bakım (Bakım Takibi) — masaüstüyle birebir ──
app.MapGet("/api/maintenance/definitions", (HttpContext c, string? parentDefId) =>
    S(c) is { } s ? Results.Ok(svc.MaintenanceDefinitions.List(s, parentDefId)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/maintenance/definitions", (HttpContext c, MaintDefDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.MaintenanceDefinitions.Create(s,
        new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition(d.Name, d.IntervalValue, string.IsNullOrWhiteSpace(d.IntervalUnit) ? "km" : d.IntervalUnit, d.ParentDefId, d.Description), d.VehicleIds) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/maintenance", (HttpContext c, MaintenanceDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var mats = d.Materials?.Select(m => new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(m.MaterialId, m.Quantity, m.FromTeamStock)).ToList();
    var id = svc.Maintenance.Save(s, new DepoWise.Infrastructure.Maintenance.NewMaintenance(
        d.VehicleId, d.DefinitionId, d.SubDefinitionId, d.TechnicianId, Doc(d.Description), Doc(d.SubDefinitionNote),
        d.PerformedKm, d.PerformedHour, d.PerformedDate, mats), Guid.NewGuid().ToString("N"));
    return Results.Ok(new { id });
}).RequireAuthorization();
// İş #5 (2026-08-09): bakım kaydının YAN ETKİSİZ alanları (açıklama/not/teknisyen). Malzeme ve sayaç
// alanları BİLİNÇLİ olarak burada DEĞİL — onlar için iptal + yeniden oluştur yolu kullanılır.
app.MapPut("/api/maintenance/{id}/metadata", (HttpContext c, string id, MaintenanceMetaDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Maintenance.UpdateMetadata(s, id, d.Description, d.SubDefinitionNote, d.TechnicianId, d.Version)) }) : Results.Unauthorized()).RequireAuthorization();
// İş #5: günlük faaliyetin YAN ETKİSİZ alanları (açıklama/operatör/süre).
app.MapPut("/api/daily/{id}/metadata", (HttpContext c, string id, DailyMetaDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.DailyActivity.UpdateMetadata(s, id, d.Description, d.OperatorId, d.DurationDays, d.Version)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/maintenance/cancel", (HttpContext c, IdReasonDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Maintenance.Cancel(s, d.Id, string.IsNullOrWhiteSpace(d.Reason) ? "Kullanıcı iptali" : d.Reason)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/maintenance/definitions/{id}", (HttpContext c, string id, MaintDefDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.MaintenanceDefinitions.Update(s, id, new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition(
        d.Name, d.IntervalValue, string.IsNullOrWhiteSpace(d.IntervalUnit) ? "km" : d.IntervalUnit, d.ParentDefId, d.Description),
        d.Version is > 0 ? d.Version : null); // B-1: 0/null = sürüm bilinmiyor → kilit kontrolü yok
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
// B-5 (PRT-01 Grup 5): muayene/sigorta belgesi İPTALİ — fiziksel silme YOK (is_deleted=1 + gerekçe + audit).
// Gerekçe boşsa servis ArgumentException atar → ortak middleware 400 döndürür (yakıt/talep iptali deseni).
app.MapPost("/api/inspection/{id}/cancel", (HttpContext c, string id, InspectionCancelDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Inspection.Cancel(s, id, d?.Reason ?? "", d?.Version is > 0 ? d.Version : null)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Yakıt ──
app.MapGet("/api/fuel/depot", (HttpContext c, bool? includeCancelled) => S(c) is { } s ? Results.Ok(svc.Fuel.ListDepotEntries(s, 200, includeCancelled == true)) : Results.Unauthorized()).RequireAuthorization();
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
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.PersonnelId)) throw new ArgumentException("Yakıt dağıtımında personel (işlemi yapan) zorunludur."); // madde 8
    return Results.Ok(new { id = svc.Fuel.Distribute(s, new DepoWise.Infrastructure.Operations.NewDistribution(
        d.VehicleId, d.Liters, d.CurrentMeter, d.UnitPrice, "TRY", d.PersonnelId, d.DistributionDate, Doc(d.Note),
        RecipientPersonnelId: Doc(d.RecipientPersonnelId), PrevMeter: d.PrevMeter), Guid.NewGuid().ToString("N")) });
}).RequireAuthorization();
// ── Yakıt kaydı İPTALİ (kullanıcı kararları Y1–Y5, 2026-08-09) — ortak FuelService; sayaç GERİ ALINMAZ ──
app.MapPost("/api/fuel/{id}/cancel", (HttpContext c, string id, FuelCancelDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // B-4 (PRT-01 Grup 3, 2026-08-10): sabit "Kullanıcı iptali" yedeği KALDIRILDI. FuelService gerekçeyi zaten
    // ZORUNLU tutuyor; buradaki yedek o kuralı eziyor ve denetim kaydına kullanıcının yazmadığı bir gerekçe
    // yazıyordu (gerçek gerekçeden ayırt edilemez). Boş gelirse servis ArgumentException atar → ortak hata
    // middleware'i 400 + {"error":"İptal gerekçesi zorunlu."} döndürür.
    svc.Fuel.CancelDistribution(s, id, d?.Reason ?? "");
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapPost("/api/fuel/depot/{id}/cancel", (HttpContext c, string id, FuelCancelDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    svc.Fuel.CancelDepotEntry(s, id, d?.Reason ?? ""); // B-4: yedek kaldırıldı (bkz. /api/fuel/{id}/cancel)
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// Düzeltme akışı (Y2): iptal edilen dağıtımın BAŞLANGIÇ SAYACI — yeni kayda taşınır.
app.MapGet("/api/fuel/{id}/prev-meter", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { prevMeter = svc.Fuel.GetCancelledPrevMeter(s, id) }) : Results.Unauthorized()).RequireAuthorization();
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
    var mats = d.Materials?.Select(m => new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(m.MaterialId, m.Quantity, m.FromTeamStock)).ToList();
    var id = svc.DailyActivity.SaveMaintenanceActivity(s, new DepoWise.Infrastructure.Maintenance.NewMaintenance(
        d.VehicleId, d.DefinitionId, d.SubDefinitionId, d.TechnicianId, Doc(d.Description), Doc(d.SubDefinitionNote),
        d.PerformedKm, d.PerformedHour, d.PerformedDate, mats), Guid.NewGuid().ToString("N"));
    return Results.Ok(new { id });
}).RequireAuthorization();
// "İlave Yağ/İlave Filtre/Tamir" (kullanıcı isteği 2026-07-19, ADR-091) — Bakım ile AYNI mekanizma, Bakım
// Tanımı/Alt Bakım kullanıcıya sorulmaz (DailyActivityService otomatik sabit tanım kullanır).
app.MapPost("/api/daily/extra", (HttpContext c, ExtraActivityDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!DepoWise.Infrastructure.Operations.ExtraActivityTypes.IsValid(d.Type))
        return Results.Json(new { error = "Geçersiz kayıt tipi." }, statusCode: 400);
    var mats = d.Materials?.Select(m => new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(m.MaterialId, m.Quantity, m.FromTeamStock)).ToList();
    var id = svc.DailyActivity.SaveExtraActivity(s, d.Type, new DepoWise.Infrastructure.Maintenance.NewMaintenance(
        d.VehicleId, "", null, d.TechnicianId, Doc(d.Description), null,
        d.PerformedKm, d.PerformedHour, d.PerformedDate, mats), Guid.NewGuid().ToString("N"));
    return Results.Ok(new { id });
}).RequireAuthorization();
// İptal ONAYI için etki özeti (bağlı bakım + malzeme satırı) — salt-okuma.
app.MapGet("/api/daily/{id}/cancel-impact", (HttpContext c, string id) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var (hasMaintenance, materialLines, totalQuantity) = svc.DailyActivity.GetCancelImpact(s, id);
    return Results.Ok(new { hasMaintenance, materialLines, totalQuantity });
}).RequireAuthorization();
// İPTAL: faaliyet + bağlı bakım + stok TEK atomik işlemde (kullanıcı kararı K1).
app.MapDelete("/api/daily/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.DailyActivity.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Araçlar (ekle/sil) ──
app.MapPost("/api/vehicles", (HttpContext c, NewVehicleDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    RequireVehicleFields(d.BranchId, d.ProductionYear); // madde 8+1: şube zorunlu + makul yıl
    return Results.Ok(new { id = svc.Vehicles.Create(s, new DepoWise.Infrastructure.Vehicles.NewVehicle(
        d.InternalCode, Doc(d.Plate), d.ProductionYear, d.CurrentMeter, string.IsNullOrWhiteSpace(d.MeterUnit) ? "km" : d.MeterUnit,
        d.BranchId, d.DriverPersonnelId, Doc(d.ChassisNo), Doc(d.EngineNo), string.IsNullOrWhiteSpace(d.Status) ? "active" : d.Status, Doc(d.StatusNote),
        d.VehicleTypeId, d.CategoryId, d.BrandId, d.VehicleModelId, d.TemplateId)) });
}).RequireAuthorization();
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
// İşlem Geçmişi (madde 4, 2026-08-06): oluşturma/şube transferi/genel güncelleme + sayaç değişimi — salt okuma.
app.MapGet("/api/vehicles/{id}/history", (HttpContext c, string id, int? take) =>
    S(c) is { } s ? Results.Ok(svc.Vehicles.RecentHistory(s, id, take is > 0 ? take.Value : 100)) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/vehicles/{id}", (HttpContext c, string id, NewVehicleDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    RequireVehicleFields(d.BranchId, d.ProductionYear); // madde 8+1
    svc.Vehicles.Update(s, id, new DepoWise.Infrastructure.Vehicles.UpdateVehicle(
        Doc(d.Plate), d.ProductionYear, string.IsNullOrWhiteSpace(d.Status) ? "active" : d.Status, Doc(d.StatusNote),
        Doc(d.ChassisNo), Doc(d.EngineNo), d.VehicleTypeId, d.CategoryId, d.BrandId, d.VehicleModelId, d.BranchId, d.DriverPersonnelId,
        TemplateId: d.TemplateId),
        expectedVersion: d.Version); // düzenleme kilidi
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// Aracın YALNIZ durumunu değiştirir (bakım ekranından "arızalı" işaretlemek için).
// PUT /api/vehicles/{id} KULLANILMAZ: o tüm alanları yazar → bakım ekranından çağrılsa araç kartının
// doldurulmamış alanlarını (marka/model/şube…) NULL'a çekerdi.
app.MapPost("/api/vehicles/{id}/status", (HttpContext c, string id, VehicleStatusDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var code = DepoWise.Application.Ui.VehicleStatus.Parse(d.Status);
    if (code is null) return Results.Json(new { error = $"Geçersiz araç durumu: {d.Status}" }, statusCode: 400);
    svc.Vehicles.SetStatus(s, id, code, Doc(d.StatusNote));
    return Results.Ok(new { ok = true, status = code });
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

// ── Malzeme şablonları (yeni-kayıt ön ayarı; oluşturan-bazlı görünürlük) ──
app.MapGet("/api/material-templates", (HttpContext c, string? search) =>
    S(c) is { } s ? Results.Ok(svc.MaterialTemplates.List(s, search)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/material-templates/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.MaterialTemplates.Get(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/material-templates", (HttpContext c, MaterialTemplateDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.MaterialTemplates.Create(s, new DepoWise.Infrastructure.Materials.NewMaterialTemplate(
        d.Name, Doc(d.Code), Doc(d.Type), d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, d.Currency ?? "TRY", Doc(d.Description), Doc(d.CompatibleVehicleIds))) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/material-templates/{id}", (HttpContext c, string id, MaterialTemplateDto d) =>
    // KLT-01d: sürüm gönderilmediyse (eski istemci) kontrol yapılmaz — geriye uyumlu.
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.MaterialTemplates.Update(s, id, new DepoWise.Infrastructure.Materials.NewMaterialTemplate(
        d.Name, Doc(d.Code), Doc(d.Type), d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, d.Currency ?? "TRY", Doc(d.Description), Doc(d.CompatibleVehicleIds)),
        d.Version > 0 ? d.Version : null)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/material-templates/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.MaterialTemplates.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
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
    var h = svc.Requests.Create(s, new DepoWise.Infrastructure.Requests.NewRequest(items, d.BranchId, d.RequesterId, d.WarehouseId, d.ApproverId, Doc(d.Description), d.RequestDate, d.SubmitImmediately, DepoWise.Application.Requests.RequestPriorityInfo.FromDb(d.Priority)));
    return Results.Ok(new { id = h.Id, docNo = h.DocNo });
}).RequireAuthorization();
app.MapGet("/api/requests/{id}/edit", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.Requests.GetForEdit(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/requests/{id}", (HttpContext c, string id, RequestDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var items = (d.Items ?? new()).Select(i => new DepoWise.Infrastructure.Requests.RequestItemInput(i.MaterialId, i.Quantity, i.VehicleId, Doc(i.Note))).ToList();
    svc.Requests.Update(s, id, new DepoWise.Infrastructure.Requests.NewRequest(items, d.BranchId, d.RequesterId, d.WarehouseId, d.ApproverId, Doc(d.Description), d.RequestDate, d.SubmitImmediately, DepoWise.Application.Requests.RequestPriorityInfo.FromDb(d.Priority)), expectedVersion: d.Version);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
app.MapPost("/api/requests/{id}/approve", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Approve(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
// B-3 (PRT-01 Grup 4, 2026-08-10): sabit "Reddedildi" yedeği KALDIRILDI. RequestService.Reject gerekçeyi
// ZATEN zorunlu tutuyor; yedek o kuralı eziyor ve denetim kaydına kullanıcının YAZMADIĞI bir gerekçe
// yazıyordu (gerçek gerekçeden ayırt edilemez). Boş gelirse servis ArgumentException atar → 400.
app.MapPost("/api/requests/{id}/reject", (HttpContext c, string id, IdReasonDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Reject(s, id, d?.Reason ?? "")) }) : Results.Unauthorized()).RequireAuthorization();
// B-4 (PRT-01 Grup 4): iptal gerekçesi artık BOŞ olamaz. Kontrol BURADA yapılır, servis imzası
// DEĞİŞTİRİLMEZ — Cancel(reason = null) hâlâ geçerli (masaüstü ve testler doğrudan çağırıyor,
// kullanıcı kararı: "servis seviyesinde zorunlu hale getirme"). Ret ucunun aksine servis tarafında
// kural olmadığı için 400'ü uç üretir (aynı dosyadaki /request-ops/{id}/status deseni).
app.MapPost("/api/requests/{id}/cancel", (HttpContext c, string id, IdReasonDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d?.Reason)) return Results.Json(new { error = "İptal gerekçesi zorunlu." }, statusCode: 400);
    svc.Requests.Cancel(s, id, d!.Reason!.Trim());
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// ── Talep Operasyonları (Faz 2) — onaylı taleplerin operasyon süreci. Stok DEĞİŞTİRİLMEZ. ──
app.MapGet("/api/request-ops", (HttpContext c, string? status) =>
    S(c) is { } s ? Results.Ok(svc.RequestOps.List(s, string.IsNullOrWhiteSpace(status) ? null : status)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/request-ops/{id}/next-states", (HttpContext c, string id) =>
    S(c) is { } s
        ? Results.Ok(svc.RequestOps.AllowedNextStates(s, id).Select(x => new
        {
            key = DepoWise.Application.Requests.RequestOperationStatusInfo.ToDb(x),
            label = DepoWise.Application.Requests.RequestOperationStatusInfo.Label(x),
            color = DepoWise.Application.Requests.RequestOperationStatusInfo.Color(x),
        }))
        : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/request-ops/{id}/history", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(svc.RequestOps.GetHistory(s, id)) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/request-ops/{id}/status", (HttpContext c, string id, RequestOpsStatusDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var to = DepoWise.Application.Requests.RequestOperationStatusInfo.FromDb(d.Status);
    if (to is null) return Results.Json(new { error = "Geçersiz operasyon durumu." }, statusCode: 400);
    // KLT-01a: version yalnız updateBranches=true iken kullanılır (gönderim alanları); durum geçişi
    // zaten durum makinesiyle korunur. Gönderilmezse (eski istemci) kontrol yapılmaz.
    return Results.Ok(new { ok = Void(() => svc.RequestOps.ChangeStatus(s, id, to.Value, d.Note, d.FromBranchId, d.ToBranchId, d.UpdateBranches,
        d.Version > 0 ? d.Version : null)) });
}).RequireAuthorization();
app.MapPut("/api/request-ops/{id}/shipment", (HttpContext c, string id, RequestOpsShipmentDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.RequestOps.UpdateShipmentInfo(s, id, d.FromBranchId, d.ToBranchId, d.Note,
        d.Version > 0 ? d.Version : null)) }) : Results.Unauthorized()).RequireAuthorization();

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
    var rows = svc.Requests.GetHistory(s, id).Select(h =>
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
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Branches.Update(s, id, new DepoWise.Infrastructure.Organization.NewBranch(d.Name, string.IsNullOrWhiteSpace(d.Kind) ? "branch" : d.Kind!, d.ParentId, Doc(d.Code), Doc(d.Password)), d.CompanyId, d.Version)) }) : Results.Unauthorized()).RequireAuthorization();
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
// Şifre SIFIRLAMA (2026-07-25): admin sıfırlar → geçici şifre = kullanıcı adı, kullanıcı ilk girişte kendi belirler.
app.MapPost("/api/users/{id}/reset-password", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { tempPassword = svc.Users.ResetPassword(s, id) }) : Results.Unauthorized()).RequireAuthorization();
app.MapDelete("/api/users/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.DeleteUser(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users/{id}/branch", (HttpContext c, string id, IdDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Branches.AssignUser(s, id, string.IsNullOrWhiteSpace(d.Id) ? null : d.Id)) }) : Results.Unauthorized()).RequireAuthorization();
// "Tüm Şubeler" yetkisi — YALNIZ süper admin belirler.
app.MapPost("/api/users/{id}/all-branches", (HttpContext c, string id, ActiveDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Users.SetViewAllBranches(s, id, d.Active)) }) : Results.Unauthorized()).RequireAuthorization();
// Kota izleme (kullanıcı + admin kullanımı).
app.MapGet("/api/quota-monitor", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var online = ServerPresence.OnlineByCompany(); // #4: firma başına anlık online kullanıcı
    var rows = svc.Users.GetQuotaMonitor(s).Select(r =>
    {
        var n = online.TryGetValue(r.CompanyId, out var v) ? v : 0;
        return new
        {
            r.CompanyId, r.CompanyName,
            r.UserText, r.ActiveText, r.AdminText, r.UserFull, r.AdminFull,
            onlineCount = n,
            onlineText = n > 0 ? $"{n} online" : "—",
        };
    });
    return Results.Ok(rows);
}).RequireAuthorization();

// ── Yetkiler (kullanıcı bazlı modül matrisi) ──
app.MapGet("/api/permissions/{userId}", (HttpContext c, string userId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var data = svc.Permissions.GetForUser(s, userId);
    // version: KLT-01c düzenleme kilidi jetonu — istemci kaydederken geri gönderir.
    return Results.Ok(new { modules = data.Modules, buttons = data.Buttons, version = data.Version });
}).RequireAuthorization();
app.MapPost("/api/permissions/{userId}", (HttpContext c, string userId, PermSaveDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var mods = (d.Modules ?? new()).Select(m => new ModulePermission(m.ModuleKey, m.CanView, m.CanCreate, m.CanEdit, m.CanDelete));
    // KLT-01c: sürüm gönderilmediyse (eski istemci) kontrol yapılmaz — geriye uyumlu.
    svc.Permissions.SaveForUser(s, userId, mods, d.Buttons ?? new(), d.Version > 0 ? d.Version : null);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── Yetki Şablonları ──
// Şablon YÖNETİM listesi (süper admin — tüm firmalar + kapsam).
app.MapGet("/api/permission-templates", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.PermissionTemplates.List(s)) : Results.Unauthorized()).RequireAuthorization();
// Kullanıcı OLUŞTURMA için görünür şablonlar (kendi firması + tüm-firma; users/Create yetkisi).
app.MapGet("/api/permission-templates/for-user", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.PermissionTemplates.ListForUserCreation(s)) : Results.Unauthorized()).RequireAuthorization();
// Şablon ağacı: SEÇİLEN firmanın admine açık modülleri (companyId boş → tüm firmalar için derleme admine-açık set).
app.MapGet("/api/permission-templates/modules", (HttpContext c, string? companyId) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    IEnumerable<(string Key, string Label, bool Restricted)> rows;
    if (string.IsNullOrWhiteSpace(companyId)) // tüm firmalar
        rows = AppModules.All.Where(m => !AppModules.IsPublic(m.Key) && !AppModules.IsSuperAdminOnly(m.Key))
            .Select(m => (m.Key, m.Label, AppModules.IsAdminRestricted(m.Key)));
    else // seçilen firma — "Süper Admin" düzeyine alınan ekranlar admine açık değildir
        rows = svc.CompanyGrants.GetControl(s, companyId)
            .Where(r => r.Level != DepoWise.Infrastructure.Organization.CompanyGrantService.LevelSuper)
            .Select(r => (r.ModuleKey, r.Label, r.Level == DepoWise.Infrastructure.Organization.CompanyGrantService.LevelAdmin));
    return Results.Ok(rows.Select(r => new { key = r.Key, label = r.Label, adminOnly = false, restricted = r.Restricted }));
}).RequireAuthorization();
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
    var id = svc.PermissionTemplates.Create(s, d.Name, d.RoleKey, mods, d.Buttons ?? new(), d.CompanyId, d.ScopeAll);
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapDelete("/api/permission-templates/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.PermissionTemplates.Delete(s, id)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Sistem Logu (audit) — Tarih Aralığı + kayıt sayısı (madde 4, kullanıcı isteği 2026-08-06) ──
app.MapGet("/api/audit", (HttpContext c, long? from, long? to, int? limit) =>
    S(c) is { } s ? Results.Ok(svc.AuditLog.List(s, from, to, limit ?? 300)) : Results.Unauthorized()).RequireAuthorization();

// ── Doğrudan stok değişikliği uyarı logu (madde 1.4/1.5, kullanıcı isteği 2026-08-06) ──
// POST: Malzeme kartından doğrudan stok değişimi kararı (uyarı gösterildikten sonra). continued=true →
// stok SAYIM/DÜZELTME (adjustment) ile güncellenir + loglanır; false → yalnız log (iptal).
app.MapPost("/api/stock/change-log", (HttpContext c, StockChangeLogDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(d.MaterialId)) throw new ArgumentException("Malzeme seçin.");
    svc.StockChangeLog.Record(s, d.MaterialId, d.NewQuantity, d.Continued, Doc(d.WarningText));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
// GET: log görüntüleme (Tarih Aralığı + kayıt sayısı). Yetki: module stock_change_log.
app.MapGet("/api/stock/change-log", (HttpContext c, long? from, long? to, int? limit) =>
    S(c) is { } s ? Results.Ok(svc.StockChangeLog.List(s, from, to, limit ?? 300)) : Results.Unauthorized()).RequireAuthorization();

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
    var path = Path.Combine(dataDir, "setup", "AlpnexSetup.exe");
    return File.Exists(path)
        ? Results.File(path, "application/octet-stream", "AlpnexSetup.exe")
        : Results.NotFound(new { error = "Kurulum aracı henüz yüklenmedi." });
});
app.MapPost("/api/setup", async (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files["file"]; if (file is null) return Results.BadRequest(new { error = "file yok" });
    var dir = Path.Combine(dataDir, "setup"); Directory.CreateDirectory(dir);
    await using var fs = File.Create(Path.Combine(dir, "AlpnexSetup.exe"));
    await file.OpenReadStream().CopyToAsync(fs, ctx.RequestAborted);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.MapGet("/api/releases/{version}/download", (string version) =>
{
    var path = svc.ReleasePackages.PathFor(version);
    return path is null ? Results.NotFound() : Results.File(path, "application/octet-stream", Path.GetFileName(path));
});

// ── Güncelleme paketleri (disk) — canlı sunucu ekranı: listele + MANUEL sil (süper admin) ──
app.MapGet("/api/releases/packages", (HttpContext ctx) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    var latest = svc.Releases.Latest()?.Version;
    var rows = svc.ReleasePackages.ListPackages().Select(p => new
    {
        version = p.Version, fileName = p.FileName, sizeBytes = p.SizeBytes,
        sizeMb = Math.Round(p.SizeBytes / (1024d * 1024d), 1),
        modifiedUtc = p.ModifiedUtc, isLatest = string.Equals(p.Version, latest, StringComparison.Ordinal),
    });
    return Results.Ok(rows);
}).RequireAuthorization();
app.MapDelete("/api/releases/packages/{version}", (HttpContext ctx, string version) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    if (!s.IsSuperAdmin) return Results.Json(new { error = "Yalnız süper admin." }, statusCode: 403);
    // En güncel sürümü silmeyi engelle (masaüstü güncelleyicisi onu indirir → kırılır).
    if (string.Equals(version, svc.Releases.Latest()?.Version, StringComparison.Ordinal))
        return Results.Json(new { error = "En güncel sürümün paketi silinemez." }, statusCode: 400);
    return svc.ReleasePackages.Delete(version)
        ? Results.Ok(new { ok = true })
        : Results.Json(new { error = "Paket bulunamadı." }, statusCode: 404);
}).RequireAuthorization();

// ── Sunucu yedek (bulut) ──
app.MapPost("/api/backups", async (HttpRequest req) =>
{
    if (DeviceToken(req) is null) return Results.Unauthorized();
    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file is null) return Results.BadRequest(new { error = "file yok" });
    await using var fs = file.OpenReadStream();
    await svc.Backups.SaveAsync(form["company"].ToString(), form["machine"].ToString(), form["filename"].ToString(), fs, req.HttpContext.RequestAborted);
    // Bakım (6 saatte bir): tamamlanan ayları zip'le + ham dosyaları sil + 3 yılı aşanları buda + disk koruması.
    svc.MachineBackups.RunMaintenanceThrottled();
    return Results.Ok(new { ok = true });
});

// ── Makine Yedekleri ekranı (süper admin): özet + aylık arşivler + indirme + elle bakım ──
app.MapGet("/api/machine-backups", (HttpContext c) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    AccessControl.Require(s, "machine_backups", PermissionAction.View);
    // Makine bilgisi (firma/şube/durum/IP) ile yedek özetini birleştir. Eşleşme: makine adı = device_name.
    var devices = svc.Enrollment.ListDevices(s).ToList();
    var sums = svc.MachineBackups.Summaries();
    // Tenant: süper admin değilse yalnız kendi firması.
    var rows = sums
        .Where(b => s.IsSuperAdmin || string.Equals(b.CompanyId, s.CompanyId, StringComparison.Ordinal))
        .Select(b =>
        {
            var d = devices.FirstOrDefault(x =>
                string.Equals(x.CompanyId, b.CompanyId, StringComparison.Ordinal) &&
                string.Equals(x.Name, b.Machine, StringComparison.OrdinalIgnoreCase));
            return new
            {
                b.CompanyId, b.Machine, b.DailyCount, b.DailyBytes, b.ArchiveCount, b.ArchiveBytes, b.TotalBytes,
                lastBackup = b.LastBackup,
                companyName = string.IsNullOrEmpty(d?.CompanyName) ? b.CompanyId : d!.CompanyName,
                branchName = string.IsNullOrEmpty(d?.BranchName) ? "—" : d!.BranchName,
                status = d?.Status ?? "—",
                ip = string.IsNullOrEmpty(d?.Ip) ? "—" : d!.Ip,
                lastSeenAt = d?.LastSeenAt,
                known = d is not null,   // sunucuda yedeği var ama makine kaydı yoksa false
            };
        })
        .OrderByDescending(x => x.lastBackup)
        .ToList();
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/machine-backups/detail", (HttpContext c, string company, string machine) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    AccessControl.Require(s, "machine_backups", PermissionAction.View);
    if (!s.IsSuperAdmin && !string.Equals(company, s.CompanyId, StringComparison.Ordinal))
        return Results.Json(new { error = "Başka firmaya ait." }, statusCode: 403);
    return Results.Ok(new
    {
        archives = svc.MachineBackups.ListArchives(company, machine),   // aylık zip'ler (3 yıl)
        daily = svc.MachineBackups.ListDaily(company, machine),         // henüz arşivlenmemiş (bu ay)
    });
}).RequireAuthorization();

app.MapGet("/api/machine-backups/download", (HttpContext c, string company, string machine, string name) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    AccessControl.Require(s, "machine_backups", PermissionAction.View);
    if (!s.IsSuperAdmin && !string.Equals(company, s.CompanyId, StringComparison.Ordinal))
        return Results.Json(new { error = "Başka firmaya ait." }, statusCode: 403);
    var p = svc.MachineBackups.ResolveArchive(company, machine, name);
    if (p is null) return Results.NotFound(new { error = "Arşiv bulunamadı." });
    return Results.File(p, "application/zip", Path.GetFileName(p));
}).RequireAuthorization();

// Elle bakım (arşivle + buda + disk koruması) — yalnız süper admin.
app.MapPost("/api/machine-backups/maintenance", (HttpContext c) =>
{
    var s = S(c); if (s is null || !s.IsSuperAdmin) return Results.Unauthorized();
    svc.MachineBackups.RunMaintenance();
    return Results.Ok(new { ok = true });
}).RequireAuthorization();
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
record SelectCompanyDto(string? CompanyId);
record AssignBranchDto(string? BranchId);
record AssignCompanyDto(string? CompanyId);
record SelfAssignDto(string? MachineName, string? BranchId);
record EnrollDto(string CompanyId, string Key, string DeviceName);
record PushDto(List<PushOp> Ops);
record PushOp(string OperationId, string EntityType, string EntityId, string PayloadJson, long? BaseVersion);
record NewCompanyDto(string Name, string? TaxNo, string? TaxOffice, string? Address, string? Phone, string? Email, string? AuthorizedPerson, int MaxUsers = 0, string? Id = null, int MaxAdmins = 0, int MachineQuota = 3);
record NameDto(string Name);
record LockDto(bool Locked);
// Version: DÜZENLEME KİLİDİ — null = kontrol yok (geriye uyumlu).
record PersonnelDto(string FullName, string? Title, string? Phone, string? BranchId, bool IsActive = true, bool IsFieldStaff = false, long? Version = null);
record TitleDto(string Name);
record AccountDto(string Username, string Password, string? RoleKey, string? BranchId);
record LinkUserDto(string? UserId);
record NewUserDto(string Username, string Password, string? FullName, List<string>? RoleKeys, string? CompanyId, string? BranchId, bool CanViewAllBranches = false, string? PersonnelId = null);
record MachineRegisterDto(string? CompanyId, string? MachineName, string? BranchId = null);
record QuotaDto(int Quota);
record VerifyBranchDto(string? CompanyId, string BranchId, string? BranchPassword);
record ConflictSeenDto(string? BranchId);
record UserThemeDto(string? Mode, string? Color, string? Style);
// Version: DÜZENLEME KİLİDİ — formun açıldığı andaki sürüm. Gönderilmezse (null) kontrol yapılmaz (geriye uyumlu).
record NewMaterialDto(string Code, string Name, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId, decimal MinStock, decimal UnitPrice, string? Description, decimal OpeningStock, List<string>? VehicleIds, List<string>? EquivalentIds, long? Version = null, string? TemplateId = null);
record IdListDto(List<string>? Ids);
record IdDto(string Id);
record AlertReadDto(string? Key, string? Signature);
record GrantLevelDto(Dictionary<string, string>? Levels);
record RoleGrantDto(Dictionary<string, List<string>>? Blocked);
record ReauthDto(string? Password);
// ADR-083 — özel kod + firma kalıcı silme
record SpecialCodeDto(string? Code, string? Password);
record PurgeCompanyDto(string? CompanyId, string? Password, string? SpecialCode, string? ConfirmName);
record LocalResetDto(string? CompanyId);   // ADR-084
record MachineResetDto(string? MachineName);   // ADR-085
record ListColumnsDto(List<string>? Columns);   // ADR-087 (liste kolon tercihi, kişisel)
record PageSizeDto(int PageSize);                // ADR-089 (kişisel sayfa boyutu)
record WidthsDto(Dictionary<string, int>? Widths); // ADR-089 (kişisel kolon genişlikleri)
record SortPrefDto(string? Key, bool Desc);        // Birim 4 (kişisel varsayılan sıralama — altyapı)
record VehicleStatusDto(string? Status, string? StatusNote);   // bakım ekranından araç durumu
record TrashRestoreDto(string? Table, string? Id, string? Password);
record VehicleModelDto(string BrandId, string Name);
record ReportReqDto(long? FromDate, long? ToDate, List<string>? BranchIds, List<string>? VehicleIds, string? CompanyId, List<string>? VehicleTypeIds, List<string>? MaintenanceDefIds, List<string>? TechnicianIds, List<string>? SupplierIds, List<string>? RequesterIds, List<string>? Statuses);
record BranchDto(string Name, string? Kind, string? ParentId, string? Code = null, string? Password = null, string? CompanyId = null, long? Version = null);
record CountLineDto(string MaterialId, decimal CountedQuantity);
// G1-05(a): OperationId OPSİYONELDİR — istemci gönderirse mevcut idempotency mekanizması (aynı işlemin
// tekrarında ikinci belge oluşmaz) devreye girer; göndermezse eski davranış aynen sürer (yeni GUID).
record StockCountDto(string? Reason, string? BranchId, List<CountLineDto>? Lines, string? OperationId = null);
record DeveloperDto(string? Code, bool Active);
record VehicleTemplateDto(string Name, string? InternalCode, string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId, int? ProductionYear, List<string>? MaterialIds);
// KLT-01d: Version = düzenleme kilidi jetonu (material_templates.version); 0/eksik → kontrol yok.
record MaterialTemplateDto(string Name, string? Code, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId, decimal MinStock = 0m, decimal UnitPrice = 0m, string? Currency = "TRY", string? Description = null, string? CompatibleVehicleIds = null, long Version = 0);
record StockReceiveDto(string Code, string Name, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId,
    decimal Quantity, decimal UnitPrice, string? BranchId, string? PersonnelId, string? VehicleId, string? Note, string? InvoiceNo, string? OrderSlipNo, string? CreditSlipNo,
    // madde 1.1 (kullanıcı isteği 2026-08-06): dolu ise mevcut malzemeye giriş — Code/Name/... yok sayılır,
    // yalnız SupplierId (kart güncellemesi için) kullanılır. Boşsa eski davranış (kod ile upsert) değişmez.
    string? MaterialId = null,
    // G1-05(a): opsiyonel idempotency jetonu — yoksa eski davranış (yeni GUID).
    string? OperationId = null);
/// <summary>İş #8: <c>Lines</c> ÇOK malzemeli işlem içindir. Verilmezse eski tek malzemeli alanlar
/// (MaterialId + Quantity) kullanılır → mevcut istemciler bozulmaz.</summary>
record StockLineDto(string MaterialId, decimal Quantity);
record StockMoveDto(string MaterialId, decimal Quantity, string? BranchId, string? PersonnelId, string? VehicleId, string? Note, string? InvoiceNo, string? OrderSlipNo, string? CreditSlipNo, List<StockLineDto>? Lines = null, string? OperationId = null);
record StockTransferDto(string MaterialId, decimal Quantity, string? FromBranchId, string? ToBranchId, string? PersonnelId, string? VehicleId, string? Note, string? InvoiceNo, string? OrderSlipNo, string? CreditSlipNo, List<StockLineDto>? Lines = null, string? OperationId = null);
record StockReverseDto(string DocumentId, string? Reason);
record FuelCancelDto(string? Reason);
record StockChangeLogDto(string MaterialId, decimal NewQuantity, bool Continued, string? WarningText);
record IdReasonDto(string Id, string? Reason);
// B-5: muayene iptali — gerekçe + düzenleme kilidi jetonu. IdReasonDto ÇOK çağıranı olduğu için
// değiştirilmedi; iptale özel bu DTO eklendi. Version gönderilmezse (0/null) kilit kontrolü yapılmaz.
record InspectionCancelDto(string? Reason, long? Version);
record MaintenanceMetaDto(string? Description, string? SubDefinitionNote, string? TechnicianId, long? Version);
record DailyMetaDto(string? Description, string? OperatorId, int? DurationDays, long? Version);
/// <summary>FromTeamStock = "Bakım Ekibi Stoğundan Kullanıldı" (2026-08-08): kayda girer, merkez depodan düşmez.
/// Varsayılan false → eski istemciler (alanı göndermeyen) bugünkü davranışı aynen alır (geriye uyumlu).</summary>
record MaintLineDto(string MaterialId, decimal Quantity, bool FromTeamStock = false);
record MaintenanceDto(string VehicleId, string DefinitionId, string? SubDefinitionId, string? TechnicianId, string? Description, string? SubDefinitionNote,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate, List<MaintLineDto>? Materials);
// B-1 (2026-08-10): Version = düzenleme kilidi jetonu. Gönderilmezse (eski istemci) null gelir → kontrol yok.
record MaintDefDto(string Name, decimal IntervalValue, string IntervalUnit, string? ParentDefId, string? Description, List<string>? VehicleIds, long? Version = null);
record InspectionDto(string VehicleId, string DocType, long? LastDate, long? NextDate, string? Result, string? Place, string? Note);
record DepotEntryDto(decimal Liters, decimal UnitPrice, string? SupplierId, string? InvoiceNo, string? Note, long? EntryDate);
record DistributionDto(string VehicleId, decimal Liters, decimal CurrentMeter, decimal? UnitPrice, string? PersonnelId, long? DistributionDate, string? Note, string? RecipientPersonnelId = null, decimal? PrevMeter = null);
record MovementDto(string MovementKind, string? VehicleId, string? FromLocationId, string? ToLocationId, string? OperatorId, int? DurationDays, string? Description, long? ActivityDate);
// ADR-091: "İlave Yağ/İlave Filtre/Tamir" — Bakım ile AYNI alanlar, yalnız DefinitionId/SubDefinitionId YOK.
record ExtraActivityDto(string Type, string VehicleId, string? TechnicianId, string? Description,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate, List<MaintLineDto>? Materials);
record NewVehicleDto(string InternalCode, string? Plate, int? ProductionYear, decimal CurrentMeter, string? MeterUnit, string? BranchId, string? DriverPersonnelId,
    string? ChassisNo, string? EngineNo, string? Status, string? StatusNote, string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId, string? TemplateId,
    long? Version = null); // DÜZENLEME KİLİDİ: null = kontrol yok (geriye uyumlu)
record RequestItemDto(string MaterialId, decimal Quantity, string? VehicleId, string? Note);
/// <summary>Priority: "normal|high|urgent|critical" (şartname madde 18). Gönderilmezse Normal (geriye uyumlu).</summary>
record RequestDto(List<RequestItemDto>? Items, string? BranchId, string? RequesterId, string? WarehouseId, string? ApproverId, string? Description, long? RequestDate, bool SubmitImmediately, string? Priority = null, long? Version = null);
/// <summary>Talep Operasyonları durum değişikliği (Faz 2). UpdateBranches=true ise gönderen/gönderilecek şube
/// de yazılır. İşlemin YAPILDIĞI şube istemciden alınmaz — sunucuda oturumdan belirlenir.</summary>
// KLT-01a: Version = düzenleme kilidi jetonu (material_requests.version); 0/eksik → kontrol yok.
record RequestOpsStatusDto(string Status, string? Note, string? FromBranchId, string? ToBranchId, bool UpdateBranches = false,
    long Version = 0);
record RequestOpsShipmentDto(string? FromBranchId, string? ToBranchId, string? Note, long Version = 0);
record RolesDto(List<string>? Roles);
record ActiveDto(bool Active);
record PasswordDto(string Password);
record ChangeInitialPwDto(string? NewPassword);
record SubCategoryDto(string Name, string? ParentId);
record ModulePermDto(string ModuleKey, bool CanView, bool CanCreate, bool CanEdit, bool CanDelete);
record PermSaveDto(List<ModulePermDto>? Modules, List<string>? Buttons, long Version = 0);
record TemplateDto(string Name, string? RoleKey, List<ModulePermDto>? Modules, List<string>? Buttons, string? CompanyId = null, bool ScopeAll = false);

/// <summary>#19 — Canlı sunucu durumu sayaçları (süreç boyunca).</summary>
static class ServerMetrics
{
    public static long Requests;
    public static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;

    // CPU% örnekleme: iki poll arasındaki işlemci-zamanı farkını duvar-saati farkına oranlar (çekirdek sayısına böler).
    private static readonly object _cpuLock = new();
    private static TimeSpan _lastCpu = TimeSpan.Zero;
    private static DateTime _lastCpuAt = DateTime.MinValue;

    public static double SampleCpuPercent()
    {
        lock (_cpuLock)
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var cpu = proc.TotalProcessorTime;
            if (_lastCpuAt == DateTime.MinValue) { _lastCpu = cpu; _lastCpuAt = now; return 0; }
            var wallMs = (now - _lastCpuAt).TotalMilliseconds;
            var usedMs = (cpu - _lastCpu).TotalMilliseconds;
            _lastCpu = cpu; _lastCpuAt = now;
            if (wallMs <= 0) return 0;
            var pct = usedMs / (wallMs * Math.Max(1, Environment.ProcessorCount)) * 100.0;
            return Math.Round(Math.Clamp(pct, 0, 100), 1);
        }
    }
}

/// <summary>#4 — Bellek-içi online KULLANICI izleme (tek sunucu; kalıcı depo gerektirmez, ücretsiz).
///
/// TEKİLLEŞTİRME (kullanıcının şartı): Sayım **oturum/login başına değil, KULLANICI başınadır**. Sözlük
/// <c>userId</c> ile anahtarlanır → aynı kullanıcı hem web'den hem masaüstünden girse (hatta birden çok sekme/
/// makine) **1 online** sayılır. Farklı kullanıcılar ayrı sayılır. Bkz. <c>ServerPresenceTests</c>.
///
/// Not: Kullanıcı birden çok platformda farklı FİRMA bağlamındaysa (süper admin firma seçimi), en SON istek
/// attığı firmada online görünür — kişi tek olduğundan çift sayılmaz.
/// </summary>
public static class ServerPresence
{
    /// <summary>Bu süre içinde istek atan kullanıcı "online" sayılır.</summary>
    public const long WindowMs = 5 * 60 * 1000; // son 5 dk

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Seen, string Company)> _seen = new();

    /// <summary>Kimliği doğrulanmış her istekte çağrılır. Anahtar userId → aynı kullanıcının ikinci platformu
    /// yeni kayıt AÇMAZ, mevcut kaydı tazeler (tekilleştirme burada olur).</summary>
    public static void Touch(string userId, string companyId, long? nowMs = null)
        => _seen[userId] = (nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), companyId);

    public static int TotalOnline(long? nowMs = null)
    {
        var cut = (nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) - WindowMs;
        Prune(cut);
        return _seen.Count(kv => kv.Value.Seen >= cut);
    }

    /// <summary>Firma → online KULLANICI sayısı (kişi bazında tekil).</summary>
    public static Dictionary<string, int> OnlineByCompany(long? nowMs = null)
    {
        var cut = (nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) - WindowMs;
        Prune(cut);
        return _seen.Where(kv => kv.Value.Seen >= cut)
                    .GroupBy(kv => kv.Value.Company)
                    .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Pencerenin dışında kalan kayıtları sözlükten düşür (yoksa süresiz büyür — bellek sızıntısı).</summary>
    private static void Prune(long cut)
    {
        foreach (var kv in _seen)
            if (kv.Value.Seen < cut) _seen.TryRemove(kv.Key, out _);
    }

    /// <summary>Yalnız test için: izleyiciyi sıfırla.</summary>
    public static void ResetForTests() => _seen.Clear();
}

/// <summary>
/// Yalnız TEST altyapısı için: top-level statements kullanan bu uygulamayı
/// <c>WebApplicationFactory&lt;Program&gt;</c> ile bellek-içi ayağa kaldırabilmenin standart yolu
/// (Paket 1, 2026-08-09 — çok-firmalı izolasyon testleri gerçek HTTP hattından koşar).
/// Çalışma zamanı davranışını DEĞİŞTİRMEZ.
/// </summary>
public partial class Program { }
