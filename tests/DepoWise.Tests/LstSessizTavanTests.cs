using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ LST-01 — TAVANLI LİSTELER SESSİZCE KESMEZ (2026-09-07) ═══
///
/// <para><b>Sorun:</b> bazı ekranlar en fazla N satır okur ama <b>okudukları satır sayısını</b>
/// "toplam" diye yazar. 10.000 kaydı olan bir firmada ekran "300 kayıt" der; kullanıcı toplamının
/// bu olduğunu sanır ve geri kalanı sessizce kaybolur. Denetim izi (Sistem Logu) ve stok değişiklik
/// kaydı tam da geriye bakmak için tutulur — eksik olduğunu söylememek en kötü davranıştır.</para>
///
/// <para><b>Çözüm:</b> tavan korunur (tek seferde 10.000 satır çizmek ekranı kilitler) ama
/// <c>Sayim</c> ile AYNI filtrenin gerçek toplamı sorulur ve tavana takıldığı kullanıcıya söylenir.</para>
///
/// <para>Bu testler gerçek veri üretir: tavandan FAZLA kayıt yazılır, listenin kestiği ama sayımın
/// doğruyu söylediği kanıtlanır.</para>
/// </summary>
public class LstSessizTavanTests : IDisposable
{
    private const string Co = "LST";
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;
    private const long T0 = 1_700_000_000_000;

    public LstSessizTavanTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_lst_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();

        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "lst_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Denetim kaydı üretir (ham SQL — tek transaction, hızlı).</summary>
    private void DenetimUret(int adet)
    {
        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO audit_logs(id,company_id,user_id,entity_type,entity_id,action,created_at) " +
            "VALUES(@i,@c,@u,'material',@e,'update',@ts);";
        Microsoft.Data.Sqlite.SqliteParameter P(string ad)
        { var p = (Microsoft.Data.Sqlite.SqliteParameter)cmd.CreateParameter(); p.ParameterName = ad; cmd.Parameters.Add(p); return p; }
        var i = P("@i"); var c = P("@c"); var u = P("@u"); var e = P("@e"); var ts = P("@ts");
        c.Value = Co; u.Value = _admin.UserId;
        for (int k = 0; k < adet; k++)
        {
            i.Value = Guid.NewGuid().ToString("N");
            e.Value = "mat-" + k;
            ts.Value = T0 + k;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// ⭐ ASIL TEST: liste tavanda keser, sayım GERÇEK toplamı verir.
    /// Ekran bu iki sayıyı karşılaştırıp kullanıcıyı uyarabilsin diye ikisi de gerekir.
    /// </summary>
    [Fact]
    public void SistemLogu_ListeKeser_SayimGercegiSoyler()
    {
        DenetimUret(450);
        var svc = new AuditLogService(_f);

        var satirlar = svc.List(_admin, limit: 300);
        Assert.Equal(300, satirlar.Count);              // tavan uygulanıyor (bilinçli)

        var toplam = svc.Sayim(_admin);
        Assert.Equal(450, toplam);                      // GERÇEK toplam
        Assert.True(toplam > satirlar.Count,
            "Bu testin anlamı: toplam, gösterilen satır sayısından büyük olabilir.");
    }

    /// <summary>Sayım listeyle AYNI filtreyi uygular — iki sayı birbirini tutmalı.</summary>
    [Fact]
    public void SistemLogu_Sayim_AyniFiltreyi_Uygular()
    {
        DenetimUret(450);
        var svc = new AuditLogService(_f);

        // İlk 100 kaydın zaman aralığı.
        var toplam = svc.Sayim(_admin, T0, T0 + 99);
        Assert.Equal(100, toplam);

        // Aynı aralıkta liste de 100 döner (tavanın altında).
        var satirlar = svc.List(_admin, T0, T0 + 99, limit: 300);
        Assert.Equal(100, satirlar.Count);
        Assert.Equal(satirlar.Count, toplam);
    }

    /// <summary>Kayıt yoksa sayım 0 döner (ekran "kayıt yok" der, yanıltıcı sayı göstermez).</summary>
    [Fact]
    public void KayitYoksa_Sayim_SifirDoner()
    {
        var svc = new AuditLogService(_f);
        Assert.Equal(0, svc.Sayim(_admin));
        Assert.Empty(svc.List(_admin));
    }

    /// <summary>
    /// İKİ ORTAM SÖZLEŞMESİ: ekranlar sayıyı dönen satır listesinden DEĞİL gerçek toplamdan alır
    /// ve tavana takıldığında kullanıcıyı uyarır (masaüstü + web).
    /// </summary>
    [Fact]
    public void Ekranlar_GercekToplami_Kullanir_VeUyarir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { d!.FullName }.Concat(p).ToArray()));

        foreach (var vm in new[] { "AuditLogViewModel.cs", "StockChangeLogViewModel.cs" })
        {
            var s = Oku("src", "DepoWise.Desktop", "ViewModels", vm);
            Assert.Contains(".Sayim(", s);
            Assert.Contains("en yenisinden", s);
            Assert.DoesNotContain("$\"{Items.Count} kayıt (loglar silinemez)\"", s.Replace("? $\"{toplam} kayıt (loglar silinemez)\"", ""));
        }

        foreach (var (sayfa, uc) in new[] { ("Audit.razor", "/api/audit/count"), ("StockChangeLog.razor", "/api/stock/change-log/count") })
        {
            var s = Oku("src", "DepoWise.Web", "Components", "Pages", sayfa);
            Assert.Contains(uc, s);
            Assert.Contains("_gercekToplam", s);
            Assert.Contains("tanesi gösteriliyor", s);
        }

        // API uçları var ve yetki istiyor.
        var api = Oku("src", "DepoWise.Api", "Program.cs");
        Assert.Contains("/api/audit/count", api);
        Assert.Contains("/api/stock/change-log/count", api);
    }

    public void Dispose() { try { File.Delete(_db); } catch { } }
}
