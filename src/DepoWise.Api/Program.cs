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
// Sıfır-sürtünmeli kayıt: masaüstü açılışta kendini 'pending' cihaz olarak kaydeder (auth gerekmez).
app.MapPost("/api/machines/register", (MachineRegisterDto d) =>
    Results.Ok(svc.Enrollment.RegisterSelf(
        string.IsNullOrWhiteSpace(d.CompanyId) ? "DEPOWISE" : d.CompanyId!,
        string.IsNullOrWhiteSpace(d.MachineName) ? "Bilinmeyen Makine" : d.MachineName!)));
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

app.MapPut("/api/companies/{id}", (HttpContext ctx, string id, NewCompanyDto dto) =>
{
    var s = Session(ctx); if (s is null) return Results.Unauthorized();
    svc.Companies.Update(s, id, new DepoWise.Infrastructure.Organization.NewCompany(
        dto.Name, dto.TaxNo, dto.TaxOffice, dto.Address, dto.Phone, dto.Email, dto.AuthorizedPerson));
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// ── İş modülleri: liste (okuma) uçları — hepsi yetki korumalı (servis AccessControl.View) ──
DepoWise.Application.Common.PageRequest Page() => new() { Limit = 500 };
SessionContext? S(HttpContext ctx) => Session(ctx);
static string? Doc(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
static bool Void(Action a) { a(); return true; }

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
// Malzeme alt kategorileri (seçili kategorinin çocukları)
app.MapGet("/api/materials/subcategories", (HttpContext c, string? parentId) =>
    S(c) is { } s ? Results.Ok(svc.Lookups.ListCategories(s, string.IsNullOrWhiteSpace(parentId) ? null : parentId)) : Results.Unauthorized()).RequireAuthorization();

// Roller (kullanıcı oluşturma için)
app.MapGet("/api/roles", (HttpContext c) => S(c) is null ? Results.Unauthorized()
    : Results.Ok(RoleKeys.Seed.Where(r => r.Key != RoleKeys.SuperAdmin).Select(r => new { key = r.Key, name = r.Name }))).RequireAuthorization();

// ── Yazma (ekle/sil) uçları — servis AccessControl (Create/Delete) enforce eder ──
app.MapPost("/api/branches", (HttpContext c, NameDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Branches.Create(s, new DepoWise.Infrastructure.Organization.NewBranch(d.Name)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/personnel", (HttpContext c, PersonnelDto d) => S(c) is { } s ? Results.Ok(new { id = svc.Personnel.Create(s, new DepoWise.Infrastructure.Org.NewPersonnel(d.FullName, d.Title, d.Phone, null)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/users", (HttpContext c, NewUserDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    // Firma: YALNIZ süper admin seçebilir; diğerleri kendi firmasına bağlar (yetki yükseltme engeli).
    var companyId = s.IsSuperAdmin && !string.IsNullOrWhiteSpace(d.CompanyId) ? d.CompanyId! : s.CompanyId;
    var id = svc.Users.CreateUser(s, new DepoWise.Infrastructure.Security.NewUser(
        d.Username, d.Password, d.FullName, d.RoleKeys ?? new List<string>(), companyId, null, d.BranchId));
    return Results.Ok(new { id });
}).RequireAuthorization();
app.MapPost("/api/materials", (HttpContext c, NewMaterialDto d) =>
{
    var s = S(c); if (s is null) return Results.Unauthorized();
    var id = svc.Materials.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial(
        d.Code, d.Name, d.Type, d.CategoryId, d.UnitId, d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, "TRY", Doc(d.Description)));
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
app.MapGet("/api/modules", (HttpContext c) => S(c) is null ? Results.Unauthorized()
    : Results.Ok(AppModules.All.Select(m => new { key = m.Key, label = m.Label }))).RequireAuthorization();

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

// ── Muayene / Sigorta ──
app.MapPost("/api/inspection", (HttpContext c, InspectionDto d) =>
    S(c) is { } s ? Results.Ok(new { id = svc.Inspection.Save(s, new DepoWise.Infrastructure.Maintenance.NewInspection(
        d.VehicleId, d.DocType, d.LastDate, d.NextDate, Doc(d.Result), Doc(d.Place), Doc(d.Note))) }) : Results.Unauthorized()).RequireAuthorization();

// ── Yakıt ──
app.MapGet("/api/fuel/depot", (HttpContext c) => S(c) is { } s ? Results.Ok(svc.Fuel.ListDepotEntries(s)) : Results.Unauthorized()).RequireAuthorization();
app.MapGet("/api/fuel/summary", (HttpContext c) => S(c) is { } s ? Results.Ok(new { depotBalance = svc.Fuel.GetDepotBalance(s), currentPrice = svc.Fuel.GetCurrentFuelPrice(s) }) : Results.Unauthorized()).RequireAuthorization();
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
app.MapGet("/api/vehicles/models/{brandId}", (HttpContext c, string brandId) =>
    S(c) is { } s ? Results.Ok(svc.Lookups.ListVehicleModels(s, brandId)) : Results.Unauthorized()).RequireAuthorization();

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
app.MapPost("/api/requests/{id}/approve", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Approve(s, id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPost("/api/requests/{id}/reject", (HttpContext c, string id, IdReasonDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Requests.Reject(s, id, string.IsNullOrWhiteSpace(d?.Reason) ? "Reddedildi" : d!.Reason!)) }) : Results.Unauthorized()).RequireAuthorization();

// ── Personel (sil) + Şube/Şantiye (güncelle/sil) ──
app.MapDelete("/api/personnel/{id}", (HttpContext c, string id) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Lookups.Delete(s, "personnel", id)) }) : Results.Unauthorized()).RequireAuthorization();
app.MapPut("/api/branches/{id}", (HttpContext c, string id, NameDto d) =>
    S(c) is { } s ? Results.Ok(new { ok = Void(() => svc.Branches.Update(s, id, new DepoWise.Infrastructure.Organization.NewBranch(d.Name))) }) : Results.Unauthorized()).RequireAuthorization();
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
record NewUserDto(string Username, string Password, string? FullName, List<string>? RoleKeys, string? CompanyId, string? BranchId);
record MachineRegisterDto(string? CompanyId, string? MachineName);
record NewMaterialDto(string Code, string Name, string? Type, string? CategoryId, string? UnitId, string? BrandId, string? SupplierId, decimal MinStock, decimal UnitPrice, string? Description, decimal OpeningStock);
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
