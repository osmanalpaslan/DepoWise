using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-15 / M-79 / M-80 (AR-GE 2026-10-17, B1 adım 4) ═══
///
///  RED1 — Sunucu reddettiği HER satırı kimliğiyle ve nedeniyle döndürür (öksüz çocuk = kalıcı; başka firmanın kaydı = kalıcı)
///  RED2 — Masaüstü listesi: kaydedilir, tekrar reddedilince deneme sayısı artar, sonra kabul edilince SİLİNİR
///  RED3 — "Listeden kaldır" yalnız gizler; tekrar reddedilse de gizli kalır
///  RED4 — "Yeniden gönder" satırı yeniden değişti işaretler (yerel sayaç) → gönderme paketine girer
///  RED5 — Kart metni: tablo adı teknik önekten arınır, kalıcı/geçici/yerelde yok ipuçları doğru
///  RED6 — 500'den fazla red yanıtı şişirmez (üst sınır)
///  DNM1 — Sunucu dönemi kararları: ilk kayıt, aynı dönem, dönem değişti, ufuk geriledi
///  DNM2 — SQLite sunucu rolünde v2 yanıtı dönem taşır
///  SIF1 — "Yereli sıfırla" önce GÖNDERİR, bekleyen/reddedilen varsa sayılı uyarıyla ayrıca onay ister (kaynak kilidi)
/// </summary>
public class GonderimRedleriTests : IDisposable
{
    private const string Co = "RED", Yabanci = "RED2";
    private readonly string _sunucuYol, _masaYol;
    private readonly SqliteConnectionFactory _sunucu, _masa;
    private readonly TestClock _clock = new();
    private readonly SessionContext _admin;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
    }

    public GonderimRedleriTests()
    {
        _sunucuYol = Path.Combine(Path.GetTempPath(), "dw_red_srv_" + Guid.NewGuid().ToString("N") + ".db");
        _masaYol = Path.Combine(Path.GetTempPath(), "dw_red_pc_" + Guid.NewGuid().ToString("N") + ".db");
        _sunucu = new SqliteConnectionFactory(_sunucuYol);
        _masa = new SqliteConnectionFactory(_masaYol);
        new MigrationRunner(_sunucu).Run();
        new MigrationRunner(_masa).Run();
        Calistir(_sunucu, "INSERT OR IGNORE INTO sync_server_role(id) VALUES (1);");
        foreach (var f in new[] { _sunucu, _masa })
            foreach (var c in new[] { Co, Yabanci })
                Calistir(f, $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{c}','{c}',1,1,1,0);");
        var uid = new UserService(_masa).EnsureInitialAdmin(Co, "admin_red", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_sunucuYol); } catch { }
        try { File.Delete(_masaYol); } catch { }
    }

    private static void Calistir(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    /// <summary>Öksüz muadil eşleşmesi (ebeveyn malzeme sunucuda yok) + başka firmanın kimliğini taşıyan malzeme + geçerli malzeme.</summary>
    private string Paket(string gecerliId) => $$"""
        { "tables": {
            "materials": [
              { "id": "{{gecerliId}}", "code": "R-1", "name": "Geçerli", "created_at": 1, "updated_at": 2, "version": 1, "is_deleted": 0 },
              { "id": "YABANCI-1", "code": "R-9", "name": "Ele geçirme", "created_at": 1, "updated_at": 9, "version": 1, "is_deleted": 0 } ],
            "material_equivalents": [ { "material_id": "YOK-A", "equivalent_material_id": "YOK-B" } ] } }
        """;

    [Fact]
    public void RED1_Sunucu_Reddedilen_Satirlari_Kimligi_Ve_Nedeniyle_Dondurur()
    {
        Calistir(_sunucu, $"INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('YABANCI-1','{Yabanci}','X','Onların',1,1,1,0);");
        var res = new BusinessSyncService(_sunucu).Apply(Co, Json(Paket("GECERLI-1")));

        Assert.NotNull(res.Rejected);
        var red = res.Rejected!;
        Assert.DoesNotContain(red, r => r.Key == "GECERLI-1");
        var ele = Assert.Single(red, r => r.Table == "materials");
        Assert.Equal("YABANCI-1", ele.Key);
        Assert.True(ele.Permanent);
        Assert.Contains("başka bir firmaya", ele.Reason);
        var oksuz = Assert.Single(red, r => r.Table == "material_equivalents");
        Assert.Equal("YOK-A|YOK-B", oksuz.Key);   // birleşik anahtar: birincil anahtar sırasıyla
        Assert.True(oksuz.Permanent);
    }

    [Fact]
    public void RED2_Liste_Kaydedilir_Tekrarda_Artar_Kabulde_Silinir()
    {
        var svc = new GonderimRedleriService(_masa, _clock);
        var paket = Paket("GECERLI-2");
        var red = new[] { new RejectedRow("materials", "YABANCI-1", "materials: kayıt kimliği başka bir firmaya ait (atlandı).", true) };
        svc.Isle(Co, paket, red);
        Assert.Equal(1, svc.AcikSayisi(Co));

        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        svc.Isle(Co, paket, red);
        var k = Assert.Single(svc.Acik(Co));
        Assert.Equal(2, k.Deneme);
        Assert.True(k.SonZaman > k.IlkZaman);
        Assert.Equal("Malzeme", k.TabloEtiketi);

        svc.Isle(Co, paket, Array.Empty<RejectedRow>());   // aynı satır bu kez KABUL edildi
        Assert.Equal(0, svc.AcikSayisi(Co));
        Assert.Equal(0, svc.AcikSayisi(Yabanci));          // firma kapsamı
    }

    [Fact]
    public void RED3_Listeden_Kaldirilan_Gizli_Kalir()
    {
        var svc = new GonderimRedleriService(_masa, _clock);
        var red = new[] { new RejectedRow("materials", "X-1", "neden", false) };
        svc.Isle(Co, """{ "tables": {} }""", red);
        var k = Assert.Single(svc.Acik(Co));
        svc.Yoksay(Co, k.Anahtar);
        Assert.Equal(0, svc.AcikSayisi(Co));
        svc.Isle(Co, """{ "tables": {} }""", red);          // tekrar reddedildi
        Assert.Equal(0, svc.AcikSayisi(Co));
    }

    [Fact]
    public void RED4_Yeniden_Gonder_Satiri_Gonderme_Paketine_Sokar()
    {
        var m = new MaterialService(_masa, _clock).Create(_admin, new NewMaterial("R-40", "Gidemedi"));
        var sync = new BusinessSyncService(_masa, _clock);
        var imlec = sync.LocalChangeSeq();
        var svc = new GonderimRedleriService(_masa, _clock);
        svc.Isle(Co, """{ "tables": {} }""", new[] { new RejectedRow("materials", m, "materials: geçici hata", false) });
        var k = Assert.Single(svc.Acik(Co));
        Assert.True(k.YereldeVar);
        Assert.Contains("R-40", k.Tanim);

        Assert.DoesNotContain(m, sync.BuildPushSnapshot(Co, "pc", imlec));
        Assert.True(svc.YenidenGonder(Co, k.Anahtar));
        Assert.Contains(m, sync.BuildPushSnapshot(Co, "pc", imlec));
        Assert.False(svc.YenidenGonder(Co, "olmayan-anahtar"));
    }

    [Fact]
    public void RED5_Kart_Metinleri()
    {
        var kalici = (new GonderilemeyenKayit("a", "materials", "Malzeme", "id1", "R-1 · Çimento",
            "materials: kayıt kimliği başka bir firmaya ait (atlandı).", true, 1, 1, 1, true));
        Assert.Equal("Malzeme · R-1 · Çimento", kalici.Baslik);
        Assert.Equal("Neden: kayıt kimliği başka bir firmaya ait (atlandı).", kalici.NedenMetni);   // teknik tablo öneki yok
        Assert.Contains("kendiliğinden düzelmez", kalici.Ipucu);

        var gecici = (new GonderilemeyenKayit("b", "materials", "Malzeme", "id2", "id2", "zaman aşımı", false, 1, 2, 3, true));
        Assert.Contains("Yeniden Gönder", gecici.Ipucu);
        Assert.Contains("3 deneme", gecici.ZamanMetni);

        var yok = (new GonderilemeyenKayit("c", "materials", "Malzeme", "id3", "id3", "x", true, 1, 1, 1, false));
        Assert.False(yok.YereldeVar);
        Assert.Contains("artık bu bilgisayarda yok", yok.Ipucu);
    }

    [Fact]
    public void RED6_Red_Listesi_Ust_Siniri()
    {
        var satirlar = string.Join(",", Enumerable.Range(0, BusinessSyncService.MaxRejected + 50)
            .Select(i => $$"""{ "material_id": "YOK-{{i}}", "equivalent_material_id": "YOK-B" }"""));
        var res = new BusinessSyncService(_sunucu).Apply(Co, Json($$"""{ "tables": { "material_equivalents": [ {{satirlar}} ] } }"""));
        Assert.Equal(BusinessSyncService.MaxRejected + 50, res.Skipped);
        Assert.Equal(BusinessSyncService.MaxRejected, res.Rejected!.Count);
    }

    [Theory]
    [InlineData("", "5-16384", 0, 100, SunucuDonemi.Karar.IlkKayit)]
    [InlineData("5-16384", "5-16384", 100, 120, SunucuDonemi.Karar.Devam)]
    [InlineData("5-16384", "5-16384", 100, 100, SunucuDonemi.Karar.Devam)]      // değişiklik yok
    [InlineData("5-16384", "5-99999", 100, 500, SunucuDonemi.Karar.SunucuDegisti)]   // dökümden geri yükleme / yeni sunucu
    [InlineData("5-16384", "5-16384", 500, 120, SunucuDonemi.Karar.SunucuDegisti)]   // ufuk geriledi (geri sarma)
    [InlineData("5-16384", "", 100, 120, SunucuDonemi.Karar.Devam)]             // eski sunucu dönem göndermiyor
    [InlineData("", "", 0, 0, SunucuDonemi.Karar.Devam)]
    public void DNM1_Sunucu_Donemi_Kararlari(string eski, string yeni, long after, long next, SunucuDonemi.Karar beklenen)
        => Assert.Equal(beklenen, SunucuDonemi.Degerlendir(eski, yeni, after, next));

    [Fact]
    public void DNM2_V2_Yaniti_Donem_Tasir()
    {
        var (json, _) = new BusinessSyncService(_sunucu).BuildDeltaV2(Co, 0);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("sqlite", doc.RootElement.GetProperty("epoch").GetString());
    }

    [Fact]
    public void SIF1_Yereli_Sifirla_Once_Gonderir_Kayip_Varsa_Ayrica_Sorar()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var src = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"));
        var bas = src.IndexOf("private async System.Threading.Tasks.Task ResetLocalAndResync()", StringComparison.Ordinal);
        Assert.True(bas > 0);
        var govde = src.Substring(bas, src.IndexOf("// İş verisi eşitleme — DUYARLI", bas, StringComparison.Ordinal) - bas);
        var push = govde.IndexOf("await BusinessSyncPushService.PushAsync();", StringComparison.Ordinal);
        var kontrol = govde.IndexOf("BusinessSyncPushService.YerelBekleyenVar()", StringComparison.Ordinal);
        var onay = govde.IndexOf("\"Yine de Sıfırla\"", StringComparison.Ordinal);
        var sil = govde.IndexOf("PurgeBusinessData(cid)", StringComparison.Ordinal);
        Assert.True(push > 0 && kontrol > push && onay > kontrol && sil > onay,
            "M-79: sıra 'gönder → bekleyen var mı → sayılı onay → sil' olmalı");
        Assert.Contains("GonderilemeyenSayisi()", govde);
    }
}
