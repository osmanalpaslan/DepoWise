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
        Assert.Equal(new[] { "ÇK-3" }, Satirlar(new HareketSutunFiltresi(Aciklama: "şantiye")).Select(x => x.Kod));   // B'nin aynı notu gelmez
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
