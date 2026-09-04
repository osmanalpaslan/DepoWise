using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ LST-01 (2026-09-04) — TAVANLI LİSTELERİN SAYFALANMASI ═══
///
/// <b>Kapatılan kusur sınıfı:</b> liste ekranları sabit bir tavanla okuyor ve sorgu en yeniden
/// başlıyordu → tavanın ötesindeki kayıtlar <b>sessizce</b> düşüyordu. Kesildiğine dair hiçbir uyarı
/// yoktu; kayıt "kaybolmuş" gibi duruyordu. Kullanıcının babası 02.08.2026 tarihli bir yakıt kaydını
/// tam olarak böyle kaybetti (ARA İŞ 6) — bu iş aynı kusuru kalan ekranlarda kapatır.
///
/// <b>Neden "eski yolu da koş" testi var:</b> düzeltmenin gerçekten bir şeyi değiştirdiğini kanıtlamak
/// için kusurun KENDİSİ de aynı testte gösterilir. Yalnız yeni yolun çalıştığını görmek, eski yolun
/// bozuk olduğunu kanıtlamaz.
///
///  LST1 — Stok hareketleri: eski yol tavanda kesiyor, yeni yol hepsine erişiyor
///  LST2 — Stok hareketleri: sayfalar tutarlı (tekrar/atlama yok) ve toplam doğru
///  LST3 — Stok hareketleri: filtre TOPLAMI da daraltır (sayfa ile toplam ayrışmaz)
///  LST4 — Araç bakımları: eski yol tavanda kesiyor, yeni yol hepsine erişiyor
///  LST5 — Araç bakımları: arama araç kodu · tanım · açıklama · BELGE NO üzerinde çalışır
///  LST6 — Araç bakımları: tarih aralığı filtresi
///  LST7 — Sayfa boyutu sınırlanır (500 tavanı) ve geçersiz sayfa 1'e çekilir
/// </summary>
public class ListeSayfalamaTests : IDisposable
{
    private const string Co = "LST";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly StockService _stock;
    private readonly MaintenanceService _maint;
    private readonly SessionContext _admin;
    private readonly string _mat, _sube, _arac, _def;
    private static readonly long Gun = 1_700_000_000_000;

    public ListeSayfalamaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_lst_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
        _arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        _def = new MaintenanceDefinitionService(_f)
            .Create(_admin, new NewMaintenanceDefinition("Yağ Değişimi", 100m, "day", null, null));

        _stock = new StockService(_f);
        _maint = new MaintenanceService(_f);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stok hareketi üretir. Her giriş bir hareket satırı doğurur.</summary>
    private void StokHareketleri(int adet)
    {
        for (int i = 0; i < adet; i++)
            _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 1m, 10m) }, $"op-in-{i}", branchId: _sube, docDate: Gun);
    }

    private void Bakimlar(int adet, string? belgeOneki = null)
    {
        for (int i = 0; i < adet; i++)
            _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, StockLocationId: _sube,
                InvoiceNo: belgeOneki is null ? null : $"{belgeOneki}{i}"), $"op-m-{i}");
    }

    // ══════════════ STOK HAREKETLERİ ══════════════

    /// <summary>1 — ⭐ KUSURUN KENDİSİ + DÜZELTMESİ AYNI TESTTE. Eski yol tavanda kesiyor ve kesildiğini
    /// SÖYLEMİYOR; yeni yol toplamı bildiriyor ve son sayfaya erişilebiliyor.</summary>
    [Fact]
    public void LST1_Eski_Yol_Tavanda_Kesiyor_Yeni_Yol_Hepsine_Erisiyor()
    {
        StokHareketleri(25);

        // ESKİ YOL: tavan 10 verilince 10 satır döner — geri kalan 15'ten HABER YOK.
        var eski = _stock.SearchMovements(_admin, null, null, null, 10);
        Assert.Equal(10, eski.Count);   // kullanıcı burada "25 kayıt var" bilgisini HİÇ göremiyordu

        // YENİ YOL: toplam bildirilir, sayfa sayısı hesaplanır, son sayfaya erişilir.
        var s1 = _stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, page: 1, pageSize: 10);
        Assert.Equal(25, s1.TotalCount);
        Assert.Equal(3, s1.TotalPages);
        Assert.Equal(10, s1.Items.Count);

        var son = _stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, page: 3, pageSize: 10);
        Assert.Equal(5, son.Items.Count);   // son sayfa — eski yolla ASLA görülemezdi
    }

    /// <summary>2 — Sayfalar TUTARLI: hiçbir kayıt iki sayfada birden çıkmaz, hiçbiri atlanmaz.
    /// Sıralama kararlı değilse bu test kırılır (LIMIT/OFFSET kararsız sıralamayla güvenilmezdir).</summary>
    [Fact]
    public void LST2_Sayfalar_Tutarli_Tekrar_Ve_Atlama_Yok()
    {
        StokHareketleri(30);

        var hepsi = new List<string>();
        for (int p = 1; p <= 3; p++)
            hepsi.AddRange(_stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, p, 10)
                .Items.Select(x => x.DocumentId + "|" + x.Code + "|" + x.CreatedAt));

        Assert.Equal(30, hepsi.Count);
        Assert.Equal(30, hepsi.Distinct().Count());   // tekrar YOK
    }

    /// <summary>3 — Filtre TOPLAMI da daraltır. Sayım ve sayfa AYNI WHERE'i kullanmazsa kullanıcı
    /// "8 kayıt" yazısını görür ama 30 satır listelenir (ya da tersi) — sessiz tutarsızlık.</summary>
    [Fact]
    public void LST3_Filtre_Toplami_Da_Daraltir()
    {
        StokHareketleri(12);
        var digerMat = new MaterialService(_f).Create(_admin, new NewMaterial("M-2", "Demir", UnitPrice: 5m));
        for (int i = 0; i < 4; i++)
            _stock.ReceiveIn(_admin, new[] { new StockLine(digerMat, 1m, 5m) }, $"op-d-{i}", branchId: _sube, docDate: Gun);

        var tumu = _stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, 1, 50);
        Assert.Equal(16, tumu.TotalCount);

        var suzulmus = _stock.SearchMovementsGrid(_admin, null, null, null, null, null, new[] { digerMat }, 1, 50);
        Assert.Equal(4, suzulmus.TotalCount);            // TOPLAM daraldı
        Assert.Equal(4, suzulmus.Items.Count);           // sayfa da aynı sonucu veriyor
        Assert.All(suzulmus.Items, x => Assert.Equal("M-2", x.Code));
    }

    // ══════════════ ARAÇ BAKIMLARI ══════════════

    [Fact]
    public void LST4_Bakimda_Eski_Yol_Kesiyor_Yeni_Yol_Erisiyor()
    {
        Bakimlar(15);

        var eski = _maint.ListMaintenances(_admin, limit: 5);
        Assert.Equal(5, eski.Count);   // sessizce kesildi

        var grid = _maint.SearchMaintenancesGrid(_admin, page: 1, pageSize: 5);
        Assert.Equal(15, grid.TotalCount);
        Assert.Equal(3, grid.TotalPages);
        Assert.Equal(5, _maint.SearchMaintenancesGrid(_admin, page: 3, pageSize: 5).Items.Count);
    }

    /// <summary>5 — Arama gerçekten çalışır. <b>Belge no dâhil</b>: MUH-01b ile eklenen alan aranamıyorsa
    /// kullanıcı elindeki faturayla kaydı bulamaz — alan pratikte yok gibidir.</summary>
    [Fact]
    public void LST5_Bakim_Aramasi_Belge_No_Dahil_Calisir()
    {
        Bakimlar(3, "FTR-");
        _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, StockLocationId: _sube,
            InvoiceNo: "BENZERSIZ-9", Description: "Özel açıklama"), "op-ozel");

        Assert.Equal(1, _maint.SearchMaintenancesGrid(_admin, freeText: "BENZERSIZ-9").TotalCount);
        Assert.Equal(1, _maint.SearchMaintenancesGrid(_admin, freeText: "Özel açıklama").TotalCount);
        Assert.Equal(4, _maint.SearchMaintenancesGrid(_admin, freeText: "ARC-1").TotalCount);       // araç kodu
        Assert.Equal(4, _maint.SearchMaintenancesGrid(_admin, freeText: "Yağ Değişimi").TotalCount); // tanım adı
        Assert.Equal(0, _maint.SearchMaintenancesGrid(_admin, freeText: "hicbiryerde-yok").TotalCount);
    }

    [Fact]
    public void LST6_Bakimda_Tarih_Araligi_Suzer()
    {
        _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, StockLocationId: _sube), "op-t1");
        _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun + 10_000_000, StockLocationId: _sube), "op-t2");

        Assert.Equal(2, _maint.SearchMaintenancesGrid(_admin).TotalCount);
        Assert.Equal(1, _maint.SearchMaintenancesGrid(_admin, fromMs: Gun - 1000, toMs: Gun + 1000).TotalCount);
        Assert.Equal(0, _maint.SearchMaintenancesGrid(_admin, fromMs: Gun + 50_000_000).TotalCount);
    }

    /// <summary>7 — Sınırlar: sayfa boyutu 500'de kırpılır (istemci 1.000.000 isteyip sunucuyu
    /// yoramaz) ve geçersiz sayfa numarası 1'e çekilir (negatif OFFSET ile SQL hatası olmaz).</summary>
    [Fact]
    public void LST7_Sayfa_Sinirlari_Korunur()
    {
        StokHareketleri(3);

        var buyuk = _stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, page: 1, pageSize: 100_000);
        Assert.Equal(500, buyuk.PageSize);

        var gecersiz = _stock.SearchMovementsGrid(_admin, null, null, null, null, null, null, page: -5, pageSize: 10);
        Assert.Equal(1, gecersiz.Page);
        Assert.Equal(3, gecersiz.Items.Count);

        var bakimBuyuk = _maint.SearchMaintenancesGrid(_admin, page: 0, pageSize: 100_000);
        Assert.Equal(1, bakimBuyuk.Page);
        Assert.Equal(500, bakimBuyuk.PageSize);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
