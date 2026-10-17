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
/// ═══ M-17 (AR-GE 2026-10-17) — SAYFALI, KALDIĞI YERDEN SÜREN ÇEKME ═══
///
///  SAY1 — Sayfaların birleşimi = sayfasız paket (eksik/fazla yok); hiçbir sayfa sınırı aşmaz; tüm sayfalarda ufuk aynı
///  SAY2 — Sayfaya giren ebeveynin (malzeme) TÜM çocukları (muadil) aynı sayfada — alıcı onları arada silmesin
///  SAY3 — Sayfalama SIRASINDA eklenen kayıt kaybolmaz: sonraki delta (after = ufuk) onu getirir
///  SAY4 — Bozuk sayfa imleci reddedilir (ArgumentException → uç 400)
///  SAY5 — Döngü: 3. sayfada bağlantı koptu → ilerleme kayıtlı; ikinci çağrı aynı ufukla kaldığı sayfadan sürer, sonuç tam
///  SAY6 — Döngü: ilk sayfa reddedilirse (sunucu dönemi değişti) hiçbir şey uygulanmaz
///  SAY7 — Uçtan uca (gerçek servis + masaüstü veritabanı): 1 satırlık sayfalarla bile masaüstü TAM veriye ulaşır (çapraz bağlı muadiller dahil)
/// </summary>
public class SayfaliCekimTests : IDisposable
{
    private const string Co = "SAY";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly BusinessSyncService _sync;
    private readonly MaterialService _materials;
    private readonly SessionContext _admin;
    private readonly List<string> _silinecek = new();

    public SayfaliCekimTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_say_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir(_f, "INSERT OR IGNORE INTO sync_server_role(id) VALUES (1);");
        Calistir(_f, $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin_say", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sync = new BusinessSyncService(_f);
        _materials = new MaterialService(_f);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in _silinecek.Append(_dbPath)) try { File.Delete(p); } catch { }
    }

    private static void Calistir(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static (Dictionary<string, List<JsonElement>> Tablolar, long Next, string? Sayfa) Coz(string json)
    {
        var doc = JsonDocument.Parse(json).RootElement.Clone();
        var t = doc.GetProperty("tables").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().ToList());
        var s = doc.TryGetProperty("nextPage", out var np) && np.ValueKind == JsonValueKind.String ? np.GetString() : null;
        return (t, doc.GetProperty("nextAfter").GetInt64(), s);
    }

    private static string Anahtar(string tablo, JsonElement r)
        => tablo + ":" + (r.TryGetProperty("id", out var id) ? id.ToString()
            : string.Join("|", r.EnumerateObject().Where(p => p.Name.EndsWith("_id")).OrderBy(p => p.Name).Select(p => p.Value.ToString())));

    /// <summary>Sayfaları sonuna kadar çeker: (tüm satır anahtarları, sayfa sayısı, ufuklar, en büyük sayfa).</summary>
    private (HashSet<string> Hepsi, int Sayfa, HashSet<long> Ufuklar, int EnBuyuk) TumSayfalar(int limit, long after = 0, Action<int>? arada = null)
    {
        var hepsi = new HashSet<string>(); var ufuklar = new HashSet<long>();
        string? sayfa = null; long? u = null; int n = 0, enBuyuk = 0;
        do
        {
            var (t, next, s) = Coz(_sync.BuildDeltaV2(Co, after, null, limit, sayfa, u).Json);
            n++; u ??= next; ufuklar.Add(next);
            var anaSatir = t.Where(kv => kv.Key != "material_equivalents").Sum(kv => kv.Value.Count);
            enBuyuk = Math.Max(enBuyuk, anaSatir);
            foreach (var (tablo, rows) in t) foreach (var r in rows) hepsi.Add(Anahtar(tablo, r));
            sayfa = s;
            arada?.Invoke(n);
        } while (sayfa is not null);
        return (hepsi, n, ufuklar, enBuyuk);
    }

    private void Veri(int malzeme)
    {
        string? onceki = null;
        for (int i = 0; i < malzeme; i++)
        {
            var m = _materials.Create(_admin, new NewMaterial($"S-{i:D4}", $"Malzeme {i}"));
            if (onceki is not null && i % 10 == 0) _materials.AddEquivalent(_admin, m, onceki);
            onceki = m;
        }
    }

    [Fact]
    public void SAY1_Sayfalarin_Birlesimi_Tam_Paketle_Ayni()
    {
        Veri(250);
        var (t0, _, s0) = Coz(_sync.BuildDeltaV2(Co, 0).Json);
        Assert.Null(s0);   // limitsiz çağrı sayfalamaz (eski davranış)
        var tam = t0.SelectMany(kv => kv.Value.Select(r => Anahtar(kv.Key, r))).ToHashSet();

        var (hepsi, sayfa, ufuklar, enBuyuk) = TumSayfalar(limit: 40);
        Assert.True(sayfa >= 7, $"250+ satır 40'lık sayfalarda en az 7 sayfa olmalı (oldu: {sayfa})");
        Assert.True(enBuyuk <= 40, $"sayfa sınırı aşıldı: {enBuyuk}");
        Assert.Single(ufuklar);                    // tüm sayfalar aynı kesit
        Assert.Equal(tam.OrderBy(x => x), hepsi.OrderBy(x => x));
    }

    [Fact]
    public void SAY2_Ebeveynin_Cocuklari_Ayni_Sayfada()
    {
        Veri(120);
        string? sayfa = null; long? u = null;
        do
        {
            var (t, next, s) = Coz(_sync.BuildDeltaV2(Co, 0, null, 25, sayfa, u).Json);
            u ??= next;
            var malzemeler = t.TryGetValue("materials", out var ms) ? ms.Select(r => r.GetProperty("id").GetString()).ToHashSet() : new();
            var cocuklar = t.TryGetValue("material_equivalents", out var es) ? es : new();
            using var conn = _f.Create();
            foreach (var mid in malzemeler)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM material_equivalents WHERE material_id=@m;";
                cmd.AddWithValue("@m", mid!);
                var dbde = Convert.ToInt32(cmd.ExecuteScalar());
                var sayfada = cocuklar.Count(r => r.GetProperty("material_id").GetString() == mid);
                Assert.True(sayfada >= dbde, $"malzeme {mid}: {dbde} muadilin {sayfada}'i sayfada");
            }
            sayfa = s;
        } while (sayfa is not null);
    }

    [Fact]
    public void SAY3_Sayfalama_Sirasinda_Eklenen_Kayit_Kaybolmaz()
    {
        Veri(100);
        string? yeni = null;
        var (hepsi, _, ufuklar, _) = TumSayfalar(limit: 30, arada: n => { if (n == 1) yeni = _materials.Create(_admin, new NewMaterial("S-YENI", "Arada eklendi")); });
        var ufuk = Assert.Single(ufuklar);
        var (sonraki, _, _, _) = TumSayfalar(limit: 30, after: ufuk);
        Assert.True(hepsi.Contains("materials:" + yeni) || sonraki.Contains("materials:" + yeni),
            "sayfalama sırasında eklenen kayıt ne bu çekimde ne sonraki deltada geldi");
        Assert.Contains("materials:" + yeni, sonraki);   // ufuktan sonra yazıldığı için deltada kesin gelir
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1:")]
    [InlineData("999:")]
    [InlineData("3:@@@")]
    [InlineData("3:eyJ4IjoxfQ")]   // base64 {"x":1} — dizi değil
    public void SAY4_Bozuk_Imlec_Reddedilir(string imlec)
        => Assert.Throws<ArgumentException>(() => _sync.BuildDeltaV2(Co, 0, null, 50, imlec));

    /// <summary>Sahte sunucu: gerçek BuildDeltaV2'yi sorgu dizesinden çağırır; istenirse belirli sayfada "koparır".</summary>
    private Func<string, Task<(int, string?)>> SahteSunucu(int? koparSayfa, List<int> sayac)
        => q =>
        {
            sayac.Add(1);
            if (koparSayfa == sayac.Count) return Task.FromResult<(int, string?)>((503, null));
            var p = q.Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
            var json = _sync.BuildDeltaV2(Co, long.Parse(p["after"]), null, int.Parse(p["limit"]),
                p.GetValueOrDefault("page"), p.TryGetValue("u", out var u) ? long.Parse(u) : null).Json;
            return Task.FromResult<(int, string?)>((200, json));
        };

    [Fact]
    public async Task SAY5_Kopan_Cekim_Kaldigi_Yerden_Surer()
    {
        Veri(150);
        var uygulanan = new List<string>();
        SayfaIlerlemesi? kayit = null;
        int Uygula(JsonElement kok)
        {
            int n = 0;
            foreach (var t in kok.GetProperty("tables").EnumerateObject())
                foreach (var r in t.Value.EnumerateArray()) { uygulanan.Add(Anahtar(t.Name, r)); n++; }
            return n;
        }
        var sayac1 = new List<int>();
        var ilk = await SayfaliCekim.CalistirAsync(0, null, SahteSunucu(koparSayfa: 3, sayac1), (_, _, _) => true, Uygula, i => kayit = i, sayfaBoyu: 40);
        Assert.Equal(SayfaliDurum.Basarisiz, ilk.Durum);
        Assert.Equal(503, ilk.HttpDurum);
        Assert.NotNull(kayit);                                      // ilerleme kayıtlı
        var kayitliUfuk = kayit!.U;
        var ikiSayfa = uygulanan.Count;

        // Kayıt metne yazılıp okunur (gerçekte ayarlar tablosunda durur).
        var devam = SayfaIlerlemesi.Oku(kayit.Yaz());
        var sayac2 = new List<int>();
        bool ilkSayfaSoruldu = false;
        var ikinci = await SayfaliCekim.CalistirAsync(0, devam, SahteSunucu(null, sayac2), (_, _, _) => ilkSayfaSoruldu = true, Uygula, i => kayit = i, sayfaBoyu: 40);
        Assert.Equal(SayfaliDurum.Tamam, ikinci.Durum);
        Assert.False(ilkSayfaSoruldu);                              // devam eden çekim baştan başlamadı
        Assert.Equal(kayitliUfuk, ikinci.NextAfter);                // aynı kesit
        Assert.Null(kayit);                                         // bitti → ilerleme silindi
        Assert.True(uygulanan.Count > ikiSayfa);

        var (tamT, _, _) = Coz(_sync.BuildDeltaV2(Co, 0).Json);
        var tam = tamT.SelectMany(kv => kv.Value.Select(r => Anahtar(kv.Key, r))).ToHashSet();
        Assert.True(tam.SetEquals(uygulanan.ToHashSet()), "kopma + devam sonrası uygulanan küme tam paketle aynı olmalı");
    }

    [Fact]
    public async Task SAY6_Ilk_Sayfa_Reddedilirse_Hicbir_Sey_Uygulanmaz()
    {
        Veri(30);
        int uygulanan = 0;
        var s = await SayfaliCekim.CalistirAsync(500, null, SahteSunucu(null, new()), (_, _, _) => false, _ => ++uygulanan, _ => { }, sayfaBoyu: 10);
        Assert.Equal(SayfaliDurum.SunucuReddetti, s.Durum);
        Assert.Equal(0, uygulanan);
    }

    [Fact]
    public async Task SAY7_Masaustu_Tek_Satirlik_Sayfalarla_Tam_Veriye_Ulasir()
    {
        Veri(25);
        var yol = Path.Combine(Path.GetTempPath(), "dw_say_pc_" + Guid.NewGuid().ToString("N") + ".db");
        _silinecek.Add(yol);
        var masa = new SqliteConnectionFactory(yol);
        new MigrationRunner(masa).Run();
        Calistir(masa, $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var mSync = new BusinessSyncService(masa);
        var s = await SayfaliCekim.CalistirAsync(0, null, SahteSunucu(null, new()), (_, _, _) => true,
            kok => mSync.ApplyPull(Co, kok, null, bakiyeHesapla: false).Upserted, _ => { }, sayfaBoyu: 1);
        Assert.Equal(SayfaliDurum.Tamam, s.Durum);
        Assert.True(s.Sayfa >= 25);
        using var a = _f.Create(); using var b = masa.Create();
        foreach (var tablo in new[] { "materials", "material_equivalents" })
        {
            using var c1 = a.CreateCommand(); c1.CommandText = $"SELECT COUNT(*) FROM {tablo};";
            using var c2 = b.CreateCommand(); c2.CommandText = $"SELECT COUNT(*) FROM {tablo};";
            Assert.Equal(Convert.ToInt64(c1.ExecuteScalar()), Convert.ToInt64(c2.ExecuteScalar()));
        }
    }
}
