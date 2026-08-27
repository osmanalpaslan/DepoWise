using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ZMT-01 (ADR-167, 2026-08-28) — ZİMMET TESTLERİ ═══
///
/// Kilitler: PK-B1 stoklu hibrit (teslim stoğu düşürür, iade döndürür, EKİPMAN stok dışı) ·
/// PK-B2 tek işlem devir (çift kayıt, stok oynamaz) · PK-B3 kayıp dönmez / hasarlı döner ·
/// geçmiş değişmezliği · idempotent retry (İKİNCİ STOK DÜŞÜMÜ OLMAZ) · tenant · kapsam ·
/// senkron sıra/kapı/uçtan uca · migration canlı-veri kanıtı.
/// </summary>
public class ZimmetTests : IDisposable
{
    private const string Co = "ZMT";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AssignmentService _svc;
    private readonly StockService _stock;
    private readonly string _uid, _depo, _depo2, _mat, _ekp, _ali, _veli;
    private readonly SessionContext _admin;

    public ZimmetTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_zmt_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _depo = branches.Create(_admin, new NewBranch("Merkez Depo"));
        _depo2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Matkap Ucu"));
        _ekp = new EquipmentService(_f).Create(_admin, new NewEquipment("EKP-1", "Jeneratör"));
        var pers = new PersonnelService(_f, new ScopeResolver(_f));
        _ali = pers.Create(_admin, new NewPersonnel("Ali Usta", null, null, null));
        _veli = pers.Create(_admin, new NewPersonnel("Veli Usta", null, null, null));
        _stock = new StockService(_f);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 100m) }, "op-acilis", branchId: _depo);
        _svc = new AssignmentService(_f);
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

    private decimal Stok(string branchId)
        => _stock.GetBalancesByLocation(_admin, _mat).TryGetValue(branchId, out var q) ? q : 0m;

    private decimal Kiside(string personnelId)
        => _svc.Holdings(_admin, personnelId: personnelId).Where(h => h.AssetId == _mat).Sum(h => h.Quantity);

    // ══════════════ PK-B1 — STOKLU HİBRİT ══════════════

    /// <summary>⭐ TESLİM stoğu düşürür, İADE geri getirir; kişideki bakiye defterden doğru türetilir.</summary>
    [Fact]
    public void ZMT1_Teslim_Stogu_Dusurur_Iade_Dondurur()
    {
        _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-t1");
        Assert.Equal(90m, Stok(_depo));
        Assert.Equal(10m, Kiside(_ali));

        _svc.Return(_admin, "material", _mat, _ali, 4m, _depo, null, null, "op-i1");
        Assert.Equal(94m, Stok(_depo));
        Assert.Equal(6m, Kiside(_ali));
    }

    /// <summary>⭐ EKİPMAN stok dışıdır (PK-B1) ve TEK kişide olabilir.</summary>
    [Fact]
    public void ZMT2_Ekipman_Stok_Disi_Ve_Tek_Kiside()
    {
        _svc.Issue(_admin, "equipment", _ekp, _ali, 1m, null, null, null, "op-e1");
        Assert.Equal(100m, Stok(_depo));   // stok OYNAMADI
        Assert.Single(_svc.Holdings(_admin), h => h.AssetId == _ekp && h.PersonnelId == _ali);

        // Ali'deyken Veli'ye teslim EDİLEMEZ:
        Assert.Throws<ArgumentException>(() => _svc.Issue(_admin, "equipment", _ekp, _veli, 1m, null, null, null, "op-e2"));
        // İade sonrası serbest:
        _svc.Return(_admin, "equipment", _ekp, _ali, 1m, null, null, null, "op-e3");
        _svc.Issue(_admin, "equipment", _ekp, _veli, 1m, null, null, null, "op-e4");
        Assert.Single(_svc.Holdings(_admin), h => h.AssetId == _ekp && h.PersonnelId == _veli);
    }

    /// <summary>Depodaki stoktan FAZLA teslim, mevcut negatif stok kalkanına takılır (stok kapısı bypass edilmedi).</summary>
    [Fact]
    public void ZMT3_Stoktan_Fazla_Teslim_Engellenir()
    {
        Assert.ThrowsAny<Exception>(() =>
            _svc.Issue(_admin, "material", _mat, _ali, 500m, _depo, null, null, "op-f1"));
        Assert.Equal(100m, Stok(_depo));    // hiçbir şey değişmedi (transaction geri alındı)
        Assert.Equal(0m, Kiside(_ali));
    }

    /// <summary>Kişideki zimmetten FAZLA iade/devir engellenir.</summary>
    [Fact]
    public void ZMT4_Zimmetten_Fazla_Iade_Engellenir()
    {
        _svc.Issue(_admin, "material", _mat, _ali, 5m, _depo, null, null, "op-t2");
        Assert.Throws<ArgumentException>(() => _svc.Return(_admin, "material", _mat, _ali, 8m, _depo, null, null, "op-i2"));
        Assert.Throws<ArgumentException>(() => _svc.Transfer(_admin, "material", _mat, _ali, _veli, 8m, _depo, null, null, "op-d2"));
        Assert.Equal(95m, Stok(_depo));
        Assert.Equal(5m, Kiside(_ali));
    }

    // ══════════════ PK-B3 — KAYIP / HASAR ══════════════

    /// <summary>⭐ KAYIP: zimmet kapanır, stok GERİ GELMEZ; HASARLI iade stoğa DÖNER (notuyla izlenir).</summary>
    [Fact]
    public void ZMT5_Kayip_Donmez_Hasarli_Doner()
    {
        _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-t3");   // stok 90
        _svc.Lost(_admin, "material", _mat, _ali, 3m, _depo, null, "düştü kırıldı", "op-k1");
        Assert.Equal(90m, Stok(_depo));    // kayıp stoğa DÖNMEDİ
        Assert.Equal(7m, Kiside(_ali));

        _svc.Return(_admin, "material", _mat, _ali, 2m, _depo, null, "çatlak", "op-h1", damaged: true);
        Assert.Equal(92m, Stok(_depo));    // hasarlı iade stoğa DÖNDÜ
        Assert.Equal(5m, Kiside(_ali));
        Assert.Contains(_svc.History(_admin, assetId: _mat), m => m.MovementType == "damaged_return");
    }

    // ══════════════ PK-B2 — DEVİR ══════════════

    /// <summary>⭐ TEK işlem devir: stok OYNAMAZ; defterde ÇİFT kayıt (aynı grup); zincir geçmişi tam okunur.</summary>
    [Fact]
    public void ZMT6_Devir_Tek_Islem_Cift_Kayit_Stok_Oynamaz()
    {
        _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-t4");   // stok 90
        _svc.Transfer(_admin, "material", _mat, _ali, _veli, 10m, _depo, null, null, "op-d1");

        Assert.Equal(90m, Stok(_depo));            // devirde stok DEĞİŞMEZ
        Assert.Equal(0m, Kiside(_ali));
        Assert.Equal(10m, Kiside(_veli));

        var hareketler = _svc.History(_admin, assetId: _mat);
        var cikis = hareketler.Single(m => m.MovementType == "transfer_out");
        var giris = hareketler.Single(m => m.MovementType == "transfer_in");
        Assert.Equal(cikis.GroupId, giris.GroupId);   // çift, aynı grupla bağlı
        Assert.Equal(_ali, cikis.PersonnelId);
        Assert.Equal(_veli, giris.PersonnelId);

        // Zincir: Veli → Ali'ye geri devir → geçmişte DÖRT devir izi (2+2) + teslim; hiçbiri kaybolmaz.
        _svc.Transfer(_admin, "material", _mat, _veli, _ali, 10m, _depo, null, null, "op-d3");
        var tum = _svc.History(_admin, assetId: _mat);
        Assert.Equal(2, tum.Count(m => m.MovementType == "transfer_out"));
        Assert.Equal(2, tum.Count(m => m.MovementType == "transfer_in"));
        Assert.Contains(tum, m => m.MovementType == "issue");
    }

    /// <summary>⭐ GEÇMİŞ DEĞİŞMEZLİĞİ (kullanıcı §11): hiçbir işlem mevcut defter satırını GÜNCELLEMEZ —
    /// devir sonrası eski satırların tamamı bit-bit aynı kalır.</summary>
    [Fact]
    public void ZMT7_Gecmis_Satirlari_Degismez()
    {
        _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-t5");
        // Deterministik kanıt: her satırın TAM içeriği bir kümede tutulur; devir SONRASI eski satır
        // kümesi yeni kümenin ALT KÜMESİ olmalı (tek satır bile değişse/silinse test düşer).
        System.Collections.Generic.HashSet<string> Satirlar()
        {
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, personnel_id, movement_type, direction, quantity, doc_date, operation_id, created_at, version " +
                              "FROM assignment_movements;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < r.FieldCount; i++) sb.Append(Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append(Convert.ToChar(124));
                set.Add(sb.ToString());
            }
            return set;
        }
        var once = Satirlar();
        _svc.Transfer(_admin, "material", _mat, _ali, _veli, 10m, _depo, null, null, "op-d4");
        var sonra = Satirlar();
        Assert.True(once.IsSubsetOf(sonra), "Devir mevcut defter satırlarını DEĞİŞTİRDİ — geçmiş bozulmuş olurdu.");
        Assert.Equal(once.Count + 2, sonra.Count);   // yalnız devir çifti eklendi
    }

    // ══════════════ İDEMPOTENCY ══════════════

    /// <summary>⭐⭐ RETRY KALKANI: aynı operationId ikinci kez → İKİNCİ hareket YOK ve İKİNCİ STOK DÜŞÜMÜ YOK.</summary>
    [Fact]
    public void ZMT8_Ayni_Islem_Iki_Kez_Uygulanmaz()
    {
        _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-r1");
        _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-r1");   // retry
        Assert.Equal(90m, Stok(_depo));    // BİR kez düştü
        Assert.Equal(10m, Kiside(_ali));
        Assert.Single(_svc.History(_admin, assetId: _mat), m => m.MovementType == "issue");

        _svc.Transfer(_admin, "material", _mat, _ali, _veli, 10m, _depo, null, null, "op-r2");
        _svc.Transfer(_admin, "material", _mat, _ali, _veli, 10m, _depo, null, null, "op-r2");   // retry
        Assert.Equal(2, _svc.History(_admin, assetId: _mat).Count(m => m.MovementType.StartsWith("transfer")));
    }

    // ══════════════ YETKİ + KAPSAM + TENANT ══════════════

    [Fact]
    public void ZMT9_Yetki_Kapilari()
    {
        var yetkisiz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.Holdings(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Issue(yetkisiz, "material", _mat, _ali, 1m, _depo, null, null, "op-y1"));

        // assignments VAR ama STOK yetkisi YOK → malzeme teslimi stok kapısına takılır (yan kapı değil);
        // ekipman teslimi (stok dışı) çalışır.
        var stoksuz = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("assignments", true, true, true, false) }));
        Assert.Throws<ForbiddenException>(() => _svc.Issue(stoksuz, "material", _mat, _ali, 1m, _depo, null, null, "op-y2"));
        _svc.Issue(stoksuz, "equipment", _ekp, _ali, 1m, null, null, null, "op-y3");
        Assert.Equal(100m, Stok(_depo));
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsam dışı şubenin zimmet hareketi görünmez; kapsam dışına işlem yapılamaz.</summary>
    [Fact]
    public void ZMT10_Sube_Kapsami()
    {
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 50m) }, "op-acilis2", branchId: _depo2);
        _svc.Issue(_admin, "material", _mat, _ali, 5m, _depo, null, null, "op-s1");
        _svc.Issue(_admin, "material", _mat, _veli, 7m, _depo2, null, null, "op-s2");

        var dar = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("assignments", true, true, true, false),
                new ModulePermission("stock", true, true, true, false),
            })) { ScopeBranchIds = new[] { _depo } };

        var gorulen = _svc.Holdings(dar);
        Assert.Contains(gorulen, h => h.PersonnelId == _ali);
        Assert.DoesNotContain(gorulen, h => h.PersonnelId == _veli);   // depo2 kapsam dışı
        Assert.Throws<ForbiddenException>(() => _svc.Issue(dar, "material", _mat, _veli, 1m, _depo2, null, null, "op-s3"));
    }

    /// <summary>⭐ TENANT: başka firma zimmeti göremez; bu firmanın varlık/personeliyle işlem yapamaz.</summary>
    [Fact]
    public void ZMT11_Firma_Izolasyonu()
    {
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _svc.Issue(_admin, "material", _mat, _ali, 5m, _depo, null, null, "op-z1");
        Assert.Empty(_svc.Holdings(yabanci));
        Assert.Throws<ArgumentException>(() => _svc.Issue(yabanci, "material", _mat, _ali, 1m, null, null, null, "op-z2"));
    }

    // ══════════════ TARİH (ADR-162 ile) ══════════════

    /// <summary>İşlem tarihi İŞ GÜNÜdür ve geri-tarih yetkisine bağlıdır: yetkisiz kullanıcının verdiği
    /// geçmiş tarih sessizce "şimdi"ye normalleşir; kayıt anı daima gerçek saattir.</summary>
    [Fact]
    public void ZMT12_Geri_Tarih_Yetkiye_Bagli()
    {
        var gecmis = 1_600_000_000_000L;
        var stoklu = new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("assignments", true, true, true, false),
                new ModulePermission("stock", true, true, true, false),
            }));   // btn-backdate YOK
        _svc.Issue(stoklu, "material", _mat, _ali, 1m, _depo, gecmis, null, "op-g1");
        var h = _svc.History(_admin, assetId: _mat).Single(m => m.MovementType == "issue");
        Assert.NotEqual(gecmis, h.DocDate);   // normalleşti ("şimdi")

        // Admin (bypass) geri tarih verebilir:
        _svc.Return(_admin, "material", _mat, _ali, 1m, _depo, gecmis, null, "op-g2");
        Assert.Equal(gecmis, _svc.History(_admin, assetId: _mat).Single(m => m.MovementType == "return").DocDate);
    }

    // ══════════════ SENKRON ══════════════

    [Fact]
    public void ZMT13_Senkron_Listesi_Ve_Kapisi()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.Contains("assignment_movements", t);
        Assert.True(t.IndexOf("personnel") < t.IndexOf("assignment_movements"));
        Assert.True(t.IndexOf("materials") < t.IndexOf("assignment_movements"));
        Assert.True(t.IndexOf("equipment") < t.IndexOf("assignment_movements"));
        Assert.Equal(AssignmentService.Module, BusinessSyncService.ModuleOf("assignment_movements"));
    }

    /// <summary>⭐ UÇTAN UCA: zimmet defteri + bağlı stok hareketi AYNI pakette hedefe taşınır;
    /// paket ikinci kez uygulanınca kopya oluşmaz.</summary>
    [Fact]
    public void ZMT14_Senkron_Uctan_Uca_Idempotent()
    {
        var dstPath = Path.Combine(Path.GetTempPath(), "dw_zmt_dst_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var dst = new SqliteConnectionFactory(dstPath);
            new MigrationRunner(dst).Run();
            Firma(dst, Co);
            using (var conn = dst.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                                  "VALUES(@b,@c,'Merkez Depo','branch',1,1,1,0);";
                cmd.AddWithValue("@b", _depo);
                cmd.AddWithValue("@c", Co);
                cmd.ExecuteNonQuery();
            }

            _svc.Issue(_admin, "material", _mat, _ali, 10m, _depo, null, null, "op-snk1");

            var clock = new SystemClock();
            using var snap = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co));
            var dstSvc = new BusinessSyncService(dst, clock);
            var r1 = dstSvc.ApplyPull(Co, snap.RootElement);
            Assert.Empty(r1.Errors);

            long Say(string sql)
            {
                using var conn = dst.Create();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            Assert.Equal(1, Say("SELECT COUNT(*) FROM assignment_movements WHERE operation_id='op-snk1'"));
            Assert.Equal(1, Say("SELECT COUNT(*) FROM stock_movements WHERE operation_id LIKE 'assign:op-snk1%'"));

            dstSvc.ApplyPull(Co, snap.RootElement);   // ikinci uygulama
            Assert.Equal(1, Say("SELECT COUNT(*) FROM assignment_movements WHERE operation_id='op-snk1'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dstPath); } catch { }
        }
    }

    // ══════════════ ⭐⭐ MIGRATION076 CANLI-VERİ KANITI ══════════════

    [Fact]
    public void ZMT15_Migration076_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_zmt_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 75)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted)
    VALUES('P1','C1','Ali',1,11,11,1,0);
INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,currency_code,created_at,updated_at,version,is_deleted)
    VALUES('M1','C1','K-1','Çimento','0','10','TRY',12,12,1,0);
INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,created_at)
    VALUES('SM1','C1','M1',NULL,'in',1,'5','op-1',13);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "personnel", "materials", "stock_movements", "companies" })
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
            Assert.Equal(new[] { 76 }, new MigrationRunner(f, new IMigration[] { new Migration076_Assignments() }).Run());
            Assert.Equal(once, Foto(f));
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM assignment_movements;";
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
    public void ZMT16_Migration076_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration076_Assignments.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP " })
            Assert.DoesNotContain(yasak, govde);
    }
}
