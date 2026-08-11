using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-05 (PRT-01 Grup 6, 2026-08-11) — SÜPER ADMİN BAŞKA FİRMA BAĞLAMINDAYKEN PAROLA DOĞRULAMASI.
///
/// Bulunan durum: Kalıcı Silme, Çöp Kutusu (liste/geri yükleme), özel kod değiştirme ve test verisi
/// sıfırlama uçları parolayı <c>VerifyUserPassword(companyId, userId, …)</c> ile doğruluyordu. Süper admin
/// "Firma Seç" ile başka firma bağlamına geçtiğinde kendi kullanıcı kaydı EV firmasında kaldığı için
/// firma-filtreli sorgu satırı bulamıyor ve DOĞRU parola "Parola hatalı" olarak reddediliyordu.
///
/// Bu hata 2026-07-20'de bulunup YALNIZ <c>/api/admin/reset-company-business</c> için düzeltilmişti
/// (<see cref="AuthService.VerifyUserPassword(string,string)"/> aşırı yüklemesi); diğer uçlar eski
/// sürümde kalmıştı. Bu testler düzeltmenin tüm uçlara yayıldığını ve parola kapısının KALDIRILMADIĞINI
/// birlikte kilitler.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiSuperAdminCompanyContextTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string OtherCompany = "G605-DIGER";
    private const string AdminCompany = "G605-ADMIN";
    private const string AdminUser = "g605_admin";
    private const string AdminPass = "Test!2026";

    private HttpClient _super = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();

        // Süper adminin EV firması "DEPOWISE" (tohum). Başka bir firma + kendi admini kurulur.
        svc.Users.EnsureInitialAdmin(OtherCompany, "g605_diger", AdminPass, RoleKeys.CompanyAdmin);
        svc.Users.EnsureInitialAdmin(AdminCompany, AdminUser, AdminPass, RoleKeys.CompanyAdmin);

        _super = await _host.LoginSeedAsync();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    /// <summary>"Firma Seç": süper admine BAŞKA firma bağlamında yeni token verir.</summary>
    private async Task SwitchCompanyAsync(string companyId)
    {
        var r = await _super.PostAsJsonAsync("/api/auth/select-company", new { companyId });
        r.EnsureSuccessStatusCode();
        var token = (await ApiTestHost.JsonAsync(r)).GetProperty("token").GetString();
        _super.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    private static Task<HttpResponseMessage> OpenTrashAsync(HttpClient c, string password)
        => c.PostAsJsonAsync("/api/trash", new { password });

    [Fact]
    public async Task SuperAdmin_KendiFirmasinda_DogruParolayla_CopKutusunu_Acar()
    {
        var r = await OpenTrashAsync(_super, ApiTestHost.SeedPassword);
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SuperAdmin_BASKA_Firma_Baglaminda_DogruParolayla_CopKutusunu_Acar()
    {
        // G6-05'in ta kendisi: düzeltme öncesi burası 403 "Parola hatalı" dönüyordu.
        await SwitchCompanyAsync(OtherCompany);

        var r = await OpenTrashAsync(_super, ApiTestHost.SeedPassword);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_BASKA_Firma_Baglaminda_YanlisParola_REDDEDILIR()
    {
        // Parola kapısı KALDIRILMADI: yanlış parola her bağlamda reddedilmeye devam eder.
        await SwitchCompanyAsync(OtherCompany);

        var r = await OpenTrashAsync(_super, "yanlis-parola");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Parola hatalı.", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task SuperAdmin_BASKA_Firma_Baglaminda_Kalici_Silmede_Parola_Kapisini_Gecer()
    {
        // Kalıcı Silme'de parola İLK kapıdır; geçilirse sıradaki kapı (özel kod) devreye girer.
        // Yanıtın "Özel kod hatalı" olması = parola doğrulaması artık başarılı (G6-05 düzeldi).
        await SwitchCompanyAsync(OtherCompany);

        var r = await _super.PostAsJsonAsync("/api/admin/purge-company", new
        {
            companyId = OtherCompany, password = ApiTestHost.SeedPassword,
            specialCode = "0000", confirmName = OtherCompany,
        });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("Özel kod hatalı.", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Firma_Admini_Kendi_Firmasinda_Etkilenmedi()
    {
        // Süper admin OLMAYAN kullanıcıda oturum firması = kendi firması → davranış değişmemeli.
        var admin = await _host.LoginAsync(AdminUser, AdminPass, AdminCompany);

        (await OpenTrashAsync(admin, AdminPass)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await OpenTrashAsync(admin, "yanlis")).StatusCode);
    }
}
