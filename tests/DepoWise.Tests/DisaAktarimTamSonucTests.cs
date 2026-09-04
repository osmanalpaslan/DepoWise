using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Org;
using Xunit;

namespace DepoWise.Tests;

/// <summary>FAZ K — dışa aktarımın gerçekten TÜM sonucu verdiğinin kanıtı (PRT-02 tamamlayıcısı).</summary>
public class DisaAktarimTamSonucTests : IDisposable
{
    private const string Co = "EXP";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;

    public DisaAktarimTamSonucTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_exp_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// ⭐ FAZ K — 🔴 KENDİ EKLEDİĞİM KUSURUN KİLİDİ.
    ///
    /// Dışa aktarım "filtrelenmiş TÜM sonucu indirir" diyordu ama
    /// <c>List(..., Limit = 100_000)</c> çağırıyordu. <c>PageRequest.NormalizedLimit</c> her isteği
    /// <b>200</b>'de kırptığı için dosya sessizce 200 satırda kesilirdi ve kullanıcı bunu ASLA fark
    /// etmezdi — bu turda kapatılan "sessiz eksiklik" sınıfının aynısı.
    ///
    /// Bu test 200 sınırının ÖTESİNE geçildiğini kanıtlar. Biri ileride yolu tekrar <c>List</c>'e
    /// çevirirse burada kırılır.
    /// </summary>
    [Fact]
    public void PRT6_Personel_Disa_Aktarimi_200_Sinirinda_Kesilmez()
    {
        var svc = new DepoWise.Infrastructure.Org.PersonnelService(_f, new ScopeResolver(_f));
        const int adet = 260;   // MaxLimit(200) ÜSTÜ — kusur olsaydı 200'de kesilirdi
        for (int i = 1; i <= adet; i++)
            Calistir("INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('{Guid.NewGuid():N}','{Co}','Personel {i:D4}',1,{i},{i},1,0);");

        // ESKİ YOL: sayfalama tavanı yüzünden 200'de kesilir (kusurun kanıtı).
        Assert.Equal(200, svc.List(_admin, new PageRequest { Limit = 100_000 }).Items.Count);

        // YENİ YOL: hepsi gelir.
        Assert.Equal(adet, svc.ListAllForExport(_admin).Count);

        // Arama da korunur (dışa aktarım FİLTRELENMİŞ sonucu indirir, her şeyi değil).
        Assert.Single(svc.ListAllForExport(_admin, "Personel 0007"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
