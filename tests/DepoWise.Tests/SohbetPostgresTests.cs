using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Chat;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SOHBET — POSTGRESQL'DE GERÇEK KOŞU (2026-09-07, kullanıcı şikâyeti üzerine) ═══
///
/// <para><b>Neden var:</b> kullanıcı 1.0.187'de hâlâ *"gönder düğmesine basıyorum ama mesaj
/// penceresi boş"* diyor. Oysa <see cref="SohbetUctanUcaTests"/> yeşil. Sebep ölçüldü:
/// <b>o testler SQLite üzerinde koşuyor, ÜRETİM ise PostgreSQL.</b> Sohbetin SQL'i iki lehçede
/// hiç karşılaştırılmamıştı — yani "yeşil" olan şey, kullanıcının kullandığı veritabanı DEĞİLDİ.</para>
///
/// <para>CLAUDE.md §4: <i>"lehçe farkları toplanmıştır; iki lehçe de test edilir."</i> Sohbet bu
/// kuralın dışında kalmış. Bu dosya o boşluğu kapatır: aynı akış PostgreSQL'de koşar.</para>
/// </summary>
[Collection("PostgresSchema")]
public class SohbetPostgresTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    [SkippableFact]
    public void Sohbet_PostgreSQLde_UctanUca_Calisir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var aliId = users.EnsureInitialAdmin("PGSOHBET", "ali", "Test!2026", RoleKeys.CompanyAdmin);
        var veliId = users.EnsureInitialAdmin("PGSOHBET", "veli", "Test!2026", RoleKeys.Staff);

        var ali = new SessionContext(aliId, "PGSOHBET", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var veli = new SessionContext(veliId, "PGSOHBET", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var chat = new ChatService(factory, clock);

        // ── 1) ÇEVRİMDIŞI alıcıya gönderim (kullanıcının bildirdiği tam senaryo) ──
        // Veli hiç yoklama yapmadı → last_seen_at NULL → ÇEVRİM DIŞI. Gönderim yine de çalışmalı.
        var id1 = chat.Gonder(ali, veliId, "Merhaba, bunu çevrimdışıyken gönderdim.");
        Assert.False(string.IsNullOrWhiteSpace(id1));

        // ── 2) GÖNDEREN kendi mesajını GÖRMELİ (pencere boş kalmamalı) ──
        var aliGoruyor = chat.Konusma(ali, veliId);
        Assert.Single(aliGoruyor);
        Assert.Equal("Merhaba, bunu çevrimdışıyken gönderdim.", aliGoruyor[0].Body);
        Assert.True(aliGoruyor[0].Mine);

        // ── 3) ARTIMLI YOKLAMA (since) — arayüz her turda BUNU kullanır ──
        // sinceMs = son mesajın zamanı → yeni yok. Bu sorgu PG'de patlarsa arayüz mesajları
        // ASLA gösteremez; kullanıcının gördüğü "boş pencere" tam olarak budur.
        var sonZaman = aliGoruyor[^1].CreatedAt;
        Assert.Empty(chat.Konusma(ali, veliId, sonZaman));

        clock.Advance(1000);
        chat.Gonder(ali, veliId, "İkinci mesaj.");
        var artimli = chat.Konusma(ali, veliId, sonZaman);
        Assert.Single(artimli);
        Assert.Equal("İkinci mesaj.", artimli[0].Body);

        // ── 4) ALICI çevrimiçi olunca birikmiş mesajları GÖRÜR ──
        var veliGoruyor = chat.Konusma(veli, aliId);
        Assert.Equal(2, veliGoruyor.Count);
        Assert.All(veliGoruyor, m => Assert.False(m.Mine));
        Assert.Equal(2, chat.ToplamOkunmamis(veli));

        // ── 5) Kişi listesi + çevrimiçi/okunmamış (PG'de alt sorgu ve ORDER BY doğru mu) ──
        var kisiler = chat.Kisiler(veli);
        var aliSatiri = Assert.Single(kisiler, k => k.UserId == aliId);
        Assert.Equal(2, aliSatiri.Unread);

        // Kisiler() çağrısı VELİ'nin "görüldü" damgasını tazeler → Ali için Veli artık çevrim içi.
        Assert.True(Assert.Single(chat.Kisiler(ali), k => k.UserId == veliId).Online);

        // ── 6) Okundu işaretleme ──
        Assert.Equal(2, chat.OkunduIsaretle(veli, aliId));
        Assert.Equal(0, chat.ToplamOkunmamis(veli));
    }
}
