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
/// GUI-02 (2026-08-13) — <b>GERÇEK MASAÜSTÜ GUI TESTİNDE BULUNAN AÇIK.</b>
///
/// Şube A oturumunda elle girilen "açılış" hareketi, Şube B oturumunda da görünüyordu.
///
/// <b>Kök neden:</b> elle hareket yolunda (<see cref="PartyLedgerService.Add"/>) şube HİÇ çözülmüyordu.
/// Ne masaüstü ne web <c>BranchId</c> gönderiyordu; <c>BranchAccess.Require(null)</c> serbest olduğu için
/// satır <c>branch_id = NULL</c> yazılıyordu. Okuma filtresi şubesiz satırları bilerek herkese gösterir
/// (<c>OR branch_id IS NULL</c>) → şubesiz hareket <b>her şubenin</b> ekstresine, bakiyesine ve raporuna
/// giriyordu. Fatura/tahsilat/ödeme <see cref="BranchAccess.Resolve"/> kullandığı için doğruydu;
/// yalnız elle giriş yolu bu kapıyı atlıyordu.
///
/// İkinci açık: <see cref="PartyLedgerService.Reverse"/> karşı kaydı <c>branch_id = NULL</c> yazıyor ve
/// aslın şubesi için kapsam KONTROLÜ YAPMIYORDU (yetkisiz şubenin hareketi iptal edilebiliyordu).
/// </summary>
public class PartyLedgerBranchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly SessionContext _admin;
    private readonly string _subeA, _subeB;
    private const string Co = "A";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public PartyLedgerBranchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_gui02_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(Co, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _subeA = branches.Create(_admin, new NewBranch("Sube A"));
        _subeB = branches.Create(_admin, new NewBranch("Sube B"));
    }

    /// <summary>Verilen şubede ÇALIŞAN, iki şubeye de yetkili kullanıcı (masaüstü oturumunun karşılığı).</summary>
    private SessionContext Oturum(string calismaSubesi) =>
        new("u1", Co, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(PartyService.Module, true, true, true, true),
        }))
        { ScopeBranchIds = new[] { _subeA, _subeB }, OperatingBranchId = calismaSubesi };

    private string Cari() => _parties.Create(_admin, new NewParty("C-001", "Test Cari", PartyTypes.Both));

    private string? SubeOku(string entryId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id FROM party_ledger WHERE id=@id;";
        cmd.AddWithValue("@id", entryId);
        return cmd.ExecuteScalar() as string;
    }

    // ═══════════════════════════════════════════════════════════════════
    // 1 — ELLE HAREKET ŞUBE TAŞIR
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>L1 — 🔴 ASIL HATA: elle açılış hareketi ÇALIŞMA ŞUBESİNE yazılır (NULL değil).</summary>
    [Fact]
    public void L1_Elle_Hareket_Calisma_Subesine_Yazilir()
    {
        var cari = Cari();

        var eid = _ledger.Add(Oturum(_subeA), new NewLedgerEntry(cari, PartyDocTypes.Opening, 1500m, true));

        Assert.Equal(_subeA, SubeOku(eid));   // eski davranış: null
    }

    /// <summary>L2 — 🔴 ASIL BELİRTİ: Şube A'da girilen hareket Şube B'nin bakiyesine GİRMEZ.</summary>
    [Fact]
    public void L2_A_Hareketi_B_Bakiyesine_Girmez()
    {
        var cari = Cari();
        _ledger.Add(Oturum(_subeA), new NewLedgerEntry(cari, PartyDocTypes.Opening, 1500m, true));

        var bakiyeB = _ledger.Balance(Oturum(_subeB), cari);
        var bakiyeA = _ledger.Balance(Oturum(_subeA), cari);

        Assert.Equal(0m, bakiyeB.Balance);     // eski davranış: 1500 (şubesiz satır her şubede görünüyordu)
        Assert.Equal(1500m, bakiyeA.Balance);
    }

    /// <summary>L3 — A + B birlikte istendiğinde TOPLAM gelir (birleşik görünüm bozulmadı).</summary>
    [Fact]
    public void L3_A_Arti_B_Toplanir()
    {
        var cari = Cari();
        _ledger.Add(Oturum(_subeA), new NewLedgerEntry(cari, PartyDocTypes.Opening, 1500m, true));
        _ledger.Add(Oturum(_subeB), new NewLedgerEntry(cari, PartyDocTypes.Opening, 700m, true));

        var toplam = _ledger.Balance(Oturum(_subeA), cari, new[] { _subeA, _subeB });

        Assert.Equal(2200m, toplam.Balance);
    }

    /// <summary>L4 — Açıkça verilen şube DOĞRULANIR ve mevcut yazma izolasyonu KORUNUR:
    /// <list type="bullet">
    ///   <item>Oturumun çalışma şubesi varsa BAŞKA şubeye yazılamaz (okuma çok şubeli, yazma TEK şube).</item>
    ///   <item>Çalışma şubesi yoksa kapsam içindeki açık şube kabul edilir.</item>
    ///   <item>Kapsam DIŞI şube her hâlükârda reddedilir.</item>
    /// </list></summary>
    [Fact]
    public void L4_Acik_Sube_Dogrulanir()
    {
        var cari = Cari();
        var branches = new BranchService(_factory, _clock);
        var subeC = branches.Create(_admin, new NewBranch("Sube C"));

        // Çalışma şubesi A iken B'ye yazmak: yazma tek şubedir → reddedilir.
        Assert.Throws<ForbiddenException>(() =>
            _ledger.Add(Oturum(_subeA), new NewLedgerEntry(cari, PartyDocTypes.Opening, 100m, true, BranchId: _subeB)));

        // Çalışma şubesi yokken kapsam içi açık şube kabul edilir.
        var calismaSubesiz = new SessionContext("u3", Co, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        { new ModulePermission(PartyService.Module, true, true, true, true) }))
        { ScopeBranchIds = new[] { _subeA, _subeB } };
        var eid = _ledger.Add(calismaSubesiz, new NewLedgerEntry(cari, PartyDocTypes.Opening, 100m, true, BranchId: _subeB));
        Assert.Equal(_subeB, SubeOku(eid));

        // Kapsam dışı şube (C) hiçbir koşulda kabul edilmez.
        Assert.Throws<ForbiddenException>(() =>
            _ledger.Add(calismaSubesiz, new NewLedgerEntry(cari, PartyDocTypes.Opening, 100m, true, BranchId: subeC)));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 2 — TERS KAYIT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>L5 — Ters kayıt ASLIN ŞUBESİNİ taşır (şubesiz kalıp her ekstrede görünmez).</summary>
    [Fact]
    public void L5_Ters_Kayit_Aslin_Subesini_Tasir()
    {
        var cari = Cari();
        var eid = _ledger.Add(Oturum(_subeA), new NewLedgerEntry(cari, PartyDocTypes.Opening, 1500m, true));

        var ters = _ledger.Reverse(Oturum(_subeA), eid, "GUI testi");

        Assert.Equal(_subeA, SubeOku(ters));   // eski davranış: null
    }

    /// <summary>L6 — Kapsam DIŞI şubenin hareketi ters kaydedilemez.</summary>
    [Fact]
    public void L6_Yetkisiz_Subenin_Hareketi_Ters_Kaydedilemez()
    {
        var cari = Cari();
        var eid = _ledger.Add(Oturum(_subeB), new NewLedgerEntry(cari, PartyDocTypes.Opening, 500m, true));

        var yalnizA = new SessionContext("u2", Co, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        { new ModulePermission(PartyService.Module, true, true, true, true) }))
        { ScopeBranchIds = new[] { _subeA } };

        Assert.Throws<ForbiddenException>(() => _ledger.Reverse(yalnizA, eid, "olmamalı"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
