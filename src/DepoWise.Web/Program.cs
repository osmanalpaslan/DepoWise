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
// STK-04: stok lokasyonu seçenekleri — oturumda BİR KEZ indirilir, tüm stok ekranları paylaşır (N+1 yok).
builder.Services.AddScoped<DepoWise.Web.Services.LocationOptions>();

var app = builder.Build();

// ⭐ FAZ J (2026-09-05) — GÜVENLİK BAŞLIKLARI.
//
// Bulgu: web tarayıcıya HİÇBİR güvenlik başlığı göndermiyordu. HSTS zaten vardı (yukarıda), ama
// tıklama-hırsızlığı (clickjacking), MIME tipi tahmini ve referrer sızıntısı açık kalıyordu.
//
// Eklenenler ve NEDEN bunlar:
//  • X-Content-Type-Options: nosniff — tarayıcı içerik tipini TAHMİN ETMESİN. Yüklenen bir dosya
//    yanlış tiple servis edilirse tarayıcı onu betik sanıp çalıştırabilir.
//  • X-Frame-Options: DENY — uygulama başka bir sitenin çerçevesine konulamaz. Alpnex hiçbir yerde
//    iframe içinde kullanılmıyor; kullanıcının farkında olmadan tıklaması engellenir.
//  • Referrer-Policy: same-origin — dış bağlantılara giderken tam adres (içinde kayıt kimlikleri
//    olabilir) karşı tarafa SIZMASIN.
//  • X-Permitted-Cross-Domain-Policies: none — eski eklenti tabanlı çapraz alan erişimini kapatır.
//
// ⚠️ CSP (Content-Security-Policy) BİLİNÇLİ OLARAK EKLENMEDİ: Blazor Server + MudBlazor satır içi
// betik/stil kullanır ve yanlış bir politika arayüzü SESSİZCE bozar (ekran açılır, düğmeler
// çalışmaz). Kullanıcı başka bir şehirde ve tek başına; ölçmeden eklenen bir CSP, koruduğundan
// fazlasını kırardı. Ayrı bir iş olarak, gerçek tarayıcıda doğrulanarak yapılmalıdır.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

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
