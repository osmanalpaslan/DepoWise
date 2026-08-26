using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MAK-01 · MAKİNE AKTİVASYON YAŞAM DÖNGÜSÜ ═══ (denetim 2026-08-26, dördüncü tur)
///
/// Önceki turlarda MAK-01 "anonim kayıt kotayı tüketebiliyor" diye kaydedilmiş ve model değişikliği
/// <b>kullanıcı kararına</b> bırakılmıştı. Bu tur, modelin gerçekten bir <b>çıkmaz</b> yaratıp
/// yaratmadığını senaryo senaryo ölçer — çünkü "kilitlenme" iddiası doğruysa bu bir tasarım hatasıdır,
/// değilse yalnız bir kullanım zahmetidir ve ikisi farklı şeylerdir.
///
/// <b>Ölçülen senaryolar</b> (kullanıcı listesi A–G):
/// <list type="bullet">
///   <item><b>A</b> — gerçek makine kurulur → <c>active</c>.</item>
///   <item><b>B</b> — sahte kayıtlar kotayı doldurur → yönetici sahteyi iptal edince gerçek makine açılır.</item>
///   <item><b>C</b> — kota DOLUYKEN yönetici bekleyen gerçek makineyi <b>onaylayabilir</b> (çıkmaz YOK).</item>
///   <item><b>D</b> — aynı makine tekrar kurulur → yeni kota tüketmez.</item>
///   <item><b>E</b> — iptal edilmiş (revoked) makine kendiliğinden aktifleşemez.</item>
///   <item><b>G</b> — A firmasının cihaz jetonu B firmasının verisini çekemez.</item>
/// </list>
/// <b>F (internet yok)</b> masaüstü tarafındadır (<c>MachineGate</c> önbelleği) ve Avalonia arayüzü
/// otomatize edilemediği için burada <b>test edilmemiştir</b> — uydurma test yazılmadı.
/// </summary>
[Collection("PostgresSchema")]
public class MakineYasamDonguTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "MAK-A";
    private const string CoB = "MAK-B";
    private const string Pass = "Mak!2026";
    private ServerServices _svc = null!;
    private HttpClient _adminA = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        // Kota bilerek KÜÇÜK (2): amaç kotayı doldurmak, büyük sayı değil.
        Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                 "VALUES(@c,'A Firmasi',1,1,1,0,2,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", CoA));
        Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                 "VALUES(@c,'B Firmasi',1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", CoB));

        _svc.Users.EnsureInitialAdmin(CoA, "mak_admin_a", Pass, RoleKeys.CompanyAdmin);
        _adminA = await _host.LoginAsync("mak_admin_a", Pass, CoA);
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

    private T Oku<T>(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? default! : (T)Convert.ChangeType(v, typeof(T));
    }

    /// <summary>Masaüstünün açılışta yaptığı ANONİM kayıt (giriş ekranından önce).</summary>
    private async Task<string> KaydetAsync(string makineAdi, string firma = CoA)
    {
        var r = await _host.Anonymous().PostAsJsonAsync("/api/machines/register",
            new { companyId = firma, machineName = makineAdi });
        if (!r.IsSuccessStatusCode) return $"HTTP-{(int)r.StatusCode}";
        var j = await ApiTestHost.JsonAsync(r);
        return j.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
    }

    private string MakineId(string ad, string firma = CoA)
        => Oku<string>("SELECT id FROM sync_devices WHERE company_id=@c AND device_name=@n;", ("@c", firma), ("@n", ad));

    // ── A ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A — kota müsaitken gerçek makine sorunsuz kurulur.</summary>
    [Fact]
    public async Task MAK_A_Gercek_Makine_Kurulur()
        => Assert.Equal("active", await KaydetAsync("GERCEK-1"));

    // ── B ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ B — sahte kayıtlar kotayı doldurursa gerçek makine <c>pending</c> kalır; ama yönetici sahteyi
    /// İPTAL edince gerçek makine bir sonraki açılışta kendiliğinden aktifleşir. Yani durum
    /// <b>geri döndürülebilirdir</b> — kalıcı bir kilitlenme değildir.
    /// </summary>
    [Fact]
    public async Task MAK_B_Sahte_Kayitlar_Iptal_Edilince_Gercek_Makine_Acilir()
    {
        Assert.Equal("active", await KaydetAsync("SAHTE-1"));
        Assert.Equal("active", await KaydetAsync("SAHTE-2"));
        Assert.Equal("pending", await KaydetAsync("GERCEK-1"));      // kota (2) doldu

        // Yönetici sahte makineyi pasife alır.
        var iptal = await _adminA.PostAsJsonAsync($"/api/machines/{MakineId("SAHTE-1")}/revoke", new { });
        Assert.True(iptal.IsSuccessStatusCode, $"iptal başarısız: {(int)iptal.StatusCode}");

        // Gerçek makine bir sonraki açılışta yeniden kaydolur → artık yer var.
        Assert.Equal("active", await KaydetAsync("GERCEK-1"));
    }

    // ── C ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ C — KOTA DOLUYKEN bile yönetici bekleyen gerçek makineyi <b>onaylayabilir</b>.
    /// Bu, "model kullanıcıyı kilitliyor" iddiasının doğru olmadığını gösterir: kurtarma yolu vardır
    /// ve sahte kaydı silmeyi bile gerektirmez.
    /// </summary>
    [Fact]
    public async Task MAK_C_Kota_Dolu_Iken_Yonetici_Onaylayabilir()
    {
        await KaydetAsync("SAHTE-1");
        await KaydetAsync("SAHTE-2");
        Assert.Equal("pending", await KaydetAsync("GERCEK-1"));

        var id = MakineId("GERCEK-1");
        var onay = await _adminA.PostAsJsonAsync($"/api/machines/{id}/approve", new { });

        Assert.True(onay.IsSuccessStatusCode, $"onay başarısız: {(int)onay.StatusCode}");
        Assert.Equal("active", Oku<string>("SELECT status FROM sync_devices WHERE id=@i;", ("@i", id)));
        // Onay AYRICA cihaz jetonu üretir → makine gerçekten senkron yapabilir hâle gelir.
        Assert.False(string.IsNullOrEmpty(Oku<string>("SELECT token_hash FROM sync_devices WHERE id=@i;", ("@i", id))));
    }

    // ── D ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>D — aynı makine tekrar kurulunca YENİ satır açılmaz (kota boşa harcanmaz).</summary>
    [Fact]
    public async Task MAK_D_Ayni_Makine_Tekrar_Kurulunca_Kota_Tuketmez()
    {
        await KaydetAsync("GERCEK-1");
        await KaydetAsync("GERCEK-1");
        await KaydetAsync("GERCEK-1");

        Assert.Equal(1, Oku<long>("SELECT COUNT(*) FROM sync_devices WHERE company_id=@c AND device_name='GERCEK-1';",
            ("@c", CoA)));
    }

    // ── E ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ E — İPTAL EDİLMİŞ makine kendiliğinden aktifleşemez (yönetici kararı korunur).</summary>
    [Fact]
    public async Task MAK_E_Iptal_Edilen_Makine_Kendiliginden_Aktiflesemez()
    {
        await KaydetAsync("GERCEK-1");
        await _adminA.PostAsJsonAsync($"/api/machines/{MakineId("GERCEK-1")}/revoke", new { });

        var durum = await KaydetAsync("GERCEK-1");   // makine tekrar açılıyor

        Assert.Equal("revoked", durum);
        Assert.Equal("revoked", Oku<string>("SELECT status FROM sync_devices WHERE company_id=@c AND device_name='GERCEK-1';",
            ("@c", CoA)));
    }

    // ── G ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ G — A firmasının cihaz jetonu B firmasının verisini ÇEKEMEZ.
    /// HTTP koduna değil, dönen sayfanın İÇERİĞİNE bakılır.
    /// </summary>
    [Fact]
    public void MAK_G_Cihaz_Jetonu_Baska_Firmanin_Verisini_Cekemez()
    {
        var sA = new SessionContext("admA", CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var anahtar = _svc.Enrollment.CreateEnrollmentKey(sA);
        var cihaz = _svc.Enrollment.Enroll(CoA, anahtar, "A-CIHAZ");
        var jeton = _svc.Enrollment.ApproveDevice(sA, cihaz.DeviceId).Token;

        // İki firmaya da sunucu değişikliği yaz (B'ninki gizli kalmalı).
        Calistir("INSERT INTO server_changes(company_id, operation_id, entity_type, entity_id, payload_json, valid, created_at) " +
                 "VALUES(@c,'op-A','material','mA','{\"ad\":\"A-VERISI\"}',1,1);", ("@c", CoA));
        Calistir("INSERT INTO server_changes(company_id, operation_id, entity_type, entity_id, payload_json, valid, created_at) " +
                 "VALUES(@c,'op-B','material','mB','{\"ad\":\"B-GIZLI-VERISI\"}',1,1);", ("@c", CoB));

        var sayfa = _svc.Sync.Pull(jeton, 0, 100);
        var icerik = string.Join("|", sayfa.Items.Select(i => i.PayloadJson));

        Assert.Contains("A-VERISI", icerik, StringComparison.Ordinal);
        Assert.DoesNotContain("B-GIZLI-VERISI", icerik, StringComparison.Ordinal);
    }

    /// <summary>G/b — geçersiz cihaz jetonu hiçbir şey çekemez.</summary>
    [Fact]
    public void MAK_Gb_Uydurma_Jeton_Reddedilir()
        => Assert.Throws<ForbiddenException>(() => _svc.Sync.Pull("uydurma-jeton", 0, 100));
}
