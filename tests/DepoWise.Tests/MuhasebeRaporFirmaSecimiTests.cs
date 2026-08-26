using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RPR-14 · MUHASEBE RAPORLARI FİRMA SEÇİCİSİNİ SESSİZCE YOK SAYIYORDU ═══ (denetim 2026-08-26)
///
/// <b>Bulunan durum.</b> Rapor ekranı süper adminde bir <b>"Firma (Süper Admin)"</b> seçicisi gösterir ve
/// seçilen firmayı her rapor isteğinde <c>companyId</c> olarak gönderir (<c>Reports.razor</c>, hem rapor
/// hem Excel çağrısında). 15 rapor bunu <c>ReportGate.ResolveCompany</c> ile çözerken, <b>6 ön muhasebe
/// raporu</b> (<c>acc-*</c>) alanı hiç okumuyor, doğrudan <c>s.CompanyId</c> kullanıyordu.
///
/// <b>Sonuç:</b> süper admin listeden <b>B firmasını</b> seçtiğinde rapor yine <b>A firmasının</b> verisini
/// getiriyor, ama başlıkta/ekranda B seçili görünüyordu → <b>yanlış firmanın mali rakamları</b>.
/// Sessizdir: hata yok, boş sonuç yok, yalnızca yanlış veri.
///
/// ⚠️ Tenant açığı DEĞİLDİR — yön TERSİDİR: uç, istenen firmayı kullanmak yerine kendi firmasına
/// düşüyordu (fazla değil, YANLIŞ gösterme). Süper admin olmayan kullanıcı için davranış değişmez:
/// <c>ResolveCompany</c> yabancı firma istendiğinde 403 verir, boş/kendi firması istendiğinde
/// oturum firmasını döndürür — yani bugünkü davranışın aynısı.
///
/// <b>Neden bugüne dek görülmedi:</b> üretimde tek firma var → seçicide başka firma yok.
/// </summary>
[Collection("PostgresSchema")]
public class MuhasebeRaporFirmaSecimiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "ACC-A";
    private const string CoB = "ACC-B";
    private ServerServices _svc = null!;
    private HttpClient _super = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
            Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) " +
                     "VALUES(@c,@n,1,1,1,0) ON CONFLICT(id) DO NOTHING;", ("@c", id), ("@n", ad));

        // Kasa hesabı YALNIZ A firmasında. B firmasında hiç hesap yok.
        Calistir("INSERT INTO finance_accounts(id,company_id,code,name,account_kind,currency_code," +
                 "is_default,is_active,created_at,updated_at,version,is_deleted) " +
                 "VALUES('acc-a-1',@c,'KASA-A','A FIRMASI KASASI','cash','TRY',1,1,1,1,1,0);", ("@c", CoA));

        _super = await _host.LoginSeedAsync();   // tohum süper admin — oturum firması DEPOWISE
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private async Task<string> RaporAsync(string tip, string? firma)
    {
        var r = await _super.PostAsJsonAsync($"/api/reports/{tip}", new { companyId = firma });
        return await r.Content.ReadAsStringAsync();
    }

    /// <summary>⭐ RPR-14a — süper admin A'yı seçince A'nın kasası görünmeli (seçici ÇALIŞMALI).</summary>
    [Fact]
    public async Task RPR14a_Secilen_Firmanin_Verisi_Gelir()
        => Assert.Contains("A FIRMASI KASASI", await RaporAsync("acc-cash", CoA), StringComparison.Ordinal);

    /// <summary>⭐ RPR-14b — B seçiliyken A'nın kasası GÖRÜNMEMELİ (yanlış firma verisi yok).</summary>
    [Fact]
    public async Task RPR14b_Baska_Firma_Secilince_Digerinin_Verisi_Gelmez()
        => Assert.DoesNotContain("A FIRMASI KASASI", await RaporAsync("acc-cash", CoB), StringComparison.Ordinal);

    /// <summary>Aynı kural cari bakiye raporunda da geçerli (tek tek değil, ORTAK çözüm).</summary>
    [Fact]
    public async Task RPR14c_Cari_Bakiye_De_Firma_Secimini_Uygular()
    {
        var r = await _super.PostAsJsonAsync("/api/reports/acc-balances", new { companyId = CoB });
        Assert.True(r.IsSuccessStatusCode, $"beklenen: 200, gelen: {(int)r.StatusCode}");
    }

    /// <summary>Regresyon kilidi: firma GÖNDERİLMEZSE oturum firması kullanılır (eski davranış).</summary>
    [Fact]
    public async Task RPR14d_Firma_Gonderilmezse_Oturum_Firmasi()
    {
        var govde = await RaporAsync("acc-cash", null);
        Assert.DoesNotContain("A FIRMASI KASASI", govde, StringComparison.Ordinal);   // oturum DEPOWISE
    }

    /// <summary>Regresyon kilidi: süper admin OLMAYAN kullanıcı yabancı firma isteyemez (403).</summary>
    [Fact]
    public async Task RPR14e_Yetkisiz_Yabanci_Firma_Isteyemez()
    {
        _svc.Users.EnsureInitialAdmin(CoA, "acc_admin_a", "Acc!2026", DepoWise.Application.Security.RoleKeys.CompanyAdmin);
        var adminA = await _host.LoginAsync("acc_admin_a", "Acc!2026", CoA);

        var r = await adminA.PostAsJsonAsync("/api/reports/acc-cash", new { companyId = CoB });

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }
}
