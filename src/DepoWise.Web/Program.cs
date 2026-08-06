using DepoWise.Web.Components;
using DepoWise.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Tarih/takvim Türkçe görünsün (MudDatePicker ay adları "Ocak…", gün/ay/yıl sırası TR) — kullanıcı bildirimi
// 2026-08-05, masaüstüyle eşit. Sayı biçimi INVARIANT (nokta) bırakılır → mevcut sayı girişi/gösterimi DEĞİŞMEZ.
var trCulture = (System.Globalization.CultureInfo)new System.Globalization.CultureInfo("tr-TR").Clone();
trCulture.NumberFormat = System.Globalization.CultureInfo.InvariantCulture.NumberFormat;
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = trCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = trCulture;

// DataProtection anahtarlarını KALICI diske yaz (antiforgery/oturum yeniden başlatmada bozulmasın).
var keysDir = Directory.Exists("/dpkeys") ? "/dpkeys" : Path.Combine(builder.Environment.ContentRootPath, "dpkeys");
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("DepoWiseWeb");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // KRİTİK: InputFile ile fotoğraf yükleme dosya baytlarını SignalR devresi üzerinden akıtır. Varsayılan
    // MaximumReceiveMessageSize = 32 KB olduğundan, birkaç yüz KB'lik bir foto seçilince devre DÜŞÜYOR →
    // kayıt sunucuda oluşsa bile ekran takılı kalıyor (spinner sonsuz döner). 12 MB'a çıkarıldı (foto akışı için).
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 12 * 1024 * 1024);
builder.Services.AddMudServices();

// API tabanı (appsettings: Api:BaseUrl) — web yalnız bu API'yi tüketir (iş kuralı taşımaz).
var apiBase = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5224";
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase), Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<DepoWise.Web.Services.ThemeState>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
