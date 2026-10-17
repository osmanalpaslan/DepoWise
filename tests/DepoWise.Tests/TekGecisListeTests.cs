using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-85 (AR-GE 2026-10-17) — SearchGridAll TEK GEÇİŞ ═══
///
/// "Tümü" (Excel'e aktar + hücre filtresi seçenekleri) eskiden 500'lük sayfalarla her sayfada yeniden COUNT + OFFSET
/// çalıştırıyordu → süre satır sayısıyla KARESEL büyüyordu (07 §4.2: 40 bin satırda 23–50 sn).
///
///  TGL1 — Malzeme: 1.150 satırda "tümü" = sayfaların birleşimi (aynı sıra), filtreli ve sıralı da; SQL komut sayısı sabit
///  TGL2 — Araç: aynı denklik + komut sayısı satır sayısından bağımsız
///  TGL3 — Günlük faaliyet: 1.100 satırda aynı denklik (varsayılan sıra: en yeni üstte) + komut sayısı
/// </summary>
public class TekGecisListeTests : IDisposable
{
    private const string Co = "TGL";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;

    public TekGecisListeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_tgl_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin_tgl", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { } }

    private static List<T> Sayfalar<T>(Func<int, GridResult<T>> sayfa)
    {
        var hepsi = new List<T>();
        for (int p = 1; ; p++)
        {
            var r = sayfa(p);
            hepsi.AddRange(r.Items);
            if (p >= r.TotalPages || r.Items.Count == 0) break;
        }
        return hepsi;
    }

    [Fact]
    public void TGL1_Malzeme_Tumu_Sayfalarla_Ayni_Ve_Tek_Sorgu()
    {
        var svc = new MaterialService(_f);
        for (int i = 0; i < 1150; i++) svc.Create(_admin, new NewMaterial($"M-{i:D5}", i % 3 == 0 ? $"Vida {i}" : $"Somun {i}"));

        foreach (var (filtre, sira, ters) in new (MaterialGridFilter, string?, bool)[]
                 {
                     (new MaterialGridFilter(), null, false),
                     (new MaterialGridFilter(), "code", true),
                     (new MaterialGridFilter(Name: "Vida"), "name", false),
                 })
        {
            var tumu = svc.SearchGridAll(_admin, filtre, sira, ters);
            var sayfali = Sayfalar(p => svc.SearchGrid(_admin, filtre, p, 500, sira, ters));
            Assert.Equal(sayfali.Select(x => x.Id), tumu.Select(x => x.Id));
        }
        Assert.Equal(1150, svc.SearchGridAll(_admin, new MaterialGridFilter()).Count);

        var sayan = new SayanSqlite(_f);
        new MaterialService(sayan).SearchGridAll(_admin, new MaterialGridFilter());
        var buyuk = sayan.KomutSayisi;
        Assert.True(buyuk <= 2, $"1.150 satırlık 'tümü' {buyuk} SQL komutu çalıştırdı — tek geçiş olmalı");
    }

    [Fact]
    public void TGL2_Arac_Tumu_Sayfalarla_Ayni_Ve_Tek_Sorgu()
    {
        var svc = new VehicleService(_f);
        for (int i = 0; i < 1100; i++) svc.Create(_admin, new NewVehicle($"A-{i:D5}", Plate: $"34 TG {i:D4}"));
        var tumu = svc.SearchGridAll(_admin, new VehicleGridFilter());
        var sayfali = Sayfalar(p => svc.SearchGrid(_admin, new VehicleGridFilter(), p, 500));
        Assert.Equal(1100, tumu.Count);
        Assert.Equal(sayfali.Select(x => x.Id), tumu.Select(x => x.Id));

        var sayan = new SayanSqlite(_f);
        new VehicleService(sayan).SearchGridAll(_admin, new VehicleGridFilter());
        Assert.True(sayan.KomutSayisi <= 2, $"'tümü' {sayan.KomutSayisi} SQL komutu çalıştırdı");
    }

    [Fact]
    public void TGL3_Gunluk_Faaliyet_Tumu_Sayfalarla_Ayni_Ve_Tek_Sorgu()
    {
        using (var conn = _f.Create())
        using (var tx = conn.BeginTransaction())
        {
            for (int i = 0; i < 1100; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO daily_activities(id, company_id, activity_type, movement_kind, description, activity_date, operation_id, created_at, updated_at) " +
                                  "VALUES(@id, @c, 'movement', 'movement', @d, @t, @op, 1, 1);";
                cmd.AddWithValue("@id", $"da-{i:D5}");
                cmd.AddWithValue("@c", Co);
                cmd.AddWithValue("@d", $"Hareket {i}");
                cmd.AddWithValue("@t", 1_800_000_000_000L + (i % 40) * 86_400_000L);   // aynı günde çok kayıt → kararlı ikincil sıra şart
                cmd.AddWithValue("@op", $"op-{i:D5}");
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        var svc = new DailyActivityService(_f, new MaintenanceService(_f));
        var tumu = svc.SearchGridAll(_admin, new DailyActivityGridFilter());
        var sayfali = Sayfalar(p => svc.SearchGrid(_admin, new DailyActivityGridFilter(), p, 500));
        Assert.Equal(1100, tumu.Count);
        Assert.Equal(sayfali.Select(x => x.Id), tumu.Select(x => x.Id));
        Assert.True(tumu[0].DateRaw >= tumu[^1].DateRaw);   // en yeni üstte

        var sayan = new SayanSqlite(_f);
        new DailyActivityService(sayan, new MaintenanceService(sayan)).SearchGridAll(_admin, new DailyActivityGridFilter());
        Assert.True(sayan.KomutSayisi <= 2, $"'tümü' {sayan.KomutSayisi} SQL komutu çalıştırdı");
    }
}

/// <summary>SQLite bağlantısının KENDİSİNDEN türeyen komut sayacı: <c>SqlDialect.IsSqlite</c> doğru tanır (sarmalayan
/// <see cref="SayanFabrika"/> lehçeyi PostgreSQL sanıyordu). Yalnız okuma testleri için (pragma kurulumu yok).</summary>
internal sealed class SayanSqlite : IDbConnectionFactory
{
    private readonly string _cs;
    private int _komut;
    public SayanSqlite(SqliteConnectionFactory ic) { using var c = ic.Create(); _cs = c.ConnectionString; DatabasePath = ic.DatabasePath; }
    public int KomutSayisi => _komut;
    public string DatabasePath { get; }
    public System.Data.Common.DbConnection Create() { var c = new Baglanti(_cs, () => _komut++); c.Open(); return c; }

    private sealed class Baglanti : Microsoft.Data.Sqlite.SqliteConnection
    {
        private readonly Action _say;
        public Baglanti(string cs, Action say) : base(cs) => _say = say;
        protected override System.Data.Common.DbCommand CreateDbCommand() { _say(); return base.CreateDbCommand(); }
    }
}