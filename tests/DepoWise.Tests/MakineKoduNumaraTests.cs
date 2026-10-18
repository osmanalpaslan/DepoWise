using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-78 / K-21 (AR-GE 2026-10-17) — BİLGİSAYAR KODLU BELGE NUMARASI ═══
///
///  KOD1 — Kod dizisi A1…A9, B1…Z9, AA1…; geçerlilik denetimi (SQL'e giren değer yalnız bu biçim)
///  KOD2 — Sunucu kodu atar: ilk A1, ikinci A2; aynı bilgisayar tekrar kayıtta AYNI kod; başka firma yine A1
///  KOD3 — İki ÇEVRİMDIŞI bilgisayar aynı gün belge + talep açar → numaralar farklı (GIR-2026-A1-0001 / GIR-2026-A2-0001);
///         sıra bilgisayara özel artar; ikisi de sunucuya gider (eskiden ikincisi benzersizlik ihlaliyle düşüyordu)
///  KOD4 — Kodsuz (sunucu/web) numaralandırma kodlu numaraları saymaz: GIR-2026-0003 varken → GIR-2026-0004
///  KOD5 — Masaüstü oturumu kodu tek noktadan alır (DesktopServices.Session — kaynak kilidi)
/// </summary>
public class MakineKoduNumaraTests : IDisposable
{
    private const string Co = "KOD";
    private static readonly long Gun = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private readonly List<string> _yollar = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var y in _yollar) try { File.Delete(y); } catch { }
    }

    private (SqliteConnectionFactory F, SessionContext S) Db(string ad, string? kod, bool sunucu = false)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"dw_kod_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
        _yollar.Add(yol);
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run(); if (sunucu) Migration099_SyncTxid.SunucuRoluKur(f);
        using (var c = f.Create())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = 
                $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);" +
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('KOD2','KOD2',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(f).EnsureInitialAdmin(Co, "admin_" + ad, "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { MachineCode = kod };
        return (f, s);
    }

    [Fact]
    public void KOD1_Kod_Dizisi_Ve_Gecerlilik()
    {
        Assert.Equal("A1", BelgeNumarasi.Kod(0));
        Assert.Equal("A9", BelgeNumarasi.Kod(8));
        Assert.Equal("B1", BelgeNumarasi.Kod(9));
        Assert.Equal("Z9", BelgeNumarasi.Kod(233));
        Assert.Equal("AA1", BelgeNumarasi.Kod(234));
        Assert.Equal("AB1", BelgeNumarasi.Kod(243));
        var hepsi = Enumerable.Range(0, 2000).Select(BelgeNumarasi.Kod).ToList();
        Assert.Equal(hepsi.Count, hepsi.Distinct().Count());
        Assert.All(hepsi, k => Assert.True(BelgeNumarasi.GecerliKod(k)));
        foreach (var kotu in new[] { null, "", "a1", "A0", "A10", "A1'--", "ABC1", "1A" })
            Assert.False(BelgeNumarasi.GecerliKod(kotu));
    }

    [Fact]
    public void KOD2_Sunucu_Kodu_Atar_Ve_Korur()
    {
        var (f, _) = Db("srv", null, sunucu: true);
        var e = new EnrollmentService(f);
        Assert.Equal("A1", e.RegisterSelf(Co, "PC-1").MachineCode);
        Assert.Equal("A2", e.RegisterSelf(Co, "PC-2").MachineCode);
        Assert.Equal("A1", e.RegisterSelf(Co, "PC-1").MachineCode);   // aynı bilgisayar → aynı kod
        Assert.Equal("A1", e.RegisterSelf("KOD2", "PC-9").MachineCode); // firma içinde benzersiz
        Assert.Equal("A3", e.RegisterSelf(Co, "PC-3").MachineCode);
    }

    [Fact]
    public void KOD3_Iki_Cevrimdisi_Bilgisayar_Cakismaz_Ve_Ikisi_De_Sunucuya_Gider()
    {
        var (srv, _) = Db("srv", null, sunucu: true);
        var numaralar = new List<string>();
        foreach (var kod in new[] { "A1", "A2" })
        {
            var (f, s) = Db("pc" + kod, kod);
            var mat = new MaterialService(f).Create(s, new NewMaterial("KOD-" + kod, "Malzeme " + kod));
            var stok = new StockService(f);
            var b1 = stok.ReceiveIn(s, new[] { new StockLine(mat, 5m) }, "op-" + kod + "-1", docDate: Gun);
            var b2 = stok.ReceiveIn(s, new[] { new StockLine(mat, 1m) }, "op-" + kod + "-2", docDate: Gun);
            Assert.Equal($"GIR-2026-{kod}-0001", b1.DocNo);
            Assert.Equal($"GIR-2026-{kod}-0002", b2.DocNo);   // sıra bu bilgisayara özel
            var talep = new RequestService(f, stok).Create(s, new NewRequest(new[] { new RequestItemInput(mat, 2m) }, RequestDate: Gun));
            Assert.Equal($"TLP-2026-{kod}-0001", talep.DocNo);
            numaralar.AddRange(new[] { b1.DocNo, b2.DocNo, talep.DocNo });

            var paket = JsonDocument.Parse(new BusinessSyncService(f).BuildPushSnapshot(Co, "pc" + kod, -1)).RootElement.Clone();
            var res = new BusinessSyncService(srv).Apply(Co, paket);
            Assert.DoesNotContain(res.Rejected ?? Array.Empty<RejectedRow>(), r => r.Table is "stock_documents" or "material_requests");
        }
        Assert.Equal(numaralar.Count, numaralar.Distinct().Count());
        using var c = srv.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT (SELECT COUNT(*) FROM stock_documents WHERE company_id='{Co}') * 10 + (SELECT COUNT(*) FROM material_requests WHERE company_id='{Co}');";
        Assert.Equal(42L, Convert.ToInt64(cmd.ExecuteScalar()));   // 4 belge + 2 talep — hepsi sunucuda
    }

    [Fact]
    public void KOD4_Kodsuz_Numara_Kodlulari_Saymaz()
    {
        var (f, s) = Db("eski", null);
        using (var c = f.Create())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO stock_documents(id, company_id, doc_no, doc_type, doc_date, created_at) VALUES " +
                $"('d1','{Co}','GIR-2026-A1-0009','in',{Gun},1), ('d2','{Co}','GIR-2026-0003','in',{Gun},1);";
            cmd.ExecuteNonQuery();
        }
        var mat = new MaterialService(f).Create(s, new NewMaterial("K-1", "M"));
        var b = new StockService(f).ReceiveIn(s, new[] { new StockLine(mat, 1m) }, "op-eski", docDate: Gun);
        Assert.Equal("GIR-2026-0004", b.DocNo);

        s.MachineCode = "A1";
        var k = new StockService(f).ReceiveIn(s, new[] { new StockLine(mat, 1m) }, "op-kodlu", docDate: Gun);
        Assert.Equal("GIR-2026-A1-0010", k.DocNo);
    }

    [Fact]
    public void KOD5_Masaustu_Oturumu_Kodu_Tek_Noktadan_Alir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var ds = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "DesktopServices.cs"));
        Assert.Contains("if (value is not null && value.MachineCode is null) value.MachineCode = MachineGate.MakineKodu(value.CompanyId);", ds);
        var mg = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "MachineGate.cs"));
        Assert.Contains("Str(root, \"machineCode\")", mg);
    }
}
