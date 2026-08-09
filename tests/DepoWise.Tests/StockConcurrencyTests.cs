using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// FAZ 3-ÖN — stok bakiyesi eşzamanlılık düzeltmesi (kullanıcı kararları 2026-08-08).
///
/// Buradaki testler SQLite'ta koşar ve şunları kanıtlar:
///  • CAS (compare-and-swap) koşulu gerçekten koruyor mu (yanlış beklenen değerde 0 satır),
///  • ONDALIK BASAMAK TUZAĞI: "10.00" gibi ölçekli metin sahte çakışma üretmiyor (en kritik detay),
///  • tekrar (retry) YALNIZ yarışta çalışıyor; iş kuralı/sistem hatasında ÇALIŞMIYOR (kullanıcı kararı K-5),
///  • mevcut davranış (giriş/çıkış/transfer/sayım/iptal/bakım) DEĞİŞMEDİ.
/// Gerçek eşzamanlı yarış yalnız PostgreSQL'de oluşabilir → <see cref="PostgresStockConcurrencyTests"/>.
/// </summary>
public class StockConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly SessionContext _admin;

    public StockConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_cas_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string SeedMaterial(decimal opening = 10m)
    {
        var m = _materials.Create(_admin, new NewMaterial("MAT-" + Guid.NewGuid().ToString("N")[..6], "Filtre"));
        _opening.RecordOpening(_admin, m, opening, "op-" + Guid.NewGuid().ToString("N"));
        return m;
    }

    private string? RawBalance(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        return cmd.ExecuteScalar() as string;
    }

    private void SetRawBalance(string materialId, string text)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE stock_balances SET quantity=@q WHERE material_id=@m;";
        cmd.AddWithValue("@q", text);
        cmd.AddWithValue("@m", materialId);
        cmd.ExecuteNonQuery();
    }

    // ── 1. CAS mekanizmasının kendisi ────────────────────────────────────────────────────

    [Fact]
    public void CAS_Kosulu_Beklenen_Deger_Tutmazsa_Hicbir_Satiri_Guncellemez()
    {
        var m = SeedMaterial(10m);
        var raw = RawBalance(m);
        Assert.NotNull(raw);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Yanlış "beklenen" değerle CAS → 0 satır (yarış tespiti tam olarak buna dayanır).
        cmd.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m AND quantity=@expected;";
        cmd.AddWithValue("@m", m);
        cmd.AddWithValue("@expected", raw + "X");
        Assert.Equal(0, cmd.ExecuteNonQuery());

        // Doğru beklenen değerle → 1 satır.
        using var ok = conn.CreateCommand();
        ok.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m AND quantity=@expected;";
        ok.AddWithValue("@m", m);
        ok.AddWithValue("@expected", raw!);
        Assert.Equal(1, ok.ExecuteNonQuery());
    }

    /// <summary>
    /// EN KRİTİK TEST (T-05): <c>Money.Serialize</c> decimal ölçeğini korur → "10" ile "10.00" değer olarak
    /// eşit, METİN olarak farklıdır. CAS koşulu veritabanından okunan HAM metni kullanmazsa, ölçekli değer
    /// yazılmış bir satırda her deneme başarısız olur ve işlem KALICI olarak kilitlenirdi.
    /// </summary>
    [Fact]
    public void Ondalik_Olcekli_Bakiye_Metni_Sahte_Cakisma_Uretmez()
    {
        var m = SeedMaterial(10m);
        SetRawBalance(m, "10.00");                 // ölçekli metin — "10" ile aynı değer, farklı metin

        _stock.IssueOut(_admin, new[] { new StockLine(m, 3m) }, "op-scale", personnelId: null);

        Assert.Equal(7m, _stock.GetBalance(_admin, m));    // ilk denemede geçmeli, sahte çakışma olmamalı
    }

    // ── 2. Tekrar (retry) davranışı — kullanıcı kararı K-5 ───────────────────────────────

    [Fact]
    public void Retry_Yalnizca_Yaris_Durumunda_Calisir_Ve_Sonunda_Basarili_Olur()
    {
        int calls = 0;
        var result = StockBalanceWriter.Run(() =>
        {
            calls++;
            if (calls < 3) throw new StockConcurrencyException("test yarışı");
            return "tamam";
        }, "test");

        Assert.Equal("tamam", result);
        Assert.Equal(3, calls);                    // 2 kez yarış, 3. denemede başarı
    }

    [Fact]
    public void Retry_Hakki_Bitince_Kullaniciya_Teknik_Olmayan_Mesaj_Doner()
    {
        int calls = 0;
        var ex = Assert.Throws<StockBusyException>(() =>
            StockBalanceWriter.Run<object>(() => { calls++; throw new StockConcurrencyException("hep yarış"); }, "test"));

        Assert.Equal(StockBalanceWriter.MaxRetries + 1, calls);   // ilk deneme + 3 tekrar = 4
        Assert.Equal(StockBalanceWriter.BusyMessage, ex.Message);
        Assert.DoesNotContain("CAS", ex.Message);
        Assert.DoesNotContain("transaction", ex.Message);
    }

    [Fact]
    public void Retry_Is_Kurali_Ve_Sistem_Hatalarini_TEKRARLAMAZ()
    {
        // İş kuralı: negatif stok → tek deneme, olduğu gibi yukarı fırlar.
        int neg = 0;
        Assert.Throws<NegativeStockException>(() =>
            StockBalanceWriter.Run<object>(() => { neg++; throw new NegativeStockException("yetersiz"); }, "test"));
        Assert.Equal(1, neg);

        // Yetki hatası → tek deneme.
        int forb = 0;
        Assert.Throws<ForbiddenException>(() =>
            StockBalanceWriter.Run<object>(() => { forb++; throw new ForbiddenException("yetki yok"); }, "test"));
        Assert.Equal(1, forb);

        // Sistem/veritabanı hatası → tek deneme (yarışla karıştırılmaz).
        int sys = 0;
        Assert.Throws<InvalidOperationException>(() =>
            StockBalanceWriter.Run<object>(() => { sys++; throw new InvalidOperationException("bağlantı koptu"); }, "test"));
        Assert.Equal(1, sys);
    }

    // ── 2b. BELGE NUMARASI (doc_no) YARIŞI — PG bulgusu 2026-08-08, kullanıcı kararı S1 ──
    //
    // Gerçek yarış yalnız PostgreSQL'de oluşur (bkz. PostgresStockConcurrencyTests). Burada mekanizma
    // DETERMİNİSTİK olarak kanıtlanır: doc_no benzersizlik ihlali tekrarlanır, BAŞKA hiçbir ihlal
    // tekrarlanmaz. (SQLite ve PostgreSQL'in ürettiği iki farklı metin de ayrı ayrı doğrulanır.)

    /// <summary>SQLite'ın doc_no ihlali için ürettiği metin — yarış sayılmalı.</summary>
    private static Exception SqliteDocNoViolation()
        => new Microsoft.Data.Sqlite.SqliteException(
            "UNIQUE constraint failed: stock_documents.company_id, stock_documents.doc_type, stock_documents.doc_no", 19);

    /// <summary>PostgreSQL'in doc_no ihlali için ürettiği metin (indeks adıyla) — yarış sayılmalı.</summary>
    private static Exception PgDocNoViolation()
        => new Microsoft.Data.Sqlite.SqliteException(
            "duplicate key value violates unique constraint \"ux_stock_documents_no\"", 19);

    [Fact]
    public void DocNo_Cakismasi_YARIS_Sayilir_Ve_Tekrar_Edilir()
    {
        foreach (var make in new Func<Exception>[] { SqliteDocNoViolation, PgDocNoViolation })
        {
            Assert.True(StockBalanceWriter.IsDocumentNumberRace(make()));

            int calls = 0;
            var result = StockBalanceWriter.Run(() =>
            {
                calls++;
                if (calls < 3) throw make();   // iki kez doc_no çakışması, sonra başarı
                return "tamam";
            }, "test");

            Assert.Equal("tamam", result);
            Assert.Equal(3, calls);            // tekrar GERÇEKTEN yapıldı
        }
    }

    [Fact]
    public void DocNo_Cakismasi_Tekrar_Hakki_Bitince_Kullanici_Mesajina_Doner()
    {
        int calls = 0;
        var ex = Assert.Throws<StockBusyException>(() =>
            StockBalanceWriter.Run<object>(() => { calls++; throw SqliteDocNoViolation(); }, "test"));

        Assert.Equal(StockBalanceWriter.MaxRetries + 1, calls);   // ilk deneme + 3 tekrar (sınır DEĞİŞMEDİ)
        Assert.Equal(StockBalanceWriter.BusyMessage, ex.Message);
    }

    [Fact]
    public void BASKA_Veritabani_Hatalari_YARIS_SAYILMAZ_Ve_Tekrar_EDILMEZ()
    {
        // Kapsam bilerek dardır: yalnız doc_no benzersizlik ihlali yarıştır.
        var digerleri = new Exception[]
        {
            new Microsoft.Data.Sqlite.SqliteException("UNIQUE constraint failed: stock_movements.operation_id", 19),
            new Microsoft.Data.Sqlite.SqliteException("duplicate key value violates unique constraint \"ux_material_requests_no\"", 19),
            new Microsoft.Data.Sqlite.SqliteException("FOREIGN KEY constraint failed", 19),
            new Microsoft.Data.Sqlite.SqliteException("database is locked", 5),
            new InvalidOperationException("baglanti koptu"),
        };

        foreach (var e in digerleri)
        {
            Assert.False(StockBalanceWriter.IsDocumentNumberRace(e), $"yanlışlıkla yarış sayıldı: {e.Message}");
            int calls = 0;
            Assert.ThrowsAny<Exception>(() => StockBalanceWriter.Run<object>(() => { calls++; throw e; }, "test"));
            Assert.Equal(1, calls);            // TEK deneme — tekrar yok
        }
    }

    // ── 3. Mevcut davranış değişmedi (regresyon) ─────────────────────────────────────────

    [Fact]
    public void Giris_Cikis_Transfer_Sayim_Ve_Iptal_Eskisi_Gibi_Calisir()
    {
        var m = SeedMaterial(10m);

        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "op-in");
        Assert.Equal(15m, _stock.GetBalance(_admin, m));

        var outDoc = _stock.IssueOut(_admin, new[] { new StockLine(m, 4m) }, "op-out", personnelId: null);
        Assert.Equal(11m, _stock.GetBalance(_admin, m));

        // Negatif stok kalkanı: DEĞİŞMEDİ.
        Assert.Throws<NegativeStockException>(() =>
            _stock.IssueOut(_admin, new[] { new StockLine(m, 999m) }, "op-over", personnelId: null));
        Assert.Equal(11m, _stock.GetBalance(_admin, m));

        // İdempotency: aynı operation_id ikinci kez → yeni hareket yok.
        _stock.IssueOut(_admin, new[] { new StockLine(m, 4m) }, "op-out", personnelId: null);
        Assert.Equal(11m, _stock.GetBalance(_admin, m));

        // Sayım: fark hareketi üretir.
        _stock.Count(_admin, new[] { new CountLine(m, 20m) }, "sayım farkı", "op-count");
        Assert.Equal(20m, _stock.GetBalance(_admin, m));

        // İptal (ters kayıt): çıkış geri gelir.
        _stock.ReverseDocument(_admin, outDoc.DocumentId, "hatalı kayıt");
        Assert.Equal(24m, _stock.GetBalance(_admin, m));
    }

    [Fact]
    public void Bakim_Malzemesi_Ortak_Yaziciyi_Kullanir_Ve_Negatife_Izin_Verir()
    {
        var vehicles = new VehicleService(_factory, _clock);
        var defs = new MaintenanceDefinitionService(_factory, _clock);
        var maint = new MaintenanceService(_factory, _clock);

        var m = SeedMaterial(1m);
        var v = vehicles.Create(_admin, new NewVehicle("V-1"));
        var def = defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 100m, "km"));

        // Bakım tarafı negatife İZİN VERİR (ADR / kullanıcı isteği 2026-08-06) — bu davranış değişmedi.
        var id = maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 100m,
            Materials: new[] { new MaintenanceMaterialLine(m, 5m) }), "op-mnt");
        Assert.Equal(-4m, _stock.GetBalance(_admin, m));   // 1 - 5

        maint.Cancel(_admin, id, "yanlış kayıt");
        Assert.Equal(1m, _stock.GetBalance(_admin, m));    // ters hareketle geri geldi
    }

    [Fact]
    public void RecomputeBalances_Defterden_Dogru_Bakiyeyi_Yeniden_Kurar()
    {
        var m = SeedMaterial(10m);
        _stock.IssueOut(_admin, new[] { new StockLine(m, 3m) }, "op-a", personnelId: null);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "op-b");
        Assert.Equal(12m, _stock.GetBalance(_admin, m));

        // Bakiye önbelleğini bozup yeniden kurdur → defterden doğru değer gelmeli (iyimser koruma
        // SQLite'ta tek yazar olduğu için hiç tetiklenmez; davranış değişmez).
        SetRawBalance(m, "999");
        _stock.RecomputeBalances("A");
        Assert.Equal(12m, _stock.GetBalance(_admin, m));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
