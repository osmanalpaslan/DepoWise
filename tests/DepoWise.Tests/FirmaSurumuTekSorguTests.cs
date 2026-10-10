using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-10 — firma sürümü TEK SORGU (Supabase trafik ölçümü: eski hesap her çağrıda ~174 veritabanı
/// gidiş-dönüşü yapıyordu, günde GB'larca trafik). FSU1: yeni hesap, eski "tablo tablo MAX" hesabıyla BİREBİR
/// aynı değeri verir. FSU2: başka firmanın kaydı sürümü etkilemez (tenant). FSU3: yeni kayıt sürümü ilerletir.
/// </summary>
public class FirmaSurumuTekSorguTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "depowise_fsu_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly FakeClock _clock = new();
    private readonly SessionContext _a, _b;

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_750_000_000_000);
    }

    public FirmaSurumuTekSorguTests()
    {
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
        var users = new UserService(_f, _clock);
        _a = new SessionContext(users.EnsureInitialAdmin("A", "a", "a123456", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _b = new SessionContext(users.EnsureInitialAdmin("B", "b", "b123456", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    public void Dispose() { try { File.Delete(_db); } catch { } }

    /// <summary>ESKİ yöntem (değişiklikten önceki kodun birebiri): tablo tablo MAX.</summary>
    private long EskiYontem(string companyId)
    {
        using var conn = _f.Create();
        long max = 0;
        foreach (var table in BusinessSyncService.Tables)
        {
            if (!DbIntrospect.TableExists(conn, null, table)) continue;
            var cols = DbIntrospect.ColumnNames(conn, table);
            var stamp = cols.Contains("updated_at")
                ? (cols.Contains("created_at") ? "COALESCE(updated_at, created_at)" : "updated_at")
                : (cols.Contains("created_at") ? "created_at" : null);
            if (stamp is null) continue;
            using var cmd = conn.CreateCommand();
            var hasCompany = cols.Contains("company_id");
            cmd.CommandText = hasCompany ? $"SELECT MAX({stamp}) FROM {table} WHERE company_id=@c;" : $"SELECT MAX({stamp}) FROM {table};";
            if (hasCompany) cmd.AddWithValue("@c", companyId);
            var v = cmd.ExecuteScalar();
            if (v is not null and not DBNull) { var l = Convert.ToInt64(v); if (l > max) max = l; }
        }
        return max;
    }

    [Fact]
    public void FSU1_Yeni_Hesap_Eski_Hesapla_Ayni()
    {
        var sync = new BusinessSyncService(_f);
        Assert.Equal(EskiYontem("A"), sync.CompanyVersion("A"));   // boş firma
        new MaterialService(_f, _clock).Create(_a, new NewMaterial("M-1", "Filtre"));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        new VehicleService(_f, _clock).Create(_a, new NewVehicle("V-1"));
        Assert.Equal(EskiYontem("A"), sync.CompanyVersion("A"));
        Assert.Equal(EskiYontem("B"), sync.CompanyVersion("B"));
    }

    [Fact]
    public void FSU2_Baska_Firmanin_Kaydi_Surumu_Etkilemez_FSU3_Yeni_Kayit_Ilerletir()
    {
        var sync = new BusinessSyncService(_f);
        new MaterialService(_f, _clock).Create(_a, new NewMaterial("M-1", "Filtre"));
        var once = sync.CompanyVersion("A");
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        new MaterialService(_f, _clock).Create(_b, new NewMaterial("M-B", "B malzemesi"));   // B yazdı
        Assert.Equal(once, sync.CompanyVersion("A"));                                        // A değişmedi
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        new MaterialService(_f, _clock).Create(_a, new NewMaterial("M-2", "Yağ"));            // A yazdı
        Assert.True(sync.CompanyVersion("A") > once);
    }
}
