using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 4 (Custom Rapor) — FAZ 3 / S1: ESKİ İSTEMCİ + BİLİNMEYEN SENKRON TABLOSU ═══
///
/// <b>Neden var:</b> ADR-186 / PK-CR-02=A, custom rapor tanımlarının YENİ bir tabloda saklanıp
/// <see cref="BusinessSyncService"/> ile senkronlanmasına karar verdi. FAZ 1'de "eski istemciler
/// bilinmeyen tabloyu sessizce yok sayar" iddiası vardı ama bu iddia YALNIZ KOD OKUNARAK kabul
/// edilmemesi gereken bir noktadır (kullanıcı talimatı: "varsayımla kapatma, GERÇEK TEST YAP").
///
/// <b>Bu dosya HENÜZ YENİ TABLO OLUŞTURMADAN mekanizmayı kanıtlar:</b> senkron paketine, alıcının
/// TANIMADIĞI bir tablo adı konur ve alıcının davranışı ölçülür. Mekanizma tablo-adından bağımsız
/// olduğu için sonuç, ileride eklenecek <c>custom_report_defs</c> için de aynen geçerlidir.
///
/// <b>Kanıtlanan iki kapı:</b>
///  • <c>ApplyCore</c> döngüsü <b>ALICININ KENDİ</b> <see cref="BusinessSyncService.Tables"/> dizisini
///    gezer (paketin tablolarını DEĞİL) → pakette fazladan gelen tablo hiç ziyaret edilmez.
///  • Ziyaret edilse bile <c>TableExists</c> kapısı, yerel şemada olmayan tabloyu atlar.
///
/// ⚠️ Bu testler ürün kodunu DEĞİŞTİRMEZ; yalnız mevcut davranışı ölçer (S1 doğrulaması).
/// Production'a bağlanılmaz; her test kendi geçici SQLite dosyasında çalışır.
/// </summary>
public class CustomRaporSenkronOnDogrulamaTests : IDisposable
{
    private readonly string _srcPath, _dstPath;
    private readonly SqliteConnectionFactory _src, _dst;
    private readonly TestClock _clock = new();

    public CustomRaporSenkronOnDogrulamaTests()
    {
        _srcPath = Path.Combine(Path.GetTempPath(), "dw_cr_src_" + Guid.NewGuid().ToString("N") + ".db");
        _dstPath = Path.Combine(Path.GetTempPath(), "dw_cr_dst_" + Guid.NewGuid().ToString("N") + ".db");
        _src = new SqliteConnectionFactory(_srcPath);
        _dst = new SqliteConnectionFactory(_dstPath);
        new MigrationRunner(_src).Run();
        new MigrationRunner(_dst).Run();
        SeedCompany(_src, "CR-A");
        SeedCompany(_dst, "CR-A");
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_srcPath); } catch { }
        try { File.Delete(_dstPath); } catch { }
    }

    private static void Exec(SqliteConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
        => Exec(f, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@n,1,1,1,0);",
            ("@i", id), ("@n", id));

    private static void InsertPersonnel(SqliteConnectionFactory f, string id, string name, long updatedAt)
        => Exec(f, "INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) " +
                   "VALUES(@i,'CR-A',@n,1,1,@u,1,0);",
            ("@i", id), ("@n", name), ("@u", updatedAt));

    private static string? Scalar(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : Convert.ToString(v);
    }

    /// <summary>Gerçek snapshot'a, alıcının TANIMADIĞI bir tablo enjekte eder (yeni sunucunun
    /// gönderdiği "gelecekteki" tabloyu taklit eder).</summary>
    private static string PaketeBilinmeyenTabloEkle(string snapshotJson, string bilinmeyenTablo)
    {
        using var doc = JsonDocument.Parse(snapshotJson);
        var kok = doc.RootElement;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var p in kok.EnumerateObject())
            {
                if (p.NameEquals("tables"))
                {
                    w.WritePropertyName("tables");
                    w.WriteStartObject();
                    foreach (var t in p.Value.EnumerateObject()) t.WriteTo(w);
                    // ⭐ Alıcının bilmediği tablo — yeni sunucudan gelen custom rapor tanımlarını taklit eder.
                    w.WritePropertyName(bilinmeyenTablo);
                    w.WriteStartArray();
                    w.WriteStartObject();
                    w.WriteString("id", "CRD-1");
                    w.WriteString("company_id", "CR-A");
                    w.WriteString("name", "Aylık Yakıt Dökümü");
                    w.WriteNumber("updated_at", 9_000L);
                    w.WriteEndObject();
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                else p.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // ESK-01 — ÇEKME (pull): eski istemci, bilinmeyen tablo içeren paketi ALIR
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ ANA KİLİT (PK-CR-02=A ön koşulu): Paket, alıcının TANIMADIĞI bir tablo içerse bile
    /// (a) İSTİSNA ATILMAZ, (b) bilinen tablolar NORMAL uygulanır, (c) bilinmeyen tablo sessizce
    /// yok sayılır, (d) transaction geri alınmaz. Yani yeni sunucu + eski masaüstü BOZULMAZ.</summary>
    [Fact]
    public void ESK01_Bilinmeyen_Tablo_Iceren_Paket_Eski_Istemciyi_Bozmaz()
    {
        InsertPersonnel(_src, "P1", "Ali", updatedAt: 5000);
        InsertPersonnel(_src, "P2", "Veli", updatedAt: 5000);

        var snapshot = new BusinessSyncService(_src, _clock).BuildSnapshot("CR-A");
        var bozulmus = PaketeBilinmeyenTabloEkle(snapshot, "custom_report_defs");

        using var doc = JsonDocument.Parse(bozulmus);

        // (a) İstisna YOK
        var sonuc = new BusinessSyncService(_dst, _clock).ApplyPull("CR-A", doc.RootElement);

        // (b) Bilinen tablolar uygulandı
        Assert.Equal("Ali", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='P1';"));
        Assert.Equal("Veli", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='P2';"));
        Assert.True(sonuc.Upserted >= 2, $"En az 2 satır uygulanmalıydı, uygulanan: {sonuc.Upserted}");

        // (c) Bilinmeyen tablo için HATA ÜRETİLMEDİ (sessiz yok sayma)
        Assert.DoesNotContain(sonuc.Errors, e => e.Contains("custom_report_defs", StringComparison.Ordinal));

        // (d) Bilinmeyen tablo yerelde OLUŞTURULMADI (senkron şema yaratmaz)
        Assert.Equal("0", Scalar(_dst,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='custom_report_defs';"));
    }

    /// <summary>⭐ Aynı senaryo GÖNDERME (push) yönünde: sunucu, eski istemciden gelen ve bilinmeyen
    /// tablo içeren paketi de bozulmadan işler.</summary>
    [Fact]
    public void ESK02_Bilinmeyen_Tablo_Iceren_Push_Paketi_Sunucuyu_Bozmaz()
    {
        InsertPersonnel(_src, "P3", "Ayşe", updatedAt: 6000);

        var snapshot = new BusinessSyncService(_src, _clock).BuildSnapshot("CR-A");
        var bozulmus = PaketeBilinmeyenTabloEkle(snapshot, "gelecekteki_tablo_x");
        using var doc = JsonDocument.Parse(bozulmus);

        var sonuc = new BusinessSyncService(_dst, _clock).Apply("CR-A", doc.RootElement);

        Assert.Equal("Ayşe", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='P3';"));
        Assert.DoesNotContain(sonuc.Errors, e => e.Contains("gelecekteki_tablo_x", StringComparison.Ordinal));
    }

    /// <summary>⭐ TableExists kapısı: alıcının <c>Tables</c> listesinde OLAN ama yerel şemasında
    /// BULUNMAYAN tablo atlanır — diğer tablolar yine uygulanır.
    ///
    /// Gerçek "eski şema" ile ölçülür: <c>announcements</c> Migration081'de eklendiği için şema 80'de
    /// kurulan bir veritabanında YOKTUR; buna karşın güncel kodun <c>Tables</c> dizisinde VARDIR.
    /// Bu, "yeni kod + eski yerel şema" durumunun birebir provasıdır.</summary>
    [Fact]
    public void ESK03_Yerel_Semada_Olmayan_Tablo_TableExists_Ile_Atlanir()
    {
        var eskiYol = Path.Combine(Path.GetTempPath(), "dw_cr_eski_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var eski = new SqliteConnectionFactory(eskiYol);
            new MigrationRunner(eski, MigrationCatalog.All().Where(m => m.Version <= 80)).Run();
            SeedCompany(eski, "CR-A");

            // Ön koşul: bu şemada announcements YOK ama güncel Tables listesinde VAR.
            Assert.Equal("0", Scalar(eski,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='announcements';"));
            Assert.Contains("announcements", BusinessSyncService.Tables);

            InsertPersonnel(_src, "P4", "Mehmet", updatedAt: 7000);
            using var snap = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("CR-A"));

            // İstisna YOK; personel uygulanır, announcements sessizce atlanır.
            var sonuc = new BusinessSyncService(eski, _clock).ApplyPull("CR-A", snap.RootElement);

            Assert.Equal("Mehmet", Scalar(eski, "SELECT full_name FROM personnel WHERE id='P4';"));
            Assert.DoesNotContain(sonuc.Errors, e => e.Contains("announcements", StringComparison.Ordinal));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(eskiYol); } catch { }
        }
    }

    /// <summary>⭐ Bilinmeyen tablo, GEÇERLİ satırların transaction'ını geri aldırmaz:
    /// aynı pakette hem bilinmeyen tablo hem çok sayıda geçerli satır varken hepsi yazılır.</summary>
    [Fact]
    public void ESK04_Bilinmeyen_Tablo_Gecerli_Satirlari_Rollback_Ettirmez()
    {
        for (int i = 1; i <= 10; i++) InsertPersonnel(_src, $"PB{i}", $"Kişi {i}", updatedAt: 8000);

        var snapshot = new BusinessSyncService(_src, _clock).BuildSnapshot("CR-A");
        using var doc = JsonDocument.Parse(PaketeBilinmeyenTabloEkle(snapshot, "custom_report_defs"));

        new BusinessSyncService(_dst, _clock).ApplyPull("CR-A", doc.RootElement);

        Assert.Equal("10", Scalar(_dst, "SELECT COUNT(*) FROM personnel WHERE id LIKE 'PB%';"));
    }

    /// <summary>⭐ Senkron ŞEMA YARATMAZ (PK-CR-02 tasarım sınırı): tablo yalnız migration ile gelir.
    /// Bu kilit, ileride custom rapor tablosunun senkron tarafından "kendiliğinden" oluşturulacağı
    /// yanlış varsayımını engeller — tanım tablosu Migration ile kurulmak ZORUNDADIR.</summary>
    [Fact]
    public void ESK05_Senkron_Yeni_Tablo_Olusturmaz()
    {
        var oncekiTabloSayisi = Scalar(_dst, "SELECT COUNT(*) FROM sqlite_master WHERE type='table';");

        var snapshot = new BusinessSyncService(_src, _clock).BuildSnapshot("CR-A");
        using var doc = JsonDocument.Parse(PaketeBilinmeyenTabloEkle(snapshot, "custom_report_defs"));
        new BusinessSyncService(_dst, _clock).ApplyPull("CR-A", doc.RootElement);

        Assert.Equal(oncekiTabloSayisi, Scalar(_dst, "SELECT COUNT(*) FROM sqlite_master WHERE type='table';"));
    }
}
