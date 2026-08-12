using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-3c — GAP-6 (SENKRON ŞUBE İZOLASYONU) + GAP-7 (ŞUBE KAPSAMI YÖNETİMİ).
///
/// <b>GAP-6 neden kritikti:</b> <c>BuildSnapshot</c> yalnız <c>companyId</c> alıyordu → çevrimdışı
/// masaüstü cihazı, kullanıcının yetkisi olmayan ŞUBELERİN cari/fatura/kasa verisini de indiriyordu.
/// Push tarafında da satırın şubesi denetlenmiyordu → manipüle edilmiş <c>branch_id</c> ile yetkisiz
/// şubeye finansal kayıt yazılabilirdi.
///
/// <b>GAP-7 neden kritikti:</b> <c>user_scopes</c> yönetilebilir değildi; kapsam yalnız
/// <c>BranchService.AssignScope</c> ile eklenebiliyor, kaldırılamıyor, devir tavanı uygulanmıyordu.
///
/// <b>parties BİLİNÇLİ olarak süzülmez:</b> cari kartı firma genelinde tekildir. Kart süzülseydi,
/// izinli şubedeki HAREKET sahipsiz kalır ve yabancı anahtar/görünürlük bozulurdu.
/// </summary>
public class BranchScopeSyncGrantTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly InvoiceService _invoices;
    private readonly FinanceService _finance;
    private readonly BusinessSyncService _sync;
    private readonly PermissionService _perms;
    private readonly UserService _users;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce;
    private const string CoA = "A";

    public BranchScopeSyncGrantTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g43c_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, _stock, _ledger, _clock);
        _finance = new FinanceService(_factory, _ledger, _clock);
        _sync = new BusinessSyncService(_factory, _clock);
        _perms = new PermissionService(_factory, _clock);
        _users = new UserService(_factory, _clock);

        var id = _users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static SessionContext Kapsamli(string id, params string[] scope) =>
        new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(PartyService.Module, true, true, true, true),
            new ModulePermission(InvoiceService.Module, true, true, true, true),
            new ModulePermission(FinanceService.Module, true, true, true, true),
            new ModulePermission("stock", true, true, true, true),
            new ModulePermission("permissions", true, true, true, true),
        }))
        { ScopeBranchIds = scope.Length == 0 ? null : scope };

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));
    private string Cari(string code = "C-001") => _parties.Create(_admin, new NewParty(code, "Test Cari", PartyTypes.Both));
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private string Hesap(string code, string? branchId) =>
        _finance.CreateAccount(_admin, new NewFinanceAccount(code, code, FinanceAccountKinds.Cash, BranchId: branchId));

    /// <summary>İki şubede de veri üretir: hesap + fatura + cari hareketi.</summary>
    private (string AnkFatura, string DuzFatura) IkiSubedeVeri()
    {
        var m = Mat("M-1"); var c = Cari();
        var kAnk = Hesap("K-ANK", _ankara);
        var kDuz = Hesap("K-DUZ", _duzce);
        var fAnk = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _ankara));
        var fDuz = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 200m) }, Op(), BranchId: _duzce));
        _finance.Add(_admin, new NewFinanceEntry(kAnk, FinanceTxnTypes.Payment, 10m, Op(), PartyId: c, BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(kDuz, FinanceTxnTypes.Payment, 20m, Op(), PartyId: c, BranchId: _duzce));
        return (fAnk.Id, fDuz.Id);
    }

    /// <summary>Snapshot içindeki bir tablonun satır sayısı.</summary>
    private static int Satir(string json, string table)
    {
        using var doc = JsonDocument.Parse(json);
        var t = doc.RootElement.GetProperty("tables");
        return t.TryGetProperty(table, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.GetArrayLength() : 0;
    }

    /// <summary>Snapshot'ta verilen şubeye ait satır var mı?</summary>
    private static bool IcerirSube(string json, string table, string branchId)
    {
        using var doc = JsonDocument.Parse(json);
        var t = doc.RootElement.GetProperty("tables");
        if (!t.TryGetProperty(table, out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
        foreach (var row in arr.EnumerateArray())
            if (row.TryGetProperty("branch_id", out var b) && b.ValueKind == JsonValueKind.String
                && b.GetString() == branchId) return true;
        return false;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GAP-6 — PULL (sunucu → cihaz)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>S1 — Oturum VERİLMEZSE davranış eskisi gibi: her şey iner (geriye dönük uyum).</summary>
    [Fact]
    public void S1_Oturumsuz_Snapshot_Degismedi()
    {
        IkiSubedeVeri();
        var json = _sync.BuildSnapshot(CoA);
        Assert.Equal(2, Satir(json, "invoices"));
        Assert.True(IcerirSube(json, "invoices", _ankara));
        Assert.True(IcerirSube(json, "invoices", _duzce));
    }

    /// <summary>
    /// S2 — ⭐ PULL İZOLASYONU: DÜZCE kullanıcısının snapshot'ına ANKARA'nın faturası, kasası ve
    /// cari hareketi HİÇ GİRMEZ (yanıta bile).
    /// </summary>
    [Fact]
    public void S2_Pull_Yalniz_Izinli_Subeyi_Indirir()
    {
        IkiSubedeVeri();
        var json = _sync.BuildSnapshot(CoA, "dev", 0, Kapsamli("u1", _duzce));

        Assert.Equal(1, Satir(json, "invoices"));
        Assert.False(IcerirSube(json, "invoices", _ankara));
        Assert.True(IcerirSube(json, "invoices", _duzce));

        Assert.False(IcerirSube(json, "finance_accounts", _ankara));
        Assert.True(IcerirSube(json, "finance_accounts", _duzce));

        Assert.False(IcerirSube(json, "finance_transactions", _ankara));
        Assert.False(IcerirSube(json, "party_ledger", _ankara));
    }

    /// <summary>
    /// S3 — ⭐ ÇOCUK TABLOLAR EBEVEYNLE SÜZÜLÜR: <c>invoice_lines</c> kendi şube kolonu olmadığı
    /// için faturasının şubesine bakar → yetkisiz şubenin satırları inmez, FK sahipsiz kalmaz.
    /// </summary>
    [Fact]
    public void S3_Fatura_Satirlari_Ebeveynle_Suzulur()
    {
        IkiSubedeVeri();
        var admin = _sync.BuildSnapshot(CoA);
        var duzce = _sync.BuildSnapshot(CoA, "dev", 0, Kapsamli("u2", _duzce));

        Assert.Equal(2, Satir(admin, "invoice_lines"));
        Assert.Equal(1, Satir(duzce, "invoice_lines"));   // yalnız DÜZCE faturasının satırı
    }

    /// <summary>
    /// S4 — ⭐ CARİ KARTI SÜZÜLMEZ (bilinçli): kart firma genelinde tekildir; süzülseydi izinli
    /// şubedeki hareket sahipsiz kalırdı. İzolasyon HAREKETTE yapılır.
    /// </summary>
    [Fact]
    public void S4_Cari_Karti_Suzulmez_Hareket_Suzulur()
    {
        IkiSubedeVeri();
        var duzce = _sync.BuildSnapshot(CoA, "dev", 0, Kapsamli("u3", _duzce));

        Assert.Equal(1, Satir(duzce, "parties"));          // kart İNER (FK sahipsiz kalmasın)
        Assert.False(IcerirSube(duzce, "party_ledger", _ankara));   // hareket SÜZÜLÜR
        Assert.True(IcerirSube(duzce, "party_ledger", _duzce));
    }

    /// <summary>S5 — Ortak katalog tabloları (malzeme, KDV, seri) süzülmez — şubesizdirler.</summary>
    [Fact]
    public void S5_Ortak_Kataloglar_Suzulmez()
    {
        IkiSubedeVeri();
        var duzce = _sync.BuildSnapshot(CoA, "dev", 0, Kapsamli("u4", _duzce));
        Assert.True(Satir(duzce, "materials") > 0);
        Assert.False(BusinessSyncService.IsBranchScoped("materials"));
        Assert.False(BusinessSyncService.IsBranchScoped("parties"));
        Assert.False(BusinessSyncService.IsBranchScoped("vat_rates"));
        Assert.False(BusinessSyncService.IsBranchScoped("invoice_series"));
    }

    /// <summary>S6 — Kapsamsız kullanıcı (fallback) her şeyi indirir — mevcut davranış korunur.</summary>
    [Fact]
    public void S6_Kapsamsiz_Kullanici_Hepsini_Indirir()
    {
        IkiSubedeVeri();
        var json = _sync.BuildSnapshot(CoA, "dev", 0, Kapsamli("u5"));
        Assert.Equal(2, Satir(json, "invoices"));
    }

    /// <summary>S7 — FK sırası BOZULMADI: hesap tanımı hareketten, fatura satırdan önce gider.</summary>
    [Fact]
    public void S7_FK_Sirasi_Korundu()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("parties") < t.IndexOf("party_ledger"));
        Assert.True(t.IndexOf("finance_accounts") < t.IndexOf("finance_transactions"));
        Assert.True(t.IndexOf("invoices") < t.IndexOf("invoice_lines"));
        Assert.True(t.IndexOf("invoices") < t.IndexOf("invoice_allocations"));
        Assert.True(t.IndexOf("finance_transactions") < t.IndexOf("invoice_allocations"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GAP-6 — PUSH (cihaz → sunucu)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S8 — ⭐ PUSH KAPISI: cihaz manipüle edilmiş <c>branch_id</c> ile YETKİSİZ şubeye kasa hesabı
    /// gönderirse UYGULANMAZ; kendi şubesininki uygulanır (kısmi/yetkisiz yazım yok).
    /// </summary>
    [Fact]
    public void S8_Push_Yetkisiz_Sube_Reddedilir()
    {
        var now = 1_700_000_000_000L;
        var payload = JsonDocument.Parse($$"""
        {
          "companyId": "{{CoA}}",
          "tables": {
            "finance_accounts": [
              { "id":"acc-duz", "company_id":"{{CoA}}", "code":"PUSH-DUZ", "name":"DUZ", "account_kind":"cash",
                "currency_code":"TRY", "branch_id":"{{_duzce}}", "is_default":0, "is_active":1,
                "created_at":{{now}}, "updated_at":{{now}}, "version":1, "is_deleted":0 },
              { "id":"acc-ank", "company_id":"{{CoA}}", "code":"PUSH-ANK", "name":"ANK", "account_kind":"cash",
                "currency_code":"TRY", "branch_id":"{{_ankara}}", "is_default":0, "is_active":1,
                "created_at":{{now}}, "updated_at":{{now}}, "version":1, "is_deleted":0 }
            ]
          }
        }
        """).RootElement;

        var res = _sync.Apply(Kapsamli("u6", _duzce), payload);

        Assert.Equal(1, res.Upserted);                 // yalnız DÜZCE satırı
        Assert.Contains(res.Errors, e => e.Contains("kapsam dışı"));

        var kodlar = new FinanceQueryService(_factory).Accounts(_admin).Select(x => x.Account.Code).ToList();
        Assert.Contains("PUSH-DUZ", kodlar);
        Assert.DoesNotContain("PUSH-ANK", kodlar);     // ⭐ yetkisiz şubeye YAZILMADI
    }

    /// <summary>S9 — Şubesiz (firma geneli) satır push'ta kabul edilir — eski/şubesiz veri engellenmez.</summary>
    [Fact]
    public void S9_Subesiz_Satir_Kabul_Edilir()
    {
        var now = 1_700_000_000_000L;
        var payload = JsonDocument.Parse($$"""
        {
          "companyId": "{{CoA}}",
          "tables": {
            "finance_accounts": [
              { "id":"acc-genel", "company_id":"{{CoA}}", "code":"PUSH-GEN", "name":"GEN", "account_kind":"cash",
                "currency_code":"TRY", "branch_id":null, "is_default":0, "is_active":1,
                "created_at":{{now}}, "updated_at":{{now}}, "version":1, "is_deleted":0 }
            ]
          }
        }
        """).RootElement;

        var res = _sync.Apply(Kapsamli("u7", _duzce), payload);
        Assert.Equal(1, res.Upserted);
    }

    /// <summary>S10 — Aynı push iki kez uygulanınca MÜKERRER kayıt oluşmaz (idempotency korundu).</summary>
    [Fact]
    public void S10_Tekrar_Push_Mukerrer_Olusturmaz()
    {
        var now = 1_700_000_000_000L;
        var json = $$"""
        {
          "companyId": "{{CoA}}",
          "tables": {
            "finance_accounts": [
              { "id":"acc-x", "company_id":"{{CoA}}", "code":"PUSH-X", "name":"X", "account_kind":"cash",
                "currency_code":"TRY", "branch_id":"{{_duzce}}", "is_default":0, "is_active":1,
                "created_at":{{now}}, "updated_at":{{now}}, "version":1, "is_deleted":0 }
            ]
          }
        }
        """;
        var u = Kapsamli("u8", _duzce);
        _sync.Apply(u, JsonDocument.Parse(json).RootElement);
        _sync.Apply(u, JsonDocument.Parse(json).RootElement);

        Assert.Single(new FinanceQueryService(_factory).Accounts(_admin).Where(x => x.Account.Code == "PUSH-X"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GAP-7 — ŞUBE KAPSAMI YÖNETİMİ
    // ═════════════════════════════════════════════════════════════════════════

    private string YeniKullanici(string username)
        => _users.CreateUser(_admin, new NewUser(username, "Test!2026", username, new[] { RoleKeys.Staff }, CoA));

    /// <summary>P1 — Kapsam yazılır ve okunur; kip "explicit" olur.</summary>
    [Fact]
    public void P1_Kapsam_Yazilir_Okunur()
    {
        var u = YeniKullanici("p1");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce });

        var v = _perms.GetBranchScope(_admin, u);
        Assert.Equal("explicit", v.Mode);
        Assert.Equal("Seçili şubeler", v.ModeText);
        Assert.Equal(new[] { _duzce }, v.ScopeBranchIds);
    }

    /// <summary>P2 — Boş liste kapsamı KALDIRIR (kullanıcı own/all davranışına döner).</summary>
    [Fact]
    public void P2_Bos_Liste_Kapsami_Kaldirir()
    {
        var u = YeniKullanici("p2");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce, _ankara });
        Assert.Equal(2, _perms.GetBranchScope(_admin, u).ScopeBranchIds.Count);

        _perms.SaveBranchScope(_admin, u, Array.Empty<string>());
        var v = _perms.GetBranchScope(_admin, u);
        Assert.Empty(v.ScopeBranchIds);
        Assert.NotEqual("explicit", v.Mode);
    }

    /// <summary>
    /// P3 — ⭐ DEVİR TAVANI: DÜZCE kapsamlı yetkili, ANKARA'yı BAŞKASINA VEREMEZ.
    /// Sessizce kırpılmaz — hata verir.
    /// </summary>
    [Fact]
    public void P3_Sahip_Olmadigi_Subeyi_Devredemez()
    {
        var hedef = YeniKullanici("p3");
        var duzceliYetkili = Kapsamli("mgr", _duzce);

        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(duzceliYetkili, hedef, new[] { _ankara }));
        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(duzceliYetkili, hedef, new[] { _duzce, _ankara }));

        // Kendi kapsamını verebilir.
        _perms.SaveBranchScope(duzceliYetkili, hedef, new[] { _duzce });
        Assert.Equal(new[] { _duzce }, _perms.GetBranchScope(_admin, hedef).ScopeBranchIds);
    }

    /// <summary>P4 — Atanabilir liste AKTÖRÜN kapsamıyla kırpılır (UI yetkisiz şubeyi göremez).</summary>
    [Fact]
    public void P4_Atanabilir_Liste_Kirpilir()
    {
        var hedef = YeniKullanici("p4");

        Assert.Equal(2, _perms.GetBranchScope(_admin, hedef).AssignableBranches.Count);              // admin: ikisi
        Assert.Single(_perms.GetBranchScope(Kapsamli("mgr2", _duzce), hedef).AssignableBranches);    // yalnız DÜZCE
    }

    /// <summary>P5 — Kullanıcı KENDİ kapsamını değiştiremez (kendini genişletme yolu kapalı).</summary>
    [Fact]
    public void P5_Kendi_Kapsamini_Degistiremez()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _perms.SaveBranchScope(_admin, _admin.UserId, new[] { _duzce }));
    }

    /// <summary>P6 — FİRMA İZOLASYONU: başka firmanın şubesi kapsam olarak verilemez.</summary>
    [Fact]
    public void P6_Firma_Izolasyonu()
    {
        var u = YeniKullanici("p6");
        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(_admin, u, new[] { "yabanci-sube-id" }));
        Assert.Empty(_perms.GetBranchScope(_admin, u).ScopeBranchIds);   // kısmi yazım YOK
    }

    /// <summary>P7 — Yetkisiz kullanıcı kapsam okuyamaz/yazamaz.</summary>
    [Fact]
    public void P7_Yetkisiz_Kapsam_Yonetemez()
    {
        var u = YeniKullanici("p7");
        var yetkisiz = new SessionContext("nobody", CoA, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _perms.GetBranchScope(yetkisiz, u));
        Assert.Throws<ForbiddenException>(() => _perms.SaveBranchScope(yetkisiz, u, new[] { _duzce }));
    }

    /// <summary>P8 — Kapsam yazımı AUDIT bırakır (izlenebilirlik).</summary>
    [Fact]
    public void P8_Audit_Olusur()
    {
        var u = YeniKullanici("p8");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce });

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_type='user_scopes' AND entity_id=@u;";
        cmd.AddWithValue("@c", CoA); cmd.AddWithValue("@u", u);
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) > 0);
    }

    /// <summary>
    /// P9 — ⭐ UÇTAN UCA: kapsam yazıldıktan sonra o kullanıcının OTURUMU gerçekten kısıtlanır
    /// (yetki fotoğrafı tazelenir) — kapsam yalnız kayıtta kalmaz, erişimi değiştirir.
    /// </summary>
    [Fact]
    public void P9_Kapsam_Oturumu_Gercekten_Kisitlar()
    {
        var u = YeniKullanici("p9");
        _perms.SaveBranchScope(_admin, u, new[] { _duzce });

        var auth = new AuthService(_factory, _clock);
        var oturum = auth.CreateSessionForUser(CoA, u);
        Assert.NotNull(oturum);
        Assert.Equal(new[] { _duzce }, BranchAccess.Allowed(oturum!));
        Assert.False(BranchAccess.CanAccess(oturum!, _ankara));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
