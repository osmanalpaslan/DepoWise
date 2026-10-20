using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-36 (AR-GE 2026-10-19, 05 F-078) — SAYAÇ KURALLARI ═══
///  SK1 — Geçmiş: araç kartı + yakıt okumaları tarih sırasıyla; son okuma ve eski sayaç uyarısı
///  SK2 — Geçersiz say: hatalı-yüksek okuma kayıt iptal edilmeden hesaptan çıkar, araç sayacı düşer; geri alınabilir
///  SK3 — Yetki (ters kayıt düğmesi), gerekçe, firma ayrımı; bilinmeyen kaynak
///  SK4 — Geriye tarihli okuma: önceki/sonraki okumalarla uyuşmazsa uyarı, uyuşursa yok; geçersiz okuma kıyasa girmez
///  SK5 — Migration112 tekrar çalışabilir; eski şemada hesap eskisi gibi, geçersiz sayma anlaşılır hata
///  SK6 — İki platform + API (kaynak kilidi)
/// </summary>
public class SayacKurallariTests : IDisposable
{
    private const string Co = "SYK";
    private const long G = 86_400_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_syk_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly FuelService _yakit;
    private readonly SayacService _sayac;
    private readonly VehicleService _araclar;
    private readonly string _arac;
    private readonly long _bugun;

    public SayacKurallariTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "syk", "Sayac!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _araclar = new VehicleService(_f);
        _arac = _araclar.Create(_s, new NewVehicle("SK-1", "06 SK 1", CurrentMeter: 1_000m));
        _yakit = new FuelService(_f);
        _sayac = new SayacService(_f);
        var simdi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _bugun = simdi - simdi % G;
        _yakit.AddDepotEntry(_s, new NewDepotEntry(1_000m, 40m, "TRY", EntryDate: _bugun - 40 * G), Op());
        // Araç 40 gün önce kaydedilmiş gibi: kart okuması yakıt fişlerinden ÖNCE olsun (gerçek sıra).
        Exec($"UPDATE vehicle_meter_logs SET created_at={_bugun - 40 * G} WHERE vehicle_id='{_arac}';");
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
    private decimal Sayac() => _araclar.GetMeter(_s, _arac);
    private string Dagit(decimal sayac, long tarih) => _yakit.Distribute(_s, new NewDistribution(_arac, 10m, sayac, DistributionDate: tarih), Op());

    [Fact]
    public void SK1_Gecmis_Ve_Eski_Sayac()
    {
        Dagit(1_200m, _bugun - 35 * G);
        Dagit(1_300m, _bugun - 33 * G);
        var g = _sayac.Gecmis(_s, _arac);
        Assert.Equal(3, g.Count);                         // araç kartı + iki yakıt
        Assert.Equal(1_300m, g[0].Deger);                 // en yeni üstte
        Assert.Contains("Yakıt", g[0].Satir);
        Assert.Contains("gündür güncellenmedi", _sayac.EskiSayacUyarisi(_s, _arac));
        Dagit(1_400m, _bugun);
        Assert.Null(_sayac.EskiSayacUyarisi(_s, _arac));
    }

    [Fact]
    public void SK2_Gecersiz_Say_Ve_Geri_Al()
    {
        Dagit(1_200m, _bugun - 2 * G);
        var hatali = Dagit(1_550_000m, _bugun - G);      // fişe fazladan basamak
        Assert.Equal(1_550_000m, Sayac());
        _sayac.GecersizSay(_s, _arac, "yakit", hatali, "basamak hatası");
        Assert.Equal(1_200m, Sayac());
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT is_deleted FROM fuel_distributions WHERE id='{hatali}';")));   // kayıt geçerli
        Assert.True(_sayac.Gecmis(_s, _arac).Single(x => x.KaynakId == hatali).Gecersiz);
        Assert.Contains("GEÇERSİZ", _sayac.Gecmis(_s, _arac).Single(x => x.KaynakId == hatali).Satir);
        _sayac.GecersizSay(_s, _arac, "yakit", hatali, "yanlış işaretlendi", gecersiz: false);
        Assert.Equal(1_550_000m, Sayac());
        Assert.Contains("sayacGecersiz", (string)Tek($"SELECT after_json FROM audit_logs WHERE entity_type='vehicle' AND entity_id='{_arac}' ORDER BY created_at DESC, rowid DESC LIMIT 1;")!);
    }

    private object? Tek(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    [Fact]
    public void SK3_Yetki_Gerekce_Firma()
    {
        var hatali = Dagit(9_999_999m, _bugun);
        var duzenleyici = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("vehicles", true, true, true, false) }));   // ters kayıt düğmesi YOK
        Assert.Throws<ForbiddenException>(() => _sayac.GecersizSay(duzenleyici, _arac, "yakit", hatali, "x"));
        Assert.Throws<ArgumentException>(() => _sayac.GecersizSay(_s, _arac, "yakit", hatali, " "));
        Assert.Throws<ArgumentException>(() => _sayac.GecersizSay(_s, _arac, "bilinmeyen", hatali, "x"));

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('SYB','SYB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("SYB", "syb", "Sayac!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "SYB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _sayac.GecersizSay(b, _arac, "yakit", hatali, "x"));
        Assert.Throws<ForbiddenException>(() => _sayac.Gecmis(b, _arac));
        Assert.Equal(9_999_999m, Sayac());
    }

    [Fact]
    public void SK4_Geriye_Tarihli_Okuma_Tutarliligi()
    {
        Dagit(1_200m, _bugun - 10 * G);
        Dagit(1_500m, _bugun - 2 * G);
        Assert.Null(_sayac.Tutarlilik(_s, _arac, _bugun - 5 * G, 1_300m));                    // arada → uyumlu
        Assert.Contains("ÖNCEKİ", _sayac.Tutarlilik(_s, _arac, _bugun - 5 * G, 1_100m));     // öncekinden küçük
        Assert.Contains("SONRAKİ", _sayac.Tutarlilik(_s, _arac, _bugun - 5 * G, 1_600m));    // sonrakinden büyük
        Assert.Null(_sayac.Tutarlilik(_s, _arac, _bugun, 1_600m));                            // bugün, en yeni
        var hatali = Dagit(9_000m, _bugun - 3 * G);
        _sayac.GecersizSay(_s, _arac, "yakit", hatali, "hatalı");
        Assert.Null(_sayac.Tutarlilik(_s, _arac, _bugun - 5 * G, 1_300m));                    // geçersiz okuma kıyasa girmez
    }

    [Fact]
    public void SK5_Migration_Ve_Eski_Sema()
    {
        using (var c = _f.Create()) new Migration112_MeterVoid().Up(c, null!);   // tekrar çalışabilir
        var d = Dagit(2_000m, _bugun);
        Exec("ALTER TABLE fuel_distributions DROP COLUMN meter_void;");
        Assert.Equal(2_000m, Sayac());
        Exec($"UPDATE vehicles SET current_meter='5' WHERE id='{_arac}';");   // bozuk değer
        Assert.True(_araclar.RecalculateMeter(_s, _arac, "test"));   // eski şemada (kolon yok) hesap çalışır ve düzeltir
        Assert.Equal(2_000m, Sayac());
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _sayac.GecersizSay(_s, _arac, "yakit", d, "x")).Message);
        Assert.False(_sayac.Gecmis(_s, _arac).Single(x => x.KaynakId == d).Gecersiz);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void SK6_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/vehicles/{id}/meter-history\"", api);
        Assert.Contains("app.MapPost(\"/api/vehicles/{id}/meter-void\"", api);
        Assert.Contains("app.MapGet(\"/api/vehicles/{id}/meter-check\"", api);
        Assert.Contains("DesktopServices.Sayac.GecersizSay(", Oku("src/DepoWise.Desktop/ViewModels/VehiclesViewModel.cs"));
        Assert.Contains("SayacGecersizSayCommand", Oku("src/DepoWise.Desktop/Views/VehiclesView.axaml"));
        Assert.Contains("DesktopServices.Sayac.Tutarlilik(", Oku("src/DepoWise.Desktop/ViewModels/FuelViewModel.cs"));
        Assert.Contains("/meter-void", Oku("src/DepoWise.Web/Components/Pages/Vehicles.razor"));
        Assert.Contains("/meter-check?date=", Oku("src/DepoWise.Web/Components/Pages/Fuel.razor"));
    }
}
