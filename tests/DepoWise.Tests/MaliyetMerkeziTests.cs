using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MLY-01 (ADR-168, 2026-08-28) — MALİYET MERKEZİ TESTLERİ ═══
///
/// Kilitler: tanım CRUD + yetki + tenant · tek kayıt = tek merkez (bağ upsert) · bağ, MEVCUT kayıtları
/// DEĞİŞTİRMEZ (dış tablo) · özet C# decimal doğru toplar ve para birimlerini KARIŞTIRMAZ · şube kapsamı
/// özet/bağda yan kapı değildir · senkron listesi/kapısı · migration canlı-veri kanıtı ·
/// mevcut stok/yakıt akışları merkez SEÇİLMEDEN aynen çalışır (regresyon).
/// </summary>
public class MaliyetMerkeziTests : IDisposable
{
    private const string Co = "MLY";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly CostCenterService _svc;
    private readonly StockService _stock;
    private readonly FuelService _fuel;
    private readonly string _uid, _depo, _depo2, _mat, _arac, _cc1, _cc2;
    private readonly SessionContext _admin;
    private static readonly long Gun = 1_700_000_000_000;

    public MaliyetMerkeziTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_mly_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _depo = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _depo2 = branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Şantiye B", "site"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
        _arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f).Create(_admin,
            new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        _stock = new StockService(_f);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 100m, 10m) }, "op-acilis", branchId: _depo, docDate: Gun);
        _fuel = new FuelService(_f);
        _svc = new CostCenterService(_f);
        _cc1 = _svc.Create(_admin, new NewCostCenter("Asfalt İşi", "AS-1"));
        _cc2 = _svc.Create(_admin, new NewCostCenter("Bakım Departmanı"));
    }

    private static void Firma(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    // ══════════════ TANIM ══════════════

    [Fact]
    public void MLY1_Tanim_CRUD_Ve_Kilit()
    {
        var rows = _svc.List(_admin);
        Assert.Equal(2, rows.Count);
        var v = rows.Single(x => x.Id == _cc1).Version;
        _svc.Update(_admin, _cc1, new NewCostCenter("Asfalt İşi 2", "AS-1", "passive"), v);
        Assert.Equal("Pasif", _svc.List(_admin).Single(x => x.Id == _cc1).StatusDisplay);
        Assert.Throws<ConcurrencyException>(() => _svc.Update(_admin, _cc1, new NewCostCenter("X"), v));
        Assert.Single(_svc.Options(_admin));   // pasif merkez işlem seçeneklerinde ÇIKMAZ
    }

    [Fact]
    public void MLY2_Yetki_Ve_Tenant()
    {
        var yetkisiz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewCostCenter("X")));

        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_svc.List(yabanci));
        Assert.Throws<ArgumentException>(() => _svc.Update(yabanci, _cc1, new NewCostCenter("Çalındı")));
    }

    [Fact]
    public void MLY3_Soft_Delete_Cop_Kutusu()
    {
        _svc.Delete(_admin, _cc2);
        Assert.DoesNotContain(_svc.List(_admin), x => x.Id == _cc2);
        var trash = new TrashService(_f);
        Assert.Contains(trash.List(_admin, reauthenticated: true), t => t.Table == "cost_centers" && t.Id == _cc2);
        trash.Restore(_admin, "cost_centers", _cc2, reauthenticated: true);
        Assert.Contains(_svc.List(_admin), x => x.Id == _cc2);
    }

    // ══════════════ BAĞ ══════════════

    /// <summary>⭐ Bağ DIŞ tablodadır: bağlama işlemi kaynak kaydın HİÇBİR değerini değiştirmez (bit-bit);
    /// aynı kayda ikinci merkez seçilince bağ GÜNCELLENİR (tek-merkez); boş merkez bağı kaldırır.</summary>
    [Fact]
    public void MLY4_Bag_Mevcut_Kaydi_Degistirmez_Ve_Tek_Merkez()
    {
        var doc = _stock.IssueOut(_admin, new[] { new StockLine(_mat, 5m) }, "op-out1", branchId: _depo, docDate: Gun);
        string Foto()
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM stock_documents WHERE id=@id;";
            cmd.AddWithValue("@id", doc.DocumentId);
            using var r = cmd.ExecuteReader();
            r.Read();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < r.FieldCount; i++) sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            return sb.ToString();
        }
        var once = Foto();
        _svc.Link(_admin, "stock_document", doc.DocumentId, _cc1);
        Assert.Equal(once, Foto());   // kaynak kayıt BİT-BİT aynı

        _svc.Link(_admin, "stock_document", doc.DocumentId, _cc2);   // merkez değişti → bağ güncellendi
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*), MAX(cost_center_id) FROM cost_center_links WHERE entity_id=@e AND is_deleted=0;";
            cmd.AddWithValue("@e", doc.DocumentId);
            using var r = cmd.ExecuteReader();
            r.Read();
            Assert.Equal(1L, r.GetInt64(0));         // TEK bağ satırı (tek-merkez kuralı)
            Assert.Equal(_cc2, r.GetString(1));
        }
        _svc.Link(_admin, "stock_document", doc.DocumentId, null);   // bağ kaldırıldı
        Assert.Empty(_svc.Summary(_admin, 0, long.MaxValue));
    }

    [Fact]
    public void MLY5_Bag_Tenant_Ve_Tur_Korumasi()
    {
        var doc = _stock.IssueOut(_admin, new[] { new StockLine(_mat, 1m) }, "op-out2", branchId: _depo, docDate: Gun);
        Firma(_f, "BASKA2");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA2", "admin3", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA2", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ArgumentException>(() => _svc.Link(yabanci, "stock_document", doc.DocumentId, _cc1));
        Assert.Throws<ArgumentException>(() => _svc.Link(_admin, "personnel", "x", _cc1));   // izinsiz tür
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsam dışı şubenin belgesine bağ kurulamaz; kapsam dışı maliyet özete girmez.</summary>
    [Fact]
    public void MLY6_Sube_Kapsami_Yan_Kapi_Degil()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 50m, 10m) }, "op-acilis2", branchId: _depo2, docDate: Gun);
        var d1 = _stock.IssueOut(_admin, new[] { new StockLine(_mat, 2m) }, "op-s1", branchId: _depo, docDate: Gun);
        var d2 = _stock.IssueOut(_admin, new[] { new StockLine(_mat, 3m) }, "op-s2", branchId: _depo2, docDate: Gun);
        _svc.Link(_admin, "stock_document", d1.DocumentId, _cc1);
        _svc.Link(_admin, "stock_document", d2.DocumentId, _cc1);

        var dar = Personel(kapsam: new[] { _depo },
            izinler: new[] { ("cost_centers", true, true, true, true), ("stock", true, true, true, false) });
        // Bağ: kapsam dışı şubenin belgesine KURULAMAZ
        Assert.Throws<ForbiddenException>(() => _svc.Link(dar, "stock_document", d2.DocumentId, _cc2));
        // Özet: yalnız kapsam içi belge (2×10=20); depo2 belgesi (3×10=30) GÖRÜNMEZ
        var ozet = _svc.Summary(dar, 0, long.MaxValue);
        Assert.Equal(20m, ozet.Single(x => x.Category == "Malzeme Çıkışı").Amount);
        // Admin tümünü görür (20+30=50)
        Assert.Equal(50m, _svc.Summary(_admin, 0, long.MaxValue).Single(x => x.Category == "Malzeme Çıkışı").Amount);
    }

    // ══════════════ ÖZET ══════════════

    /// <summary>⭐ Özet üç kalemi C# decimal ile DOĞRU toplar; tarih aralığına uyar; farklı para birimi AYRI satır.</summary>
    [Fact]
    public void MLY7_Ozet_Dogru_Toplar()
    {
        var doc = _stock.IssueOut(_admin, new[] { new StockLine(_mat, 4m) }, "op-oz1", branchId: _depo, docDate: Gun);
        _svc.Link(_admin, "stock_document", doc.DocumentId, _cc1);
        var yakitDepo = _fuel.AddDepotEntry(_admin, new NewDepotEntry(100m, 42.5m, "TRY", null, null, null, Gun), "op-fd1");
        _svc.Link(_admin, "fuel_depot_entry", yakitDepo, _cc1);
        var dagitim = _fuel.Distribute(_admin, new NewDistribution(_arac, 40m, 1500m, 43m, "TRY", null, Gun, null), "op-fx1");
        _svc.Link(_admin, "fuel_distribution", dagitim, _cc2);

        var ozet = _svc.Summary(_admin, Gun - 1000, Gun + 1000);
        Assert.Equal(40m, ozet.Single(x => x.CostCenterId == _cc1 && x.Category == "Malzeme Çıkışı").Amount);      // 4×10
        Assert.Equal(4250m, ozet.Single(x => x.CostCenterId == _cc1 && x.Category == "Yakıt Depo Girişi").Amount); // 100×42.5
        Assert.Equal(1720m, ozet.Single(x => x.CostCenterId == _cc2 && x.Category == "Yakıt Dağıtımı").Amount);    // 40×43
        Assert.All(ozet, x => Assert.Equal("TRY", x.Currency));

        // Tarih aralığı dışı → boş
        Assert.Empty(_svc.Summary(_admin, Gun + 10_000, Gun + 20_000));
    }

    /// <summary>⭐ REGRESYON: merkez SEÇİLMEDEN mevcut stok/yakıt akışları AYNEN çalışır; hiçbir bağ oluşmaz;
    /// stok bakiyesi mevcut kurallarla aynı kalır (mevcut hesapların anlamı değişmedi).</summary>
    [Fact]
    public void MLY8_Merkezsiz_Akis_Degismedi()
    {
        _stock.IssueOut(_admin, new[] { new StockLine(_mat, 7m) }, "op-r1", branchId: _depo, docDate: Gun);
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(10m, 40m, "TRY", null, null, null, Gun), "op-r2");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cost_center_links;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));   // bağ YOK — akışlar merkezden bağımsız
        Assert.Equal(93m, _stock.GetBalancesByLocation(_admin, _mat)[_depo]);   // 100-7 (mevcut kural)
    }

    // ══════════════ SENKRON ══════════════

    [Fact]
    public void MLY9_Senkron_Listesi_Ve_Kapisi()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.Contains("cost_centers", t);
        Assert.Contains("cost_center_links", t);
        Assert.True(t.IndexOf("cost_centers") < t.IndexOf("cost_center_links"), "tanım bağdan ÖNCE (FK).");
        Assert.True(t.IndexOf("stock_documents") < t.IndexOf("cost_center_links"), "kaynak kayıt bağdan ÖNCE.");
        Assert.Equal(CostCenterService.Module, BusinessSyncService.ModuleOf("cost_centers"));
        Assert.Equal(CostCenterService.Module, BusinessSyncService.ModuleOf("cost_center_links"));
    }

    // ══════════════ ⭐⭐ MIGRATION077 CANLI-VERİ KANITI ══════════════

    [Fact]
    public void MLY10_Migration077_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_mly_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 76)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,currency_code,created_at,updated_at,version,is_deleted)
    VALUES('M1','C1','K-1','Çimento','0','10','TRY',12,12,1,0);
INSERT INTO stock_documents(id,company_id,doc_type,doc_no,doc_date,status,created_at,updated_at,version,is_deleted)
    VALUES('D1','C1','out','OUT-1',13,'active',13,13,1,0);
INSERT INTO fuel_depot_entries(id,company_id,liters,unit_price,currency_code,entry_date,operation_id,created_at,updated_at,version,is_deleted)
    VALUES('F1','C1','50','40','TRY',14,'op-1',14,14,1,0);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "stock_documents", "fuel_depot_entries", "materials", "companies" })
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT * FROM {t} ORDER BY 1;";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        for (int i = 0; i < r.FieldCount; i++)
                            sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
                }
                return sb.ToString();
            }
            var once = Foto(f);
            Assert.Equal(new[] { 77 }, new MigrationRunner(f, new IMigration[] { new Migration077_CostCenters() }).Run());
            Assert.Equal(once, Foto(f));   // mevcut tablolar BİT-BİT aynı (ALTER dahi yok)
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM cost_centers) + (SELECT COUNT(*) FROM cost_center_links);";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    [Fact]
    public void MLY11_Migration077_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration077_CostCenters.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }
}
