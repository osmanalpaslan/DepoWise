using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ BAKIM UYARISI → BAKIM KAYDI KÖPRÜSÜ (kullanıcı isteği 2026-09-02) ═══
///
/// Kullanıcı: "hangi araç ve hangi bakımın eksik olduğunu tespit edemiyorum. uyarılarda listelenen
/// bakımların araç kodu, plakası ve yüzde kaç aşım yapmış veya yaklaşmış göstermeli" + satıra
/// tıklayınca ilgili kayıt açılmalı.
///
/// Bu testler UI'yi değil, iki arayüzün de dayandığı SÖZLEŞMEYİ kilitler:
///  BK1 — Uyarı satırı araç KODU ve PLAKA'yı AYRI alanlarda döndürür.
///  BK2 — Uyarı satırı <c>vehicleId</c> döndürür (köprü bunsuz kurulamaz).
///  BK3 — Yüzde alanı (<c>progressText</c>) doldurulur.
///  BK4 — Plakasız araçta plaka alanı boş STRING değil, görünür bir tire olur (kolon kaymaz).
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class BakimUyariKopruTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "UYARI-A";
    private const string Pass = "Test!2026";

    private HttpClient _c = null!;
    private ServerServices _svc = null!;
    private SessionContext _s = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        var uid = _svc.Users.EnsureInitialAdmin(Co, "uyari_super", Pass, RoleKeys.SuperAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var sube = _svc.Branches.Create(_s, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _c = await _host.LoginAsync("uyari_super", Pass, Co, sube);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    /// <summary>Bakımı HİÇ yapılmamış, tanıma atanmış bir araç → kesin uyarı üretir (NeverPerformed).</summary>
    private (string VehicleId, string Kod) UyariUret(string kod, string? plaka)
    {
        var arac = _svc.Vehicles.Create(_s, new DepoWise.Infrastructure.Vehicles.NewVehicle(kod, Plate: plaka));
        var tanim = _svc.MaintenanceDefinitions.Create(_s,
            new NewMaintenanceDefinition("Uyarı Bakımı " + kod, 30m, "day", null, null));
        _svc.MaintenanceDefinitions.SetVehicles(_s, tanim, new[] { arac });
        return (arac, kod);
    }

    /// <summary>BK1/BK2/BK3 — kod, plaka, vehicleId ve yüzde alanları uçtan uca gelir.</summary>
    [Fact]
    public async Task BK1_Uyari_Satiri_Kod_Plaka_VehicleId_Ve_Yuzde_Dondurur()
    {
        var (aracId, kod) = UyariUret("UYR-001", "34 ABC 123");

        var liste = await ApiTestHost.JsonAsync(await _c.GetAsync("/api/maintenance/alerts"));
        var satir = liste.EnumerateArray().Single(x => x.GetProperty("vehicleId").GetString() == aracId);

        Assert.Equal(kod, satir.GetProperty("vehicleCode").GetString());        // BK1: yalnız İÇ KOD
        Assert.Equal("34 ABC 123", satir.GetProperty("plate").GetString());     // BK1: plaka AYRI alan
        Assert.Equal(aracId, satir.GetProperty("vehicleId").GetString());       // BK2: köprü anahtarı
        Assert.StartsWith("%", satir.GetProperty("progressText").GetString());  // BK3: yüzde
        Assert.False(string.IsNullOrWhiteSpace(satir.GetProperty("definition").GetString()));
    }

    /// <summary>BK4 — plakasız araçta plaka alanı boş bırakılmaz (kolon hizası bozulmasın).</summary>
    [Fact]
    public async Task BK4_Plakasiz_Aracta_Plaka_Alani_Tire_Olur()
    {
        var (aracId, _) = UyariUret("UYR-002", null);

        var liste = await ApiTestHost.JsonAsync(await _c.GetAsync("/api/maintenance/alerts"));
        var satir = liste.EnumerateArray().Single(x => x.GetProperty("vehicleId").GetString() == aracId);

        Assert.Equal("—", satir.GetProperty("plate").GetString());
    }
}
