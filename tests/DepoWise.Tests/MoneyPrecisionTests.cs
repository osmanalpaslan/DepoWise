using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G5 (2026-08-18) — PARA / MİKTAR KESİNLİĞİ.
///
/// <b>DEN-D1 (asıl sorun)</b> — <c>FuelService.DepotBalance</c> depo bakiyesini
/// <c>SUM(CAST(liters AS REAL))</c> ile hesaplıyordu ve bu değer İKİ İŞ KURALI KAPISINDA karar veriyordu:
/// "Depo yakıtı yetersiz" (dağıtım reddi) ve "bakiye eksiye düşer" (iptal reddi). Projenin kendi kuralı
/// float'ı yasaklıyor (<c>StockBalanceWriter</c>). Somut hata: çok sayıda ondalıklı giriş biriktiğinde
/// toplam 999,9999999999999 çıkabilir → tam 1000 L'lik dağıtım <b>haksız yere reddedilir</b>.
///
/// <b>DEN-D2</b> — "Malzeme — Şablonlu" raporunun "Toplam Stok" kolonu HAM <c>double</c> yazılıyordu
/// (biçimlendirici yok) → kullanıcı <c>1234,5600000000002</c> gibi bir değer görebiliyordu.
///
/// ⚠️ <b>DENETİM RAPORUNDA DÜZELTME:</b> DEN-D2'nin yakıt tüketim raporlarını da etkilediği yazılmıştı.
/// Kontrol edildi: o raporlarda değerler <c>#,##0.00</c> / <c>#,##0.##</c> ile biçimlendiriliyor →
/// kayan nokta artığı kullanıcıya YANSIMIYOR. Gerçek etki yalnız biçimlendiricisi olmayan kolondaydı.
/// </summary>
public class MoneyPrecisionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly SessionContext _admin;
    private const string Co = "MNY-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public MoneyPrecisionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_money_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ── DEN-D1 ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// ⭐ ASIL HATA: 0,1 L × 10 giriş = TAM 1,0 L olmalı. Kayan noktada bu toplam
    /// 0,9999999999999999 çıkar ve 1,0 L'lik dağıtım "yetersiz" diye reddedilirdi.
    /// </summary>
    [Fact]
    public void Depo_Bakiyesi_Ondalikli_Girislerde_TAM_Cikar()
    {
        var fuel = new FuelService(_factory, _clock);
        for (int i = 0; i < 10; i++)
            Sql($"INSERT INTO fuel_depot_entries(id,company_id,liters,unit_price,entry_date,operation_id,created_at,updated_at,version,is_deleted) " +
                $"VALUES('E{i}','{Co}','0.1','10',10,'op-{i}',10,10,1,0);");

        var bakiye = fuel.GetDepotBalance(_admin);

        Assert.Equal(1.0m, bakiye);            // kayan noktada 0,9999999999999999 çıkardı
        Assert.True(bakiye >= 1.0m, "Tam 1,0 L'lik dağıtım haksız yere reddedilirdi.");
    }

    /// <summary>Klasik 0,1 + 0,2 = 0,3 senaryosu (float'ta 0,30000000000000004).</summary>
    [Fact]
    public void Depo_Bakiyesi_Klasik_Float_Hatasini_Uretmez()
    {
        var fuel = new FuelService(_factory, _clock);
        Sql($"INSERT INTO fuel_depot_entries(id,company_id,liters,unit_price,entry_date,operation_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('E1','{Co}','0.1','10',10,'op-1',10,10,1,0);");
        Sql($"INSERT INTO fuel_depot_entries(id,company_id,liters,unit_price,entry_date,operation_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('E2','{Co}','0.2','10',10,'op-2',10,10,1,0);");

        Assert.Equal(0.3m, fuel.GetDepotBalance(_admin));
    }

    /// <summary>Dağıtım düşülür; sonuç yine tam olmalı.</summary>
    [Fact]
    public void Depo_Bakiyesi_Dagitimi_Kesin_Duser()
    {
        var fuel = new FuelService(_factory, _clock);
        Sql($"INSERT INTO fuel_depot_entries(id,company_id,liters,unit_price,entry_date,operation_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('E1','{Co}','100.05','10',10,'op-1',10,10,1,0);");
        Sql($"INSERT INTO vehicles(id,company_id,internal_code,plate,created_at,updated_at,version,is_deleted) " +
            $"VALUES('V1','{Co}','AR-1','34ABC01',10,10,1,0);");
        Sql($"INSERT INTO fuel_distributions(id,company_id,vehicle_id,liters,unit_price,distribution_date,operation_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('D1','{Co}','V1','0.05','10',10,'op-d1',10,10,1,0);");

        Assert.Equal(100.0m, fuel.GetDepotBalance(_admin));
    }

    // ── DEN-D2 ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>"Toplam Stok" kolonunda kayan nokta artığı GÖRÜNMEMELİ.</summary>
    [Fact]
    public void Sablonlu_Malzeme_Raporunda_Toplam_Stok_Temiz()
    {
        var branches = new BranchService(_factory, _clock);
        var sube = branches.Create(_admin, new NewBranch("Merkez"));
        Sql($"INSERT INTO material_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
            $"VALUES('T1','{Co}','Çimento Şablonu',10,10,1,0);");
        // 0,1 + 0,2 → float'ta 0,30000000000000004
        for (int i = 1; i <= 2; i++)
        {
            Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,template_id,min_stock,created_at,updated_at,version,is_deleted) " +
                $"VALUES('M{i}','{Co}','K{i}','Malzeme {i}',NULL,'T1','0',10,10,1,0);");
            Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) " +
                $"VALUES('{Co}','M{i}','{sube}','0.{i}',10);");
        }

        var t = new ReportService(_factory, _clock).MaterialsByTemplate(_admin, new ReportRequest(Executed: true));

        var toplamStok = Convert.ToString(t.Rows[0][3]) ?? "";
        Assert.DoesNotContain("0000000", toplamStok);   // kayan nokta artığı yok
        Assert.Equal(0.3m, Money.Parse(toplamStok));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
