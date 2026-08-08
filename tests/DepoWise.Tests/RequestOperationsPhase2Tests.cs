using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// TALEP OPERASYONLARI — FAZ 2 (kullanıcı onayı 2026-08-08, Migration061).
/// Kapsam: onaylı geçiş matrisi (kullanıcı düzeltmesi dâhil), yetki ayrımı (ops/warehouse/purchase + admin
/// bypass), gönderim bilgileri, işlem geçmişi (kind='operation' + op_branch_id SUNUCUDAN), şube güvenliği.
/// FAZ 2 SINIRI: stok DEĞİŞMEZ, kısmi miktar/alternatif malzeme/satın alma detayı/dosya/bildirim YOK.
/// </summary>
public class RequestOperationsPhase2Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly RequestService _requests;
    private readonly RequestOperationsService _ops;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _admin;
    private readonly string _material;
    private readonly string _branchA = "BR-A", _branchB = "BR-B";

    public RequestOperationsPhase2Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ops2_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        var materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _requests = new RequestService(_factory, new StockService(_factory, _clock), _clock);
        _ops = new RequestOperationsService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _material = materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES(@id,'A','Merkez',1,1);", ("@id", _branchA));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES(@id,'A','Karaman',1,1);", ("@id", _branchB));
        _opening.RecordOpening(_admin, _material, 100m, "op-open");
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Onaylanmış (dolayısıyla operasyonu Beklemede olan) bir talep üretir.</summary>
    private string ApprovedRequest()
    {
        var h = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(_material, 10m) }, SubmitImmediately: true));
        _requests.Approve(_admin, h.Id);
        return h.Id;
    }

    /// <summary>Belirli yetkilerle kullanıcı oturumu (deny-by-default).</summary>
    private static SessionContext User(string id, params string[] modules)
    {
        var perms = modules.Select(m => new ModulePermission(m, true, false, true, false)).ToArray();
        return new SessionContext(id, "A", new[] { RoleKeys.Staff }, new PermissionSet(perms, Array.Empty<string>()));
    }

    private string? OpsStatus(string id)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT operation_status FROM material_requests WHERE id=@id;";
        cmd.AddWithValue("@id", id);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    // ── GEÇİŞ MATRİSİ (kullanıcı onaylı) ──
    [Fact]
    public void Matris_BeklemedenKaynakSecimi_Serbest()
    {
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.PendingOps, RequestOperationStatus.UnderReview));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.PendingOps, RequestOperationStatus.FromWarehouse));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.PendingOps, RequestOperationStatus.BranchTransfer));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.PendingOps, RequestOperationStatus.Purchasing));
        // Sıçrama yok: doğrudan teslim/tamam olamaz
        Assert.False(RequestOperationStateMachine.CanTransition(RequestOperationStatus.PendingOps, RequestOperationStatus.Delivered));
        Assert.False(RequestOperationStateMachine.CanTransition(RequestOperationStatus.PendingOps, RequestOperationStatus.Completed));
    }

    [Fact]
    public void Matris_KULLANICI_DUZELTMESI_TeslimEdildiden_KismenKarsilandiYOK()
    {
        // Kullanıcı kararı: bu geçiş kaldırıldı (Faz 3'te miktar bazlı ele alınacak).
        Assert.False(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Delivered, RequestOperationStatus.PartiallyFulfilled));
        // Teslim Edildi'den yalnız Tamamlandı (+ İptal ortak kuralı)
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Delivered, RequestOperationStatus.Completed));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Delivered, RequestOperationStatus.CancelledOps));
    }

    [Fact]
    public void Matris_EldenTeslim_VeKaynakDegisikligi_Korunuyor()
    {
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.FromWarehouse, RequestOperationStatus.Delivered));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Shipped, RequestOperationStatus.Delivered));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.FromWarehouse, RequestOperationStatus.Purchasing));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Purchasing, RequestOperationStatus.FromWarehouse));
        Assert.True(RequestOperationStateMachine.CanTransition(RequestOperationStatus.OrderPlaced, RequestOperationStatus.Shipped));
    }

    [Fact]
    public void Matris_TerminalDurumlar_GecisVermez()
    {
        foreach (var t in RequestOperationStatusInfo.All)
        {
            Assert.False(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Completed, t));
            Assert.False(RequestOperationStateMachine.CanTransition(RequestOperationStatus.CancelledOps, t));
        }
        Assert.True(RequestOperationStateMachine.IsTerminal(RequestOperationStatus.Completed));
        Assert.True(RequestOperationStateMachine.IsTerminal(RequestOperationStatus.CancelledOps));
    }

    [Fact]
    public void Matris_TerminalOlmayanHerDurumdan_IptalMumkun()
    {
        foreach (var s in RequestOperationStatusInfo.All.Where(x => !RequestOperationStateMachine.IsTerminal(x)))
            Assert.True(RequestOperationStateMachine.CanTransition(s, RequestOperationStatus.CancelledOps));
    }

    [Fact]
    public void Matris_AyniDuruma_GecisYok()
        => Assert.False(RequestOperationStateMachine.CanTransition(RequestOperationStatus.Shipped, RequestOperationStatus.Shipped));

    // ── SERVİS: durum değiştirme ──
    [Fact]
    public void DurumDegistir_GecerliGecis_Uygulanir_VeGecmiseYazilir()
    {
        var id = ApprovedRequest();
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse, "depodan verilecek");

        Assert.Equal("from_warehouse", OpsStatus(id));
        var hist = _ops.GetHistory(_admin, id);
        Assert.Single(hist);
        Assert.Equal("Beklemede", hist[0].FromText);
        Assert.Equal("Depodan Karşılanacak", hist[0].ToText);
        Assert.Equal("depodan verilecek", hist[0].Reason);
    }

    [Fact]
    public void DurumDegistir_GecersizGecis_Reddedilir()
    {
        var id = ApprovedRequest();
        Assert.Throws<InvalidOperationException>(() => _ops.ChangeStatus(_admin, id, RequestOperationStatus.Completed));
        Assert.Equal("pending_ops", OpsStatus(id));   // değişmedi
    }

    [Fact]
    public void OnaylanmamisTalep_OperasyonaAlinamaz()
    {
        var h = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(_material, 3m) }, SubmitImmediately: true));
        Assert.Throws<InvalidOperationException>(() => _ops.ChangeStatus(_admin, h.Id, RequestOperationStatus.FromWarehouse));
    }

    [Fact]
    public void TamAkis_DepodanTeslimTamamlandi()
    {
        var id = ApprovedRequest();
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Shipped);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.ArrivedAtBranch);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Delivered);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Completed);

        Assert.Equal("completed", OpsStatus(id));
        Assert.Equal(5, _ops.GetHistory(_admin, id).Count);   // hiçbir adım kaybolmadı (§13)
        // Terminal: artık geçiş yok
        Assert.Throws<InvalidOperationException>(() => _ops.ChangeStatus(_admin, id, RequestOperationStatus.CancelledOps));
    }

    [Fact]
    public void SatinAlmaAkisi_SiparisHazirlanmadanSevk_Edilebilir()
    {
        var id = ApprovedRequest();
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Purchasing);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.OrderPlaced);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Shipped);   // "Hazırlanıyor" atlandı (kullanıcı onaylı)
        Assert.Equal("shipped", OpsStatus(id));
    }

    // ── YETKİ ──
    [Fact]
    public void Yetki_YalnizOps_SatinAlmaAdimiYapamaz()
    {
        var id = ApprovedRequest();
        var u = User("u1", RequestOperationStateMachine.ModuleOps);
        Assert.Throws<ForbiddenException>(() => _ops.ChangeStatus(u, id, RequestOperationStatus.Purchasing));
        Assert.Equal("pending_ops", OpsStatus(id));
    }

    [Fact]
    public void Yetki_YalnizOps_DepoAdimiYapamaz()
    {
        var id = ApprovedRequest();
        var u = User("u2", RequestOperationStateMachine.ModuleOps);
        Assert.Throws<ForbiddenException>(() => _ops.ChangeStatus(u, id, RequestOperationStatus.FromWarehouse));
    }

    [Fact]
    public void Yetki_SatinAlmaYetkilisi_SatinAlmaAdiminiYapar()
    {
        var id = ApprovedRequest();
        var u = User("u3", RequestOperationStateMachine.ModuleOps, RequestOperationStateMachine.ModulePurchase);
        _ops.ChangeStatus(u, id, RequestOperationStatus.Purchasing);
        Assert.Equal("purchasing", OpsStatus(id));
    }

    [Fact]
    public void Yetki_DepoYetkilisi_DepoAdiminiYapar()
    {
        var id = ApprovedRequest();
        var u = User("u4", RequestOperationStateMachine.ModuleOps, RequestOperationStateMachine.ModuleWarehouse);
        _ops.ChangeStatus(u, id, RequestOperationStatus.FromWarehouse);
        Assert.Equal("from_warehouse", OpsStatus(id));
    }

    [Fact]
    public void Yetki_OpsYetkisiOlmayan_HicbirIslemYapamaz()
    {
        var id = ApprovedRequest();
        var u = User("u5", RequestOperationStateMachine.ModuleWarehouse);   // ops yetkisi YOK
        Assert.Throws<ForbiddenException>(() => _ops.ChangeStatus(u, id, RequestOperationStatus.FromWarehouse));
        Assert.Throws<ForbiddenException>(() => _ops.List(u));
    }

    [Fact]
    public void Yetki_AdminBypass_TumAdimlariYapar()
    {
        var id = ApprovedRequest();
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Purchasing);     // ek yetki olmadan
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.OrderPlaced);
        Assert.Equal("order_placed", OpsStatus(id));
    }

    [Fact]
    public void SonrakiDurumlar_YetkiyeGoreFiltrelenir()
    {
        var id = ApprovedRequest();
        var opsOnly = User("u6", RequestOperationStateMachine.ModuleOps);
        var next = _ops.AllowedNextStates(opsOnly, id);
        Assert.Contains(RequestOperationStatus.UnderReview, next);      // genel adım
        Assert.Contains(RequestOperationStatus.CancelledOps, next);     // iptal genel
        Assert.DoesNotContain(RequestOperationStatus.Purchasing, next); // satın alma yetkisi yok
        Assert.DoesNotContain(RequestOperationStatus.FromWarehouse, next);
    }

    // ── GÖNDERİM BİLGİLERİ + ŞUBE GÜVENLİĞİ ──
    [Fact]
    public void GonderimBilgileri_Kaydedilir()
    {
        var id = ApprovedRequest();
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.BranchTransfer, "transfer",
            fromBranchId: _branchA, toBranchId: _branchB, updateBranches: true);

        var row = _ops.List(_admin).First(r => r.Id == id);
        Assert.Equal("Merkez", row.FromBranchName);
        Assert.Equal("Karaman", row.ToBranchName);
        Assert.Equal("transfer", row.OpsNote);
    }

    [Fact]
    public void BaskaFirmanin_Subesi_Reddedilir()
    {
        Exec("INSERT INTO companies(id,name,created_at,updated_at) VALUES('B','Diger',1,1);");
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('BR-X','B','Yabanci',1,1);");
        var id = ApprovedRequest();
        Assert.Throws<ForbiddenException>(() => _ops.ChangeStatus(_admin, id, RequestOperationStatus.BranchTransfer,
            fromBranchId: "BR-X", toBranchId: null, updateBranches: true));
    }

    [Fact]
    public void IslemGecmisi_IslemSubesini_SUNUCUDAN_Yazar()
    {
        var id = ApprovedRequest();
        // Kullanıcının çalışma şubesi (oturumdan) — istemci gönderemez.
        var u = User("u7", RequestOperationStateMachine.ModuleOps, RequestOperationStateMachine.ModuleWarehouse);
        u.OperatingBranchId = _branchB;
        _ops.ChangeStatus(u, id, RequestOperationStatus.FromWarehouse);

        var hist = _ops.GetHistory(_admin, id);
        Assert.Single(hist);
        Assert.Equal("Karaman", hist[0].BranchName);   // op_branch_id oturumdan geldi
    }

    // ── FAZ 2 SINIRI: stok değişmez ──
    [Fact]
    public void Faz2_StokHareketi_OLUSMAZ()
    {
        var id = ApprovedRequest();
        var before = _opening.GetBalance(_admin, _material);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Shipped);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Delivered);
        Assert.Equal(before, _opening.GetBalance(_admin, _material));   // otomatik stok Faz 3'te
    }

    // ── Onay geçmişi ile operasyon geçmişi AYRI ──
    [Fact]
    public void OnayGecmisi_VeOperasyonGecmisi_Ayri()
    {
        var id = ApprovedRequest();
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.UnderReview);

        Assert.Single(_ops.GetHistory(_admin, id));                  // yalnız operasyon (kind='operation')
        Assert.True(_requests.GetHistory(id).Count >= 2);            // onay geçmişi ayrı durur, silinmedi
    }

    [Fact]
    public void Liste_YalnizOnayliTalepleri_Getirir()
    {
        _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(_material, 1m) }, SubmitImmediately: true)); // onaysız
        var approved = ApprovedRequest();
        var rows = _ops.List(_admin);
        Assert.Single(rows);
        Assert.Equal(approved, rows[0].Id);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
