using Microsoft.Data.Sqlite;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TEST VERİTABANI TEMİZLİĞİ — HEDEFLİ (2026-09-07, ölçülerek bulundu) ═══
///
/// <para><b>Bulunan hata:</b> 177 test sınıfı <c>Dispose</c> içinde
/// <c>SqliteConnection.ClearAllPools()</c> çağırıyordu. Bu metot <b>SÜREÇ GENELİNDE</b> çalışır:
/// o an <i>paralel koşan başka bir testin</i> açık bağlantısını da kapatır. Sonuç, rastgele bir
/// testte <c>ObjectDisposedException: SQLitePCL.sqlite3</c> — yani ürün bozulmadan, her koşuda
/// BAŞKA bir testin düşmesi.</para>
///
/// <para>Gerçekten yaşandı: aynı gece iki tam süit koşuldu; birincisinde bir performans testi,
/// ikincisinde tamamen ilgisiz bir rapor testi bu istisnayla düştü. Böyle bir "gürültü", gerçek
/// hataları da örter — çünkü kırmızı sonuç sıradanlaşır.</para>
///
/// <para><b>Çözüm:</b> havuz YALNIZ testin kendi veritabanı için temizlenir. Bağlantı dizesi,
/// <c>SqliteConnectionFactory.Create()</c> ile <b>birebir aynı</b> kurulur — aksi hâlde
/// <c>ClearPool</c> eşleşmez ve dosya silinemez.</para>
/// </summary>
internal static class TestDbTemizlik
{
    /// <summary>Fabrikanın kullandığı bağlantı dizesinin AYNISI (eşleşmezse havuz temizlenmez).</summary>
    private static string BaglantiDizesi(string dbPath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

    /// <summary>
    /// Testin kendi veritabanının havuzunu temizler ve dosyasını siler.
    /// <b>ClearAllPools KULLANMAZ</b> — o, paralel koşan diğer testleri düşürüyordu.
    /// Dosya silinemezse sessizce geçilir: koşu başındaki süpürge artıkları toplar.
    /// </summary>
    public static void Bitir(string dbPath)
    {
        try
        {
            using var c = new SqliteConnection(BaglantiDizesi(dbPath));
            SqliteConnection.ClearPool(c);
        }
        catch { }

        foreach (var ek in new[] { "", "-wal", "-shm" })
            try { File.Delete(dbPath + ek); } catch { }
    }
}
