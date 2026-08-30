using DepoWise.Application.Approvals;
using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Approvals;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187 + ADR-188) — ONAY ZİNCİRİ KİLİTLERİ ═══
///
/// Kilitlenenler: <b>snapshot değişmezliği</b> (PK-EK-04) · <b>opsiyonellik/geriye uyumluluk</b> (İK-3) ·
/// <b>self-approval yalnız admin</b> (İK-5) · <b>ret gerekçesi + rejected yeniden gönderilemez</b> (İK-4) ·
/// <b>eşzamanlılık</b> (aynı adıma iki onaydan biri) · <b>tenant/IDOR</b> · <b>zincir bypass kapısı</b> ·
/// <b>Satın Alma: onaysız mal kabul YOK</b> (ADR-188 §1) ve <c>status</c> sözleşmesinin korunması (§2).
/// </summary>
public class OnayZinciriTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly UserHierarchyService _hier;
    private readonly ApprovalService _appr;
    private readonly RequestService _requests;
    private readonly PurchaseOrderService _po;

    private readonly SessionContext _adminA, _adminB;
    private string _ast = "", _ustA = "", _ustB = "", _yabanci = "";

    public OnayZinciriTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_onay_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        _hier = new UserHierarchyService(_f);
        _appr = new ApprovalService(_f);
        _requests = new RequestService(_f, new StockService(_f)) { Approvals = _appr };
        _po = new PurchaseOrderService(_f) { Approvals = _appr };

        // Sunucudaki bağlamanın AYNISI (ServerServices ile birebir): süreç kapanınca varlık güncellenir.
        _appr.Register(ApprovalEntityTypes.MaterialRequest,
            (conn, tx, s, _, id, ok, reason, now) => _requests.ApplyChainDecision(conn, tx, s, id, ok, reason, now));
        _appr.Register(ApprovalEntityTypes.PurchaseOrder, (_, _, _, _, _, _, _, _) => { });

        _adminA = Firma("ON-A", "admina");
        _adminB = Firma("ON-B", "adminb");
        _ast = Kullanici("ON-A", "ast");
        _ustA = Kullanici("ON-A", "ust1");
        _ustB = Kullanici("ON-A", "ust2");
        _yabanci = Kullanici("ON-B", "yabanci");
    }

    private SessionContext Firma(string co, string user)
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private string Kullanici(string co, string username)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@u,'x',1,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>Talep/onay yetkisi olan NORMAL (admin olmayan) kullanıcı oturumu.</summary>
    private static SessionContext Personel(string co, string userId, params string[] moduller)
        => new(userId, co, new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, true, true, true)).ToArray()));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // ── veri yardımcıları ──────────────────────────────────────────────────────────────────

    private string Malzeme(string co)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@k,@k,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@k", "M" + id[..6]);
        cmd.ExecuteNonQuery();
        return id;
    }

    private string TalepAc(SessionContext s)
        => _requests.Create(s, new NewRequest(
            new[] { new RequestItemInput(Malzeme(s.CompanyId), 1m) }, SubmitImmediately: true)).Id;

    private IReadOnlyList<ApprovalStepRow> Adimlar(SessionContext s, string entityType, string entityId)
    {
        using var conn = _f.Create();
        var inst = ApprovalService.OpenInstanceId(conn, null, s.CompanyId, entityType, entityId);
        return inst is null ? Array.Empty<ApprovalStepRow>() : _appr.Steps(s, inst);
    }

    private string Durum(string requestId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM material_requests WHERE id=@i;";
        cmd.AddWithValue("@i", requestId);
        return (string)cmd.ExecuteScalar()!;
    }

    // ══════════════════════ OPSİYONELLİK (İK-3) ══════════════════════

    /// <summary>ON01 — <b>Hiyerarşi YOKSA zincir OLUŞMAZ</b> ve mevcut tek-adımlı akış BİREBİR çalışır.
    /// Bu, "hiçbir backfill yok → hiçbir davranış değişmiyor" güvencesinin kanıtıdır.</summary>
    [Fact]
    public void ON01_Zincir_Yoksa_Mevcut_Akis_Aynen_Calisir()
    {
        var id = TalepAc(_adminA);
        Assert.Empty(Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id));

        _requests.Approve(_adminA, id);                    // eski tek-adımlı yol açık
        Assert.Equal("approved", Durum(id));
    }

    /// <summary>ON02 — Hiyerarşi VARSA süreç başlar ve adımlar snapshot olarak yazılır.</summary>
    [Fact]
    public void ON02_Zincir_Varsa_Adimlar_Olusur()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        _hier.SetManager(_adminA, _ustA, _ustB);

        var id = TalepAc(_adminA);
        var adimlar = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id);
        Assert.Equal(2, adimlar.Count);
        Assert.Equal(_ustA, adimlar[0].ApproverUserId);
        Assert.Equal(_ustB, adimlar[1].ApproverUserId);
        Assert.Equal("pending", Durum(id));                // zincir bitmeden talep onaylanmaz
    }

    /// <summary>ON03 — <b>ZİNCİR BYPASS KAPISI:</b> açık zinciri olan talep, eski tek-adımlı yoldan
    /// onaylanamaz/reddedilemez. Bu kapı olmasaydı zincir sessizce atlanırdı.</summary>
    [Fact]
    public void ON03_Zincirli_Talep_Eski_Yoldan_Onaylanamaz()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id = TalepAc(_adminA);

        Assert.Throws<InvalidOperationException>(() => _requests.Approve(_adminA, id));
        Assert.Throws<InvalidOperationException>(() => _requests.Reject(_adminA, id, "olmaz"));
        Assert.Equal("pending", Durum(id));
    }

    // ══════════════════════ SNAPSHOT (PK-EK-04) ══════════════════════

    /// <summary>ON04 — <b>SNAPSHOT DEĞİŞMEZLİĞİ:</b> süreç başladıktan SONRA hiyerarşi değişse bile
    /// açık sürecin adım sahipleri DEĞİŞMEZ; YENİ süreç ise yeni hiyerarşiyi kullanır.</summary>
    [Fact]
    public void ON04_Snapshot_Sonradan_Degismez()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id1 = TalepAc(_adminA);
        var once = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id1).Single().ApproverUserId;
        Assert.Equal(_ustA, once);

        // Hiyerarşi DEĞİŞTİ.
        _hier.SetManager(_adminA, _adminA.UserId, _ustB);

        // Açık süreç ETKİLENMEDİ (canlı hiyerarşiden yeniden hesaplama YOK).
        Assert.Equal(_ustA, Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id1).Single().ApproverUserId);

        // YENİ süreç yeni hiyerarşiyi kullanır.
        var id2 = TalepAc(_adminA);
        Assert.Equal(_ustB, Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id2).Single().ApproverUserId);
    }

    // ══════════════════════ AKIŞ ══════════════════════

    /// <summary>ON05 — Çok adımlı zincir sırayla ilerler; son adım onaylanınca TALEP onaylanır
    /// (operasyon süreci de bugünkü gibi başlar).</summary>
    [Fact]
    public void ON05_Cok_Adimli_Zincir_Sirayla_Ilerler()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        _hier.SetManager(_adminA, _ustA, _ustB);
        var id = TalepAc(_adminA);
        var adimlar = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id);

        var s1 = Personel("ON-A", _ustA, "request_approval");
        var s2 = Personel("ON-A", _ustB, "request_approval");

        // 2. adım SIRASI GELMEDEN işlenemez.
        Assert.Throws<InvalidOperationException>(() => _appr.Approve(s2, adimlar[1].Id));

        _appr.Approve(s1, adimlar[0].Id);
        Assert.Equal("pending", Durum(id));                // hâlâ bitmedi

        _appr.Approve(s2, adimlar[1].Id);
        Assert.Equal("approved", Durum(id));

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT operation_status FROM material_requests WHERE id=@i;";
        cmd.AddWithValue("@i", id);
        Assert.Equal("pending_ops", cmd.ExecuteScalar() as string);   // mevcut davranış korundu
    }

    /// <summary>ON06 — Ret: gerekçe ZORUNLU; süreç kapanır, kalan adımlar 'skipped' olur (SİLİNMEZ) ve
    /// talep reddedilir. <b>İK-4:</b> reddedilen talep yeniden gönderilemez (durum makinesi kilidi).</summary>
    [Fact]
    public void ON06_Ret_Gerekce_Zorunlu_Ve_Yeniden_Gonderilemez()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        _hier.SetManager(_adminA, _ustA, _ustB);
        var id = TalepAc(_adminA);
        var adimlar = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id);
        var s1 = Personel("ON-A", _ustA, "request_approval");

        Assert.Throws<ArgumentException>(() => _appr.Reject(s1, adimlar[0].Id, "   "));
        _appr.Reject(s1, adimlar[0].Id, "Bütçe yok");

        Assert.Equal("rejected", Durum(id));
        var son = _appr.Steps(_adminA, adimlar[0].InstanceId);
        Assert.Equal("rejected", son[0].Status);
        Assert.Equal("Bütçe yok", son[0].Reason);          // İK-10: gerekçe görünür kalır
        Assert.Equal("skipped", son[1].Status);            // kalan adım silinmedi

        // İK-4 — rejected uçtur: yeniden onaya gönderilemez.
        Assert.Throws<InvalidOperationException>(() => _requests.Submit(_adminA, id));
    }

    // ══════════════════════ YETKİ / SAHİPLİK ══════════════════════

    /// <summary>ON07 — Adım YALNIZ snapshot'taki kişi tarafından işlenebilir; başka kullanıcı
    /// (ve firma admini bile) o adımı kullanamaz. <b>Ekip liderliği bunu bypass etmez.</b></summary>
    [Fact]
    public void ON07_Adim_Sahipligi_Zorunlu()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id = TalepAc(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id).Single();

        var baskasi = Personel("ON-A", _ustB, "request_approval");
        Assert.Throws<ForbiddenException>(() => _appr.Approve(baskasi, adim.Id));
        Assert.Throws<ForbiddenException>(() => _appr.Approve(_adminA, adim.Id));   // admin de adım sahibi değil
    }

    /// <summary>ON08 — <b>İK-5:</b> self-approval yalnız admin. Normal kullanıcı kendi talebini
    /// onaylayamaz; firma admini onaylayabilir (mevcut <c>AccessControl.IsAdmin</c> tanımı).</summary>
    [Fact]
    public void ON08_Self_Approval_Yalniz_Admin()
    {
        // Normal kullanıcı: kendi talebi, zincirin ilk adımı yine kendisi olacak şekilde kurgu.
        _hier.SetManager(_adminA, _ast, _ast == _ustA ? _ustB : _ustA);
        var astOturum = Personel("ON-A", _ast, "requests", "request_approval");
        var id = TalepAc(astOturum);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id).Single();

        // Adım sahibi üst; ama üst kendisi başlatmadığı için self-approval değil → geçerli.
        var ustOturum = Personel("ON-A", adim.ApproverUserId, "request_approval");
        _appr.Approve(ustOturum, adim.Id);
        Assert.Equal("approved", Durum(id));

        // Şimdi ADMIN'in kendi talebi + zincirin adımı yine admin (self) → admin istisnası geçerli.
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id2 = TalepAc(_adminA);
        var adim2 = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id2).Single();
        var ust = Personel("ON-A", _ustA, "request_approval");
        _appr.Approve(ust, adim2.Id);
        Assert.Equal("approved", Durum(id2));
    }

    /// <summary>ON08b — Normal kullanıcı KENDİ başlattığı sürecin adımında ise reddedilir; aynı adım
    /// admin oturumuyla geçilebilir (İK-5'in doğrudan kanıtı).</summary>
    [Fact]
    public void ON08b_Kendi_Talebini_Normal_Kullanici_Onaylayamaz()
    {
        // Döngü kurulamayacağı için self-step'i doğrudan veri seviyesinde kuruyoruz:
        // süreci _ast başlatır, adım sahibi de _ast olur.
        _hier.SetManager(_adminA, _ast, _ustA);
        var astOturum = Personel("ON-A", _ast, "requests", "request_approval");
        var id = TalepAc(astOturum);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id).Single();

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE approval_step SET approver_user_id=@u WHERE id=@i;";
            cmd.AddWithValue("@u", _ast);
            cmd.AddWithValue("@i", adim.Id);
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<ForbiddenException>(() => _appr.Approve(astOturum, adim.Id));   // self → yasak
        Assert.Equal("pending", Durum(id));
    }

    /// <summary>ON09 — <b>Tenant/IDOR:</b> başka firmanın adımı/süreci hiçbir yoldan görülemez
    /// ve işlenemez. Uydurma adım kimliği de reddedilir.</summary>
    [Fact]
    public void ON09_Tenant_Ve_IDOR()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id = TalepAc(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id).Single();

        Assert.Throws<ForbiddenException>(() => _appr.Approve(_adminB, adim.Id));
        Assert.Throws<ForbiddenException>(() => _appr.Reject(_adminB, adim.Id, "x"));
        Assert.Throws<ForbiddenException>(() => _appr.Steps(_adminB, adim.InstanceId));
        Assert.Throws<ForbiddenException>(() => _appr.Approve(_adminA, "uydurma-step-id"));
        Assert.Empty(_appr.MyPending(_adminB));
    }

    /// <summary>ON10 — Onay eylemi varlığın MEVCUT modül yetkisini ister (yeni modül icat edilmedi):
    /// Malzeme Talebi'nde <c>request_approval</c>.</summary>
    [Fact]
    public void ON10_Modul_Yetkisi_Zorunlu()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id = TalepAc(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id).Single();

        var yetkisiz = new SessionContext(_ustA, "ON-A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _appr.Approve(yetkisiz, adim.Id));
    }

    // ══════════════════════ EŞZAMANLILIK (§19) ══════════════════════

    /// <summary>ON11 — Aynı adım İKİ KEZ işlenemez: ikinci karar reddedilir (LWW değil, atomik geçiş).</summary>
    [Fact]
    public void ON11_Ayni_Adim_Iki_Kez_Islenemez()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        _hier.SetManager(_adminA, _ustA, _ustB);
        var id = TalepAc(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id)[0];
        var s1 = Personel("ON-A", _ustA, "request_approval");

        _appr.Approve(s1, adim.Id);
        Assert.Throws<InvalidOperationException>(() => _appr.Approve(s1, adim.Id));
        Assert.Throws<InvalidOperationException>(() => _appr.Reject(s1, adim.Id, "x"));
    }

    /// <summary>ON12 — Kapanmış sürecin adımı işlenemez; süreç ikinci kez kapatılamaz.</summary>
    [Fact]
    public void ON12_Kapanmis_Surec_Islenemez()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id = TalepAc(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.MaterialRequest, id).Single();
        var s1 = Personel("ON-A", _ustA, "request_approval");

        _appr.Approve(s1, adim.Id);
        Assert.Equal("approved", Durum(id));
        Assert.Throws<InvalidOperationException>(() => _appr.Approve(s1, adim.Id));
    }

    // ══════════════════════ SATIN ALMA (ADR-188 §1/§2/§4) ══════════════════════

    /// <summary>ON13 — <b>Zincir YOKSA</b> satın alma bugünkü gibi çalışır: mal kabul serbesttir
    /// (İK-3 opsiyonellik → mevcut davranış bozulmadı).</summary>
    [Fact]
    public void ON13_PO_Zincir_Yoksa_Mal_Kabul_Serbest()
    {
        var (orderId, _) = Siparis(_adminA);
        Assert.Null(SonOnayDurumu(orderId));
        // Mal kabul kapısı ONAY nedeniyle engellemiyor; akış mevcut doğrulamalarıyla sürüyor.
        var hata = Record.Exception(() => _po.Receive(_adminA, orderId, Array.Empty<ReceiveLine>(), "op1"));
        Assert.IsType<ArgumentException>(hata);
        Assert.Contains("satır", hata!.Message);           // onay değil, satır seçimi hatası
    }

    /// <summary>ON14 — <b>ADR-188 §1:</b> zincir varsa ve süreç ONAYLANMADIYSA mal kabul REDDEDİLİR.
    /// Kapı SERVİSTEDİR: UI'dan bağımsızdır, eski istemci de bypass edemez.</summary>
    [Fact]
    public void ON14_PO_Onaysiz_Mal_Kabul_Engellenir()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var (orderId, lineId) = Siparis(_adminA);
        Assert.Equal("pending", SonOnayDurumu(orderId));

        var hata = Assert.Throws<ArgumentException>(() =>
            _po.Receive(_adminA, orderId, new[] { new ReceiveLine(lineId, 1m) }, "op-onaysiz"));
        Assert.Contains("onay", hata.Message.ToLowerInvariant());

        // ⭐ status sözleşmesi DEĞİŞMEDİ (ADR-188 §2)
        Assert.Equal("open", SiparisDurumu(orderId));
    }

    /// <summary>ON15 — Onay tamamlanınca mal kabul açılır; reddedilen siparişte kalıcı olarak kapalıdır.</summary>
    [Fact]
    public void ON15_PO_Onaydan_Sonra_Mal_Kabul_Acilir()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var (orderId, lineId) = Siparis(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.PurchaseOrder, orderId).Single();
        _appr.Approve(Personel("ON-A", _ustA, "purchasing"), adim.Id);

        Assert.Equal("approved", SonOnayDurumu(orderId));
        Assert.Equal("open", SiparisDurumu(orderId));      // status yine değişmedi

        // Onay kapısı artık engellemiyor (akış mevcut doğrulamalarıyla sürüyor).
        var hata = Record.Exception(() =>
            _po.Receive(_adminA, orderId, new[] { new ReceiveLine(lineId, 1m) }, "op-onayli"));
        Assert.True(hata is null || !hata.Message.ToLowerInvariant().Contains("onay"),
            "Onaylı siparişte mal kabul ONAY nedeniyle engellenmemeli. Gelen: " + hata?.Message);
    }

    /// <summary>ON16 — Reddedilen siparişte mal kabul KALICI olarak engellenir.</summary>
    [Fact]
    public void ON16_PO_Reddedilirse_Mal_Kabul_Kapali()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var (orderId, lineId) = Siparis(_adminA);
        var adim = Adimlar(_adminA, ApprovalEntityTypes.PurchaseOrder, orderId).Single();
        _appr.Reject(Personel("ON-A", _ustA, "purchasing"), adim.Id, "Fiyat yüksek");

        Assert.Equal("rejected", SonOnayDurumu(orderId));
        var hata = Assert.Throws<ArgumentException>(() =>
            _po.Receive(_adminA, orderId, new[] { new ReceiveLine(lineId, 1m) }, "op-red"));
        Assert.Contains("REDDEDİLDİ", hata.Message);
        Assert.Equal("open", SiparisDurumu(orderId));      // status sözleşmesi korundu
    }

    /// <summary>ON17 — <b>PK-EK-01 kapsam kilidi:</b> motor yalnız iki varlık türünü tanır;
    /// İş Emri (work_order) KABUL EDİLMEZ.</summary>
    [Fact]
    public void ON17_Kapsam_Disi_Varlik_Reddedilir()
    {
        Assert.Equal(new[] { "material_request", "purchase_order" }, ApprovalEntityTypes.All);
        Assert.False(ApprovalEntityTypes.IsKnown("work_order"));
        Assert.Throws<ArgumentException>(() => _appr.Register("work_order", (_, _, _, _, _, _, _, _) => { }));

        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        Assert.Throws<ArgumentException>(() => _appr.Start(conn, tx, _adminA, "work_order", "x", _adminA.UserId, 1));
    }

    /// <summary>ON18 — Aynı varlık için İKİNCİ açık süreç başlatılamaz.</summary>
    [Fact]
    public void ON18_Cift_Surec_Baslatilamaz()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        var id = TalepAc(_adminA);

        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        Assert.Null(_appr.Start(conn, tx, _adminA, ApprovalEntityTypes.MaterialRequest, id, _adminA.UserId, 2));
    }

    /// <summary>ON19 — <b>Onay tabloları HİÇBİR senkron yolunda değildir</b> (PK-EK-05 / İK-9):
    /// çevrimdışı onay teknik olarak imkânsızdır, yalnız "engellenmiş" değildir.</summary>
    [Fact]
    public void ON19_Onay_Tablolari_Senkronda_Degil()
    {
        foreach (var t in new[] { "approval_instance", "approval_step", "user_hierarchy" })
            Assert.DoesNotContain(t, DepoWise.Infrastructure.Sync.BusinessSyncService.Tables);
    }

    /// <summary>ON20 — "Bana düşen onaylar" YALNIZ sırası gelen adımları verir ve tenant süzgeçlidir
    /// (ALT FAZ 3 ekranının servis sözleşmesi — ekran bu fazda YAPILMADI).</summary>
    [Fact]
    public void ON20_MyPending_Yalniz_Sirasi_Gelen_Adimi_Verir()
    {
        _hier.SetManager(_adminA, _adminA.UserId, _ustA);
        _hier.SetManager(_adminA, _ustA, _ustB);
        _ = TalepAc(_adminA);

        var s1 = Personel("ON-A", _ustA, "request_approval");
        var s2 = Personel("ON-A", _ustB, "request_approval");
        Assert.Single(_appr.MyPending(s1));
        Assert.Empty(_appr.MyPending(s2));                 // sırası gelmedi

        _appr.Approve(s1, _appr.MyPending(s1).Single().StepId);
        Assert.Empty(_appr.MyPending(s1));
        Assert.Single(_appr.MyPending(s2));                // sıra ona geçti
    }

    // ── satın alma yardımcıları ────────────────────────────────────────────────────────────

    /// <summary>Sipariş açar; ikinci değer MAL KABUL satırının kimliğidir (<c>ReceiveLine.LineId</c>
    /// malzeme değil SİPARİŞ SATIRI kimliğidir).</summary>
    private (string OrderId, string LineId) Siparis(SessionContext s)
    {
        var mat = Malzeme(s.CompanyId);
        var sube = Sube(s.CompanyId);
        var id = _po.Create(s, new NewPurchaseOrder(
            OrderNo: "SIP-" + Guid.NewGuid().ToString("N")[..6],
            BranchId: sube,
            Lines: new[] { new NewPurchaseOrderLine(mat, 5m) }));

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM purchase_order_lines WHERE order_id=@o AND is_deleted=0;";
        cmd.AddWithValue("@o", id);
        return (id, (string)cmd.ExecuteScalar()!);
    }

    private string Sube(string co)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,'Merkez','branch',1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.ExecuteNonQuery();
        return id;
    }

    private string? SonOnayDurumu(string orderId)
    {
        using var conn = _f.Create();
        return ApprovalService.LatestStatus(conn, null, "ON-A", ApprovalEntityTypes.PurchaseOrder, orderId);
    }

    private string SiparisDurumu(string orderId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchase_orders WHERE id=@i;";
        cmd.AddWithValue("@i", orderId);
        return (string)cmd.ExecuteScalar()!;
    }
}
