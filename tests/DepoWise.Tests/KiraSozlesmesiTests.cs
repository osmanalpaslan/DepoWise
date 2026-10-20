using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-39 (AR-GE 2026-10-19, 05 F-084, K-19 "a + çift yön") — KİRALIK MAKİNE SÖZLEŞMESİ ═══
///  KS1 — Sözleşme: cari + yön kaydedilir; kiralık olmayan araç / yabancı cari / geçersiz yön reddedilir; cari borcu YAZILMAZ
///  KS2 — Dönem raporu: gün biriminde kesişen gün × fiyat; ay biriminde gün/30; maliyet ve gelir ayrı
///  KS3 — Saat biriminde dönemdeki sayaç farkı × fiyat
///  KS4 — Bitiş uyarısı: 15 gün içinde biten ve süresi geçmiş (pasife alınmamış) kiralar; pasif olan uyarı vermez
///  KS5 — Firma ayrımı + eski şema; iki platform/API kaynak kilidi
/// </summary>
public class KiraSozlesmesiTests : IDisposable
{
    private const string Co = "KRS";
    private const long G = 86_400_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_krs_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly TestClock _saat = new();
    private readonly VehicleService _araclar;
    private readonly KiraService _kira;
    private readonly string _cari;
    private readonly long _bugun;

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_000); }

    public KiraSozlesmesiTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "krs", "Kira!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _araclar = new VehicleService(_f, _saat);
        _kira = new KiraService(_f, _saat);
        _cari = new PartyService(_f, _saat).Create(_s, new NewParty("K-1", "Vinç Kiralama A.Ş.", PartyTypes.Both));
        var n = _saat.UtcNow.ToUnixTimeMilliseconds();
        _bugun = n - n % G;
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

    private object? Tek(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private string Kiralik(string kod, decimal fiyat, string birim, long bas, long? bit = null, string meterUnit = "km")
        => _araclar.Create(_s, new NewVehicle(kod, kod, MeterUnit: meterUnit, Rental: new RentalInfo("Serbest metin", bas, bit, fiyat, birim)));

    [Fact]
    public void KS1_Sozlesme()
    {
        var a = Kiralik("KR-1", 1000m, "day", _bugun - 10 * G);
        _kira.SozlesmeAyarla(_s, a, _cari, KiraService.Disari);
        Assert.Equal(_cari, Tek($"SELECT rental_party_id FROM vehicles WHERE id='{a}';"));
        Assert.Equal("out", Tek($"SELECT rental_direction FROM vehicles WHERE id='{a}';"));
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM party_ledger WHERE company_id='{Co}';")));   // K-19: otomatik cari kaydı yok

        var ozmal = _araclar.Create(_s, new NewVehicle("OZ-1", "OZ-1"));
        Assert.Throws<ForbiddenException>(() => _kira.SozlesmeAyarla(_s, ozmal, _cari, KiraService.Iceri));
        Assert.Throws<ArgumentException>(() => _kira.SozlesmeAyarla(_s, a, "yok", KiraService.Iceri));
        Assert.Throws<ArgumentException>(() => _kira.SozlesmeAyarla(_s, a, _cari, "x"));
    }

    [Fact]
    public void KS2_Donem_Raporu_Gun_Ay()
    {
        var gunluk = Kiralik("KR-G", 1000m, "day", _bugun - 5 * G);                 // dönemde 6 gün (bugün dahil)
        var aylik = Kiralik("KR-A", 30_000m, "month", _bugun - 40 * G);             // dönemin tamamı: 30 gün = 1 ay
        _kira.SozlesmeAyarla(_s, aylik, _cari, KiraService.Disari);
        var rows = _kira.DonemRaporu(_s, _bugun - 29 * G, _bugun);
        var g = rows.Single(x => x.VehicleId == gunluk);
        Assert.Equal(6m, g.Miktar);
        Assert.Equal(6000m, g.Tutar);
        Assert.False(g.GelirMi);
        Assert.Contains("Serbest metin", g.Ozet);   // cari bağlanmamışsa serbest metin görünür
        var ay = rows.Single(x => x.VehicleId == aylik);
        Assert.Equal(1m, ay.Miktar);
        Assert.Equal(30_000m, ay.Tutar);
        Assert.True(ay.GelirMi);
        Assert.Contains("Vinç Kiralama", ay.Ozet);
        Assert.Throws<ArgumentException>(() => _kira.DonemRaporu(_s, _bugun, _bugun - G));
        Assert.Empty(_kira.DonemRaporu(_s, _bugun - 100 * G, _bugun - 50 * G).Where(x => x.VehicleId == gunluk));   // dönem dışı
    }

    [Fact]
    public void KS3_Saatlik_Sayac_Farki()
    {
        var a = Kiralik("KR-S", 500m, "hour", _bugun - 20 * G, meterUnit: "hour");
        var yakit = new DepoWise.Infrastructure.Operations.FuelService(_f, _saat);
        yakit.AddDepotEntry(_s, new DepoWise.Infrastructure.Operations.NewDepotEntry(500m, 40m, "TRY", EntryDate: _bugun - 30 * G), "d1");
        yakit.Distribute(_s, new DepoWise.Infrastructure.Operations.NewDistribution(a, 10m, 100m, DistributionDate: _bugun - 10 * G), "y1");
        yakit.Distribute(_s, new DepoWise.Infrastructure.Operations.NewDistribution(a, 10m, 140m, DistributionDate: _bugun - 2 * G), "y2");
        var r = _kira.DonemRaporu(_s, _bugun - 15 * G, _bugun).Single(x => x.VehicleId == a);
        Assert.Equal(40m, r.Miktar);
        Assert.Equal(20_000m, r.Tutar);
    }

    [Fact]
    public void KS4_Bitis_Uyarisi()
    {
        var yakin = Kiralik("KR-Y", 1m, "day", _bugun - 30 * G, _bugun + 5 * G);
        var gecmis = Kiralik("KR-P", 1m, "day", _bugun - 30 * G, _bugun - 3 * G);
        Kiralik("KR-U", 1m, "day", _bugun - 30 * G, _bugun + 60 * G);               // uzak → uyarı yok
        var iade = Kiralik("KR-I", 1m, "day", _bugun - 30 * G, _bugun - 1 * G);
        _araclar.EndRental(_s, iade, _bugun - G);                                    // pasife alındı → uyarı yok
        var u = _kira.BitisUyarilari(_s);
        Assert.Equal(2, u.Count);
        Assert.Equal(gecmis, u[0].VehicleId);
        Assert.Contains("geçti", u[0].Metin);
        Assert.Equal(yakin, u[1].VehicleId);
        Assert.Equal(5, u[1].KalanGun);
    }

    [Fact]
    public void KS5_Firma_Eski_Sema_Kaynak_Kilidi()
    {
        var a = Kiralik("KR-F", 1m, "day", _bugun - 3 * G);
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('KRB','KRB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("KRB", "krb", "Kira!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "KRB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _kira.SozlesmeAyarla(b, a, null, KiraService.Iceri));
        Assert.Empty(_kira.DonemRaporu(b, _bugun - 10 * G, _bugun));

        Exec("ALTER TABLE vehicles DROP COLUMN rental_direction;");
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() => _kira.SozlesmeAyarla(_s, a, _cari, KiraService.Iceri)).Message);
        Assert.Single(_kira.DonemRaporu(_s, _bugun - 10 * G, _bugun));               // eski şemada rapor çalışır (yön = maliyet)
        using (var c = _f.Create()) new Migration114_RentalContract().Up(c, null!);

        string Kok() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent; return d!.FullName; }
        string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/vehicles/rental/report\"", api);
        Assert.Contains("app.MapPut(\"/api/vehicles/{id}/rental-contract\"", api);
        Assert.Contains("DesktopServices.Kira.SozlesmeAyarla(", Oku("src/DepoWise.Desktop/ViewModels/VehiclesViewModel.cs"));
        Assert.Contains("KiraRaporuHesaplaCommand", Oku("src/DepoWise.Desktop/Views/VehiclesView.axaml"));
        var web = Oku("src/DepoWise.Web/Components/Pages/Vehicles.razor");
        Assert.Contains("/rental-contract", web);
        Assert.Contains("/api/vehicles/rental/report", web);
        Assert.Contains("/api/vehicles/rental/warnings", web);
    }
}
