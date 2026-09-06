using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AÇILIŞTA BOZUK İNDEKS ONARIMI (kullanıcı bildirimi 2026-09-07) ═══
///
/// <para><b>Yaşanan:</b> kullanıcı 1.0.184'e güncelledi ve uygulama <b>hiç açılmadı</b>. Açılış
/// günlüğündeki tek satır: <c>ok=False … wrong # of entries in index ix_audit_company_time</c>.
/// Bozulan şey bir <b>indeksti</b>; kullanıcının verisi yerindeydi (onarımdan önce ve sonra sayımlar
/// birebir aynı çıktı: denetim 2012 · malzeme 75 · araç 75 · gönderilmemiş kayıt 0) ama açılış
/// kontrolü uygulamayı tümden durduruyor ve "yedeği geri yükleyin" diyordu.</para>
///
/// <para><b>Düzeltme:</b> indeks TÜRETİLMİŞ veridir — tablodan yeniden üretilir, hiçbir kaydı
/// değiştirmez. Bu yüzden yalnız indeks kaynaklı bozulmada <c>REINDEX</c> denenir ve kontrol
/// tekrarlanır. Düzelirse uygulama normal açılır (olay günlüğe yazılır); düzelmezse ya da bozulma
/// TABLO sayfalarındaysa eski davranış korunur — veri riski varken uygulama açılmamalıdır.</para>
///
/// <para><b>Neden gerçek bozulma taklit edilmiyor:</b> SQLite dosyasını taşınabilir ve kararlı
/// biçimde "indeks bozuk ama tablo sağlam" hâline getirmenin desteklenen bir yolu yok; ham bayt
/// oynamak SQLite sürümüne bağımlı, kırılgan bir test üretirdi. Bu yüzden burada (1) sağlıklı
/// veritabanının davranışı ve (2) onarım sözleşmesi doğrulanır. Gerçek onarım, kullanıcının bozuk
/// veritabanı üzerinde <b>elle çalıştırılarak</b> doğrulandı: <c>integrity_check</c> "ok" döndü ve
/// tüm sayımlar korundu.</para>
/// </summary>
public class AcilisBozukIndeksOnarimiTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;

    public AcilisBozukIndeksOnarimiTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_saglik_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
    }

    /// <summary>Sağlıklı veritabanı: açılış kontrolü geçer ve ONARIM YAPILMAZ (gereksiz REINDEX yok).</summary>
    [Fact]
    public async Task SaglikliVeritabani_Gecer_VeOnarimYapilmaz()
    {
        var sonuc = await new DatabaseHealth(_f).CheckAsync();
        Assert.True(sonuc.Ok, "Sağlıklı veritabanı açılış kontrolünü geçmeli. Hata: " + sonuc.Error);
        Assert.Null(sonuc.Error);
        Assert.Null(sonuc.Onarim);
        Assert.Equal("wal", sonuc.JournalMode);
        Assert.True(sonuc.ForeignKeysOn);
        Assert.True(sonuc.WriteReadOk);
    }

    /// <summary>REINDEX veriyi DEĞİŞTİRMEZ — onarımın güvenli olmasının dayanağı budur.</summary>
    [Fact]
    public void Reindex_KayitlariDegistirmez()
    {
        using (var conn = _f.Create())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('X','X',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }

        long Say()
        {
            using var c = _f.Create();
            using var k = c.CreateCommand();
            k.CommandText = "SELECT COUNT(*) FROM companies;";
            return Convert.ToInt64(k.ExecuteScalar());
        }

        var once = Say();
        using (var conn = _f.Create())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "REINDEX;";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(once, Say());

        using (var conn = _f.Create())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", cmd.ExecuteScalar()?.ToString());
        }
    }

    /// <summary>
    /// SÖZLEŞME: kontrol, indeks kaynaklı bozulmada önce ONARIM dener; tablo bozulmasında denemez.
    /// (Gerçek bozulma taklit edilemediği için sözleşme kaynaktan korunur — yukarıdaki açıklamaya bakın.)
    /// </summary>
    [Fact]
    public void Kontrol_YalnizIndeks_Bozulmasinda_Onarim_Dener()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        var s = File.ReadAllText(Path.Combine(d!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "DatabaseHealth.cs"));

        Assert.Contains("Contains(\"index\"", s);   // yalnız indeks kaynaklıysa
        Assert.Contains("REINDEX", s);
        Assert.Contains("Onarim:", s);
        // Onarım tutmadıysa ya da tablo bozulmasıysa uygulama YİNE açılmamalı.
        Assert.Contains("Veritabanı dosyası hasarlı görünüyor", s);
    }

    /// <summary>
    /// Güncelleme kurulumu artık HATA FIRLATMAZ: iki çağıran <c>async void</c> olduğu için fırlayan
    /// hata yakalanamıyor ve uygulama sessizce ölüyordu ("güncelledim, açılmıyor").
    /// </summary>
    [Fact]
    public void GuncellemeKurulumu_Hata_Firlatmaz_KullaniciyaSoyler()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { d!.FullName }.Concat(p).ToArray()));

        var svc = Oku("src", "DepoWise.Desktop", "AutoUpdateService.cs");
        Assert.Contains("public static bool InstallPendingNow()", svc);
        Assert.Contains("SonKurulumHatasi", svc);
        Assert.Contains("ClearPending();", svc);   // bozuk paketle sonsuz döngü olmasın

        // Üç çağıranın üçü de dönüş değerini KONTROL etmeli.
        foreach (var yol in new[]
                 {
                     new[] { "src", "DepoWise.Desktop", "App.axaml.cs" },
                     new[] { "src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs" },
                     new[] { "src", "DepoWise.Desktop", "Views", "MainWindow.axaml.cs" },
                 })
        {
            var k = Oku(yol);
            Assert.Contains("AutoUpdateService.InstallPendingNow()", k);
            Assert.Contains("Güncelleme Kurulamadı", k);
        }
    }

    public void Dispose() { try { File.Delete(_db); } catch { } }
}
