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

    [Fact]
    public void Baglanti_Adresi_Yoksa_Testler_Atlanir()
        => WithEnv(confirm: PostgresTestGuard.ConfirmValue, url: null,
            () => Assert.Contains("DEPOWISE_PG_URL", PostgresTestGuard.SkipReason()));

    [Fact]
    public void Acik_Onay_Yoksa_Yikici_Testler_ATLANIR()
        => WithEnv(confirm: null, url: "postgres://sahte/deneme",
            () => Assert.Contains(PostgresTestGuard.ConfirmVar, PostgresTestGuard.SkipReason()));

    [Fact]
    public void Yanlis_Onay_Degeri_Kabul_Edilmez()
        => WithEnv(confirm: "evet", url: "postgres://sahte/deneme",
            () => Assert.NotNull(PostgresTestGuard.SkipReason()));

    [Fact]
    public void Her_Sey_Tamamsa_Atlama_Sebebi_Kalmaz()
        => WithEnv(confirm: PostgresTestGuard.ConfirmValue, url: "postgres://sahte/deneme",
            () => Assert.Null(PostgresTestGuard.SkipReason()));

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
