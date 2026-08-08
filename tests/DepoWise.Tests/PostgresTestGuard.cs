using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Tests;

/// <summary>
/// POSTGRESQL TEST GÜVENLİK KAPISI (kullanıcı isteği 2026-08-08).
///
/// PostgreSQL testlerinin bir kısmı <c>DROP SCHEMA public CASCADE</c> çalıştırır — yani bağlandığı
/// veritabanındaki HER ŞEYİ siler. <c>DEPOWISE_PG_URL</c> yanlışlıkla CANLI veritabanını gösterirse
/// gerçek firma verisi geri dönülemez biçimde kaybolur.
///
/// Bu sınıf, yıkıcı hiçbir işlem yapılmadan ÖNCE çalışan ve TEK BİR kontrol bile başarısız olursa
/// testi DURDURAN (fail-closed) bir kapıdır. Kontroller "izin verilenler" mantığındadır: canlı
/// veritabanına ait hiçbir bilgi (adres, ad, kullanıcı, şifre) burada YAZILI DEĞİLDİR.
///
/// Kapıdan geçmek için AŞAĞIDAKİLERİN TAMAMI gerekir:
///   K1. <c>DEPOWISE_PG_TEST_CONFIRM</c> ortam değişkeni tam olarak <see cref="ConfirmValue"/> olmalı.
///       (Yalnızca bağlantı adresinin tanımlı olması yıkıcı testleri BAŞLATMAZ.)
///   K2. Veritabanı adı "test" içermeli (canlı veritabanı adı bu koşulu sağlamaz).
///   K3. Şema BOŞ olmalı; değilse yalnız "test boyutunda" veri barındırmalı (aşağıdaki eşikler).
///   K4. Veritabanı toplam boyutu <see cref="MaxDbSizeMb"/> MB'ı aşmamalı.
///   K5. Bağlantı salt-okunur bir yedek/replika olmamalı (yazma denemesi anlamsız olmasın).
///
/// Hiçbir hata mesajında bağlantı adresi/şifre yer almaz; yalnız veritabanı adı ve sayımlar görünür.
/// </summary>
internal static class PostgresTestGuard
{
    /// <summary>Yıkıcı testleri açan açık onay değeri. Bilerek uzun ve Türkçe — kazara set edilemez.</summary>
    public const string ConfirmVar = "DEPOWISE_PG_TEST_CONFIRM";
    public const string ConfirmValue = "EVET-BU-BOS-TEST-VERITABANI";

    /// <summary>Veritabanı adında ARANAN işaret (izin-listesi mantığı).</summary>
    public const string RequiredNameMarker = "test";

    /// <summary>Bu boyutun üstü "test veritabanı" sayılmaz.</summary>
    public const int MaxDbSizeMb = 50;

    // "Test boyutu" eşikleri: canlı veri bunların KATBEKAT üstündedir (ör. gerçek firmada 2500+ malzeme).
    private static readonly (string Table, long Max)[] VolumeLimits =
    {
        ("companies", 5),
        ("users", 10),
        ("materials", 100),
        ("stock_movements", 500),
        ("vehicles", 50),
        ("personnel", 50),
        ("material_requests", 100),
    };

    /// <summary>Testin başında çağrılır: koşullar sağlanmıyorsa test ATLANIR (patlamaz).</summary>
    public static void SkipUnlessSafe()
    {
        var reason = SkipReason();
        Xunit.Skip.If(reason is not null, reason ?? "");
    }

    public static string? Url => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    /// <summary>Yıkıcı testler için atlama sebebi (null = koşabilir). Onay yoksa test ATLANIR, patlamaz.</summary>
    public static string? SkipReason()
    {
        if (string.IsNullOrWhiteSpace(Url))
            return "DEPOWISE_PG_URL yok → PostgreSQL testi atlandı.";
        if (Environment.GetEnvironmentVariable(ConfirmVar) != ConfirmValue)
            return $"{ConfirmVar} onayı yok → şema sıfırlayan PostgreSQL testi atlandı (canlı veri koruması).";
        return null;
    }

    /// <summary>
    /// Şemayı SIFIRLAR — ama yalnız tüm güvenlik kontrolleri geçerse. Yıkıcı testler <c>DROP SCHEMA</c>
    /// yerine BU metodu çağırır; böylece kapı atlanamaz.
    /// </summary>
    public static void ResetSchema(IDbConnectionFactory factory)
    {
        AssertSafeTestDatabase(factory);
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Bağlanılan veritabanının GERÇEKTEN boş/test veritabanı olduğunu doğrular.
    /// Kontrollerden biri bile geçmezse <see cref="InvalidOperationException"/> fırlatır → test başarısız
    /// olur ve HİÇBİR yıkıcı işlem yapılmaz.
    /// </summary>
    public static void AssertSafeTestDatabase(IDbConnectionFactory factory)
    {
        // K1 — açık onay (bağlantı adresi tek başına yetmez).
        if (Environment.GetEnvironmentVariable(ConfirmVar) != ConfirmValue)
            throw new InvalidOperationException(
                $"GÜVENLİK: {ConfirmVar} ortam değişkeni '{ConfirmValue}' değil. Şema sıfırlayan test çalıştırılmadı.");

        using var conn = factory.Create();

        // K2 — veritabanı adı "test" işareti taşımalı.
        var dbName = Scalar(conn, "SELECT current_database();") as string ?? "";
        if (dbName.IndexOf(RequiredNameMarker, StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException(
                $"GÜVENLİK: veritabanı adı '{dbName}' içinde '{RequiredNameMarker}' geçmiyor → test veritabanı sayılmadı. " +
                "Yıkıcı test çalıştırılmadı (canlı veritabanı olabilir).");

        // K5 — salt-okunur replika değil (yazma testleri anlamlı olsun).
        if (Convert.ToBoolean(Scalar(conn, "SELECT pg_is_in_recovery();")))
            throw new InvalidOperationException("GÜVENLİK: bağlanılan sunucu salt-okunur replika. Test çalıştırılmadı.");

        // K4 — boyut eşiği.
        var sizeMb = Convert.ToInt64(Scalar(conn, "SELECT pg_database_size(current_database()) / 1048576;"));
        if (sizeMb > MaxDbSizeMb)
            throw new InvalidOperationException(
                $"GÜVENLİK: veritabanı '{dbName}' boyutu {sizeMb} MB (> {MaxDbSizeMb} MB) → test veritabanı sayılmadı. Test çalıştırılmadı.");

        // K3 — şema boş mu? Boşsa en güvenli durum; değilse "test boyutu" eşikleri aranır.
        var tableCount = Convert.ToInt64(Scalar(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"));
        if (tableCount == 0) return;   // tertemiz boş veritabanı → geç

        var problems = new List<string>();
        foreach (var (table, max) in VolumeLimits)
        {
            if (!TableExists(conn, table)) continue;
            var rows = Convert.ToInt64(Scalar(conn, $"SELECT COUNT(*) FROM \"{table}\";"));
            if (rows > max) problems.Add($"{table}={rows} (izin: {max})");
        }
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"GÜVENLİK: veritabanı '{dbName}' GERÇEK VERİ içeriyor gibi görünüyor → {string.Join(", ", problems)}. " +
                "Yıkıcı test çalıştırılmadı. Lütfen BOŞ bir test veritabanı kullanın.");
    }

    private static bool TableExists(DbConnection conn, string table)
        => Convert.ToInt64(Scalar(conn,
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='{table}';")) > 0;

    private static object? Scalar(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
