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
}
