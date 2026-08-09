using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Talep Raporu (2026-08-08 — ortak standarda taşındı) hesaplama + davranış doğruluğu. Her satır bir malzeme
/// talebidir (belge listesi). Senaryolar: normal talep, çok kalemli talep, kalemsiz talep, reddedilen/iptal
/// taleplerin LİSTEDE KALMASI, şube/talep eden/durum filtreleri, yetkisiz şube (fail-closed), tarih dışı hariç,
/// TotalRow (talep + kalem sayısı) ayrımı, NumCell HAM/görüntü, durum Türkçe etiketi, varsayılan sıralama.
/// Derived-table (correlated subquery yok) çıktısı test edilir. Kolon sırası:
/// 0 Şube · 1 Belge No · 2 Tarih · 3 Talep Eden · 4 Onaylayan · 5 Durum · 6 Kalem Sayısı · 7 Açıklama.
/// </summary>
public class RequestReportTests : IDisposable
{
    private const long Base = 1_700_000_000_000;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _mat;

    public RequestReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_reqrep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var clock = new TestClock();
        var users = new UserService(_factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = new MaterialService(_factory, clock).Create(_admin, new NewMaterial("MAT1", "Parça"));
        Seed();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Base);
    }

    private void Seed()
    {
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Base));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", Base));
        Pers("P1", "Ali Talep");
        Pers("P2", "Veli Talep");
        Pers("P3", "Onay Yetkilisi");

        // r1: B1, Ali, onaylayan P3, ONAYLI, 2 kalem, açıklamalı
        Req("r1", "TAL-001", "B1", "P1", "P3", "approved", "Santiye ihtiyaci", Base);
        Item("r1", "5"); Item("r1", "3");
        // r2: B1, Veli, onaysız, BEKLEMEDE, 1 kalem
        Req("r2", "TAL-002", "B1", "P2", null, "pending", null, Base);
        Item("r2", "10");
        // r3: B2, Ali, REDDEDİLDİ, KALEMSİZ (listede kalmalı)
        Req("r3", "TAL-003", "B2", "P1", "P3", "rejected", null, Base);
        // r4: B2, Veli, İPTAL, 1 kalem (listede kalmalı)
        Req("r4", "TAL-004", "B2", "P2", null, "cancelled", null, Base);
        Item("r4", "1");
        // r5: tarih DIŞI (uzak gelecek) → geçerli aralıkta elenir
        Req("r5", "TAL-005", "B1", "P1", null, "draft", null, Base + 500_000_000_000L);
        Item("r5", "99");
    }

    [Fact]
    public void Rapor_TemelYapi_TarihDisiHaric()
    {
        var t = Run();
        Assert.Equal("Talep Raporu", t.Title);
        Assert.Equal(8, t.Headers.Count);
        Assert.Equal(4, t.Rows.Count);       // r1..r4 (r5 tarih dışı)
        Assert.NotNull(t.Numeric);
        Assert.NotNull(t.TotalRow);
    }

    [Fact]
    public void NormalTalep_TumAlanlar_Dogru()
    {
        var r1 = Row(Run(), "TAL-001");
        Assert.Equal("Merkez", (string)r1[0]!);
        Assert.Equal("Ali Talep", (string)r1[3]!);
        Assert.Equal("Onay Yetkilisi", (string)r1[4]!);
        Assert.Equal("Onaylı", (string)r1[5]!);           // durum Türkçe etiket
        Assert.Equal(2.0, D(r1[6]), 3);                   // 2 kalem
        Assert.Equal("Santiye ihtiyaci", (string)r1[7]!); // açıklama
    }

    [Fact]
    public void CokKalemliTalep_KalemSayisi_SatirAdedi()
    {
        Assert.Equal(2.0, D(Row(Run(), "TAL-001")[6]), 3);   // miktar (5+3=8) DEĞİL, satır adedi 2
        Assert.Equal(1.0, D(Row(Run(), "TAL-002")[6]), 3);
    }

    [Fact]
    public void KalemsizTalep_GoruntudeTire_DegerSifir()
    {
        var r3 = Row(Run(), "TAL-003");
        Assert.Equal("-", Disp(r3[6]));
        Assert.Equal(0.0, D(r3[6]), 3);
    }

    [Fact]
    public void OnaylayanYok_BosMetin()
        => Assert.Equal("", (string)Row(Run(), "TAL-002")[4]!);

    [Fact]
    public void RedVeIptalTalepler_ListedeKalir()
    {
        var t = Run();
        Assert.Contains(t.Rows, r => (string)r[1]! == "TAL-003" && (string)r[5]! == "Reddedildi");
        Assert.Contains(t.Rows, r => (string)r[1]! == "TAL-004" && (string)r[5]! == "İptal");
    }

    [Fact]
    public void VarsayilanSiralama_SubeOnce_SonraTarihYeniden()
    {
        // Şube -> Tarih DESC; ilk satırlar Merkez (B1), sonra Sahra (B2).
        var t = Run();
        Assert.Equal("Merkez", (string)t.Rows[0][0]!);
        Assert.Equal("Sahra", (string)t.Rows[^1][0]!);
    }

    // ── Filtreler ──
    [Fact]
    public void DurumFiltresi_Coklu_YalnizSeciliDurumlar()
    {
        var t = _reports.Requests(_admin, Req(statuses: new[] { "approved", "pending" }));
        Assert.Equal(2, t.Rows.Count);
        Assert.All(t.Rows, r => Assert.Contains((string)r[5]!, new[] { "Onaylı", "Beklemede" }));
    }

    [Fact]
    public void DurumFiltresi_TekDurum_IptalleriGetirir()
    {
        var t = _reports.Requests(_admin, Req(statuses: new[] { "cancelled" }));
        Assert.Single(t.Rows);
        Assert.Equal("TAL-004", (string)t.Rows[0][1]!);
    }

    [Fact]
    public void TalepEdenFiltresi_YalnizSeciliKisi()
    {
        var t = _reports.Requests(_admin, Req(requesters: new[] { "P1" }));
        Assert.Equal(2, t.Rows.Count);                    // TAL-001, TAL-003
        Assert.All(t.Rows, r => Assert.Equal("Ali Talep", (string)r[3]!));
    }

    [Fact]
    public void SubeFiltresi_YetkiliAdmin_AcikSecim()
    {
        var t = _reports.Requests(_admin, Req(branches: new[] { "B1" }));
        Assert.Equal(2, t.Rows.Count);
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
    }

    [Fact]
    public void YetkisizKullanici_SubeDegistiremez_OturumSubesineDuser()
    {
        var set = new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>());
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, set) { OperatingBranchId = "B1" };
        var t = _reports.Requests(staff, Req(branches: new[] { "B2" }));   // B2 istese de B1'e kilitli
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
        Assert.DoesNotContain(t.Rows, r => (string)r[1]! == "TAL-003");
    }

    [Fact]
    public void CokluFiltre_BirlikteCalisir()
    {
        var t = _reports.Requests(_admin, Req(branches: new[] { "B2" }, statuses: new[] { "rejected" }));
        Assert.Single(t.Rows);
        Assert.Equal("TAL-003", (string)t.Rows[0][1]!);
    }

    // ── Toplam + NumCell ──
    [Fact]
    public void ToplamSatiri_TalepVeKalemSayisi_SatirlardaDegil()
    {
        var t = Run();
        Assert.DoesNotContain(t.Rows, r => ((string)r[0]!).StartsWith("TOPLAM"));
        var top = t.TotalRow!;
        Assert.StartsWith("TOPLAM", (string)top[0]!);
        Assert.Contains("4 talep", (string)top[0]!);      // 4 talep
        Assert.Equal(4.0, D(top[6]), 3);                  // kalem 2+1+0+1
        Assert.Equal("", Disp(top[7]));                   // diğer kolonlar boş
        Assert.Equal("", Disp(top[3]));
    }

    [Fact]
    public void Toplam_SatirlarinToplamiylaEsit_CiftSaymaz()
    {
        var t = Run();
        Assert.Equal(t.Rows.Sum(r => D(r[6])), D(t.TotalRow![6]), 3);
    }

    [Fact]
    public void NumCell_HamDeger_GoruntudenBagimsiz()
    {
        var r1 = Row(Run(), "TAL-001");
        Assert.IsType<NumCell>(r1[6]);
        var n = (NumCell)r1[6]!;
        Assert.Equal(2.0, n.Value, 3);
        Assert.Equal("2", n.Display);
    }

    [Fact]
    public void DurumSecenekleri_TekKaynak_BesDurum()
    {
        Assert.Equal(5, RequestStatusOptions.All.Count);
        Assert.Equal("Onaylı", RequestStatusOptions.Label("approved"));
        Assert.Equal("bilinmeyen", RequestStatusOptions.Label("bilinmeyen"));   // bilinmeyen olduğu gibi
    }

    // ── Yardımcılar ──
    private TableModel Run() => _reports.Requests(_admin, Req());

    private static ReportRequest Req(string[]? branches = null, string[]? requesters = null, string[]? statuses = null)
        => new(true, 1, 2_000_000_000_000L, branches, null, null, null, null, null, null, requesters, statuses);

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => System.Convert.ToDouble(v),
    };

    private static string Disp(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private static IReadOnlyList<object?> Row(TableModel t, string docNo) => t.Rows.First(r => (string)r[1]! == docNo);

    private void Pers(string id, string name)
        => Exec("INSERT INTO personnel(id,company_id,full_name,created_at,updated_at) VALUES(@id,'A',@fn,@n,@n);",
            ("@id", id), ("@fn", name), ("@n", Base));

    private void Req(string id, string docNo, string branch, string requester, string? approver, string status, string? desc, long date)
        => Exec(@"INSERT INTO material_requests(id,company_id,doc_no,request_date,branch_id,requester_id,approver_id,description,status,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@no,@d,@br,@rq,@ap,@desc,@st,@n,@n,1,0);",
            ("@id", id), ("@no", docNo), ("@d", date), ("@br", branch), ("@rq", requester),
            ("@ap", (object?)approver), ("@desc", (object?)desc), ("@st", status), ("@n", Base));

    private void Item(string requestId, string qty)
        => Exec("INSERT INTO material_request_items(id,company_id,request_id,material_id,quantity) VALUES(@id,'A',@r,@m,@q);",
            ("@id", requestId + "-it-" + Guid.NewGuid().ToString("N")[..6]), ("@r", requestId), ("@m", _mat), ("@q", qty));

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
