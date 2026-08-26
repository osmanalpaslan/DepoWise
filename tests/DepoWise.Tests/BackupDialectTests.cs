using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YED-01 · SUNUCU YEDEĞİ POSTGRESQL'DE ÇALIŞMIYORDU ═══ (denetim 2026-08-26)
///
/// <b>Bulunan durum:</b> <see cref="BackupService"/> tek-dosya kopyası alır (<c>VACUUM INTO</c>) ve
/// bütünlüğü <c>PRAGMA integrity_check</c> ile doğrular; ikisi de <b>SQLite'a özgüdür</b>. Sunucu
/// 2026-07-24'te PostgreSQL'e taşındığı için üretimde "Yedek Al" düğmesi ham bir veritabanı hatasıyla
/// düşüyordu — kullanıcı ne olduğunu anlamıyor, üstelik "yedek alınıyor" sanabiliyordu.
///
/// Daha tehlikelisi <c>Restore</c> idi: yedek dosyasını <c>_factory.DatabasePath</c> üzerine kopyalar.
/// PostgreSQL'de böyle bir dosya yoktur → yol YIKICI ve anlamsızdır.
///
/// <b>Bu turda yapılan:</b> her iki yol da dosyaya DOKUNMADAN, anlaşılır bir mesajla durduruluyor.
/// PostgreSQL için gerçek bir dosya dökümü (<c>pg_dump</c>) yeni bir ÖZELLİKTİR ve kullanıcı kararına
/// bırakılmıştır — bu turda uydurulmadı.
///
/// <b>Masaüstü (SQLite) davranışı DEĞİŞMEDİ</b> — asıl kullanım yeri orasıdır ve aşağıda kilitlenir.
/// </summary>
public class BackupDialectTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _folder;
    private readonly SqliteConnectionFactory _factory;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public BackupDialectTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_yed_" + Guid.NewGuid().ToString("N") + ".db");
        _folder = Path.Combine(Path.GetTempPath(), "dw_yed_kls_" + Guid.NewGuid().ToString("N"));
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    // ── SQLite: davranış DEĞİŞMEDİ (regresyon kilidi) ─────────────────────────────────────────
    [Fact]
    public void YED01_SQLite_Yedegi_Calismaya_Devam_Ediyor()
    {
        var svc = new BackupService(_factory, new TestClock(), _folder);

        var yol = svc.Backup();

        Assert.True(File.Exists(yol), "yedek dosyası oluşmadı");
        Assert.True(new FileInfo(yol).Length > 0, "yedek dosyası boş");
        Assert.True(svc.IntegrityCheck(yol), "yedek bütünlük kontrolünden geçmedi");
        Assert.Single(svc.ListBackups());
    }

    // ── PostgreSQL: anlaşılır mesajla durur (yalnız gerçek PG varsa koşar) ────────────────────
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public void YED01_PostgreSQLde_Yedek_Anlasilir_Mesajla_Durur()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL tanımlı değil.");

        var pg = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        var svc = new BackupService(pg, new TestClock(), _folder);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.Backup());

        Assert.Contains("PostgreSQL", ex.Message, StringComparison.Ordinal);
        Assert.Contains("pg_dump", ex.Message, StringComparison.Ordinal);
        // Ham SQL/sürücü hatası SIZMAMALI (kullanıcı bunu okuyacak).
        Assert.DoesNotContain("VACUUM", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("42601", ex.Message, StringComparison.Ordinal);   // PG sözdizimi hata kodu
        // Ve HİÇBİR dosya oluşmamalı.
        Assert.Empty(Directory.Exists(_folder) ? Directory.GetFiles(_folder) : Array.Empty<string>());
    }

    [SkippableFact]
    public void YED01_PostgreSQLde_Geri_Yukleme_Dosyaya_Dokunmadan_Durur()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL tanımlı değil.");

        // Önce SQLite'tan geçerli bir yedek üret (geri yükleme girdisi gerçekçi olsun).
        var kaynak = new BackupService(_factory, new TestClock(), _folder).Backup();

        var pg = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        var svc = new BackupService(pg, new TestClock(), _folder);
        var admin = new SessionContext("a", "CO", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.Restore(admin, kaynak, reauthenticated: true));
        Assert.Contains("PostgreSQL", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Yetki kapısı geri yüklemede ÖNCE gelir (dialect kapısı onu gölgelememeli).</summary>
    [Fact]
    public void YED01_Geri_Yukleme_Yetki_Kapisi_Once()
    {
        var svc = new BackupService(_factory, new TestClock(), _folder);
        var personel = new SessionContext("p", "CO", new[] { RoleKeys.Staff }, PermissionSet.Empty);

        Assert.Throws<ForbiddenException>(() => svc.Restore(personel, "olmayan.db", reauthenticated: true));
        // Yeniden kimlik doğrulama da ayrı bir kapıdır.
        var admin = new SessionContext("a", "CO", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => svc.Restore(admin, "olmayan.db", reauthenticated: false));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_folder, true); } catch { }
    }
}
