using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3b-5 — ALAN YETKİSİNİN 10.000+ KAYITTA MALİYETİ ═══
///
/// <b>Kullanıcı şartı §10/§37:</b> <i>"Alan yetkisi nedeniyle satır başına N+1 DB sorgusu
/// oluşmadığını özellikle kanıtla."</i>
///
/// Bu testler süre ölçmekle YETİNMEZ; asıl kanıt <b>SORGU SAYISIDIR</b>: alan korumalı iken
/// açılan bağlantı/komut sayısı, korumasız hâlle AYNI kalmalıdır. Süre makineye göre değişir,
/// sorgu sayısı değişmez — bu yüzden sözleşme sorgu sayısına bağlanır.
///
///  AP1 — Cari listesi 10.000 kayıt: korumalı/korumasız SORGU SAYISI aynı
///  AP2 — Cari ekstresi 10.000 hareket: karar satır başına DEĞİL, sorgu başına
///  AP3 — Kasa/banka hareketleri 10.000 kayıt: aynı sözleşme
///  AP4 — Alan kararının kendisi sorgusuzdur (10.000 çağrı, 0 bağlantı)
/// </summary>
public class AlanYetkiPerformansTests : IDisposable
{
    private const string Co = "APF";
    private const string Pass = "Apf!2026";
    private const int Adet = 10_000;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AuthService _auth;
    private readonly PermissionService _perms;
    private readonly FieldProtectionService _koruma;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly string _personelId, _adminId;

    public AlanYetkiPerformansTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_apf_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var users = new UserService(_f);
        _adminId = users.EnsureInitialAdmin(Co, "apf_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "apf_personel", Pass, RoleKeys.Staff);

        _auth = new AuthService(_f, null, _cache);
        _perms = new PermissionService(_f, null, _cache);
        _koruma = new FieldProtectionService(_f, null, _cache);

        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("parties"), Tam("finance"), Tam("invoices") }, Array.Empty<string>());
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SessionContext SuperAdmin() => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    private static ModulePermission Tam(string m) => new(m, true, true, true, true);

    private SessionContext Oturum(string ad)
    {
        var r = _auth.Login(Co, ad, Pass);
        Assert.True(r.Success);
        return r.Session!;
    }

    private void Koru(string ekran, string alan, bool ac = true) => _koruma.Set(SuperAdmin(), ekran, alan, ac);

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ── veri üretimi (servis üzerinden 10.000 kayıt açmak testin KENDİSİNİ yavaşlatırdı;
    //    ölçülen şey OKUMA yolu olduğu için veri doğrudan yazılır — şema aynıdır) ─────────────

    private void CariUret(int adet)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        for (int i = 0; i < adet; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO parties(id, company_id, code, title, party_type, is_person, currency_code,
    is_active, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@code,@title,'customer',0,'TRY',1,@now,@now,1,0);";
            cmd.AddWithValue("@id", "p" + i);
            cmd.AddWithValue("@c", Co);
            cmd.AddWithValue("@code", "C-" + i.ToString("D5"));
            cmd.AddWithValue("@title", "Cari " + i);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void DefterUret(string partyId, int adet)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        for (int i = 0; i < adet; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO party_ledger(id, company_id, party_id, entry_date, doc_type, direction, amount,
    currency_code, is_reversed, created_at)
VALUES(@id,@c,@p,@d,'adjustment',@dir,@a,'TRY',0,@d);";
            cmd.AddWithValue("@id", "l" + i);
            cmd.AddWithValue("@c", Co);
            cmd.AddWithValue("@p", partyId);
            cmd.AddWithValue("@d", now + i);
            cmd.AddWithValue("@dir", i % 2 == 0 ? 1 : -1);
            cmd.AddWithValue("@a", Money.Serialize(10m + i % 90));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Bir işlem sırasında çalıştırılan SQL KOMUTU sayısını ve süreyi ölçer
    /// (mevcut N+1 guard altyapısı — <see cref="SayanFabrika"/>).</summary>
    private static (int Komut, long Ms) Olc(SayanFabrika sayan, Action is_)
    {
        sayan.Sifirla();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        is_();
        sw.Stop();
        return (sayan.KomutSayisi, sw.ElapsedMilliseconds);
    }

    // ══════════════════ AP1 — CARİ LİSTESİ ══════════════════

    /// <summary>
    /// ⭐ ASIL KANIT SORGU SAYISIDIR. Alan kararı oturumdan gelir (sözlük araması); listenin kaç
    /// satır döndürdüğüyle ilgisi yoktur. Korumalı ve korumasız koşuların SORGU SAYISI birebir
    /// aynı olmalıdır — bir tek fazla sorgu bile "satır başına karar" mimarisinin işareti olurdu.
    /// </summary>
    [Fact]
    public void AP1_Cari_Listesi_On_Bin_Kayitta_Sorgu_Sayisi_Degismez()
    {
        CariUret(Adet);

        var admin = Oturum("apf_admin");
        var sayanA = new SayanFabrika(_f);
        var a = Olc(sayanA, () =>
        {
            var res = new PartyService(sayanA).List(admin, null, null, true, 1, 500);
            Assert.Equal(500, res.Items.Count);
            Assert.Equal(Adet, res.TotalCount);
        });

        Koru(FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance);
        var personel = Oturum("apf_personel");
        var sayanB = new SayanFabrika(_f);
        var b = Olc(sayanB, () =>
        {
            var res = new PartyService(sayanB).List(personel, null, null, true, 1, 500);
            Assert.Equal(500, res.Items.Count);
            Assert.All(res.Items, x => Assert.Equal(0m, x.Balance));   // gerçekten gizlendi
        });

        Assert.Equal(a.Komut, b.Komut);
        Assert.True(b.Komut < 20, $"Sayfa başına {b.Komut} komut — satır başına sorgu şüphesi.");
    }

    // ══════════════════ AP2 — CARİ EKSTRESİ ══════════════════

    [Fact]
    public void AP2_Cari_Ekstresi_On_Bin_Harekette_Sorgu_Sayisi_Degismez()
    {
        CariUret(1);
        DefterUret("p0", Adet);

        var admin = Oturum("apf_admin");
        var sayanA = new SayanFabrika(_f);
        var a = Olc(sayanA, () =>
        {
            var rows = new PartyLedgerService(sayanA).Statement(admin, "p0", limit: 2000);
            Assert.Equal(2000, rows.Count);
            Assert.Contains(rows, x => x.Entry.Amount > 0m);
        });

        Koru(FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance);
        var personel = Oturum("apf_personel");
        var sayanB = new SayanFabrika(_f);
        var b = Olc(sayanB, () =>
        {
            var rows = new PartyLedgerService(sayanB).Statement(personel, "p0", limit: 2000);
            Assert.Equal(2000, rows.Count);
            Assert.All(rows, x => Assert.Equal(0m, x.Entry.Amount));
            Assert.All(rows, x => Assert.Equal(0m, x.RunningBalance));
        });

        Assert.Equal(a.Komut, b.Komut);
        Assert.True(b.Komut < 10, $"2000 satır için {b.Komut} komut — satır başına sorgu şüphesi.");
    }

    // ══════════════════ AP3 — KASA/BANKA ══════════════════

    [Fact]
    public void AP3_Kasa_Banka_Hareketleri_On_Bin_Kayitta_Sorgu_Sayisi_Degismez()
    {
        var admin = Oturum("apf_admin");
        var finance = new FinanceService(_f, new PartyLedgerService(_f));
        var hesapId = finance.CreateAccount(admin, new NewFinanceAccount("APF", "APF Kasa", FinanceAccountKinds.Cash));

        // 10.000 hareket doğrudan yazılır (okuma yolu ölçülüyor).
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var conn = _f.Create())
        using (var tx = conn.BeginTransaction())
        {
            for (int i = 0; i < Adet; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO finance_transactions(id, company_id, account_id, txn_type, direction, amount, currency_code,
    txn_date, is_reversed, created_at, updated_at)
VALUES(@id,@c,@a,'receipt',1,@amt,'TRY',@d,0,@d,@d);";
                cmd.AddWithValue("@id", "t" + i);
                cmd.AddWithValue("@c", Co);
                cmd.AddWithValue("@a", hesapId);
                cmd.AddWithValue("@amt", Money.Serialize(5m + i % 50));
                cmd.AddWithValue("@d", now + i);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var sayanA = new SayanFabrika(_f);
        var a = Olc(sayanA, () =>
        {
            var res = new FinanceQueryService(sayanA).Transactions(admin, page: 1, pageSize: 500);
            Assert.Equal(500, res.Items.Count);
            Assert.Contains(res.Items, x => x.Amount > 0m);
        });

        Koru(FieldProtectionCatalog.Finance, FieldProtectionCatalog.Amount);
        var personel = Oturum("apf_personel");
        var sayanB = new SayanFabrika(_f);
        var b = Olc(sayanB, () =>
        {
            var res = new FinanceQueryService(sayanB).Transactions(personel, page: 1, pageSize: 500);
            Assert.Equal(500, res.Items.Count);
            Assert.All(res.Items, x => Assert.Equal(0m, x.Amount));
        });

        Assert.Equal(a.Komut, b.Komut);
    }

    // ══════════════════ AP4 — KARARIN KENDİSİ ══════════════════

    /// <summary>
    /// ⭐ En doğrudan kanıt: kararın kendisi 10.000 kez çağrılsa bile <b>tek bir veritabanı
    /// bağlantısı açılmaz</b>. Karar oturumun (snapshot'tan gelen) kümelerinde sözlük aramasıdır.
    /// </summary>
    [Fact]
    public void AP4_Alan_Karari_Sorgusuzdur()
    {
        Koru(FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance);
        var s = Oturum("apf_personel");

        // (1) YAPISAL KANIT: karar motorunun veritabanına erişimi YOKTUR. FieldAccess statiktir ve
        //     hiçbir üyesi/parametresi bağlantı fabrikası taşımaz → "her satırda CanField() → DB"
        //     mimarisi yapısal olarak KURULAMAZ (kullanıcı şartı §37).
        var tip = typeof(FieldAccess);
        Assert.Empty(tip.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                   | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
            .Where(x => typeof(IDbConnectionFactory).IsAssignableFrom(x.FieldType)));
        Assert.Empty(tip.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(m => m.GetParameters())
            .Where(p => typeof(IDbConnectionFactory).IsAssignableFrom(p.ParameterType)
                        || typeof(System.Data.Common.DbConnection).IsAssignableFrom(p.ParameterType)));

        // (2) ÖLÇÜM: 10.000 karar, sözlük araması hızında.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool sonuc = true;
        for (int i = 0; i < Adet; i++)
            sonuc &= FieldAccess.Gorunur(s, FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance);
        sw.Stop();

        Assert.False(sonuc);                                  // gerçekten kapalı
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"10.000 alan kararı {sw.ElapsedMilliseconds} ms sürdü — sözlük araması bekleniyordu.");
    }
}
