using System.Net;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-04 — fotoğraflar cihaza iner (kullanıcı: "internete bağlı olmasam bile fotoğrafların sunucudan
/// cihazıma inmiş olması gerek; yalnız yükleyen bilgisayar görür uyarısı çıkmamalı").
/// FOB1-2: yeni <c>/api/photos/index</c> ucu — yalnız oturum firmasının fotoğrafları, oturumsuz erişim yok.
/// FOB3-5: masaüstü kaynak taraması — çevrimdışı okuma önbellekten, arka plan doldurma bağlı, eski metin yok.
/// </summary>
public class FotoOnbellekTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "FOB-A";
    private const string CoB = "FOB-B";
    private const string Pass = "Fob!2026";
    private ServerServices _svc = null!;
    private HttpClient _adminA = null!;
    private readonly string _fotoA = Guid.NewGuid().ToString("N");
    private readonly string _fotoB = Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
            Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                     "VALUES(@c,@n,1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", id), ("@n", ad));
        Foto(_fotoA, CoA, "arac-a");
        Foto(_fotoB, CoB, "arac-b");
        _svc.Users.EnsureInitialAdmin(CoA, "fob_admin_a", Pass, RoleKeys.CompanyAdmin);
        _adminA = await _host.LoginAsync("fob_admin_a", Pass, CoA);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private void Foto(string id, string firma, string kayit)
        => Calistir("INSERT INTO file_records(id,company_id,entity_type,entity_id,kind,storage_provider,storage_key," +
                    "mime,size_bytes,sha256,created_at,updated_at,version,is_deleted) " +
                    "VALUES(@id,@c,'vehicle',@e,'photo','local',@k,'image/jpeg',10,'abc',1,1,1,0);",
            ("@id", id), ("@c", firma), ("@e", kayit), ("@k", firma + "/vehicle/" + id + ".jpg"));

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task FOB1_Dizin_Yalniz_Kendi_Firmasinin_Fotograflarini_Verir()
    {
        var r = await _adminA.GetAsync("/api/photos/index");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var govde = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(govde);
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Contains(_fotoA, ids);
        Assert.DoesNotContain(_fotoB, ids);
        Assert.DoesNotContain("arac-b", govde);
        var a = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == _fotoA);
        Assert.Equal("vehicle", a.GetProperty("entityType").GetString());
        Assert.Equal("arac-a", a.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task FOB2_Oturumsuz_Dizin_Okunamaz()
    {
        var anonim = _host.CreateClient();
        var r = await anonim.GetAsync("/api/photos/index");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}

/// <summary>FOB3-5: masaüstü kaynak taraması. Sunucu gerektirmez — ayrı sınıfta, çünkü üstteki sınıf her test için
/// test sunucusu açıp oturum açıyor; tam takımın yükünde bu oturum açma zaman aşımına düşüp kaynak taramasını
/// yanlışlıkla kırmızıya çeviriyordu (2026-10-04 tam koşu).</summary>
public class FotoOnbellekKaynakTests
{
    private static string Kaynak(string goreli)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DepoWise.sln"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Desktop", goreli));
    }

    [Fact]
    public void FOB3_Cevrimdisi_Okuma_Cihaz_Onbellegini_Kullanir()
    {
        var src = Kaynak("DesktopPhotos.cs");
        Assert.Contains("return (CevrimdisiOku(s, entityType, entityId), true);", src);
        Assert.Contains("Onbellek.Oku(s.CompanyId, entityType, entityId, p.Id)", src);   // çevrimiçiyken de önbellek önce
        Assert.Contains("Onbellek.ListeYaz(", src);
        Assert.Contains("File.Move(gecici, yol, overwrite: true)", src);                 // yarım dosya kalmaz
    }

    [Fact]
    public void FOB4_Arka_Plan_Doldurma_Esitleme_Turuna_Bagli()
    {
        Assert.Contains("_ = DesktopPhotos.OnbellegiDoldurAsync(_session);", Kaynak(Path.Combine("ViewModels", "ShellViewModel.cs")));
        Assert.Contains("ListPhotoIndexAsync()", Kaynak("DesktopPhotos.cs"));
    }

    [Fact]
    public void FOB5_Yalniz_Bu_Bilgisayar_Metni_Kalmadi()
    {
        foreach (var f in new[] { Path.Combine("ViewModels", "MaterialsViewModel.cs"), Path.Combine("ViewModels", "VehiclesViewModel.cs"),
                     Path.Combine("Views", "MaterialQuickEditWindow.axaml.cs"), Path.Combine("Views", "VehicleQuickEditWindow.axaml.cs") })
        {
            var src = Kaynak(f);
            Assert.DoesNotContain("yalnız bu bilgisayardaki fotoğraflar", src);
            Assert.Contains("DesktopPhotos.CevrimdisiNotu", src);
        }
    }
}
