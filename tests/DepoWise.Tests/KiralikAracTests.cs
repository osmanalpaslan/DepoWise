using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-10 (kullanıcı isteği) — KİRALIK ARAÇLAR + ARAÇ DEĞİŞİMİ.
/// KRA1: kiralık araç Araç Listesi'nde YOK, Kiralık Araçlar'da VAR; seçicilerde "(Kiralık)" etiketiyle ikisi birlikte.
/// KRA2: değişim → giden araç aynı işlemde PASİF + kira bitişi = yeni aracın başlangıcı; zincir iki yönde okunur.
/// KRA3: değişim kapıları — şirket aracı, zaten pasif araç ve başka firmanın aracı değiştirilemez.
/// KRA4: doğrulamalar — kiralayan firma + başlangıç zorunlu, bitiş başlangıçtan önce olamaz, bedel eksi olamaz.
/// KRA5: Rental=null güncelleme kira bilgisini SİLMEZ (bakım/hızlı düzenleme yolları); Rental ile güncellenir;
///       şirket aracına kira bilgisi yazılamaz.
/// KRA6: Kiralamayı Bitir → pasif + bitiş günü; şirket aracında reddedilir.
/// KRA7: tablo — tarih metni gg.aa.yyyy, kiralayan firma filtresi, tarih sıralaması kronolojik, Excel kolonları.
/// </summary>
public class KiralikAracTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "depowise_kra_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _a, _b;
    private readonly VehicleService _v;

    private static long Gun(int y, int m, int d) => new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public KiralikAracTests()
    {
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
        var users = new UserService(_f, new SystemClock());
        _a = new SessionContext(users.EnsureInitialAdmin("A", "a", "a123456", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _b = new SessionContext(users.EnsureInitialAdmin("B", "b", "b123456", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _v = new VehicleService(_f);
    }

    public void Dispose() { try { File.Delete(_db); } catch { } }

    private string Kiralik(string kod, long bas, string? yerine = null, string? neden = null, SessionContext? s = null)
        => _v.Create(s ?? _a, new NewVehicle(kod, Rental: new RentalInfo("Kiracı A.Ş.", bas, ReplacedVehicleId: yerine, ReplacementReason: neden)));

    [Fact]
    public void KRA1_Kiralik_Arac_Listesinde_Yok_Kiralik_Ekraninda_Var_Secicilerde_Etiketli()
    {
        var sirket = _v.Create(_a, new NewVehicle("SRK-01"));
        var kira = Kiralik("KRL-01", Gun(2026, 10, 1));

        var liste = _v.SearchGrid(_a, new VehicleGridFilter(), 1, 50);
        Assert.Equal(new[] { sirket }, liste.Items.Select(x => x.Id));
        var kiraListe = _v.SearchGrid(_a, new VehicleGridFilter(), 1, 50, rental: true);
        Assert.Equal(new[] { kira }, kiraListe.Items.Select(x => x.Id));
        Assert.Equal("Kiracı A.Ş.", kiraListe.Items[0].RentalCompany);

        var secici = _v.List(_a);
        Assert.Equal(2, secici.Count);
        Assert.EndsWith("(Kiralık)", secici.Single(x => x.Id == kira).Display);
        Assert.DoesNotContain("Kiralık", secici.Single(x => x.Id == sirket).Display);
        Assert.True(_v.Get(_a, kira).IsRental);
        Assert.False(_v.Get(_a, sirket).IsRental);
    }

    [Fact]
    public void KRA2_Degisimde_Giden_Arac_Otomatik_Pasif_ve_Zincir_Okunur()
    {
        var eski = Kiralik("KRL-01", Gun(2026, 9, 1));
        var yeni = Kiralik("KRL-02", Gun(2026, 10, 5), yerine: eski, neden: "Motor arızası");

        var e = _v.Get(_a, eski);
        Assert.Equal(VehicleStatus.Passive, e.Status);
        Assert.Equal(Gun(2026, 10, 5), e.RentalEnd);
        Assert.Equal("KRL-02", e.ReplacedByCode);

        var y = _v.Get(_a, yeni);
        Assert.Equal(VehicleStatus.Active, y.Status);
        Assert.Equal("KRL-01", y.ReplacedVehicleCode);
        Assert.Equal("Motor arızası", y.ReplacementReason);

        Assert.Equal(new[] { yeni }, _v.ListActiveRentals(_a).Select(x => x.Id));   // giden araç artık seçilemez
        Assert.Equal("KRL-01", _v.SearchGrid(_a, new VehicleGridFilter(), 1, 50, rental: true).Items.Single(x => x.Id == yeni).ReplacedVehicle);

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_id=@e AND after_json LIKE '%replacedBy%';";
        cmd.AddWithValue("@e", eski);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void KRA3_Degisim_Kapilari()
    {
        var sirket = _v.Create(_a, new NewVehicle("SRK-01"));
        Assert.Throws<InvalidOperationException>(() => Kiralik("KRL-X", Gun(2026, 10, 1), yerine: sirket));

        var eski = Kiralik("KRL-01", Gun(2026, 9, 1));
        Kiralik("KRL-02", Gun(2026, 10, 1), yerine: eski);
        Assert.Throws<InvalidOperationException>(() => Kiralik("KRL-03", Gun(2026, 10, 2), yerine: eski));   // zaten pasif

        var bArac = Kiralik("B-KRL", Gun(2026, 10, 1), s: _b);
        Assert.Throws<ForbiddenException>(() => Kiralik("KRL-04", Gun(2026, 10, 1), yerine: bArac));
        Assert.Equal(VehicleStatus.Active, _v.Get(_b, bArac).Status);   // B'nin aracına dokunulmadı
        // Başarısız değişim yarım kayıt bırakmaz (tek transaction).
        Assert.DoesNotContain(_v.List(_a), x => x.InternalCode is "KRL-X" or "KRL-03" or "KRL-04");
    }

    [Fact]
    public void KRA4_Dogrulamalar()
    {
        Assert.Throws<ArgumentException>(() => _v.Create(_a, new NewVehicle("K1", Rental: new RentalInfo(" ", Gun(2026, 1, 1)))));
        Assert.Throws<ArgumentException>(() => _v.Create(_a, new NewVehicle("K2", Rental: new RentalInfo("Firma", null))));
        Assert.Throws<ArgumentException>(() => _v.Create(_a, new NewVehicle("K3", Rental: new RentalInfo("Firma", Gun(2026, 2, 1), End: Gun(2026, 1, 1)))));
        Assert.Throws<ArgumentException>(() => _v.Create(_a, new NewVehicle("K4", Rental: new RentalInfo("Firma", Gun(2026, 2, 1), Price: -5m))));
        var ok = _v.Create(_a, new NewVehicle("K5", Rental: new RentalInfo("Firma", Gun(2026, 2, 1), Price: 1500m, PriceUnit: "bilinmeyen")));
        var d = _v.Get(_a, ok);
        Assert.Equal(1500m, d.RentalPrice);
        Assert.Equal(RentalPriceUnits.Day, d.RentalPriceUnit);   // bilinmeyen birim → günlük
    }

    [Fact]
    public void KRA5_Guncelleme_Kira_Bilgisini_Korur_ve_Degistirir()
    {
        var id = _v.Create(_a, new NewVehicle("KRL-01", Rental: new RentalInfo("Kiracı A.Ş.", Gun(2026, 9, 1), Price: 900m, PriceUnit: "month")));
        _v.SetStatus(_a, id, VehicleStatus.Faulty, "lastik");                                  // bakım ekranı yolu
        _v.Update(_a, id, new UpdateVehicle("34 K 01", null, VehicleStatus.Active, null));     // Rental=null
        var d = _v.Get(_a, id);
        Assert.Equal("Kiracı A.Ş.", d.RentalCompany);
        Assert.Equal(900m, d.RentalPrice);
        Assert.True(d.IsRental);

        _v.Update(_a, id, new UpdateVehicle("34 K 01", null, VehicleStatus.Active, null,
            Rental: new RentalInfo("Yeni Kiracı", Gun(2026, 9, 2), End: Gun(2026, 12, 31))));
        d = _v.Get(_a, id);
        Assert.Equal("Yeni Kiracı", d.RentalCompany);
        Assert.Equal(Gun(2026, 12, 31), d.RentalEnd);
        Assert.Null(d.RentalPrice);

        var sirket = _v.Create(_a, new NewVehicle("SRK-01"));
        Assert.Throws<ForbiddenException>(() => _v.Update(_a, sirket, new UpdateVehicle(null, null, VehicleStatus.Active, null,
            Rental: new RentalInfo("X", Gun(2026, 1, 1)))));
        Assert.False(_v.Get(_a, sirket).IsRental);
    }

    [Fact]
    public void KRA6_Kiralamayi_Bitir()
    {
        var id = Kiralik("KRL-01", Gun(2026, 9, 1));
        Assert.Throws<ArgumentException>(() => _v.EndRental(_a, id, Gun(2026, 8, 1)));
        _v.EndRental(_a, id, Gun(2026, 10, 10));
        var d = _v.Get(_a, id);
        Assert.Equal(VehicleStatus.Passive, d.Status);
        Assert.Equal(Gun(2026, 10, 10), d.RentalEnd);

        var sirket = _v.Create(_a, new NewVehicle("SRK-01"));
        Assert.Throws<InvalidOperationException>(() => _v.EndRental(_a, sirket, Gun(2026, 10, 10)));
        Assert.Throws<ForbiddenException>(() => _v.EndRental(_b, id, Gun(2026, 10, 10)));   // başka firma
    }

    [Fact]
    public void KRA7_Tablo_Tarih_Filtre_Siralama_ve_Excel()
    {
        Kiralik("KRL-A", Gun(2026, 10, 2));
        _v.Create(_a, new NewVehicle("KRL-B", Rental: new RentalInfo("Başka Firma", Gun(2025, 12, 31))));

        var rows = _v.SearchGrid(_a, new VehicleGridFilter(), 1, 50, VehicleListColumns.RentalStart, false, rental: true).Items;
        Assert.Equal(new[] { "KRL-B", "KRL-A" }, rows.Select(x => x.InternalCode));   // 2025 < 2026 (metin sırası tersini verirdi)
        Assert.Equal("31.12.2025", rows[0].RentalStart);
        Assert.Equal("02.10.2026", rows[1].RentalStart);

        var filtre = _v.SearchGrid(_a, new VehicleGridFilter(RentalCompany: "başka"), 1, 50, rental: true).Items;
        Assert.Equal("KRL-B", Assert.Single(filtre).InternalCode);
        var tarih = _v.SearchGrid(_a, new VehicleGridFilter(RentalStart: "10.2026"), 1, 50, rental: true).Items;
        Assert.Equal("KRL-A", Assert.Single(tarih).InternalCode);

        var t = VehicleService.ToTableModel(_v.SearchGridAll(_a, new VehicleGridFilter(), rental: true), rental: true);
        Assert.Equal("Kiralık Araçlar", t.Title);
        Assert.Equal(VehicleListColumns.RentalAll.Count, t.Headers.Count);
        Assert.Equal(t.Headers.Count, t.Rows[0].Count);
        Assert.Contains("Kiralayan Firma", t.Headers);
    }
}
