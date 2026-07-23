using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 2 Adım 5 (2026-07-23): Türkçe arama/sıralama PostgreSQL'de doğru çalışır mı?
///
/// Masaüstü SQLite'ta Türkçe-duyarlı arama (like() ezildi) ve sıralama (TRNOCASE) çalışma zamanında
/// kaydedilir; PG'de karşılıkları Migration053'ün kurduğu collation'lardır (dw_tr / nocase / trnocase) +
/// LIKE için <see cref="DepoWise.Infrastructure.Database.SqlDialect.LikeTr"/>. Bu test onların GERÇEK
/// PostgreSQL'de (Neon) çalıştığını kanıtlar:
///   • İ↔i katlaması: "ÇELİK" araması "Çelik Halat"ı bulur (düz PG LIKE bulamazdı — büyük/küçük duyarlı).
///   • Grid filtresi (GridQuery.LikeTr yolu) aynı şekilde Türkçe-duyarsız.
///   • TRNOCASE sıralama: Ç, C'den HEMEN sonra (Z'den önce) gelir — Türk alfabesi sırası.
///   • NOCASE tekilleştirme: "Metre" ve "METRE" aynı tanım sayılır (mevcut COLLATE NOCASE SQL'i PG'de çalışır).
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (boş Neon deneme DB'si) üzerinde; her koşuda şemayı sıfırlar. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresTurkishSearchTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [SkippableFact]
    public void Turkce_Arama_Ve_Siralama_PostgreSQLde_Calisir()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL yok → PostgreSQL Türkçe arama testi atlandı.");
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);

        // Temiz şema + tüm migration'lar (053 dahil → collation'lar kurulur).
        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
            cmd.ExecuteNonQuery();
        }
        new MigrationRunner(factory).Run();

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var aId = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var a = new SessionContext(aId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var materials = new MaterialService(factory, clock);
        // Türk alfabesi sırası için özenle seçilmiş adlar: C < Ç < İ < Z.
        materials.Create(a, new NewMaterial("M-1", "Çelik Halat", UnitPrice: 10m, MinStock: 1m));
        materials.Create(a, new NewMaterial("M-2", "İzolasyon Bandı", UnitPrice: 10m, MinStock: 1m));
        materials.Create(a, new NewMaterial("M-3", "Cıvata", UnitPrice: 10m, MinStock: 1m));
        materials.Create(a, new NewMaterial("M-4", "Zeytinyağı Filtresi", UnitPrice: 10m, MinStock: 1m));

        // 1) Liste araması (code/name LIKE — SqlDialect.LikeTr). Türkçe İ↔i + büyük/küçük duyarsız.
        int Count(string term) => materials.List(a, new PageRequest { Limit = 50 }, term).Items.Count;
        Assert.Equal(1, Count("çelik"));    // birebir küçük harf
        Assert.Equal(1, Count("ÇELİK"));    // ASIL KANIT: büyük İ → i katlanır, "Çelik Halat"ı bulur
        Assert.Equal(1, Count("izolasyon")); // ad "İzolasyon" → Türkçe küçük harf "izolasyon"
        Assert.Equal(1, Count("İZOLASYON")); // büyük harf de bulur
        Assert.Equal(0, Count("xyzyok"));    // eşleşmeyen → boş

        // 2) Grid filtresi (GridQuery.LikeTr yolu) — aynı Türkçe-duyarsızlık.
        int Grid(string name) => materials.SearchGrid(a, new MaterialGridFilter(Name: name), 1, 50).TotalCount;
        Assert.Equal(1, Grid("çelik"));
        Assert.Equal(1, Grid("ÇELİK"));

        // 3) TRNOCASE sıralama (ORDER BY t.name COLLATE trnocase) — Türk alfabesi: C, Ç, İ, Z.
        var sorted = materials.SearchGrid(a, new MaterialGridFilter(), 1, 50, sortColumn: "name", sortDesc: false).Items;
        var names = sorted.Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "Cıvata", "Çelik Halat", "İzolasyon Bandı", "Zeytinyağı Filtresi" }, names);

        // 4) NOCASE tekilleştirme — "Metre" ve "METRE" aynı tanım (mevcut COLLATE NOCASE SQL'i PG'de çalışır).
        var look = new LookupService(factory, clock);
        var u1 = look.AddUnit(a, "Metre");
        var u2 = look.AddUnit(a, "METRE");
        Assert.Equal(u1, u2);
    }
}
