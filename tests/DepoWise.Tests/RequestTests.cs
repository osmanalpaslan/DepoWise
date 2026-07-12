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

public class RequestTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly RequestService _requests;
    private readonly SessionContext _admin;

    public RequestTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_req_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _requests = new RequestService(_factory, _stock, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));

    // ---- Durum makinesi (saf) ----
    [Theory]
    [InlineData(RequestStatus.Pending, RequestStatus.Approved, true)]
    [InlineData(RequestStatus.Pending, RequestStatus.Rejected, true)]
    [InlineData(RequestStatus.Approved, RequestStatus.Approved, false)]  // çift onay
    [InlineData(RequestStatus.Approved, RequestStatus.Rejected, false)]
    [InlineData(RequestStatus.Draft, RequestStatus.Approved, false)]     // önce pending
    [InlineData(RequestStatus.Rejected, RequestStatus.Approved, false)]
    public void DurumGecisleri(RequestStatus from, RequestStatus to, bool allowed)
        => Assert.Equal(allowed, RequestStatusMachine.CanTransition(from, to));

    // ---- Liste + kalemler (Faz 7c read-query) ----
    [Fact]
    public void Liste_DurumFiltresi_VeKalemler_Calisir()
    {
        var m = Mat("M-LST");
        var draft = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 3m) }));
        _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));

        Assert.Equal(2, _requests.List(_admin).Count);

        var drafts = _requests.List(_admin, RequestStatus.Draft);
        Assert.Single(drafts);
        Assert.Equal(1, drafts[0].ItemCount);

        var byDoc = _requests.List(_admin, null, draft.DocNo);
        Assert.Single(byDoc);

        var items = _requests.GetItems(_admin, draft.Id);
        Assert.Single(items);
        Assert.Equal("M-LST", items[0].MaterialCode);
        Assert.Equal(3m, items[0].Quantity);
    }

    // ---- Güncelleme + onaylı kilit ----
    [Fact]
    public void Update_KalemleriDegistirir_OnayliIseEngeller()
    {
        var m1 = Mat("M-A");
        var m2 = Mat("M-B");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m1, 2m) },
            Description: "ilk", SubmitImmediately: true));

        // Beklemede → güncellenebilir
        _requests.Update(_admin, r.Id, new NewRequest(
            new[] { new RequestItemInput(m2, 5m) }, Description: "yeni"));
        var edit = _requests.GetForEdit(_admin, r.Id);
        Assert.Equal("yeni", edit.Description);
        Assert.Single(edit.Items);
        Assert.Equal("M-B", edit.Items[0].Code);
        Assert.Equal(5m, edit.Items[0].Quantity);

        // Onaylandıktan sonra güncelleme engellenir
        _requests.Approve(_admin, r.Id);
        Assert.Throws<InvalidOperationException>(() =>
            _requests.Update(_admin, r.Id, new NewRequest(new[] { new RequestItemInput(m1, 1m) })));
    }

    // ---- PDF verisi (isimler + kalemler) ----
    [Fact]
    public void GetPdfData_BelgeVeKalemleriDoner()
    {
        var m = Mat("M-PDF");
        var r = _requests.Create(_admin, new NewRequest(
            new[] { new RequestItemInput(m, 4m) }, Description: "Acil", SubmitImmediately: true));

        var d = _requests.GetPdfData(_admin, r.Id);
        Assert.Equal(r.DocNo, d.DocNo);
        Assert.Equal(RequestStatus.Pending, d.Status);
        Assert.Equal("Acil", d.Description);
        Assert.Single(d.Items);
        Assert.Equal("M-PDF", d.Items[0].Code);
        Assert.Equal(4m, d.Items[0].Quantity);
    }

    // ---- Belge no ----
    [Fact]
    public void BelgeNo_TenantYil_Benzersiz_Artar()
    {
        var m = Mat("M-1");
        var r1 = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }));
        var r2 = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }));
        Assert.StartsWith("TLP-", r1.DocNo);
        Assert.NotEqual(r1.DocNo, r2.DocNo);
    }

    // ---- Onay stok değiştirmez ----
    [Fact]
    public void Onay_StoguDegistirmez()
    {
        var m = Mat("M-1");
        _opening.RecordOpening(_admin, m, 100m, "op-open");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 10m) }, SubmitImmediately: true));

        _requests.Approve(_admin, r.Id);
        Assert.Equal(RequestStatus.Approved, _requests.GetStatus(_admin, r.Id));
        Assert.Equal(100m, _stock.GetBalance(m)); // STOK AYNI
    }

    [Fact]
    public void CiftOnay_Engellenir()
    {
        var m = Mat("M-1");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));
        _requests.Approve(_admin, r.Id);
        Assert.Throws<InvalidOperationException>(() => _requests.Approve(_admin, r.Id)); // çift onay
    }

    [Fact]
    public void Yetkisiz_Onay_Reddedilir()
    {
        var m = Mat("M-1");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));
        // requests görüntüle+yaz var ama onay butonu (admin değil) yok
        var clerk = new SessionContext("clerk", "A", Array.Empty<string>(),
            new PermissionSet(new[] { new ModulePermission("requests", true, true, true, false) }));
        Assert.Throws<ForbiddenException>(() => _requests.Approve(clerk, r.Id));
    }

    [Fact]
    public void TalepFormu_ve_Onaylama_AyriYetki()
    {
        var m = Mat("M-1");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));

        // Yalnız FORM yetkisi (requests edit) olan ONAYLAYAMAZ — onay ayrı yetki (request_approval).
        var formOnly = new SessionContext("form", "A", Array.Empty<string>(),
            new PermissionSet(new[] { new ModulePermission("requests", true, true, true, false) }));
        Assert.Throws<ForbiddenException>(() => _requests.Approve(formOnly, r.Id));

        // Yalnız ONAYLAMA yetkisi (request_approval edit) olan ONAYLAR — form yazma gerekmez.
        var approver = new SessionContext("appr", "A", Array.Empty<string>(),
            new PermissionSet(new[] { new ModulePermission("request_approval", true, false, true, false) }));
        _requests.Approve(approver, r.Id);
        Assert.Equal(RequestStatus.Approved, _requests.GetStatus(approver, r.Id));
    }

    [Fact]
    public void OnayliTalepten_KontrolluStokCikis_StokDuser()
    {
        var m = Mat("M-1");
        _opening.RecordOpening(_admin, m, 100m, "op-open");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 10m) }, SubmitImmediately: true));
        _requests.Approve(_admin, r.Id);
        Assert.Equal(100m, _stock.GetBalance(m)); // onayda düşmedi

        _requests.CreateIssueFromRequest(_admin, r.Id, "op-issue"); // açık, kontrollü çıkış
        Assert.Equal(90m, _stock.GetBalance(m)); // şimdi düştü
    }

    [Fact]
    public void OnaysizTalepten_StokCikisi_Reddedilir()
    {
        var m = Mat("M-1");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));
        Assert.Throws<InvalidOperationException>(() => _requests.CreateIssueFromRequest(_admin, r.Id, "op"));
    }

    [Fact]
    public void DurumGecmisi_Kaydedilir()
    {
        var m = Mat("M-1");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));
        _requests.Approve(_admin, r.Id);
        var hist = _requests.GetHistory(r.Id);
        Assert.Contains(hist, h => h.To == RequestStatus.Pending);
        Assert.Contains(hist, h => h.To == RequestStatus.Approved);
    }

    [Fact]
    public void Tenant_BaskaFirmaTalebi_Erisemez()
    {
        var m = Mat("M-1");
        var r = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 1m) }, SubmitImmediately: true));
        var users = new UserService(_factory, _clock);
        var bid = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(bid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _requests.Approve(adminB, r.Id));
    }

    [Fact]
    public void Talep_DenyByDefault()
    {
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _requests.Create(noPerm, new NewRequest(new[] { new RequestItemInput("x", 1m) })));
    }

    // ---- PDF ----
    [Fact]
    public void Pdf_TurkceKarakterlerle_Olusur()
    {
        var pdf = new RequestPdfService();
        var bytes = pdf.Generate(new RequestPdfModel(
            CompanyName: "Şirket Çğüöı A.Ş.",
            DocNo: "TLP-2026-0001", RequestDate: "27/06/2026", Status: "Onaylı",
            BranchName: "Şantiye İğne", RequesterName: "Ömer Çolak", WarehouseName: "Ümit Şahin",
            ApproverName: "Gülşah Öz", Description: "Çeşitli malzeme talebi ğüşçöı",
            Items: new[] { new RequestPdfItem("M-1", "Yağ Filtresi", "Adet", 3m, "EX-001", "NMB123") }));

        Assert.True(bytes.Length > 0);
        // Ekonomik düzen de üretilmeli
        Assert.True(pdf.Generate(new RequestPdfModel("Ş", "TLP-2026-0002", "27/06/2026", "Beklemede",
            null, null, null, null, null,
            new[] { new RequestPdfItem("M-2", "Filtre", "Adet", 1m, null, null) }), economic: true).Length > 0);
        // %PDF imzası
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
