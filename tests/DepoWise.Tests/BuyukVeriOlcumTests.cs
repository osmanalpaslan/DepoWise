using System.Diagnostics;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;
using Xunit.Abstractions;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ K §7 (2026-09-05) — 10.000+ KAYIT ÖLÇÜMÜ ═══
///
/// <b>Neden gerekli:</b> bu turda stok hareketleri ve bakım listelerine sunucu taraflı sayfalama
/// (LST-01) ve indeks (Migration091) eklendi. Sayfalama her sayfada bir ek COUNT(*) çalıştırır;
/// indeks yanlışsa düzeltme performansı <b>iyileştirmek yerine kötüleştirebilirdi</b>. Bu yüzden
/// "sorgu hızlı görünüyor" yetmez — kademeli olarak ÖLÇÜLÜR.
///
/// <b>Kademeler:</b> 10 · 100 · 1.000 · 5.000 · 10.000 · 25.000 satır.
/// Her kademede ölçülen: ilk sayfa · derin sayfa (son sayfa) · arama/filtre · en büyük sayfa.
///
/// <b>Ölçüm nereye yazılır:</b> <c>artifacts/faz_k_olcum.md</c> — rapordaki sayılar TAHMİN değil,
/// bu dosyadan gelir (protokol §32: "Her sayı gerçek ölçümden gelir").
///
/// <b>Veri nasıl üretilir:</b> ham SQL ile. Servis üzerinden 25.000 belge yazmak dakikalar sürerdi ve
/// ölçülen şey liste sorgusu değil, YAZMA yolu olurdu. Okuma yolunu ölçmek için satırların nasıl
/// oluştuğu değil, tabloda ne kadar olduğu önemlidir. (Yazma yolunun doğruluğu ayrı testlerdedir.)
///
/// <b>Eşik felsefesi:</b> uydurma bir milisaniye hedefi konmaz (protokol §7). Assert yalnız
/// <b>ölçek davranışını</b> kilitler: 25.000 satırda ilk sayfa, 10 satırdaki hâline göre orantısız
/// büyümemelidir — yani sorgu satır sayısıyla DOĞRUSAL büyümemeli (indeks çalışıyor olmalı).
/// </summary>
public class BuyukVeriOlcumTests : IDisposable
{
    private const string Co = "BVO";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;
    private readonly string _mat, _sube;
    private readonly ITestOutputHelper _cikti;
    private static readonly long Gun = 1_700_000_000_000;

    public BuyukVeriOlcumTests(ITestOutputHelper cikti)
    {
        _cikti = cikti;
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_bvo_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Ham SQL ile hareket üretir. Tek transaction — 25.000 satır saniyeler içinde yazılır.</summary>
    private void HareketUret(int adet, int baslangic)
    {
        if (adet <= 0) return;
        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction," +
            "quantity,unit_price,currency_code,operation_id,note,created_at,updated_at) " +
            "VALUES(@id,@co,@mat,@br,'in',1,'1','10','TRY',@op,@note,@ts,@ts);";

        Microsoft.Data.Sqlite.SqliteParameter Ekle(string ad)
        {
            var x = (Microsoft.Data.Sqlite.SqliteParameter)cmd.CreateParameter();
            x.ParameterName = ad;
            cmd.Parameters.Add(x);
            return x;
        }

        var id = Ekle("@id"); var co = Ekle("@co"); var mat = Ekle("@mat"); var br = Ekle("@br");
        var op = Ekle("@op"); var note = Ekle("@note"); var ts = Ekle("@ts");
        co.Value = Co; mat.Value = _mat; br.Value = _sube;

        for (int i = 0; i < adet; i++)
        {
            var n = baslangic + i;
            id.Value = Guid.NewGuid().ToString("N");
            op.Value = "seed-" + n;
            // Aramanın gerçekten satır ELEMESİ için nadir bir işaret: her 1000'de bir.
            note.Value = n % 1000 == 0 ? "NADIR-" + n : "Sevkiyat " + n;
            ts.Value = Gun + n;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static long Olc(Action islem)
    {
        var sw = Stopwatch.StartNew();
        islem();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    /// <summary>
    /// Kademeli ölçüm. Her kademede aynı dört işlem yapılır; sonuç dosyaya yazılır.
    /// Assert'ler ölçeği kilitler, keyfi bir hız hedefi koymaz.
    /// </summary>
    [Fact]
    public void BV1_Stok_Hareketi_Listesi_Kademeli_Olculur()
    {
        var stock = new StockService(_f);
        int[] kademeler = { 10, 100, 1_000, 5_000, 10_000, 25_000 };
        var satirlar = new List<string>();
        long ilkKademeIlkSayfa = 0, sonKademeIlkSayfa = 0;
        int yazilan = 0;

        foreach (var hedef in kademeler)
        {
            HareketUret(hedef - yazilan, yazilan);
            yazilan = hedef;

            // 1) İlk sayfa — ekran açılışında çalışan sorgu (sayım dâhil).
            GridResult<StockMovementRow>? ilk = null;
            var msIlk = Olc(() => ilk = stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, 1, 50));
            Assert.Equal(hedef, ilk!.TotalCount);
            Assert.Equal(Math.Min(50, hedef), ilk.Items.Count);

            // 2) Derin sayfa — sayfalamanın en pahalı hâli (son sayfa).
            var sonSayfa = Math.Max(1, (int)Math.Ceiling(hedef / 50.0));
            var msDerin = Olc(() => stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, sonSayfa, 50));

            // 3) Arama — satırların ~binde birini döndürür.
            GridResult<StockMovementRow>? ara = null;
            var msAra = Olc(() => ara = stock.SearchMovementsGrid(_admin, null, null, "NADIR", null, null, null, 1, 50));

            // 4) En büyük sayfa — 200 satırlık istek.
            var msBuyukSayfa = Olc(() => stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, 1, 200));

            satirlar.Add("| " + hedef.ToString("N0") + " | " + msIlk + " | " + msDerin + " | " +
                         msAra + " (" + ara!.TotalCount + " eşleşme) | " + msBuyukSayfa + " |");
            if (hedef == kademeler[0]) ilkKademeIlkSayfa = msIlk;
            if (hedef == kademeler[^1]) sonKademeIlkSayfa = msIlk;
        }

        var rapor =
            "# FAZ K §7 — Büyük veri ölçümü (stok hareketleri listesi)\n\n" +
            "> Ölçüm: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · SQLite (masaüstü lehçesi) · " +
            "sayfa boyutu 50 · geliştirme bilgisayarı · her satır tek koşudur (ortalama alınmadı).\n\n" +
            "| Satır | İlk sayfa (ms) | Son sayfa (ms) | Arama (ms) | 200'lük sayfa (ms) |\n" +
            "|---|---|---|---|---|\n" +
            string.Join("\n", satirlar) + "\n";
        var kok = RepoKok();
        Directory.CreateDirectory(Path.Combine(kok, "artifacts"));
        File.WriteAllText(Path.Combine(kok, "artifacts", "faz_k_olcum.md"), rapor);
        _cikti.WriteLine(rapor);

        // ÖLÇEK KİLİDİ: 2.500 kat veri, ilk sayfa süresini doğrusal büyütmemeli.
        // (İndeks çalışmazsa tam tablo taraması olur ve bu oran patlar.)
        Assert.True(sonKademeIlkSayfa <= Math.Max(300, ilkKademeIlkSayfa * 50),
            "25.000 satırda ilk sayfa " + sonKademeIlkSayfa + "ms — 10 satırdaki " + ilkKademeIlkSayfa + "ms'e göre orantısız.");

        // Kullanılabilirlik tavanı: liste ekranı 25.000 satırda da saniyeler sürmemeli.
        Assert.True(sonKademeIlkSayfa < 3000, "25.000 satırda ilk sayfa " + sonKademeIlkSayfa + "ms — kullanılabilirliği bozar.");
    }

    /// <summary>
    /// Sayfa tavanı büyük veride de UYGULANIYOR mu. İstemci "hepsini ver" derse (pageSize=100000)
    /// sunucu kırpmalı — aksi hâlde 25.000 satır tek yanıtta gider, bellek ve ağ patlar.
    ///
    /// <b>Ölçülen gerçek:</b> ızgara (grid) yollarının tavanı <b>500</b>'dür ve projede
    /// <c>MaterialService · VehicleService · StockService · MaintenanceService · FuelService ·
    /// DailyActivityService · PartyService · InvoiceReads · FinanceReads</c> içinde AYNI biçimde
    /// uygulanır. İmleçli (cursor) listelerin tavanı ise ayrıdır: <c>PageRequest.MaxLimit = 200</c>.
    /// İkisi FARKLI sayılardır ve bu test farkı kayda geçirir — biri diğeri sanılıp yanlış varsayım
    /// yapılmasın (bu gece personel dışa aktarımında tam olarak bu karışıklık bir kusur üretmişti:
    /// <c>Limit = 100_000</c> yazılmıştı, 200'e kırpılıyordu).
    ///
    /// Tavanın <b>sessiz kayıp</b> üretmediği de doğrulanır: <c>TotalCount</c> gerçek toplamı söyler,
    /// yani arayüz "daha var" diyebilir.
    /// </summary>
    [Fact]
    public void BV2_Buyuk_Veride_Sayfa_Tavani_Uygulanir()
    {
        HareketUret(5_000, 0);
        var stock = new StockService(_f);
        var sonuc = stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, 1, 100_000);

        Assert.Equal(5_000, sonuc.TotalCount);                    // toplam DÜRÜST — kayıp gizlenmiyor
        Assert.Equal(500, sonuc.Items.Count);                     // ızgara tavanı
        Assert.True(sonuc.Items.Count < sonuc.TotalCount);        // tek yanıtta her şey gitmiyor

        // İmleçli liste yolunun tavanı AYRI ve daha düşüktür — ikisi karıştırılmamalı.
        Assert.Equal(200, PageRequest.MaxLimit);
        Assert.NotEqual(PageRequest.MaxLimit, sonuc.Items.Count);
    }

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
