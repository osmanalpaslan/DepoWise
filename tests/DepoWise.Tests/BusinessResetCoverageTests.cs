using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// SIF-01 / SIF-03 (2026-08-18) — FİRMA İŞ VERİSİ SIFIRLAMANIN KAPSAMI.
///
/// İki ayrı hata bu testlerle kilitlenir:
///
/// <b>SIF-01</b> — masaüstü, sunucudan gelen "yerelini sıfırla" isteğini uygularken ADR-083'ün TAM SİLME
/// fonksiyonunu (<c>PurgeLocalCompany</c>) çağırıyordu; o fonksiyon firma satırını, kullanıcıları,
/// rolleri, yetkileri ve şubeleri de siler. Sonuç: sıfırlama sonrası o makinede <b>çevrimdışı giriş
/// imkânsız</b> hâle geliyordu (bcrypt hash'i yerel <c>users</c> satırında durur). Kullanıcı şartı ise
/// açıktı: "şubeler ve kullanıcılar silinmesin".
///
/// <b>SIF-03</b> — silme listesi <see cref="DepoWise.Infrastructure.Sync.BusinessSyncService.Tables"/>
/// üzerinden yürüyordu; oysa o liste SENKRON sözleşmesidir (taşınacaklar), silinecekler değil. Farkta
/// kalan tablolar (bakiye, muayene, sayaç, log, dosya, şablon + company_id'siz satır tabloları)
/// sıfırlama sonrası <b>öksüz</b> kalıyordu.
///
/// Not: masaüstündeki <c>LocalPurgeService</c> Desktop projesindedir ve bu test projesinden
/// referanslanmaz; ortak davranış <see cref="BusinessDataExtras"/> üzerinden paylaşılır ve burada
/// sunucu tarafı (<see cref="CompanyPurgeService.ResetBusinessData"/>) üzerinden doğrulanır.
/// SIF-01'in çağrı yeri ise <see cref="LoginEkraniDogruFonksiyonuCagirir"/> ile korunur.
/// </summary>
public class BusinessResetCoverageTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly BranchService _branches;
    private readonly AuthService _auth;
    private readonly CompanyPurgeService _purge;
    private const string Co = "DEPOWISE";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public BusinessResetCoverageTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_resetcov_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _purge = new CompanyPurgeService(_factory, _clock);
    }

    private SessionContext SuperAdmin()
    {
        _users.EnsureInitialAdmin(Co, "root", "root123", RoleKeys.SuperAdmin);
        return _auth.Login(Co, "root", "root123").Session!;
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Count(string table, string where = "1=1")
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE {where};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ── R1: kullanıcı şartı — firma, şube, kullanıcı, rol KORUNUR ────────────────────────────────
    [Fact]
    public void Sifirlama_Firma_Sube_Kullanici_Rol_Korur()
    {
        var su = SuperAdmin();
        var subeId = _branches.Create(su, new NewBranch("Merkez"));
        var kullanici = _users.CreateUser(su, new NewUser("depocu", "Depo!2026", "Depo Görevlisi",
            new List<string> { RoleKeys.Staff }, Co, null, subeId, false, null));

        _purge.ResetBusinessData(su, Co);

        Assert.Equal(1, Count("companies", $"id='{Co}'"));
        Assert.Equal(1, Count("branches", $"id='{subeId}' AND is_deleted=0"));
        Assert.Equal(1, Count("users", $"id='{kullanici}' AND is_deleted=0"));
        Assert.True(Count("user_roles", $"user_id='{kullanici}'") > 0);

        // ŞARTIN ÖZÜ: kullanıcı sıfırlamadan SONRA da giriş yapabilmeli (çevrimdışı giriş de buna dayanır).
        var giris = _auth.Login(Co, "depocu", "Depo!2026");
        Assert.NotNull(giris.Session);
    }

    // ── R2: senkronda TAŞINMAYAN iş tabloları da temizlenir (SIF-03) ─────────────────────────────
    [Fact]
    public void Sifirlama_SenkronDisi_IsTablolarini_Da_Temizler()
    {
        var su = SuperAdmin();
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Çimento',NULL,1,1,1,0);");
        Sql($"INSERT INTO vehicles(id,company_id,internal_code,plate,created_at,updated_at,version,is_deleted) " +
            $"VALUES('V1','{Co}','AR-1','34ABC01',1,1,1,0);");
        Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) " +
            $"VALUES('{Co}','M1','','120',1);");
        Sql($"INSERT INTO vehicle_inspections(id,company_id,vehicle_id,doc_type,created_at,updated_at,version,is_deleted) " +
            $"VALUES('I1','{Co}','V1','inspection',1,1,1,0);");
        Sql($"INSERT INTO vehicle_meter_logs(id,company_id,vehicle_id,old_value,new_value,source,created_at) " +
            $"VALUES('L1','{Co}','V1','10','20','vehicle_form',1);");

        Assert.Equal(1, Count("stock_balances"));

        _purge.ResetBusinessData(su, Co);

        Assert.Equal(0, Count("stock_balances"));       // eski bakiye kalmamalı
        Assert.Equal(0, Count("vehicle_inspections"));  // eski muayene kalmamalı
        Assert.Equal(0, Count("vehicle_meter_logs"));   // eski sayaç geçmişi kalmamalı
        Assert.Equal(0, Count("materials"));
        Assert.Equal(0, Count("vehicles"));
    }

    // ── R3: company_id'si OLMAYAN satır tabloları öksüz kalmaz (SIF-03) ──────────────────────────
    [Fact]
    public void Sifirlama_CompanyIdsiz_CocukSatirlari_Temizler()
    {
        var su = SuperAdmin();
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Çimento',NULL,1,1,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M2','{Co}','K2','Demir',NULL,1,1,1,0);");
        Sql("INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('M1','M2');");

        Assert.Equal(1, Count("material_equivalents"));

        _purge.ResetBusinessData(su, Co);

        Assert.Equal(0, Count("material_equivalents"));   // ebeveyni silindi → öksüz kalmamalı
    }

    // ── R4: başka firmanın verisine DOKUNULMAZ (tenant izolasyonu) ───────────────────────────────
    [Fact]
    public void Sifirlama_BaskaFirmanin_Verisine_Dokunmaz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "BFIRMA");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Çimento',NULL,1,1,1,0);");
        Sql("INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            "VALUES('M9','BFIRMA','K9','Kum',NULL,1,1,1,0);");
        Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) " +
            $"VALUES('{Co}','M1','','5',1);");
        Sql("INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) " +
            "VALUES('BFIRMA','M9','','7',1);");
        Sql("INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            "VALUES('M8','BFIRMA','K8','İnce Kum',NULL,1,1,1,0);");
        Sql("INSERT INTO material_equivalents(material_id,equivalent_material_id) VALUES('M9','M8');");   // ebeveyni B firmasında DURUYOR

        _purge.ResetBusinessData(su, Co);

        Assert.Equal(2, Count("materials", "company_id='BFIRMA'"));   // M9 + M8
        Assert.Equal(1, Count("stock_balances", "company_id='BFIRMA'"));
        Assert.Equal(1, Count("material_equivalents", "material_id='M9'"));   // ebeveyni yaşıyor → silinmemeli
    }

    // ── R5: SIF-01 çağrı yeri koruması ───────────────────────────────────────────────────────────
    /// <summary>
    /// Masaüstü giriş akışı, "yerel sıfırlama" isteğini uygularken <b>iş verisi</b> temizliğini
    /// çağırmalıdır — ADR-083'ün tam silme fonksiyonunu DEĞİL. Bu, birim testle erişilemeyen bir
    /// ÇAĞRI YERİ hatasıydı (Desktop projesi bu test projesinden referanslanmaz), bu yüzden kaynak
    /// düzeyinde kilitlenir. Kalıcı silme akışı (<c>HandleCompanyPurgeAsync</c>) etkilenmez —
    /// o hâlâ <c>PurgeLocalCompany</c> kullanır ve kullanmalıdır.
    /// </summary>
    [Fact]
    public void LoginEkraniDogruFonksiyonuCagirir()
    {
        var kok = RepoKoku();
        var yol = Path.Combine(kok, "src", "DepoWise.Desktop", "ViewModels", "LoginViewModel.cs");
        Assert.True(File.Exists(yol), $"LoginViewModel bulunamadı: {yol}");
        var kaynak = File.ReadAllText(yol);

        var basla = kaynak.IndexOf("HandleCompanyLocalResetAsync(string companyId)", StringComparison.Ordinal);
        Assert.True(basla > 0, "HandleCompanyLocalResetAsync metodu bulunamadı (yeniden adlandırıldıysa test güncellenmeli).");
        var govde = kaynak[basla..];
        var bit = govde.IndexOf("\n    }", StringComparison.Ordinal);
        if (bit > 0) govde = govde[..bit];

        // Yorum metni eski adı ANLATABİLİR; kilitlenen şey gerçek ÇAĞRIDIR.
        Assert.Contains("LocalPurgeService.PurgeBusinessData(", govde);
        Assert.DoesNotContain("LocalPurgeService.PurgeLocalCompany(", govde);
    }

    /// <summary>Test ikilisi bin/Debug altında çalışır → repo kökünü yukarı doğru ararız.</summary>
    private static string RepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !File.Exists(Path.Combine(dizin.FullName, "CLAUDE.md")))
            dizin = dizin.Parent;
        Assert.NotNull(dizin);
        return dizin!.FullName;
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
