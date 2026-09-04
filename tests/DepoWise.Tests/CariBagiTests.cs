using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MUH-01c (FAZ D, 2026-09-04) — PARA DOĞURAN KAYITLARDA CARİ ═══
///
/// <b>Kapsam kararı ölçümle verildi:</b> "her kayda cari alanı" isteği incelendiğinde, karşı tarafın
/// çoğu yerde <b>zaten ulaşılabilir</b> olduğu görüldü. Yeni kolon YALNIZ gerçekten boşluk olan yere
/// (iki bakım tablosu) eklendi:
/// <list type="bullet">
///   <item><b>Bakımlar</b> — dış servis sağlayıcısı hiçbir yerde tutulmuyordu → <c>party_id</c></item>
///   <item><b>Yakıt / satın alma</b> — <c>supplier_id</c> zaten var; ikinci bir cari kolonu aynı
///   satırda İKİ GERÇEKLİK olurdu → Migration066'nın köprüsü (<c>parties.supplier_id</c>) kullanıldı</item>
///   <item><b>Stok belgesi</b> — karşı taraf <c>invoices.stock_document_id</c> + <c>invoices.party_id</c>
///   ile zaten bağlı; kolon eklemek faturayla çelişebilecek ikinci gerçeklik olurdu</item>
/// </list>
///
///  CAR1 — İki bakım tablosuna sütun eklendi (migration kanıtı)
///  CAR2 — Araç bakımına cari yazılır ve geri okunur
///  CAR3 — Ekipman bakımına cari yazılır ve geri okunur
///  CAR4 — Cari OPSİYONEL: verilmeyen kayıt aynen çalışır (mevcut akış zorunlu hâle gelmedi)
///  CAR5 — 🔴 TENANT: BAŞKA FİRMANIN carisi bağlanamaz (ID tahmini kapalı) — iki bakım hattında da
///  CAR6 — Var olmayan cari reddedilir (uydurma kimlik yazılmaz)
///  CAR7 — Tedarikçi → cari köprüsü çözülür; eşleme yoksa null döner (uydurma yok)
///  CAR8 — Köprü FİRMA KAPSAMLIDIR: başka firmanın eşlemesi görünmez
///  CAR9 — Köprü GÜNCELLENEBİLİR (Update eskiden supplier_id yazmıyordu — düzeltildi)
///  CAR10 — Migration090 yalnız EKLEME içerir (canlı veri kanıtı)
/// </summary>
public class CariBagiTests : IDisposable
{
    private const string Co = "CAR", Yabanci = "CAR2";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly PartyService _parties;
    private readonly MaintenanceService _maint;
    private readonly EquipmentMaintenanceService _eqm;
    private readonly SessionContext _admin, _yabanciAdmin;
    private readonly string _arac, _def, _ekipman, _cari, _yabanciCari;
    private static readonly long Gun = 1_700_000_000_000;

    public CariBagiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_car_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        _admin = Firma(Co, "admin_car");
        _yabanciAdmin = Firma(Yabanci, "admin_car2");

        _parties = new PartyService(_f);
        _maint = new MaintenanceService(_f);
        _eqm = new EquipmentMaintenanceService(_f);

        _arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        _def = new MaintenanceDefinitionService(_f)
            .Create(_admin, new NewMaintenanceDefinition("Yağ", 100m, "day", null, null));
        _ekipman = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{_ekipman}','{Co}','EKP-1','Jeneratör','active',1,1,1,0);");

        _cari = _parties.Create(_admin, new NewParty("C-1", "Oto Servis A.Ş.", PartyTypes.Supplier));
        _yabanciCari = _parties.Create(_yabanciAdmin, new NewParty("C-X", "Başka Firma Carisi", PartyTypes.Supplier));
    }

    private SessionContext Firma(string co, string user)
    {
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string? Oku(string table, string id)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT party_id FROM {table} WHERE id=@i;";
        cmd.AddWithValue("@i", id);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    // ══════════════ ŞEMA ══════════════

    [Fact]
    public void CAR1_Iki_Bakim_Tablosuna_Cari_Sutunu_Eklendi()
    {
        using var conn = _f.Create();
        foreach (var t in new[] { "vehicle_maintenances", "equipment_maintenances" })
            Assert.True(DbIntrospect.ColumnExists(conn, null, t, "party_id"),
                $"{t}.party_id sütunu yok — Migration090 uygulanmamış.");
    }

    // ══════════════ YAZ / OKU ══════════════

    [Fact]
    public void CAR2_Arac_Bakimina_Cari_Yazilir()
    {
        var id = _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, PartyId: _cari), "op-m1");
        Assert.Equal(_cari, Oku("vehicle_maintenances", id));
        Assert.Equal(_cari, _maint.ListMaintenances(_admin).Single(x => x.Id == id).PartyId);
    }

    [Fact]
    public void CAR3_Ekipman_Bakimina_Cari_Yazilir()
    {
        var id = _eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun, PartyId: _cari), "op-e1");
        Assert.Equal(_cari, Oku("equipment_maintenances", id));
        Assert.Equal(_cari, _eqm.List(_admin).Single(x => x.Id == id).PartyId);
    }

    /// <summary>4 — ⭐ REGRESYON: cari OPSİYONELDİR. Kendi atölyesinde yapılan bakımda boş kalır;
    /// yeni alanın mevcut akışı sessizce zorunlu hâle getirmediği kanıtlanır.</summary>
    [Fact]
    public void CAR4_Cari_Opsiyoneldir_Mevcut_Akis_Kirilmaz()
    {
        var bakim = _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun), "op-m2");
        var eqm = _eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun), "op-e2");
        Assert.Null(Oku("vehicle_maintenances", bakim));
        Assert.Null(Oku("equipment_maintenances", eqm));
    }

    // ══════════════ 🔴 TENANT GÜVENLİĞİ ══════════════

    /// <summary>5 — ⭐ EN KRİTİK: Migration090 bilinçli olarak FK KURMADI (canlı tabloda SQLite
    /// rebuild riski). Kapı bu yüzden SERVİS katmanındadır ve gerçekten kapalı olmalıdır:
    /// başka firmanın cari kimliği tahmin edilip bağlanamaz.
    /// Doğrulama serviste olduğu için masaüstünün ÇEVRİMDIŞI yolu da korunur (yalnız API'de olsaydı
    /// o yol açık kalırdı).</summary>
    [Fact]
    public void CAR5_Baska_Firmanin_Carisi_Baglanamaz()
    {
        Assert.Throws<ForbiddenException>(() =>
            _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, PartyId: _yabanciCari), "op-m3"));

        Assert.Throws<ForbiddenException>(() =>
            _eqm.Save(_admin, new NewEquipmentMaintenance(_ekipman, _def, PerformedDate: Gun, PartyId: _yabanciCari), "op-e3"));

        // Hiçbir kayıt SIZMADI: reddedilen işlem yarım kayıt bırakmaz.
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM vehicle_maintenances WHERE party_id IS NOT NULL;"));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM equipment_maintenances WHERE party_id IS NOT NULL;"));
    }

    [Fact]
    public void CAR6_Var_Olmayan_Cari_Reddedilir()
    {
        Assert.Throws<ForbiddenException>(() =>
            _maint.Save(_admin, new NewMaintenance(_arac, _def, PerformedDate: Gun, PartyId: "yok-boyle-bir-cari"), "op-m4"));
    }

    // ══════════════ TEDARİKÇİ ↔ CARİ KÖPRÜSÜ ══════════════

    /// <summary>7 — Köprü çözülür. Yakıt/satın alma karşı tarafı `supplier_id` ile tutar; cari
    /// defterine bu köprüyle bağlanır. <b>Eşleme yoksa null döner</b> — uydurma yapılmaz, sessizce
    /// yanlış cariye yazılmaz.</summary>
    [Fact]
    public void CAR7_Tedarikci_Cari_Koprusu_Cozulur()
    {
        var tedarikci = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{tedarikci}','{Co}','Petrol Ltd',1,1,1,0);");
        var cari = _parties.Create(_admin, new NewParty("C-2", "Petrol Ltd", PartyTypes.Supplier, SupplierId: tedarikci));

        Assert.Equal(cari, _parties.PartyIdBySupplier(_admin, tedarikci));

        // Eşlenmemiş tedarikçi → null (uydurma yok)
        var esleşmemis = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{esleşmemis}','{Co}','Eşlenmemiş',1,1,1,0);");
        Assert.Null(_parties.PartyIdBySupplier(_admin, esleşmemis));
        Assert.Null(_parties.PartyIdBySupplier(_admin, null));
    }

    /// <summary>8 — Köprü FİRMA KAPSAMLIDIR: başka firmanın eşlemesi bu firmadan görünmez.</summary>
    [Fact]
    public void CAR8_Kopru_Firma_Kapsamlidir()
    {
        var tedarikci = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                 $"VALUES('{tedarikci}','{Yabanci}','Yabancı Tedarikçi',1,1,1,0);");
        _parties.Create(_yabanciAdmin, new NewParty("C-Y", "Yabancı Cari", PartyTypes.Supplier, SupplierId: tedarikci));

        Assert.NotNull(_parties.PartyIdBySupplier(_yabanciAdmin, tedarikci));   // kendi firmasından görünür
        Assert.Null(_parties.PartyIdBySupplier(_admin, tedarikci));             // başka firmadan GÖRÜNMEZ
    }

    /// <summary>9 — 🔴 KAPATILAN BOŞLUK: <c>Create</c> köprüyü yazıyordu ama <c>Update</c> YAZMIYORDU
    /// → yanlış eşleme kurulduğunda düzeltilemiyordu (ve arayüzde alan zaten yoktu).</summary>
    [Fact]
    public void CAR9_Kopru_Guncellenebilir()
    {
        var t1 = Guid.NewGuid().ToString("N");
        var t2 = Guid.NewGuid().ToString("N");
        foreach (var (id, ad) in new[] { (t1, "İlk"), (t2, "İkinci") })
            Calistir("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                     $"VALUES('{id}','{Co}','{ad}',1,1,1,0);");

        var cari = _parties.Create(_admin, new NewParty("C-3", "Değişecek", PartyTypes.Supplier, SupplierId: t1));
        Assert.Equal(cari, _parties.PartyIdBySupplier(_admin, t1));

        var kayit = _parties.Get(_admin, cari);
        _parties.Update(_admin, cari, new UpdateParty("C-3", "Değişecek", PartyTypes.Supplier,
            Version: kayit.Version, SupplierId: t2));

        Assert.Equal(cari, _parties.PartyIdBySupplier(_admin, t2));   // yeni eşleme geçerli
        Assert.Null(_parties.PartyIdBySupplier(_admin, t1));          // eski eşleme kalkmış
    }

    // ══════════════ CANLI VERİ GÜVENLİĞİ ══════════════

    [Fact]
    public void CAR10_Migration090_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var kaynak = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration090_MaintenancePartyLink.cs"));

        var i = kaynak.IndexOf("add.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var sql = kaynak[i..].ToUpperInvariant();
        Assert.Contains("ADD COLUMN", sql);
        foreach (var yasak in new[] { "UPDATE ", "DELETE ", "DROP ", "INSERT ", "NOT NULL" })
            Assert.DoesNotContain(yasak, sql);
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
