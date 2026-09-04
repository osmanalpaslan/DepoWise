using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// SEÇİCİ (lookup) UÇLARININ SESSİZ KESİLMESİ — İş A, 2026-08-09.
///
/// Bulgu: <c>/api/materials</c>, <c>/api/vehicles</c>, <c>/api/personnel</c> uçları SAYFALANMIŞTIR.
/// API <c>Page()</c> ile 500 ister ama <c>PageRequest.NormalizedLimit()</c> bunu <b>200'de keser</b>
/// (<c>MaxLimit = 200</c>). Canlıda 2463 malzeme vardır.
///
/// Web'de bazı seçiciler bu ucu ARAMA OLMADAN düz bir <c>MudSelect</c>'e yüklüyordu
/// (ör. Araç Şablonları → "Uyumlu Malzemeler"). Sonuç: kullanıcı 200'den sonraki hiçbir malzemeyi
/// SEÇEMİYOR ve bunu gösteren bir uyarı da yok — sessiz işlev kaybı.
///
/// Bu testler o sınırı ve <b>aramanın sınırın ötesine ulaşabildiğini</b> kanıtlar; düzeltme bu yüzden
/// "arama kutusu ekleme" değil, <b>sunucu-taraflı aramaya geçiş</b> olarak yapılmıştır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class SelectorTruncationTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "KESIK-A";
    private const string Admin = "kesik_admin";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private ServerServices _svc = null!;
    private SessionContext _s = null!;

    /// <summary>API'nin sayfa sınırı (PageRequest.MaxLimit). Test bunu VARSAYMAZ, koddan okur.</summary>
    private static int MaxLimit => DepoWise.Application.Common.PageRequest.MaxLimit;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = _svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // Sınırın ÜSTÜNDE malzeme üret — "yanlış yeşil" olmasın diye sınır koddan okunur.
        for (int i = 1; i <= MaxLimit + 25; i++)
            _svc.Materials.Create(_s, new NewMaterial($"KOD-{i:0000}", $"Malzeme {i:0000}"));

        _client = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<List<string>> NamesAsync(string path)
    {
        var r = await _client.GetAsync(path);
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).EnumerateArray()
            .Select(e => e.GetProperty("name").GetString() ?? "").ToList();
    }

    [Fact]
    public async Task Aramasiz_secici_ucu_SINIRDA_KESILIR()
    {
        // Bu, düzeltilecek bir "hata" değil, API'nin bilinçli sayfa sınırıdır. Testin işi bunu SABİTLEMEK:
        // web bu ucu aramasız kullanırsa kullanıcı sınırın ötesindeki kaydı göremez.
        var hepsi = await NamesAsync("/api/materials");
        Assert.Equal(MaxLimit, hepsi.Count);

        // Liste EN YENİDEN eskiye sıralıdır → kesilen taraf İLK oluşturulan kayıtlardır.
        // (Bu test yazılırken varsayım tersiydi; gerçek davranış ölçülüp düzeltildi.)
        Assert.Contains($"Malzeme {MaxLimit + 25:0000}", hepsi);   // en yeni: listede
        Assert.DoesNotContain("Malzeme 0001", hepsi);              // en eski: ERİŞİLEMEZ
    }

    [Fact]
    public async Task ARAMA_sinirin_otesindeki_kayda_ULASIR()
    {
        // Düzeltmenin dayanağı: aramasız listede GÖRÜNMEYEN en eski kayda arama ile ulaşılabiliyor.
        const string erisilemeyen = "Malzeme 0001";
        Assert.DoesNotContain(erisilemeyen, await NamesAsync("/api/materials"));   // önce yokluğunu kanıtla

        var sonuc = await NamesAsync($"/api/materials?search={Uri.EscapeDataString(erisilemeyen)}");
        Assert.Contains(erisilemeyen, sonuc);
        Assert.True(sonuc.Count <= MaxLimit);
    }

    [Fact]
    public async Task Arama_BASKA_firmanin_kaydini_getirmez()
    {
        // Firma izolasyonu UI filtresine değil, SERVİS katmanına bağlıdır — arama yolu da dahil.
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('KESIK-B', 'KESIK-B', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }
        var uidB = _svc.Users.EnsureInitialAdmin("KESIK-B", "kesik_b", Pass, RoleKeys.CompanyAdmin);
        var sB = new SessionContext(uidB, "KESIK-B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _svc.Materials.Create(sB, new NewMaterial("B-GIZLI", "B firmasinin gizli malzemesi"));

        var sonuc = await NamesAsync("/api/materials?search=gizli");
        Assert.DoesNotContain("B firmasinin gizli malzemesi", sonuc);
    }

    [Fact]
    public async Task Bos_arama_ilk_sayfayi_verir_TAMAMINI_DEGIL()
    {
        // Kullanıcı hiçbir şey yazmadan seçiciyi açtığında: makul bir ilk liste gelir, 2463 kayıt DEĞİL.
        var bos = await NamesAsync("/api/materials?search=");
        Assert.Equal(MaxLimit, bos.Count);
    }

    // ── PERSONEL: arama İş A'da EKLENDİ (uçta hiç yoktu) ──────────────────────────────────

    /// <summary>
    /// ⚠️ 2026-09-04 (LST-01): <c>/api/personnel</c> artık <c>{ items, hasMore }</c> döner.
    /// Şekil BİLİNÇLİ olarak değişti: liste 200 kayıtta kesiliyordu ve kesildiğini SÖYLEMİYORDU;
    /// <c>hasMore</c> tam olarak bu sessizliği bitirmek için eklendi (bu test dosyasının anlattığı
    /// kusurun aynısı). Testlerin İDDİALARI değişmedi — yalnız yanıt okuma biçimi uyarlandı.
    /// </summary>
    private async Task<List<string>> PersonnelNamesAsync(string path)
    {
        var r = await _client.GetAsync(path);
        r.EnsureSuccessStatusCode();
        var govde = await ApiTestHost.JsonAsync(r);
        var dizi = govde.ValueKind == System.Text.Json.JsonValueKind.Object ? govde.GetProperty("items") : govde;
        return dizi.EnumerateArray()
            .Select(e => e.GetProperty("fullName").GetString() ?? "").ToList();
    }

    /// <summary>⭐ LST-01: kesilme artık GÖRÜNÜR. Bu dosyanın anlattığı "sessiz işlev kaybı" tam olarak
    /// buydu; sunucu artık kesildiğini söylüyor ve arayüz kullanıcıyı aramaya yönlendirebiliyor.</summary>
    private async Task<bool> PersonnelHasMoreAsync(string path)
    {
        var r = await _client.GetAsync(path);
        r.EnsureSuccessStatusCode();
        var govde = await ApiTestHost.JsonAsync(r);
        return govde.ValueKind == System.Text.Json.JsonValueKind.Object
               && govde.TryGetProperty("hasMore", out var hm) && hm.GetBoolean();
    }

    private void SeedPersonnel(int count)
    {
        for (int i = 1; i <= count; i++)
            _svc.Personnel.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel($"Personel {i:0000}", null, null, null));
    }

    [Fact]
    public async Task Personel_ARAMA_sinirin_otesindeki_kayda_ULASIR()
    {
        SeedPersonnel(MaxLimit + 25);

        // Aramasız liste sınırda kesilir → en eski personel GÖRÜNMEZ.
        var hepsi = await PersonnelNamesAsync("/api/personnel");
        Assert.Equal(MaxLimit, hepsi.Count);
        Assert.DoesNotContain("Personel 0001", hepsi);

        // ⭐ LST-01 (2026-09-04): kesilme artık SESSİZ DEĞİL. Bu dosyanın anlattığı kusurun özü
        // "kullanıcı kesildiğini bilmiyor" idi; sunucu artık söylüyor, arayüz de uyarı gösteriyor.
        Assert.True(await PersonnelHasMoreAsync("/api/personnel"),
            "Liste kesildi ama hasMore=false — sessiz kesilme geri döndü.");

        // Sınırın altındaki sonuçta uyarı ÇIKMAZ (yanlış alarm kullanıcıyı köreltir).
        Assert.False(await PersonnelHasMoreAsync($"/api/personnel?search={Uri.EscapeDataString("Personel 0001")}"));

        // Arama ona ULAŞIR (İş A'dan önce bu uçta arama parametresi HİÇ YOKTU).
        var bulunan = await PersonnelNamesAsync($"/api/personnel?search={Uri.EscapeDataString("Personel 0001")}");
        Assert.Contains("Personel 0001", bulunan);
    }

    [Fact]
    public async Task Personel_aramasi_BASKA_firmanin_kaydini_getirmez()
    {
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('KESIK-P', 'KESIK-P', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }
        var uidP = _svc.Users.EnsureInitialAdmin("KESIK-P", "kesik_p", Pass, RoleKeys.CompanyAdmin);
        var sP = new SessionContext(uidP, "KESIK-P", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _svc.Personnel.Create(sP, new DepoWise.Infrastructure.Org.NewPersonnel("Gizli Personel", null, null, null));

        // Firma izolasyonu SERVİS katmanındadır; arama onu delemez.
        Assert.DoesNotContain("Gizli Personel", await PersonnelNamesAsync("/api/personnel?search=Gizli"));
    }

    [Fact]
    public async Task Personel_bos_arama_ESKI_davranisi_korur()
    {
        SeedPersonnel(10);
        var aramasiz = await PersonnelNamesAsync("/api/personnel");
        var bosArama = await PersonnelNamesAsync("/api/personnel?search=");
        Assert.Equal(aramasiz, bosArama);   // "search=" boşsa hiçbir şey değişmemeli (geriye uyumlu)
    }

    [Fact]
    public async Task Personel_aramasi_TURKCE_karakter_dogru_esler()
    {
        _svc.Personnel.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("İsmail Şahin", null, null, null));
        _svc.Personnel.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Ahmet Yilmaz", null, null, null));

        var sonuc = await PersonnelNamesAsync($"/api/personnel?search={Uri.EscapeDataString("şahin")}");
        Assert.Contains("İsmail Şahin", sonuc);
        Assert.DoesNotContain("Ahmet Yilmaz", sonuc);
    }
}
