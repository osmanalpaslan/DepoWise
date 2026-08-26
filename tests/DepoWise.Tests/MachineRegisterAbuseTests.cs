using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MAK-01 · ANONİM MAKİNE KAYDI ═══ (denetim 2026-08-26)
///
/// <c>POST /api/machines/register</c> <b>tamamen anonimdir</b> ve anonim kalmak ZORUNDADIR: masaüstünün
/// makine kapısı (<c>MachineGate</c>) bu ucu <b>giriş ekranından önce</b>, hiçbir kimlik bilgisi yokken çağırır.
///
/// Bu sınıf iki şeyi ölçer:
/// <list type="number">
///   <item><b>Bulunan durum (kanıt):</b> anonim çağıran, firmanın <b>makine kotasını</b> tüketebiliyor —
///     yeni kayıt <c>ActiveCount &lt; quota</c> olduğu sürece kendiliğinden <c>active</c> oluyor. Kota
///     dolunca sonraki (gerçek) makine <c>pending</c> kalır ve senkron yapamaz. Firma kimlikleri
///     <c>/api/public/companies</c> ile herkese açık listelenebildiği için hedef bilinir.
///     ⚠️ <b>Veri sızıntısı YOK</b> — kayıt bir cihaz jetonu vermez; <c>/sync/push</c> ayrıca doğrular.
///     Ve MEVCUT aktif makineler <b>düşürülmez</b> (yalnız yeni makine etkilenir).</item>
///   <item><b>Bu turda uygulanan koruma:</b> uca IP başına hız sınırı kondu → kitlesel satır enjeksiyonu
///     ve otomatik kota tüketimi ciddi biçimde zorlaşır. Meşru akış etkilenmez (bir makine, bir kayıt).</item>
/// </list>
///
/// Aktivasyon MODELİNİN değişmesi (yeni makinelerin ancak kimlik doğrulanmış girişten sonra aktifleşmesi)
/// masaüstü kurulum akışını değiştirir; bu turda BİLİNÇLİ olarak yapılmadı — kullanıcı kararına bırakıldı.
/// </summary>
[Collection("PostgresSchema")]
public class MachineRegisterAbuseTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "MAK-CO";
    private ServerServices _svc = null!;

    public Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        // Kota bilerek KÜÇÜK (2) — testin niyeti kotayı tüketmek, sayı büyüklüğü değil.
        cmd.CommandText =
            "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
            "VALUES(@c,'Makine Firmasi',1,1,1,0,2,20,5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@c", Co);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private async Task<(HttpStatusCode Kod, string Durum)> KaydetAsync(HttpClient c, string makineAdi)
    {
        var r = await c.PostAsJsonAsync("/api/machines/register", new { companyId = Co, machineName = makineAdi });
        if (!r.IsSuccessStatusCode) return (r.StatusCode, "");
        var j = await ApiTestHost.JsonAsync(r);
        return (r.StatusCode, j.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "");
    }

    private long PendingSayisi()
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_devices WHERE company_id=@c AND status='pending';";
        cmd.AddWithValue("@c", Co);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>
    /// ⭐ MAK-01 KANIT — anonim çağıran kotayı tüketiyor ve SONRAKİ (gerçek) makine <c>pending</c> kalıyor.
    /// Bu test bir DÜZELTMEYİ değil, BULUNAN DURUMU belgeler; kota modeli bu turda bilerek değiştirilmedi.
    /// </summary>
    [Fact]
    public async Task MAK01_Anonim_Kayit_Kotayi_Tuketebiliyor()
    {
        var anon = _host.Anonymous();

        Assert.Equal("active", (await KaydetAsync(anon, "SAHTE-1")).Durum);
        Assert.Equal("active", (await KaydetAsync(anon, "SAHTE-2")).Durum);

        // Kota (2) doldu → gerçek makine artık aktifleşemiyor.
        var gercek = await KaydetAsync(anon, "GERCEK-MAKINE");
        Assert.Equal("pending", gercek.Durum);
    }

    /// <summary>
    /// ⭐ MAK-01 KORUMA — aynı IP'den seri kayıt bir noktadan sonra REDDEDİLİR (429).
    /// Sınır, meşru kullanımın (bir makine = bir kayıt, ara sıra yeniden deneme) çok üstündedir.
    /// </summary>
    [Fact]
    public async Task MAK01_Seri_Anonim_Kayit_Hiz_Siniriyla_Durdurulur()
    {
        var anon = _host.Anonymous();
        int red = 0, oncekiPending = 0;

        for (int i = 0; i < 60; i++)
        {
            var (kod, _) = await KaydetAsync(anon, "FLOOD-" + i);
            if (kod == HttpStatusCode.TooManyRequests) { red++; if (oncekiPending == 0) oncekiPending = (int)PendingSayisi(); }
        }

        Assert.True(red > 0, "60 seri anonim kayıt hiç reddedilmedi — hız sınırı uygulanmıyor.");
    }

    /// <summary>
    /// KİLİT: sınır MEŞRU akışı bozmamalı. Aynı makine adıyla tekrar tekrar kayıt (masaüstü her açılışta
    /// makine kapısını çağırır) sınırın altında kalmalı ve DAİMA çalışmalıdır.
    /// </summary>
    [Fact]
    public async Task MAK01_Ayni_Makinenin_Tekrar_Kaydi_Engellenmez()
    {
        var anon = _host.Anonymous();

        for (int i = 0; i < 10; i++)
        {
            var (kod, durum) = await KaydetAsync(anon, "MESRU-MAKINE");
            Assert.Equal(HttpStatusCode.OK, kod);
            Assert.False(string.IsNullOrEmpty(durum), "makine durumu dönmedi");
        }
    }
}
