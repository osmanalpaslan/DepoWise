using DepoWise.Infrastructure.Database;
using System.Data.Common;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// GÜVENLİK KAPISININ KENDİ TESTİ (kullanıcı isteği 2026-08-08). Hiçbir veritabanına bağlanmaz.
/// Kanıtladıkları:
///  • Onay ortam değişkeni yoksa yıkıcı testler ATLANIR (kazara çalışmaz),
///  • Onay yoksa <c>AssertSafeTestDatabase</c> BAĞLANTI DAHİ AÇMADAN hata verir (fail-closed),
///  • Kapı "izin-listesi" mantığındadır: kodda canlı veritabanına ait hiçbir bilgi yoktur.
/// </summary>
[Collection("PostgresSchema")]   // env DEGISTIRIR: paralel kosarsa diger PG testlerini ATLATIR (flaky) — serilestir.
public class PostgresTestGuardTests
{
    /// <summary>Kullanılırsa testi patlatan sahte factory — "bağlantı açılmadı" kanıtı.</summary>
    private sealed class ExplodingFactory : IDbConnectionFactory
    {
        public string DatabasePath => "(kullanılmamalı)";
        public DbConnection Create() => throw new Xunit.Sdk.XunitException(
            "GÜVENLİK İHLALİ: kapı, onay yokken veritabanına bağlanmaya çalıştı.");
    }

    private static void WithEnv(string? confirm, string? url, Action body)
    {
        var oldC = Environment.GetEnvironmentVariable(PostgresTestGuard.ConfirmVar);
        var oldU = Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");
        try
        {
            Environment.SetEnvironmentVariable(PostgresTestGuard.ConfirmVar, confirm);
            Environment.SetEnvironmentVariable("DEPOWISE_PG_URL", url);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(PostgresTestGuard.ConfirmVar, oldC);
            Environment.SetEnvironmentVariable("DEPOWISE_PG_URL", oldU);
        }
    }

    // ⭐ 2026-09-07: bu dört test eskiden SÜREÇ GENELİNDEKİ ortam değişkenini değiştiriyordu.
    // Ortam değişkeni tüm süreç için ortaktır; paralel koşan PostgreSQL testleri o anda "adres yok"
    // görüp ATLANIYORDU. Artık kararın SAF hâli sınanıyor — hiçbir şey değiştirilmiyor.
    // (Aynı sınıf hata: PgTestOrtami ve TestDbTemizlik açıklamalarına bakınız.)

    [Fact]
    public void Baglanti_Adresi_Yoksa_Testler_Atlanir()
        => Assert.Contains("DEPOWISE_PG_URL",
            PostgresTestGuard.SkipReason(url: null, confirm: PostgresTestGuard.ConfirmValue));

    [Fact]
    public void Acik_Onay_Yoksa_Yikici_Testler_ATLANIR()
        => Assert.Contains(PostgresTestGuard.ConfirmVar,
            PostgresTestGuard.SkipReason(url: "postgres://sahte/deneme", confirm: null));

    [Fact]
    public void Yanlis_Onay_Degeri_Kabul_Edilmez()
        => Assert.NotNull(PostgresTestGuard.SkipReason(url: "postgres://sahte/deneme", confirm: "evet"));

    [Fact]
    public void Her_Sey_Tamamsa_Atlama_Sebebi_Kalmaz()
        => Assert.Null(PostgresTestGuard.SkipReason(
            url: "postgres://sahte/deneme", confirm: PostgresTestGuard.ConfirmValue));

    /// <summary>
    /// ⭐ NÖBETÇİ (2026-09-07): <see cref="ApiTestHost"/> <c>DEPOWISE_PG_URL</c>'i SÜREÇ GENELİNDE
    /// siler. Bu, PostgreSQL testlerinin TAMAMINI sessizce atlattırıyordu (ölçüldü: 59 testin 47'si).
    /// Kapı artık süreç başındaki fotoğrafı kullanır; bu test o davranışı KİLİTLER — biri fotoğrafı
    /// kaldırıp canlı ortam değişkenine dönerse burada yakalanır.
    /// </summary>
    [Fact]
    public void Ortam_Degiskeni_Sonradan_Silinse_Bile_Adres_Kaybolmaz()
    {
        var once = PostgresTestGuard.Url;
        var eski = Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");
        try
        {
            Environment.SetEnvironmentVariable("DEPOWISE_PG_URL", null);   // ApiTestHost'un yaptığı
            Assert.Equal(once, PostgresTestGuard.Url);
        }
        finally { Environment.SetEnvironmentVariable("DEPOWISE_PG_URL", eski); }
    }

    [Fact]
    public void Onay_Yokken_Kapi_VERITABANINA_BAGLANMADAN_Durdurur()
        => WithEnv(confirm: null, url: "postgres://sahte/deneme", () =>
        {
            // ExplodingFactory kullanılırsa test patlar → "hiç bağlanılmadı" kanıtlanmış olur.
            var ex = Assert.Throws<InvalidOperationException>(
                () => PostgresTestGuard.AssertSafeTestDatabase(new ExplodingFactory()));
            Assert.Contains("GÜVENLİK", ex.Message);
        });

    // ── K3: "public şema TAMAMEN BOŞ olmalı" (kullanıcı kararı 2026-08-08 — eşik yaklaşımı kaldırıldı) ──

    [Fact]
    public void Tertemiz_Bos_Veritabani_Kabul_Edilir()
        => Assert.True(PostgresTestGuard.SchemaAcceptable(publicTableCount: 0, markerSchemaExists: false));

    [Fact]
    public void BOS_OLMAYAN_Veritabani_REDDEDILIR()
    {
        // Tek bir tablo bile olsa reddedilir — "az veri var, test boyutundadır" mazereti YOK.
        Assert.False(PostgresTestGuard.SchemaAcceptable(publicTableCount: 1, markerSchemaExists: false));
        Assert.False(PostgresTestGuard.SchemaAcceptable(publicTableCount: 40, markerSchemaExists: false));
        Assert.False(PostgresTestGuard.SchemaAcceptable(publicTableCount: 1000, markerSchemaExists: false));
    }

    [Fact]
    public void Karar_Satir_Sayisina_DEGIL_Tablo_Varligina_Dayanir()
    {
        // İçinde HİÇ satır olmasa bile, uygulamaya ait tablolar varsa ve şemayı kapı sıfırlamamışsa
        // veritabanı kabul edilmez. Bu, "içinde az gerçek veri olan DB'nin test sanılması" riskini kapatır.
        Assert.False(PostgresTestGuard.SchemaAcceptable(publicTableCount: 1, markerSchemaExists: false));
        // Aynı tablo sayısı, ama şemayı DAHA ÖNCE KAPI sıfırlamış → aynı koşudaki sonraki testler çalışabilir.
        Assert.True(PostgresTestGuard.SchemaAcceptable(publicTableCount: 1, markerSchemaExists: true));
    }

    [Fact]
    public void Isaret_Semasi_Yalniz_Kapinin_Yarattigi_Bir_Isarettir()
    {
        // Uygulamanın hiçbir yerinde bu şema yaratılmaz/okunmaz (tüm sorgular table_schema='public' filtreli),
        // ve DROP SCHEMA public CASCADE onu silmez → "bu DB'yi daha önce kapı sıfırladı" kanıtı olarak geçerlidir.
        Assert.Equal("dw_test_marker", PostgresTestGuard.MarkerSchema);
        Assert.DoesNotContain("public", PostgresTestGuard.MarkerSchema);
    }

    [Fact]
    public void Kapida_Canli_Veritabanina_Ait_Hicbir_Bilgi_Yazili_Degildir()
    {
        // İzin-listesi mantığı: yalnız aranan işaret ("test") sabittir; canlı ad/adres/kullanıcı YOK.
        Assert.Equal("test", PostgresTestGuard.RequiredNameMarker);
        Assert.DoesNotContain("depowise_prod", PostgresTestGuard.ConfirmValue, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("neon", PostgresTestGuard.ConfirmValue, StringComparison.OrdinalIgnoreCase);
    }
}
