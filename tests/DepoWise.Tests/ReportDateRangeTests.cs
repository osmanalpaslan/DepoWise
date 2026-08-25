using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RPR-06 · RAPOR BİTİŞ TARİHİ GÜNÜN TAMAMINI KAPSAR ═══
///
/// <b>Bulunan hata (denetim 2026-08-25):</b> masaüstü Raporlar ekranı bitiş tarihini HAM gönderiyordu.
/// Avalonia <c>DatePicker</c> seçilen günü <b>gece yarısı</b> verir; SQL koşulu <c>tarih &lt;= @to</c>
/// olduğundan <b>bitiş gününün tamamı rapordan düşüyordu</b> — "01.08 – 25.08" raporunda 25.08'in
/// kayıtları HİÇ görünmüyordu. Web aynı hatayı 2026-08-13'te düzeltmişti; masaüstü atlanmıştı
/// (→ iki platform aynı filtreyle FARKLI sonuç veriyordu).
///
/// Testler hem hatanın ETKİSİNİ (gerçek rapor üzerinden) hem de düzeltmenin doğruluğunu kilitler.
/// </summary>
public class ReportDateRangeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private const string Co = "RDR-CO";

    /// <summary>Raporun kapsayacağı gün (UTC). Sabit — testin bugüne bağlı olmaması için.</summary>
    private static readonly DateTimeOffset Gun = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestClock : IClock
    { public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 25, 14, 0, 0, TimeSpan.Zero); }

    public ReportDateRangeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rdr_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory, _clock);
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Çimento',NULL,'0',1,1,1,0);");

        // BİTİŞ GÜNÜNÜN İÇİNDE (25.08 saat 14:00 UTC) bir stok hareketi.
        var ogleden = new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Sql($"INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity," +
            $"operation_id,created_at) VALUES('MV1','{Co}','M1',NULL,'in',1,'5','OP1',{ogleden});");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>DatePicker'ın verdiği ham değer — düzeltme ÖNCESİ masaüstünün gönderdiği şey.</summary>
    private static long HamGunBasi(DateTimeOffset d) => d.ToUnixTimeMilliseconds();

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  1 · HATANIN ETKİSİ — gerçek rapor üzerinden
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ RPR-06a — <b>HATANIN KANITI:</b> bitiş tarihi gün BAŞINA inerse o günün kaydı rapora GİRMEZ.
    /// (Masaüstü düzeltilmeden önce tam olarak bu oluyordu.)
    /// </summary>
    [Fact]
    public void RPR06a_Gun_Basi_Gonderilirse_O_Gunun_Kaydi_Dusar()
    {
        var t = _reports.StockMovements(_admin, new ReportRequest(Executed: true,
            FromDate: ReportDateRange.StartMs(Gun.AddDays(-24)), ToDate: HamGunBasi(Gun)));

        Assert.Empty(t.Rows);   // ← hatanın belirtisi: "Kayıt bulunamadı"
    }

    /// <summary>⭐ RPR-06b — DÜZELTME: gün SONU gönderilince aynı kayıt rapora GİRER.</summary>
    [Fact]
    public void RPR06b_Gun_Sonu_Ile_O_Gunun_Kaydi_Gorunur()
    {
        var t = _reports.StockMovements(_admin, new ReportRequest(Executed: true,
            FromDate: ReportDateRange.StartMs(Gun.AddDays(-24)), ToDate: ReportDateRange.EndMs(Gun)));

        Assert.Single(t.Rows);
    }

    /// <summary>RPR-06c — TEK GÜNLÜK aralık (başlangıç = bitiş) boş dönmemeli.</summary>
    [Fact]
    public void RPR06c_Ayni_Gun_Araligi_Bos_Donmez()
    {
        var t = _reports.StockMovements(_admin, new ReportRequest(Executed: true,
            FromDate: ReportDateRange.StartMs(Gun), ToDate: ReportDateRange.EndMs(Gun)));

        Assert.Single(t.Rows);
    }

    /// <summary>RPR-06d — aralık DIŞINDAKİ gün gerçekten dışarıda kalmalı (filtre gevşemedi).</summary>
    [Fact]
    public void RPR06d_Onceki_Gun_Bitisi_Kaydi_Almaz()
    {
        var t = _reports.StockMovements(_admin, new ReportRequest(Executed: true,
            FromDate: ReportDateRange.StartMs(Gun.AddDays(-24)), ToDate: ReportDateRange.EndMs(Gun.AddDays(-1))));

        Assert.Empty(t.Rows);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  2 · DÖNÜŞÜM KURALI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>RPR-06e — gün sonu tam olarak 23:59:59.999 (bir sonraki günün 00:00'ı DEĞİL).</summary>
    [Fact]
    public void RPR06e_Gun_Sonu_23_59_59_999()
    {
        var son = ReportDateRange.EndMs(Gun)!.Value;
        var bas = ReportDateRange.StartMs(Gun)!.Value;

        Assert.Equal(86_400_000L - 1, son - bas);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 23, 59, 59, 999, TimeSpan.Zero).ToUnixTimeMilliseconds(), son);
    }

    /// <summary>
    /// RPR-06f — SAAT DİLİMİ KAYMASI YOK: aynı gün, farklı offset'lerle verilse bile sınır aynıdır.
    /// (Yerel saat yorumlansaydı TR'de sınır 3 saat kayardı.)
    /// </summary>
    [Fact]
    public void RPR06f_Saat_Dilimi_Kaydirmaz()
    {
        var utc = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
        var tr = new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.FromHours(3));

        Assert.Equal(ReportDateRange.StartMs(utc), ReportDateRange.StartMs(tr));
        Assert.Equal(ReportDateRange.EndMs(utc), ReportDateRange.EndMs(tr));
    }

    /// <summary>RPR-06g — null → filtre yok.</summary>
    [Fact]
    public void RPR06g_Null_Filtre_Yok()
    {
        Assert.Null(ReportDateRange.StartMs(null));
        Assert.Null(ReportDateRange.EndMs(null));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  3 · PARİTE — web ile BİREBİR aynı kural
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Web üretim kodunun (FieldChecks.ToUnixMs) aynası — bkz. WebDateConversionTests.</summary>
    private static long? WebAynasi(DateTime? d, bool endOfDay)
    {
        if (d is null) return null;
        var gun = DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Unspecified);
        var an = endOfDay ? gun.AddDays(1).AddMilliseconds(-1) : gun;
        return new DateTimeOffset(an, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// ⭐ RPR-06h — WEB ≡ MASAÜSTÜ: aynı gün için iki platform AYNI milisaniye sınırlarını üretmeli.
    /// Ayrışırlarsa aynı filtre iki platformda farklı sonuç verir (bu turda bulunan hatanın kendisi).
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 25)]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 12, 31)]
    [InlineData(2024, 2, 29)]   // artık yıl
    public void RPR06h_Web_Ile_Masaustu_Ayni_Sinirlari_Uretir(int y, int a, int g)
    {
        var d = new DateTimeOffset(y, a, g, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(WebAynasi(new DateTime(y, a, g), false), ReportDateRange.StartMs(d));
        Assert.Equal(WebAynasi(new DateTime(y, a, g), true), ReportDateRange.EndMs(d));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  4 · KAYNAK KİLİDİ — masaüstü ekranı gerçekten ortak kuralı kullanıyor mu?
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ RPR-06i — masaüstü Raporlar ekranı HAM dönüşüm yapmamalı; ortak <see cref="ReportDateRange"/>
    /// kuralını çağırmalı. (Test projesi masaüstüne referans veremez → kaynak kilidi deseni.)
    /// </summary>
    [Fact]
    public void RPR06i_Masaustu_Raporlar_Ortak_Kurali_Kullanir()
    {
        var dir = AppContext.BaseDirectory;
        for (int k = 0; k < 8 && dir is not null; k++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) break;
            dir = Directory.GetParent(dir)?.FullName;
        }
        var src = File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs"));

        Assert.Contains("ReportDateRange.StartMs(FromDate)", src);
        Assert.Contains("ReportDateRange.EndMs(ToDate)", src);
        // ESKİ HATALI DESEN kalmamalı: bitiş tarihinin ham dönüşümü.
        Assert.DoesNotContain("ToDate?.ToUnixTimeMilliseconds()", src);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
