using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-44 (AR-GE 2026-10-19, 05 F-117) — YAKITTA ŞÜPHELİ GİRİŞLERİ İŞARETLEME ═══
///  YS1 — Normal kayıtlar işaretlenmez
///  YS2 — Tüketim sapması (aracın ortancasının 2,5 katından fazla) işaretlenir; az örnekte sapma kuralı çalışmaz
///  YS3 — Şüpheli sayaç sıçraması, sayaç geri, aynı gün iki dağıtım işaretlenir; geçersiz sayılan sayaç sapmaya girmez
///  YS4 — Kayıt değişmez (yalnız işaret); firma ayrımı; iki platform + API kaynak kilidi
/// </summary>
public class YakitSupheTests : IDisposable
{
    private const string Co = "YSP";
    private const long G = 86_400_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_ysp_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly FuelService _yakit;
    private readonly YakitSupheService _suphe;
    private readonly string _arac;
    private readonly long _t0;

    public YakitSupheTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "ysp", "Supheli!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _arac = new VehicleService(_f).Create(_s, new NewVehicle("YS-1", "06 YS 1", CurrentMeter: 10_000m));
        _yakit = new FuelService(_f);
        _suphe = new YakitSupheService(_f);
        var n = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _t0 = n - n % G - 60 * G;
        _yakit.AddDepotEntry(_s, new NewDepotEntry(5_000m, 40m, "TRY", EntryDate: _t0 - G), "d");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private string D(decimal litre, decimal sayac, int gun) => _yakit.Distribute(_s, new NewDistribution(_arac, litre, sayac, DistributionDate: _t0 + gun * G), Op());

    /// <summary>Her 1.000 km'de 100 L — ortanca 0,1 L/km.</summary>
    private void Normal(int adet)
    {
        for (int i = 1; i <= adet; i++) D(100m, 10_000m + i * 1_000m, i);
    }

    [Fact]
    public void YS1_Normal_Kayit_Isaretlenmez()
    {
        Normal(5);
        Assert.Empty(_suphe.Supheliler(_s));
    }

    [Fact]
    public void YS2_Tuketim_Sapmasi()
    {
        Normal(5);
        var fazla = D(400m, 16_000m, 6);   // 1.000 km'de 400 L → 4 kat
        var s = Assert.Single(_suphe.Supheliler(_s));
        Assert.Equal(fazla, s.DistributionId);
        Assert.Contains(s.Nedenler, n => n.Contains("fazla yakıt"));

        // Az örnek (3 kayıt) → sapma kuralı çalışmaz
        var a2 = new VehicleService(_f).Create(_s, new NewVehicle("YS-2", "06 YS 2", CurrentMeter: 1_000m));
        _yakit.Distribute(_s, new NewDistribution(a2, 10m, 1_100m, DistributionDate: _t0 + G), Op());
        _yakit.Distribute(_s, new NewDistribution(a2, 90m, 1_200m, DistributionDate: _t0 + 2 * G), Op());
        Assert.DoesNotContain(_suphe.Supheliler(_s), x => x.VehicleId == a2);
    }

    [Fact]
    public void YS3_Sicrama_Geri_Ayni_Gun_Gecersiz()
    {
        Normal(4);
        var sicrama = D(100m, 150_000m, 6);                       // 14.000 → 150.000
        Assert.Contains(_suphe.Supheliler(_s).Single(x => x.DistributionId == sicrama).Nedenler, n => n.Contains("sıçrama"));

        new SayacService(_f).GecersizSay(_s, _arac, "yakit", sicrama, "basamak hatası");
        Assert.DoesNotContain(_suphe.Supheliler(_s), x => x.DistributionId == sicrama && x.Nedenler.Any(n => n.Contains("sıçrama")));

        var a = D(50m, 14_500m, 8);
        var b = D(50m, 14_900m, 8);                               // aynı gün
        var sup = _suphe.Supheliler(_s);
        Assert.Contains(sup.Single(x => x.DistributionId == a).Nedenler, n => n.Contains("aynı gün"));
        Assert.Contains(sup.Single(x => x.DistributionId == b).Nedenler, n => n.Contains("aynı gün"));

        var geri = _yakit.Distribute(_s, new NewDistribution(_arac, 20m, 12_000m, DistributionDate: _t0 + 9 * G, PrevMeter: 14_900m), Op());
        Assert.Contains(_suphe.Supheliler(_s).Single(x => x.DistributionId == geri).Nedenler, n => n.Contains("geri"));
        Assert.Empty(_suphe.Supheliler(_s, _t0 + 20 * G, _t0 + 30 * G));   // dönem süzgeci
    }

    [Fact]
    public void YS4_Kayit_Degismez_Firma_Kaynak()
    {
        Normal(5);
        var fazla = D(400m, 16_000m, 6);
        string Satir() { using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT liters || '|' || current_meter || '|' || is_deleted FROM fuel_distributions WHERE id='{fazla}';"; return (string)cmd.ExecuteScalar()!; }
        var once = Satir();
        _suphe.Supheliler(_s);
        Assert.Equal(once, Satir());

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('YSB','YSB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("YSB", "ysb", "Supheli!2026", RoleKeys.CompanyAdmin);
        Assert.Empty(_suphe.Supheliler(new SessionContext(uid, "YSB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)));

        string Kok() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent; return d!.FullName; }
        string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));
        Assert.Contains("app.MapGet(\"/api/fuel/suspicious\"", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("DesktopServices.YakitSuphe.Supheliler(", Oku("src/DepoWise.Desktop/ViewModels/FuelViewModel.cs"));
        Assert.Contains("SupheliBasligi", Oku("src/DepoWise.Desktop/Views/FuelView.axaml"));
        Assert.Contains("/api/fuel/suspicious", Oku("src/DepoWise.Web/Components/Pages/Fuel.razor"));
    }
}
