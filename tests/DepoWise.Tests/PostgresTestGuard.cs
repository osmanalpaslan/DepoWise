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
///   K3. <b>public şema TAMAMEN BOŞ olmalı</b> (uygulamaya ait tek bir tablo bile bulunmamalı).
///       TEK İSTİSNA: şemayı daha önce BU KAPININ kendisi sıfırlamışsa — bunun kanıtı, yalnız kapı
///       tarafından oluşturulan <see cref="MarkerSchema"/> işaret şemasıdır (uygulama onu asla yaratmaz,
///       <c>DROP SCHEMA public</c> onu silmez). Böylece aynı koşuda arka arkaya gelen testler çalışabilir,
///       ama içinde AZ DA OLSA gerçek veri bulunan bir veritabanı ASLA kabul edilmez.
///       ⚠️ Satır sayısı eşikleri GÜVENLİK KAPISI DEĞİLDİR (kullanıcı kararı 2026-08-08) — yalnız hata
///       mesajında TEŞHİS bilgisi olarak gösterilir.
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

    /// <summary>
    /// Kapının kendi işaret şeması. YALNIZ <see cref="ResetSchema"/> yaratır; uygulama kodu bu şemayı
    /// hiçbir yerde oluşturmaz/okumaz (tüm uygulama sorguları <c>table_schema='public'</c> ile filtreler).
    /// <c>DROP SCHEMA public CASCADE</c> bunu SİLMEZ → "bu veritabanını daha önce kapı sıfırladı" kanıtıdır.
    /// </summary>
    public const string MarkerSchema = "dw_test_marker";

    /// <summary>SADECE TEŞHİS: hata mesajında "ne bulundu" bilgisini göstermek için. Güvenlik kararı
    /// bunlara DAYANMAZ — şema boş değilse (ve işaret şeması yoksa) sayıya bakılmaksızın DURULUR.</summary>
    private static readonly string[] DiagnosticTables =
    {
        "companies", "users", "materials", "stock_movements", "vehicles", "personnel", "material_requests",
    };

    /// <summary>
    /// K3'ün SAF kararı (veritabanı gerektirmez → doğrudan test edilebilir).
    /// İzin YALNIZ iki durumda vardır:
    ///  • public şemada HİÇ tablo yok (tertemiz boş veritabanı), veya
    ///  • şemayı daha önce bu kapı sıfırlamış (işaret şeması var).
    /// Satır sayısı / veri hacmi kararı ETKİLEMEZ — içinde 1 satır gerçek veri olan bir veritabanı da
    /// reddedilir (kullanıcı kararı 2026-08-08).
    /// </summary>
    public static bool SchemaAcceptable(long publicTableCount, bool markerSchemaExists)
        => publicTableCount == 0 || markerSchemaExists;

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
        // İşaret şeması ÖNCE yaratılır: sıfırlama yarıda kalsa bile "bu DB kapıdan geçmişti" izi kalır.
        cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {MarkerSchema}; DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
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

        // K3 — public şema TAMAMEN BOŞ mu? (kullanıcı kararı 2026-08-08: eşik yaklaşımı KALDIRILDI)
        var tableCount = Convert.ToInt64(Scalar(conn,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"));
        // Tek istisna: şemayı daha önce BU KAPI sıfırlamış olmalı (işaret şeması). Uygulama bunu yaratamaz.
        var markerExists = Convert.ToInt64(Scalar(conn,
            $"SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name='{MarkerSchema}';")) > 0;
        if (SchemaAcceptable(tableCount, markerExists)) return;

        // Buraya düşen her veritabanı REDDEDİLİR — satır sayısına BAKILMAKSIZIN. Sayımlar yalnız teşhis.
        var found = new List<string>();
        foreach (var t in DiagnosticTables)
        {
            if (!TableExists(conn, t)) continue;
            found.Add($"{t}={Convert.ToInt64(Scalar(conn, $"SELECT COUNT(*) FROM \"{t}\";"))}");
        }
        throw new InvalidOperationException(
            $"GÜVENLİK: veritabanı '{dbName}' BOŞ DEĞİL — public şemada {tableCount} tablo var ve bu şemayı " +
            $"daha önce test kapısı sıfırlamamış (işaret şeması '{MarkerSchema}' yok). Yıkıcı test ÇALIŞTIRILMADI. " +
            "Az miktarda da olsa gerçek veri içeren bir veritabanı ASLA kabul edilmez; lütfen TAMAMEN BOŞ bir test " +
            "veritabanı kullanın." +
            (found.Count > 0 ? $" [yalnız teşhis: {string.Join(", ", found)}]" : ""));
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
