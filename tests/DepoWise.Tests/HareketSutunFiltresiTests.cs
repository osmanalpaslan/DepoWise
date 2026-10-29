using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// 2026-10-29 (kullanıcı isteği): Giriş-Çıkış "Son hareketler" tablosunun SÜTUN filtreleri (<see cref="HareketSutunFiltresi"/>).
///  HSF1 — SQLite: Kod / Malzeme "içerir" (Türkçe harf duyarsız), Miktar sayısal (5 · &gt;5 · 5-10), Açıklama, Yön; birlikte VE;
///         sayım ile sayfa AYNI koşul (toplam doğru); başka firmanın hareketi hiç gelmez.
///  HSF2 — PostgreSQL: aynı senaryo (lehçe farkı: LikeTr, CAST) — yalnız güvenli boş test veritabanında.
/// </summary>
[Collection("PostgresSchema")]
public class HareketSutunFiltresiTests : IDisposable
{
    private const long GunMs = 86_400_000L;
    private const long Simdi = 1_700_000_000_000;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "dw_hsf_" + Guid.NewGuid().ToString("N") + ".db");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Simdi);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void HSF1_Sutun_Filtreleri_SQLite()
    {
        var f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(f).Run();
        Senaryo(f, ekstraSirket: sql => { using var c = f.Create(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); });
    }

    [SkippableFact]
    public void HSF2_Sutun_Filtreleri_PostgreSQL()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        PostgresTestGuard.ResetSchema(f);
        new MigrationRunner(f).Run();
        Senaryo(f, ekstraSirket: sql => { using var c = f.Create(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); });
    }

    /// <summary>HSF3/HSF4 — 2026-10-29 (kullanıcı bildirimi: Giriş-Çıkış açıklama sütununda "54fff1a2…" gibi değerler): bakım tüketiminde
    /// hareketin notu bakım KAYDININ KİMLİĞİDİR. Gösterilen açıklama (Aciklama / NoteText / Excel) okunur metindir; ham kimlik
    /// yalnız Note'ta kalır ("Kaynak" bağlantısı onu kullanır). Başka firmanın bakım kaydı çözülmez. Açıklama filtresi ve arama
    /// gösterilen metinde çalışır. Belge açıklaması (kullanıcının yazdığı) sütunda görünür.</summary>
    [Fact]
    public void HSF3_Aciklama_Okunur_SQLite()
    {
        var f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(f).Run();
        AciklamaSenaryosu(f, sql => { using var c = f.Create(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); });
    }

    [SkippableFact]
    public void HSF4_Aciklama_Okunur_PostgreSQL()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        PostgresTestGuard.ResetSchema(f);
        new MigrationRunner(f).Run();
        AciklamaSenaryosu(f, sql => { using var c = f.Create(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); });
    }

    private static void AciklamaSenaryosu(IDbConnectionFactory f, Action<string> exec)
    {
        foreach (var id in new[] { "A", "B" })
            exec("INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                 $"VALUES('{id}', '{id}', 1, 1, 1, 0, 5, 20, 5);");
        var clock = new TestClock();
        var users = new UserService(f, clock);
        var a = new SessionContext(users.EnsureInitialAdmin("A", "hsa_a", "admin123", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var b = new SessionContext(users.EnsureInitialAdmin("B", "hsa_b", "admin123", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var depo = new BranchService(f, clock).Create(a, new NewBranch("Merkez"));
        var mat = new MaterialService(f, clock).Create(a, new NewMaterial("YF-1", "Yağ filtresi"));
        var vs = new DepoWise.Infrastructure.Vehicles.VehicleService(f, clock);
        var defs = new DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService(f, clock);
        var aracA = vs.Create(a, new DepoWise.Infrastructure.Vehicles.NewVehicle("EKS-01", "34 AB 12"));
        var aracB = vs.Create(b, new DepoWise.Infrastructure.Vehicles.NewVehicle("YAB-09", "06 ZZ 99"));
        var tanimA = defs.Create(a, new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition("Periyodik", 100m, "km"));
        var tanimB = defs.Create(b, new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition("Periyodik", 100m, "km"));
        exec("INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id,description,operation_id,created_at,updated_at) " +
             $"VALUES('bk-a','A','{aracA}','{tanimA}','Yağ değişimi','op-bk-a',1,1);");
        exec("INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id,operation_id,created_at,updated_at) " +
             $"VALUES('bk-b','B','{aracB}','{tanimB}','op-bk-b',1,1);");
        var st = new StockService(f, clock);
        st.ReceiveIn(a, new[] { new StockLine(mat, 10m, 1m) }, "hsa-giris", depo, note: "Fatura A-1 girişi", docDate: Simdi - 5 * GunMs);
        void Hareket(string id, string tur, int yon, string not)
            => exec("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at) " +
                    $"VALUES('{id}','A','{mat}','{depo}','{tur}',{yon},'1','{id}','{not}',{Simdi});");
        Hareket("hsa-u1", "usage", -1, "bk-a");
        Hareket("hsa-u2", "usage_reverse", 1, "bk-a");
        Hareket("hsa-u3", "usage", -1, "bk-b");        // B firmasının bakım kimliği — A'da çözülmemeli

        var satirlar = st.SearchMovementsGrid(a, null, null, null, null, null, null, 1, 50).Items;
        string Aciklama(string tur, string not) => satirlar.Single(r => r.MovementType == tur && r.Note == not).NoteText;
        Assert.Equal("Araç bakımı — EKS-01 (34 AB 12) · Yağ değişimi", Aciklama("usage", "bk-a"));
        Assert.Equal("Araç bakımı iptali — EKS-01 (34 AB 12) · Yağ değişimi", Aciklama("usage_reverse", "bk-a"));
        Assert.Equal("Bakım tüketimi", Aciklama("usage", "bk-b"));
        Assert.Equal("Fatura A-1 girişi", satirlar.Single(r => r.MovementType == "in").NoteText);   // kullanıcının belge açıklaması
        Assert.DoesNotContain(satirlar, r => r.NoteText.Contains("bk-"));                          // iç kimlik ASLA görünmez
        Assert.All(st.SearchMovements(a, null, null, null, null, null, null, 100), r => Assert.DoesNotContain("bk-", r.NoteText));
        var tablo = StockMovementListeTablosu.ToTableModel(satirlar);                                 // Excel / PDF
        Assert.DoesNotContain(tablo.Rows.SelectMany(x => x), h => (h?.ToString() ?? "").Contains("bk-"));

        // filtre ve arama GÖSTERİLEN metinde
        Assert.Equal(2, st.SearchMovementsGrid(a, null, null, null, null, null, null, 1, 50, new HareketSutunFiltresi(Aciklama: "araç bakımı")).TotalCount);
        Assert.Equal(2, st.SearchMovementsGrid(a, null, null, "34 AB", null, null, null, 1, 50).TotalCount);
        Assert.Equal(0, st.SearchMovementsGrid(a, null, null, null, null, null, null, 1, 50, new HareketSutunFiltresi(Aciklama: "bk-a")).TotalCount);

        // "Kaynak" bağlantısı ham kimlikle çalışmaya devam eder
        var u1 = satirlar.Single(r => r.MovementType == "usage" && r.Note == "bk-a");
        Assert.Equal("arac_bakimi", st.KaynakBul(a, u1.DocumentId, u1.MovementType, u1.Note)?.Tur);
    }

    private static void Senaryo(IDbConnectionFactory f, Action<string> ekstraSirket)
    {
        foreach (var id in new[] { "A", "B" })
            ekstraSirket("INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                         $"VALUES('{id}', '{id}', 1, 1, 1, 0, 5, 20, 5);");
        var clock = new TestClock();
        var users = new UserService(f, clock);
        var a = new SessionContext(users.EnsureInitialAdmin("A", "hsf_a", "admin123", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var b = new SessionContext(users.EnsureInitialAdmin("B", "hsf_b", "admin123", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var depoA = new BranchService(f, clock).Create(a, new NewBranch("Merkez"));
        var depoB = new BranchService(f, clock).Create(b, new NewBranch("Yabancı Depo"));
        var mats = new MaterialService(f, clock);
        var yf = mats.Create(a, new NewMaterial("YF-1", "Yağ filtresi"));
        var hf = mats.Create(a, new NewMaterial("HF-2", "Hava filtresi"));
        var ck = mats.Create(a, new NewMaterial("ÇK-3", "Çekiç"));
        var yabanci = mats.Create(b, new NewMaterial("YF-9", "Yağ filtresi (B)"));
        var st = new StockService(f, clock);
        string Op() => "hsf-" + Guid.NewGuid().ToString("N");
        st.ReceiveIn(a, new[] { new StockLine(yf, 10m, 1m) }, Op(), depoA, note: "Fatura A-1", docDate: Simdi - 5 * GunMs);
        st.ReceiveIn(a, new[] { new StockLine(hf, 5m, 1m) }, Op(), depoA, note: "ilk giriş", docDate: Simdi - 3 * GunMs);
        st.IssueOut(a, new[] { new StockLine(yf, 4m, 1m) }, Op(), depoA, note: "Şantiyeye gönderildi", docDate: Simdi - GunMs);
        st.ReceiveIn(a, new[] { new StockLine(ck, 25m, 1m) }, Op(), depoA, docDate: Simdi - GunMs);
        st.ReceiveIn(b, new[] { new StockLine(yabanci, 7m, 1m) }, Op(), depoB, docDate: Simdi - GunMs);
        // "Açıklama" sütunu hareket SATIRININ notunu gösterir (belge notu değil) → filtre de onu arar. Satır notu olan hareket
        // (ör. kusurlu teslim açıklaması) doğrudan yazılır; aynı not B firmasında da var (gelmemeli).
        ekstraSirket("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at) " +
                     $"VALUES('hsf-n1','A','{ck}','{depoA}','adjustment',-1,'1','hsf-n1','Şantiyeye gönderildi',{Simdi});");
        ekstraSirket("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at) " +
                     $"VALUES('hsf-n2','B','{yabanci}','{depoB}','adjustment',-1,'1','hsf-n2','Şantiyeye',{Simdi});");

        GridResult<StockMovementRow> Ara(HareketSutunFiltresi sf, int boyut = 50)
            => st.SearchMovementsGrid(a, null, null, null, null, null, null, 1, boyut, sf);
        List<(string Kod, decimal Miktar, int Yon)> Satirlar(HareketSutunFiltresi sf)
            => Ara(sf).Items.Select(r => (r.Code, r.Quantity, r.Direction)).OrderBy(x => x.Code).ThenBy(x => x.Quantity).ToList();

        Assert.Equal(5, Ara(new HareketSutunFiltresi()).TotalCount);                                    // filtre yok: yalnız A'nın 5 hareketi
        Assert.All(Satirlar(new HareketSutunFiltresi(Kod: "yf")), x => Assert.Equal("YF-1", x.Kod));     // içerir, büyük/küçük duyarsız
        Assert.Equal(2, Satirlar(new HareketSutunFiltresi(Kod: "yf")).Count);                           // B'nin YF-9'u GELMEZ
        Assert.Equal(new[] { "ÇK-3", "ÇK-3" }, Satirlar(new HareketSutunFiltresi(Kod: "çk")).Select(x => x.Kod)); // Türkçe harf
        Assert.Equal(3, Satirlar(new HareketSutunFiltresi(Malzeme: "filtre")).Count);
        Assert.Equal(new[] { 4m }, Satirlar(new HareketSutunFiltresi(Miktar: "4")).Select(x => x.Miktar));       // tam sayı: 4 (14/40 değil)
        Assert.Equal(new[] { 10m, 25m }, Satirlar(new HareketSutunFiltresi(Miktar: ">5")).Select(x => x.Miktar).OrderBy(x => x));
        Assert.Equal(new[] { 5m, 10m }, Satirlar(new HareketSutunFiltresi(Miktar: "5-10")).Select(x => x.Miktar).OrderBy(x => x));
        // satır notu (ÇK-3) + 2026-10-29 kararıyla sütunda görünen BELGE açıklaması (YF-1 çıkışı); B'nin aynı notu gelmez
        Assert.Equal(new[] { "ÇK-3", "YF-1" }, Satirlar(new HareketSutunFiltresi(Aciklama: "şantiye")).Select(x => x.Kod));
        Assert.Equal(new[] { -1, -1 }, Satirlar(new HareketSutunFiltresi(Yon: -1)).Select(x => x.Yon));
        Assert.Equal(3, Satirlar(new HareketSutunFiltresi(Yon: 1)).Count);
        Assert.Equal(new[] { 10m }, Satirlar(new HareketSutunFiltresi(Kod: "YF", Yon: 1)).Select(x => x.Miktar));   // birlikte = VE
        var sayfa = Ara(new HareketSutunFiltresi(Malzeme: "filtre"), boyut: 1);                         // sayım ve sayfa aynı koşul
        Assert.Equal(3, sayfa.TotalCount);
        Assert.Single(sayfa.Items);
        Assert.Equal(3, sayfa.TotalPages);
        // mevcut süzgeçlerle birlikte: hareket türü (Tip) + tarih (gün)
        var cikislar = st.SearchMovementsGrid(a, null, null, null, null, new[] { "out" }, null, 1, 50, new HareketSutunFiltresi(Malzeme: "filtre"));
        Assert.Single(cikislar.Items);
        var gun = (Simdi - 3 * GunMs) / GunMs * GunMs;
        var gunluk = st.SearchMovementsGrid(a, gun, gun + GunMs - 1, null, null, null, null, 1, 50, new HareketSutunFiltresi(Malzeme: "filtre"));
        Assert.Equal(new[] { "HF-2" }, gunluk.Items.Select(r => r.Code));
    }
}
